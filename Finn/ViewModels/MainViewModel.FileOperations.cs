using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Finn.Model;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            #region Commands

            private ICommand? _checkSingleFileCommand;
            public ICommand CheckSingleFileCommand => _checkSingleFileCommand ??= new RelayCommand(CheckSingleFile);

            private ICommand? _checkProjectFilesCommand;
            public ICommand CheckProjectFilesCommand => _checkProjectFilesCommand ??= new AsyncRelayCommand(CheckProjectFiles);

            private ICommand? _toggleCacheFilesCommand;
            public ICommand ToggleCacheFilesCommand => _toggleCacheFilesCommand ??= new AsyncRelayCommand(ToggleCacheFiles);

            #endregion

            /// <summary>
            /// Toggles <see cref="FileData.IsCached"/> for all selected files.
            /// Files that are newly marked for caching are pre-cached in the
            /// background immediately so subsequent opens are instant.
            /// </summary>
            private async Task ToggleCacheFiles()
            {
                if (CurrentFiles == null || CurrentFiles.Count == 0) return;

                var files = CurrentFiles.ToList();
                // Determine new state: if any selected file is not cached, we cache all; otherwise uncache all.
                bool newState = files.Any(f => !f.IsCached);

                foreach (var file in files)
                    file.IsCached = newState;

                if (!newState) return;

                // Collect all paths to pre-cache: main file + all version paths
                var paths = new List<string>();
                foreach (var file in files)
                {
                    if (!string.IsNullOrEmpty(file.Sökväg))
                        paths.Add(file.Sökväg);
                    foreach (var ver in file.Versions)
                    {
                        if (!string.IsNullOrEmpty(ver.Sökväg))
                            paths.Add(ver.Sökväg);
                    }
                }

                // Pre-cache in the background with cancellation support
                int total = paths.Count;
                int done = 0;
                var cts = new CancellationTokenSource();
                PreviewVM.SetBackgroundTaskCts(cts);
                PreviewVM.BackgroundTaskActive = true;
                PreviewVM.BackgroundTaskMessage = $"Pre-caching 0/{total}…";
                PreviewVM.BackgroundTaskProgress = 0;

                try
                {
                    foreach (var path in paths)
                    {
                        if (cts.Token.IsCancellationRequested) break;
                        try
                        {
                            await PreviewVM.PreCacheFileAsync(path, cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { break; }
                        catch { }
                        done++;
                        PreviewVM.BackgroundTaskMessage = $"Pre-caching {done}/{total}…";
                        PreviewVM.BackgroundTaskProgress = (int)(100.0 * done / total);
                    }

                    PreviewVM.BackgroundTaskMessage = cts.Token.IsCancellationRequested
                        ? $"Cancelled — cached {done}/{total} file(s)"
                        : $"Cached {done} file(s)";
                    PreviewVM.BackgroundTaskProgress = 100;

                    // Keep visible briefly so the user sees completion, then hide
                    await Task.Delay(2000).ConfigureAwait(false);
                }
                finally
                {
                    PreviewVM.SetBackgroundTaskCts(null);
                    PreviewVM.BackgroundTaskActive = false;
                    cts.Dispose();
                }
            }

            public async Task AddFile(Avalonia.Visual window)
            {
                if (CurrentProject == null) return;

                var topLevel = TopLevel.GetTopLevel(window);
                if (topLevel == null) return;
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Add File",
                    FileTypeFilter = new[] { FilePickerFileTypes.Pdf },
                    AllowMultiple = true
                });

                if (files.Count == 0) return;

                var mainWindow = topLevel as Window;
                await AddFilesWithVersionCheck(
                    files.Select(f => f.Path.LocalPath), mainWindow, "File Picker");
            }

            /// <summary>
            /// Adds files to the current project. Files that match an existing entry
            /// by name are collected and presented in a version-import dialog so the
            /// user can choose the label before they are registered as versions.
            /// Shows an import confirmation dialog so the user can review files,
            /// pick a category, and remove unwanted entries before importing.
            /// </summary>
            public async Task AddFilesWithVersionCheck(IEnumerable<string> paths, Window? mainWindow, string source = "Added")
            {
                // Build O(1) lookup structures to avoid linear scans per file
                var existingPaths = BuildKnownPathSet();
                var existingByName = BuildFileNameLookup();

                // Wide canonical lookup: all real files including group children.
                // Group children are IsAppendedFile=true, so the narrow lookup misses them;
                // we include them here so a file nested inside a group can still be found
                // as a version target.  Group header placeholders are always excluded.
                var wideByName = CurrentProject!.StoredFiles
                    .Where(f => !f.IsGroup && (!f.IsAppendedFile || f.IsGroupChild))
                    .GroupBy(f => f.Namn, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                // Pre-compile the suffix pattern once (empty prefix = no suffix detection)
                string versionPrefix = CurrentProject!.VersionSuffix ?? string.Empty;
                var suffixRx = string.IsNullOrEmpty(versionPrefix) ? null : BuildSuffixPattern(versionPrefix);

                // When a specific type is selected, use it as the default category
                string? defaultCategory = (Type != null && Type != ALL_TYPES) ? Type : null;

                var candidatePaths = new List<(string Path, string Source)>();
                var versionCandidates = new List<VersionImportEntry>();
                int skippedCount = 0;

                foreach (string path in paths)
                {
                    if (existingPaths.Contains(path))
                    {
                        skippedCount++;
                        continue;
                    }

                    string fileName = System.IO.Path.GetFileNameWithoutExtension(path);

                    if (existingByName.TryGetValue(fileName, out var existing))
                    {
                        versionCandidates.Add(new VersionImportEntry
                        {
                            ExistingFile = existing,
                            NewFilePath = path
                        });
                    }
                    else if (suffixRx != null)
                    {
                        // Suffix-aware detection: if the file name matches the version suffix
                        // pattern and the extracted base name maps to a known file (including
                        // files that already carry versions or live inside a file-group),
                        // route it directly to the version import dialog.
                        var m = suffixRx.Match(fileName);
                        if (m.Success)
                        {
                            string baseName = m.Groups["base"].Value.TrimEnd();
                            string num = m.Groups["num"].Value;
                            string label = versionPrefix + num;

                            // Find unambiguous canonical — exact unsuffixed name first,
                            // then same-base fallback (e.g. "Drawing v1" for "Drawing v2").
                            FileData? canonical = FindCanonicalForSuffixMatch(baseName, wideByName, suffixRx);

                            if (canonical != null)
                            {
                                versionCandidates.Add(new VersionImportEntry
                                {
                                    ExistingFile = canonical,
                                    NewFilePath = path,
                                    SelectedLabel = label
                                });
                                continue; // Skip normal import path
                            }
                        }

                        candidatePaths.Add((path, source));
                        existingPaths.Add(path);
                        existingByName.TryAdd(fileName, null!);
                    }
                    else
                    {
                        candidatePaths.Add((path, source));
                        existingPaths.Add(path);
                        existingByName.TryAdd(fileName, null!);
                    }
                }

                // Show import dialog for new files
                if (candidatePaths.Count > 0 && mainWindow != null)
                {
                    var dialog = new Dialogs.xImportDia
                    {
                        DataContext = this,
                        RequestedThemeVariant = mainWindow.ActualThemeVariant
                    };
                    dialog.SetFiles(candidatePaths, skippedCount, CurrentProject!.AllowedTypes, defaultCategory);
                    await dialog.ShowDialog(mainWindow);

                    if (dialog.Confirmed)
                    {
                        string assignedType = dialog.SelectedCategory;
                        var acceptedSet = new HashSet<string>(dialog.AcceptedPaths, StringComparer.OrdinalIgnoreCase);

                        var newFiles = new List<FileData>();
                        foreach (var (path, _) in candidatePaths)
                        {
                            if (!acceptedSet.Contains(path)) continue;
                            newFiles.Add(new FileData
                            {
                                Namn = System.IO.Path.GetFileNameWithoutExtension(path),
                                Filtyp = assignedType,
                                Uppdrag = CurrentProject!.Namn,
                                Sökväg = path
                            });
                        }

                        if (newFiles.Count > 0)
                        {
                            CurrentProject!.AddFiles(newFiles);
                            var newPaths = new HashSet<string>(
                                newFiles.Select(f => f.Sökväg),
                                StringComparer.OrdinalIgnoreCase);
                            AutoGroupVersions(CurrentProject!, newPaths);
                        }
                    }
                    else
                    {
                        // User cancelled — skip version dialog too
                        return;
                    }
                }

                bool versionsAdded = false;
                if (versionCandidates.Count > 0 && mainWindow != null)
                {
                    bool confirmed = await ShowVersionImportDialogAsync(mainWindow, versionCandidates);
                    if (confirmed)
                    {
                        foreach (var entry in versionCandidates)
                            entry.ExistingFile.AddVersion(entry.NewFilePath, entry.SelectedLabel);
                        versionsAdded = true;
                    }
                }

                if (candidatePaths.Count > 0 || versionsAdded)
                {
                    UpdateFilter();
                    MarkDirty();
                }
            }

            // Theme/resource updates handled by UIService

            /// <summary>
            /// Builds the version-suffix regex for <paramref name="prefix"/>.
            /// Matches: &lt;base&gt;&lt;sep&gt;&lt;prefix&gt;&lt;num&gt; where sep is space/dash/underscore
            /// and num is an ISO date, integer, or single letter (case-insensitive).
            /// </summary>
            private static System.Text.RegularExpressions.Regex BuildSuffixPattern(string prefix) =>
                new(
                    @"^(?<base>.+?)[ \-_]" + System.Text.RegularExpressions.Regex.Escape(prefix) + @"(?<num>\d{4}-\d{2}-\d{2}|\d+|[A-Z])$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            /// <summary>
            /// Given a <paramref name="baseName"/> extracted from an incoming suffixed file name
            /// (e.g. "Drawing" from "Drawing v2"), finds the single canonical file in
            /// <paramref name="wideByName"/> that should receive the new version.
            ///
            /// Two passes are tried in order:
            ///   1. Exact name match: an existing file literally named <paramref name="baseName"/>.
            ///   2. Same-base fallback: an existing file whose own name contains the same suffix
            ///      pattern and parses to the same base (e.g. "Drawing v1" → base "Drawing").
            ///      This handles the common case where there is no unsuffixed original and all
            ///      versions are stored under the first suffixed file (e.g. "Drawing v1").
            ///
            /// Returns <c>null</c> when no unambiguous canonical can be determined.
            /// </summary>
            private static FileData? FindCanonicalForSuffixMatch(
                string baseName,
                Dictionary<string, List<FileData>> wideByName,
                System.Text.RegularExpressions.Regex suffixRx)
            {
                // Pass 1: exact unsuffixed name match
                if (wideByName.TryGetValue(baseName, out var exactMatches) && exactMatches.Count == 1)
                    return exactMatches[0];

                // Pass 2: same-base fallback — find existing files that also parse to this base.
                // Priority: a file that already has versions registered on it (it is already
                // acting as the canonical); otherwise the highest-numbered suffixed file so that
                // the latest version is always treated as the "original".
                FileData? bestCanonical = null;
                int bestVer = int.MinValue;

                foreach (var (name, files) in wideByName)
                {
                    var m = suffixRx.Match(name);
                    if (!m.Success) continue;

                    string existingBase = m.Groups["base"].Value.TrimEnd();
                    if (!StringComparer.OrdinalIgnoreCase.Equals(existingBase, baseName)) continue;

                    // Ambiguous — two different files share the same base, can't pick one safely
                    if (files.Count != 1) return null;

                    FileData candidate = files[0];

                    // A file that already owns versions is always the canonical
                    if (candidate.HasVersions)
                        return candidate;

                    string raw = m.Groups["num"].Value;
                    int ver = raw.Length == 1 && char.IsLetter(raw[0])
                        ? char.ToUpperInvariant(raw[0]) - 'A' + 1
                        : System.DateTime.TryParseExact(raw, "yyyy-MM-dd",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var d)
                            ? (int)(d - System.DateTime.UnixEpoch).TotalDays
                            : int.TryParse(raw, out var n) ? n : 0;

                    // Pick highest version — the latest file is always the canonical base
                    if (ver > bestVer)
                    {
                        bestVer = ver;
                        bestCanonical = candidate;
                    }
                }

                return bestCanonical;
            }

            /// <summary>
            /// Scans eligible top-level files in <paramref name="project"/> and groups
            /// those that share a base name and differ only by a version suffix.
            /// Files are only ever grouped within the same directory — cross-directory
            /// merges are never performed regardless of name similarity.
            /// When <paramref name="newFilePaths"/> is supplied (import / folder sync)
            /// only directories that contain at least one newly-added file are considered,
            /// so an import cannot reshuffle pre-existing files in unrelated directories.
            /// Pass <c>null</c> for Reapply, which is intentionally project-wide.
            /// </summary>
            private static void AutoGroupVersions(ProjectData project,
                IReadOnlySet<string>? newFilePaths = null)
            {
                string prefix = project.VersionSuffix;
                if (string.IsNullOrEmpty(prefix)) return;

                var suffixPattern = BuildSuffixPattern(prefix);

                // Suffix-matching candidates: flat, unversioned, non-group files.
                // These are the files that will be absorbed as versions.
                var candidates = project.StoredFiles
                    .Where(f => !f.IsAppendedFile && !f.IsGroup && !f.HasVersions)
                    .ToList();

                // Wide canonical pool: all real files including group children.
                // Group children are IsAppendedFile=true, so excluding all appended files
                // would make a file nested inside a group invisible as a canonical target.
                // Group header placeholders are excluded; styled attached children are excluded.
                var wideByDirAndName = project.StoredFiles
                    .Where(f => !f.IsGroup && (!f.IsAppendedFile || f.IsGroupChild))
                    .GroupBy(
                        f => (Dir: System.IO.Path.GetDirectoryName(f.Sökväg) ?? string.Empty,
                              Name: f.Namn),
                        f => f,
                        (key, files) => (key.Dir, key.Name, Files: files.ToList()))
                    .ToList();

                // When a new-file set is provided restrict to directories that contain
                // at least one newly-added file.  This prevents a small import from
                // reshuffling pre-existing files in unrelated parts of the project.
                if (newFilePaths != null && newFilePaths.Count > 0)
                {
                    var allowedDirs = new HashSet<string>(
                        newFilePaths.Select(p => System.IO.Path.GetDirectoryName(p) ?? string.Empty),
                        StringComparer.OrdinalIgnoreCase);

                    candidates = candidates
                        .Where(f => allowedDirs.Contains(
                            System.IO.Path.GetDirectoryName(f.Sökväg) ?? string.Empty))
                        .ToList();
                }

                // Group candidates by directory — files in different directories are
                // never auto-grouped together regardless of base-name similarity.
                var byDirectory = candidates
                    .GroupBy(f => System.IO.Path.GetDirectoryName(f.Sökväg) ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase);

                bool anyGrouped = false;

                foreach (var dirGroup in byDirectory)
                {
                    string dir = dirGroup.Key;
                    var dirCandidates = dirGroup.ToList();

                    // Map base-name → list of (file, version-number) within this directory
                    var groups = new Dictionary<string, List<(FileData File, int Version)>>(
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var file in dirCandidates)
                    {
                        var m = suffixPattern.Match(file.Namn);
                        if (!m.Success) continue;

                        string baseName = m.Groups["base"].Value.TrimEnd();
                        string raw = m.Groups["num"].Value;
                        // Letters → 1-based index (A=1, B=2, …, Z=26)
                        // ISO dates (YYYY-MM-DD) → days since epoch for numeric comparison
                        // Plain digits → parsed directly
                        int ver = raw.Length == 1 && char.IsLetter(raw[0])
                            ? char.ToUpperInvariant(raw[0]) - 'A' + 1
                            : System.DateTime.TryParseExact(raw, "yyyy-MM-dd",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out var d)
                                ? (int)(d - System.DateTime.UnixEpoch).TotalDays
                                : int.TryParse(raw, out var n) ? n : 0;

                        if (!groups.TryGetValue(baseName, out var list))
                        {
                            list = new List<(FileData, int)>();
                            groups[baseName] = list;
                        }
                        list.Add((file, ver));
                    }

                    if (groups.Count == 0) continue;

                    // Build a flat-candidate name lookup (same-dir, unversioned files only)
                    // for cases where the unsuffixed original is also a new flat import.
                    var flatByName = dirCandidates
                        .GroupBy(f => f.Namn, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                    foreach (var kvp in groups)
                    {
                        string baseName = kvp.Key;
                        var group = kvp.Value;

                        // 1. Look for an unsuffixed canonical in the flat import candidates
                        //    (same directory, same base name, unversioned).
                        flatByName.TryGetValue(baseName, out var flatMatches);
                        FileData? unsuffixedFile = flatMatches?.Count == 1 ? flatMatches[0] : null;

                        // 2. If not found there, search the wider pool — this catches files
                        //    that are already in the project (possibly inside a file-group or
                        //    already carrying versions from a previous import).
                        if (unsuffixedFile == null)
                        {
                            var wideMatch = wideByDirAndName.FirstOrDefault(
                                x => StringComparer.OrdinalIgnoreCase.Equals(x.Dir, dir)
                                  && StringComparer.OrdinalIgnoreCase.Equals(x.Name, baseName));
                            if (wideMatch.Files?.Count == 1)
                                unsuffixedFile = wideMatch.Files[0];
                        }

                        bool hasUnsuffixed = unsuffixedFile != null;

                        // Need at least 2 suffixed files, OR 1 suffixed + an unambiguous unsuffixed canonical
                        if (group.Count < 2 && !hasUnsuffixed) continue;

                        // Unsuffixed file always wins as canonical.
                        // When all files are suffixed, pick the HIGHEST version as canonical
                        // so that e.g. "Drawing v2" becomes the base and "Drawing v1" is
                        // stored as a version — the latest file is always the "original".
                        FileData canonicalFile = hasUnsuffixed
                            ? unsuffixedFile!
                            : group.OrderByDescending(x => x.Version).First().File;

                        // Tag the canonical so Reset can distinguish auto groups from manual ones
                        canonicalFile.IsAutoGrouped = true;

                        foreach (var (otherFile, _) in group)
                        {
                            if (otherFile == canonicalFile) continue;

                            // Skip if this file is already registered as a version on the canonical
                            if (canonicalFile.Versions.Any(v =>
                                    string.Equals(v.Sökväg, otherFile.Sökväg,
                                        StringComparison.OrdinalIgnoreCase)))
                                continue;

                            // Re-derive the label from the actual file name so it exactly
                            // matches what is on disk (preserves letter casing, e.g. "RevA")
                            var labelMatch = suffixPattern.Match(otherFile.Namn);
                            string label = labelMatch.Success
                                ? prefix + labelMatch.Groups["num"].Value
                                : prefix + "?";

                            canonicalFile.AddVersion(otherFile.Sökväg, label);

                            // Mark the version so ResetVersionGrouping can distinguish it
                            // from manually-added versions and only dissolve auto-grouped ones.
                            var addedVersion = canonicalFile.Versions
                                .FirstOrDefault(v => string.Equals(v.Sökväg, otherFile.Sökväg,
                                    StringComparison.OrdinalIgnoreCase));
                            if (addedVersion != null)
                                addedVersion.IsAutoGrouped = true;

                            project.StoredFiles.Remove(otherFile);
                        }

                        anyGrouped = true;
                    }
                }

                if (anyGrouped)
                    project.SetFiletypeList();
            }

            /// <summary>
            /// Returns the number of unversioned top-level files that match the project's
            /// current version suffix and would be eligible for auto-grouping on Reapply.
            /// </summary>
            public static int CountAutoGroupCandidates(ProjectData project)
            {
                string prefix = project.VersionSuffix;
                if (string.IsNullOrEmpty(prefix)) return 0;

                var pattern = BuildSuffixPattern(prefix);

                return project.StoredFiles
                    .Count(f => !f.IsAppendedFile && !f.IsGroup && !f.HasVersions
                                && pattern.IsMatch(f.Namn));
            }

            /// <summary>
            /// Returns the number of canonical files that carry auto-grouped (or legacy
            /// pre-flag) versions and would be dissolved by <see cref="ResetVersionGrouping"/>.
            /// </summary>
            public static int CountAutoGroupReset(ProjectData project) =>
                project.StoredFiles.Count(f =>
                    !f.IsAppendedFile && !f.IsGroup && f.HasVersions &&
                    // Flagged auto groups
                    (f.IsAutoGrouped ||
                    // Legacy pre-flag groups: no flags set on file or any version
                     (!f.IsAutoGrouped && f.Versions.All(v => !v.IsAutoGrouped))));

            /// <summary>
            /// Re-applies version grouping to all flat (unversioned) top-level files
            /// in <paramref name="project"/> using the current <see cref="ProjectData.VersionSuffix"/>.
            /// Operates across the whole project (no directory restriction).
            /// Already-versioned files are left untouched.
            /// </summary>
            public void ReapplyVersionGrouping(ProjectData project)
            {
                AutoGroupVersions(project, newFilePaths: null); // null = project-wide
                UpdateFilter();
                BuildTreeData();
                MarkDirty();
            }

            /// <summary>
            /// Dissolves auto-grouped versions in <paramref name="project"/>: each version
            /// entry flagged with <see cref="FileVersionData.IsAutoGrouped"/> is re-instated
            /// as its own top-level <see cref="FileData"/> entry. Manually added versions on
            /// the same canonical file are left intact.
            /// <para>
            /// Legacy groups created before the <c>IsAutoGrouped</c> flag was introduced
            /// (both the canonical file and all its version entries carry <c>false</c>) are
            /// also dissolved, because no manual versions could have existed in that era.
            /// </para>
            /// <see cref="FileData.IsAutoGrouped"/> is cleared only when no auto-grouped
            /// version entries remain on the canonical file.
            /// </summary>
            public void ResetVersionGrouping(ProjectData project)
            {
                // Legacy retag pass: files that have versions but neither the canonical flag
                // nor any individual version flag set predate the IsAutoGrouped feature.
                // Since manual version groups did not exist before the feature, it is safe to
                // treat all of their version entries as auto-grouped so they can be dissolved.
                foreach (var f in project.StoredFiles
                    .Where(f => !f.IsAppendedFile && !f.IsGroup && f.HasVersions
                                && !f.IsAutoGrouped
                                && f.Versions.All(v => !v.IsAutoGrouped))
                    .ToList())
                {
                    f.IsAutoGrouped = true;
                    foreach (var v in f.Versions)
                        v.IsAutoGrouped = true;
                }

                var filesToProcess = project.StoredFiles
                    .Where(f => !f.IsAppendedFile && !f.IsGroup && f.HasVersions && f.IsAutoGrouped)
                    .ToList();

                foreach (var canonical in filesToProcess)
                {
                    // Only dissolve versions that were created by auto-grouping.
                    // Manual versions attached by the user are left in place.
                    var autoVersions = canonical.Versions
                        .Where(v => v.IsAutoGrouped)
                        .ToList();

                    if (autoVersions.Count == 0) continue;

                    // Restore each auto-grouped version as a separate flat FileData
                    foreach (var ver in autoVersions)
                    {
                        var restored = new FileData
                        {
                            Namn         = System.IO.Path.GetFileNameWithoutExtension(ver.Sökväg),
                            Sökväg       = ver.Sökväg,
                            Filtyp       = canonical.Filtyp,
                            Uppdrag      = canonical.Uppdrag,
                            Tagg         = canonical.Tagg,
                            Datum        = canonical.Datum,
                            SyncFolder   = canonical.SyncFolder,
                            IsFromFolder = canonical.IsFromFolder,
                        };
                        project.StoredFiles.Add(restored);
                    }

                    foreach (var ver in autoVersions)
                        canonical.RemoveVersion(ver);

                    // Clear the canonical flag only when all auto-grouped versions are gone.
                    // If manual versions remain, the file stays versioned.
                    if (!canonical.Versions.Any(v => v.IsAutoGrouped))
                        canonical.IsAutoGrouped = false;
                }

                project.SetFiletypeList();
                UpdateFilter();
                BuildTreeData();
                MarkDirty();
            }

            public void SetCategory(string category)
            {
                SetProjecCategory(category);
            }

            public void SetGroup(string? group)
            {
                CurrentProject!.Parent = string.IsNullOrWhiteSpace(group) ? null : group;
                MarkDirty();
            }

            public void CopyFilenameToClipboard(Avalonia.Visual window)
            {
                if (CurrentFiles == null) return;
                var text = string.Join(Environment.NewLine, CurrentFiles.Select(f => f.Namn));
                var clipboard2 = TopLevel.GetTopLevel(window)?.Clipboard;
                if (clipboard2 != null)
                    _ = clipboard2.SetTextAsync(text)
                        .ContinueWith(t => Utils.ErrorLogger.Log(t.Exception!, "CopyFilenameToClipboard"),
                            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }

            public void CopyFilepathToClipboard(Avalonia.Visual window)
            {
                if (CurrentFiles == null) return;
                var text = string.Join(Environment.NewLine, CurrentFiles.Select(f => f.Sökväg));
                var clipboard3 = TopLevel.GetTopLevel(window)?.Clipboard;
                if (clipboard3 != null)
                    _ = clipboard3.SetTextAsync(text)
                        .ContinueWith(t => Utils.ErrorLogger.Log(t.Exception!, "CopyFilepathToClipboard"),
                            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }

            public void CopyListviewToClipboard(Avalonia.Visual window)
            {
                if (CurrentFiles == null) return;
                var sb = new StringBuilder();

                foreach (FileData file in CurrentFiles)
                {
                    if (CurrentProject!.Meta_1 == true) { sb.Append(file.Namn).Append('\t'); }
                    if (CurrentProject!.Meta_2 == true) { sb.Append(file.Filtyp).Append('\t'); }
                    if (CurrentProject!.Meta_3 == true) { sb.Append(file.Uppdrag).Append('\t'); }
                    if (CurrentProject!.Meta_4 == true) { sb.Append(file.Tagg).Append('\t'); }
                    if (CurrentProject!.Meta_5 == true) { sb.Append(file.Färg).Append('\t'); }
                    if (CurrentProject!.Meta_6 == true) { sb.Append(file.Handling).Append('\t'); }
                    if (CurrentProject!.Meta_7 == true) { sb.Append(file.Status).Append('\t'); }
                    if (CurrentProject!.Meta_8 == true) { sb.Append(file.Datum).Append('\t'); }
                    if (CurrentProject!.Meta_9 == true) { sb.Append(file.Ritningstyp).Append('\t'); }
                    if (CurrentProject!.Meta_10 == true) { sb.Append(file.Beskrivning1).Append('\t'); }
                    if (CurrentProject!.Meta_11 == true) { sb.Append(file.Beskrivning2).Append('\t'); }
                    if (CurrentProject!.Meta_12 == true) { sb.Append(file.Beskrivning3).Append('\t'); }
                    if (CurrentProject!.Meta_13 == true) { sb.Append(file.Beskrivning4).Append('\t'); }
                    if (CurrentProject!.Meta_14 == true) { sb.Append(file.Revidering).Append('\t'); }
                    if (CurrentProject!.Meta_15 == true) { sb.Append(file.Sökväg).Append('\t'); }

                    sb.AppendLine();
                }
                var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
                if (clipboard != null)
                    _ = clipboard.SetTextAsync(sb.ToString())
                        .ContinueWith(t => Utils.ErrorLogger.Log(t.Exception!, "CopyListviewToClipboard"),
                            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }

            public void CheckSingleFile()
            {
                if (CurrentFile != null && !CurrentFile.IsGroup)
                    CurrentFile.IsFileMissing = !CurrentFile.IsValidPdf();
            }

            public async Task CheckProjectFiles()
            {
                await Task.Run(() => CheckFileAsync());
            }

            public async Task CheckFileAsync()
            {
                if (CurrentProject == null) return;

                // Capture the file list on the UI thread before entering
                // the background task, so we never iterate an ObservableCollection
                // from a thread-pool thread (which can corrupt it or throw).
                var files = CurrentProject.StoredFiles.ToList();

                // Clear status on the UI thread.
                await Dispatcher.UIThread.InvokeAsync(ClearFileStatus);

                int n = files.Count;
                int i = 0;
                var results = new List<(FileData File, bool Missing, int Progress)>(files.Count);

                foreach (FileData file in files)
                {
                    i++;
                    bool missing = !file.IsGroup && !file.IsValidPdf();

                    results.Add((
                        file,
                        missing,
                        n == 0 ? 0 : (int)(100 * ((float)i / n))));
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var result in results)
                    {
                        result.File.IsFileMissing = result.Missing;
                        PreviewVM.Progress = result.Progress;
                    }
                });
            }

            public void ClearFileStatus()
            {
                if (CurrentProject == null) return;
                foreach (FileData file in CurrentProject.StoredFiles)
                    file.IsFileMissing = false;
            }

            public void OpenFile()
            {
                if (CurrentFiles == null) return;
                try
                {
                    foreach (FileData file in CurrentFiles)
                    {
                        if (string.IsNullOrEmpty(file.Sökväg)) continue;
                        ProcessStartInfo psi = new()
                        {
                            FileName = file.Sökväg,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                }
                catch (Exception e)
                {
                    Utils.ErrorLogger.Log(e, "OpenFile");
                    PreviewVM.StatusMessage = $"Could not open file: {e.Message}";
                }
            }

            public void OpenFileDirect(string path)
            {
                try
                {
                    ProcessStartInfo psi = new()
                    {
                        FileName = path,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                catch (Exception e)
                {
                    Utils.ErrorLogger.Log(e, "OpenFileDirect");
                    PreviewVM.StatusMessage = $"Could not open file: {e.Message}";
                }
            }

            public void OpenMeta()
            {
                if (CurrentFiles == null) return;
                try
                {
                    foreach (FileData file in CurrentFiles)
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = file.Sökväg + ".md",
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                }
                catch (Exception e)
                {
                    Utils.ErrorLogger.Log(e, "OpenMeta");
                    PreviewVM.StatusMessage = $"Could not open notes file: {e.Message}";
                }
            }

            public void OpenDwg()
            {
                if (CurrentFile?.Filtyp == DRAWING_TYPE)
                {
                    string dwgPathOld = CurrentFile.Sökväg.Replace("Ritning", "Ritdef").Replace("pdf", "dwg");
                    string dwgPathNew = CurrentFile.Sökväg.Replace("Drawing", "Drawing Definition").Replace("pdf", "dwg");

                    try
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = dwgPathOld,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (Exception) { }

                    try
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = dwgPathNew,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (Exception) { }
                }
            }

            public void OpenDoc()
            {
                if (CurrentFile?.Filtyp == DOCUMENT_TYPE)
                {
                    string docPath = CurrentFile.Sökväg.Replace("pdf", "docx");

                    try
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = docPath,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (Exception) { }
                }
            }

            public void OpenPath()
            {
                try
                {
                    if (string.IsNullOrEmpty(CurrentFile?.Sökväg)) return;
                    string? folderpath = System.IO.Path.GetDirectoryName(CurrentFile.Sökväg);
                    Process process = Process.Start("explorer.exe", "\"" + folderpath + "\"");
                }
                catch (Exception) { }
            }

            public void OpenPathDirect(string filepath)
            {
                try
                {
                    string? folderpath = System.IO.Path.GetDirectoryName(filepath);
                    Process process = Process.Start("explorer.exe", "\"" + folderpath + "\"");
                }
                catch (Exception) { }
            }

            public void AddColor(string color)
            {
                if (CurrentFiles == null) return;
                foreach (FileData file in CurrentFiles)
                {
                    file.Färg = color;
                }
                MarkDirty();
            }

            public void ClearAll()
            {
                if (CurrentFiles == null) return;
                foreach (FileData file in CurrentFiles)
                {
                    file.Färg = "";
                    file.Tagg = "";
                }
                MarkDirty();
            }

            public void AddTag(string tag)
            {
                if (CurrentFiles == null) return;
                foreach (FileData file in CurrentFiles)
                {
                    file.Tagg = tag;
                }
                MarkDirty();
            }

            public void ClearTag()
            {
                if (CurrentFiles == null) return;
                foreach (FileData file in CurrentFiles)
                {
                    file.Tagg = "";
                }
                MarkDirty();
            }

            public void EditType(string type)
            {
                SetTypeSelected(type);
            }

            public void AddAppendedFile(string filepath, bool fromFolder = false)
            {
                if (CurrentFile != null && !CurrentProject!.StoredFiles.Any(x =>
                    string.Equals(x.Sökväg, filepath, StringComparison.OrdinalIgnoreCase)))
                {
                    var appended = new FileData()
                    {
                        Namn = System.IO.Path.GetFileNameWithoutExtension(filepath),
                        Sökväg = filepath,
                        IsFromFolder = fromFolder,
                        Uppdrag = CurrentFile.Uppdrag,
                        Filtyp = CurrentFile.Filtyp
                    };

                    appended.SetParent(CurrentFile);

                    CurrentProject.StoredFiles.Add(appended);
                    CurrentFile.HasChildren = true;
                    NotifyCurrentSelectionStructureChanged();
                    if (!CurrentFile.IsExpanded)
                    {
                        CurrentFile.IsExpanded = true;
                        UpdateFilter();
                    }
                    MarkDirty();
                }
            }

            public void AddOtherFile(string filepath)
            {
                var owner = OtherFilesOwner;
                string candidatePath = NormalizePathForComparison(filepath);
                if (owner != null && !owner.OtherFiles.Any(x =>
                    string.Equals(NormalizePathForComparison(x.Filepath), candidatePath, GetPathComparison())))
                {
                    OtherData newFile = new() { Filepath = filepath };
                    newFile.SetFile();

                    owner.OtherFiles.Add(newFile);
                    SortOtherFiles();
                    MarkDirty();
                }
            }

            private static StringComparison GetPathComparison() =>
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            private static string NormalizePathForComparison(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return string.Empty;

                try
                {
                    return Path.GetFullPath(path)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    return path;
                }
            }

            public void RemoveOtherFile(OtherData file)
            {
                if (file != null)
                {
                    bool wasSynced = file.IsFromFolder && !string.IsNullOrEmpty(file.SyncFolder);
                    string? syncFolder = file.SyncFolder;

                    OtherFilesOwner?.OtherFiles.Remove(file);
                    SortOtherFiles();
                    MarkDirty();

                    if (wasSynced && syncFolder != null)
                        FlagSyncFoldersAsPending(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { syncFolder });
                }
            }

            public void RemoveOtherFiles(IEnumerable<OtherData> files)
            {
                var owner = OtherFilesOwner;
                if (owner == null) return;

                var pendingSyncFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    if (file.IsFromFolder && !string.IsNullOrEmpty(file.SyncFolder))
                        pendingSyncFolders.Add(file.SyncFolder!);
                    owner.OtherFiles.Remove(file);
                }
                SortOtherFiles();
                MarkDirty();

                if (pendingSyncFolders.Count > 0)
                    FlagSyncFoldersAsPending(pendingSyncFolders);
            }

            private void SortOtherFiles()
            {
                var owner = OtherFilesOwner;
                if (owner != null)
                {
                    SortOtherFilesDirect(owner);
                }
            }

            private void SortOtherFilesDirect(FileData file)
            {
                file.OtherFiles.ReplaceAll(file.OtherFiles.OrderBy(x => x.Name));
            }

            /// <summary>
            /// Updates all name-based links that reference a file after it is renamed.
            /// Fixes child file <see cref="FileData.ParentNamn"/> and folder
            /// <see cref="FolderData.AttachToFile"/> so the links don't break.
            /// </summary>
            internal void UpdateFileLinks(string oldName, string newName, string? newPath)
            {
                if (string.Equals(oldName, newName, StringComparison.Ordinal))
                    return;

                // Update children that point back to this file by name
                foreach (var child in CurrentProject!.StoredFiles)
                {
                    if (string.Equals(child.ParentNamn, oldName, StringComparison.OrdinalIgnoreCase))
                        child.ParentNamn = newName;
                }

                // Update folder entries attached to this file
                foreach (var folder in CurrentProject!.Folders)
                {
                    if (string.Equals(folder.AttachToFile, oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        folder.AttachToFile = newName;
                        if (newPath != null)
                            folder.AttachToFilePath = newPath;
                    }
                }
            }

            /// <summary>
            /// Renames a file's display name and updates all child/folder links.
            /// Use this instead of setting <see cref="FileData.Namn"/> directly
            /// to keep name-based references consistent.
            /// </summary>
            public void RenameFile(FileData file, string newName)
            {
                if (file == null || string.IsNullOrEmpty(newName))
                    return;

                if (file.IsTopLevel)
                    newName = EnsureUniqueName(newName, file);

                string oldName = file.Namn;
                if (string.Equals(oldName, newName, StringComparison.Ordinal))
                    return;

                file.Namn = newName;
                UpdateFileLinks(oldName, newName, file.Sökväg);
            }

            public (bool Success, string Message) RenameOriginal(string newName)
            {
                if (CurrentFile == null)
                    return (false, "No file is selected.");

                if (string.IsNullOrWhiteSpace(newName))
                    return (false, "Please enter a file name.");

                if (!CurrentFile.IsLocal())
                    return (false, "Only available for files stored on C:\\");

                if (CurrentFile.IsTopLevel)
                    newName = EnsureUniqueName(newName, CurrentFile);

                string oldName = CurrentFile.Namn;
                string oldPath = CurrentFile.Sökväg;

                if (string.Equals(oldName, newName, StringComparison.Ordinal))
                    return (true, string.Empty);

                string extension = System.IO.Path.GetExtension(oldPath);
                string? directory = System.IO.Path.GetDirectoryName(oldPath);
                if (string.IsNullOrEmpty(directory))
                    return (false, "Could not determine the file directory for rename.");

                string newPath = System.IO.Path.Combine(directory, newName + extension);

                try
                {
                    System.IO.File.Move(oldPath, newPath);
                }
                catch (Exception ex)
                {
                    Utils.ErrorLogger.Log(ex, nameof(RenameOriginal));
                    return (false, $"Could not rename file. {ex.Message}");
                }

                CurrentFile.Sökväg = newPath;
                CurrentFile.Namn = newName;
                UpdateFileLinks(oldName, newName, newPath);
                MarkDirty();
                return (true, string.Empty);
            }

            public void ReplaceFilePath(string newPath, bool fileExists)
            {
                if (CurrentFile == null || string.IsNullOrWhiteSpace(newPath))
                    return;

                bool wasGroup = CurrentFile.IsGroup;
                string oldName = CurrentFile.Namn;
                string newName = System.IO.Path.GetFileNameWithoutExtension(newPath);
                if (CurrentFile.IsTopLevel)
                    newName = EnsureUniqueName(newName, CurrentFile);
                CurrentFile.Sökväg = newPath;
                CurrentFile.Namn = newName;
                CurrentFile.IsFileMissing = !fileExists;

                UpdateFileLinks(oldName, newName, newPath);

                // Clear group status — the file now has a real path
                if (wasGroup)
                {
                    CurrentFile.IsGroup = false;
                    RefreshChildrenFromParent(CurrentFile, syncCategory: true);
                    RefreshHierarchyState();
                    NotifyCurrentSelectionStructureChanged();
                }

                // Clear any stale sync-folder metadata. A file with a newly
                // assigned path is no longer managed by the sync system.
                if (CurrentFile.IsFromFolder)
                {
                    CurrentFile.IsFromFolder = false;
                    CurrentFile.SyncFolder = null;
                }

                MarkDirty();
            }


            public void MoveSelectedFiles(ProjectData project)
            {
                if (project == null) return;

                foreach (FileData file in CurrentFiles.ToList())
                {
                    // Skip appended files — they move with their parent
                    if (file.IsChild) continue;

                    // Groups and synced files are not allowed to move between projects.
                    // CanMoveSelectedFiles already enforces this, but guard here as well
                    // so a direct call can't bypass the check.
                    if (file.IsGroup || file.IsFromFolder) continue;

                    var children = CurrentProject!.GetChildren(file);

                    // If any child is from a sync folder, skip the whole parent —
                    // moving it would silently orphan the source folder's baseline.
                    if (children.Any(c => c.IsFromFolder)) continue;

                    if (!project.StoredFiles.Contains(file))
                    {
                        // Move children along with the parent
                        foreach (var child in children)
                        {
                            CurrentProject.StoredFiles.Remove(child);
                            if (string.IsNullOrEmpty(child.Filtyp)) child.Filtyp = NEW_TYPE;
                            child.Uppdrag = project.Namn;
                            project.StoredFiles.Add(child);
                        }

                        CurrentProject.StoredFiles.Remove(file);
                        if (string.IsNullOrEmpty(file.Filtyp)) file.Filtyp = NEW_TYPE;
                        file.Uppdrag = project.Namn;
                        project.StoredFiles.Add(file);
                    }
                }

                CurrentProject!.WireParentReferences();
                project.WireParentReferences();
                CurrentProject!.RefreshHasChildren();
                CurrentProject!.SetFiletypeList();
                project.RefreshHasChildren();
                project.SetFiletypeList();
                UpdateFilter();
                SignalTreeViewUpdate();
                MarkDirty();
            }


            public void WatermarkFiles(string text = "Arbetskopia")
            {
                if (CurrentFile == null || !CurrentFile.IsValidPdf())
                {
                    return;
                }

                string date = DateTime.Today.ToString("yyyy-MM-dd");
                string? folder = System.IO.Path.GetDirectoryName(CurrentFile.Sökväg);
                if (string.IsNullOrEmpty(folder)) return;
                string outputPath = folder + "\\" + text + " " + date;

                System.IO.Directory.CreateDirectory(outputPath);

                foreach (FileData file in CurrentFiles)
                {

                    if (file.IsValidPdf())
                    {
                        string outputFilePath = outputPath + "\\" + file.Namn + "_" + text + "_" + date + ".pdf";

                        if (!IsFileInUse(outputFilePath))
                        {
                            PdfDocument pdfDoc = new PdfDocument(new PdfReader(file.Sökväg), new PdfWriter(outputFilePath));

                            PdfFont font = PdfFontFactory.CreateFont(FontProgramFactory.CreateFont(StandardFonts.HELVETICA));
                            Document document = new Document(pdfDoc);
                            iText.Kernel.Geom.Rectangle pageSize;

                            PdfCanvas canvas;
                            int n = pdfDoc.GetNumberOfPages();
                            for (int i = 1; i <= n; i++)
                            {
                                PdfPage page = pdfDoc.GetPage(i);
                                page.NewContentStreamBefore();
                                pageSize = page.GetPageSize();
                                float fontSize = pageSize.GetWidth() / 10;

                                canvas = new PdfCanvas(page);

                                Paragraph paragraph = new Paragraph(text).SetFont(font).SetFontSize(fontSize).SetFontColor(ColorConstants.GRAY).SetOpacity(0.5f);
                                paragraph.SetMultipliedLeading(0.5f);
                                paragraph.Add(Environment.NewLine);
                                paragraph.Add(new Paragraph(date).SetFont(font).SetFontSize(fontSize / 2).SetFontColor(ColorConstants.GRAY).SetOpacity(0.5f));

                                iText.Layout.Canvas canvasWatermark2 = new iText.Layout.Canvas(canvas, pdfDoc.GetDefaultPageSize()).ShowTextAligned(paragraph, pageSize.GetWidth() / 2, pageSize.GetHeight() / 2, 1, TextAlignment.CENTER, VerticalAlignment.MIDDLE, 120);
                            }
                            pdfDoc.Close();
                        }
                    }
                }
            }

            public static bool IsFileInUse(string filePath)
            {
                if (System.IO.File.Exists(filePath) == false)
                {
                    return false;
                }
                else
                {
                    try
                    {
                        // Try opening the file with read-write access and an exclusive lock
                        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            // If we can open it, the file isn't in use
                        }
                    }
                    catch (IOException)
                    {
                        // IOException indicates the file is in use
                        return true;
                    }

                    // If no exception was thrown, the file is not in use
                    return false;
                }
            }
        }
    }
