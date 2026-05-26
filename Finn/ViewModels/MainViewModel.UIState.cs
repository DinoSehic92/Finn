using Finn.Model;
using Finn.Storage;
using Finn.Utils;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Finn.ViewModels
{
    public partial class MainViewModel
    {
        // ── Shared state bag ──────────────────────────────────────────────────

        /// <summary>
        /// Set to true during <see cref="RestoreUIState"/> to suppress
        /// re-entrant saves triggered by setting CurrentFiles.
        /// </summary>
        private bool _restoringUIState;

        /// <summary>
        /// Prevents any UIState write before <see cref="MarkUIStateReady"/> is called.
        /// The MainViewModel constructor calls SetProject which triggers SaveUIStateAsync;
        /// without this guard that would overwrite UIState.json with default values
        /// before InitializeViewModel has a chance to read and apply the real file.
        /// </summary>
        private bool _uiStateReady;

        /// <summary>
        /// Called by App.axaml.cs after UIState.json has been loaded and applied.
        /// Until this is called, all Save* methods are no-ops.
        /// </summary>
        public void MarkUIStateReady() => _uiStateReady = true;

        /// <summary>
        /// In-memory mirror of UIState.json.  Updated incrementally as the
        /// user interacts; persisted to disk by <see cref="SaveUIStateAsync"/>.
        /// </summary>
        public UIStateStorage CurrentUIState { get; set; } = new();

        /// <summary>
        /// Cancellation token source for the debounced save. Replaced on every
        /// <see cref="SaveUIStateAsync"/> call to cancel any pending write.
        /// </summary>
        private CancellationTokenSource _saveUICts = new();

        // ── Disk I/O ──────────────────────────────────────────────────────────

        /// <summary>
        /// Debounced async save: cancels any pending write, snapshots state on
        /// the UI thread, then waits 400 ms before writing. This means rapid
        /// toggling (e.g. checking several tray panels quickly) produces only
        /// one disk write instead of one per toggle, and ordering is safe.
        /// </summary>
        public void SaveUIStateAsync()
        {
            if (!_uiStateReady) return;

            // Cancel the previous pending write (no-op if it already completed).
            var old = Interlocked.Exchange(ref _saveUICts, new CancellationTokenSource());
            old.Cancel();
            old.Dispose();

            // Snapshot state on the UI thread so the background task works
            // with a stable string, not a shared mutable object.
            UI.ToUIState(CurrentUIState);
            if (currentProject != null)
                CurrentUIState.LastActiveProject = currentProject.Namn;

            string json = JsonHelper.Serialize(CurrentUIState);
            var path    = Path.Combine(SavePath, "UIState.json");
            var token   = _saveUICts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(400, token).ConfigureAwait(false);
                    await File.WriteAllTextAsync(path, json, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { /* superseded by a newer save — intentional */ }
                catch (Exception ex)
                {
                    ErrorLogger.Log(ex, "SaveUIStateAsync");
                }
            });
        }

        /// <summary>
        /// Synchronous version used by the window-close path where the process
        /// is about to exit and an async write would be abandoned.
        /// Cancels any pending debounced write first so the flush is always final.
        /// </summary>
        public void SaveUIStateSync()
        {
            if (!_uiStateReady) return;

            // Cancel any pending debounced write — we are about to do the final flush.
            _saveUICts.Cancel();

            try
            {
                UI.ToUIState(CurrentUIState);
                if (currentProject != null)
                    CurrentUIState.LastActiveProject = currentProject.Namn;

                string json = JsonHelper.Serialize(CurrentUIState);
                File.WriteAllText(Path.Combine(SavePath, "UIState.json"), json);
            }
            catch (Exception ex)
            {
                ErrorLogger.Log(ex, "SaveUIStateSync");
            }
        }

        /// <summary>
        /// Loads UIState.json and applies panel/tray flags to the UI viewmodel.
        /// Returns the loaded storage so the caller can also restore per-project state.
        /// Safe to call before projects are loaded (panel flags don't depend on projects).
        /// </summary>
        public static UIStateStorage? LoadUIState()
        {
            try
            {
                string path = Path.Combine(SavePath, "UIState.json");
                if (!File.Exists(path)) return null;
                return JsonHelper.Deserialize<UIStateStorage>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                ErrorLogger.Log(ex, "LoadUIState");
                return null;
            }
        }

        /// <summary>
        /// Applies the loaded UIState to the UI viewmodel (panel/tray flags).
        /// Must be called on the UI thread.
        /// </summary>
        public void ApplyUIStatePanels(UIStateStorage state)
        {
            UI.FromUIState(state);
        }

        // ── Per-project capture / restore ─────────────────────────────────────

        /// <summary>
        /// Snapshots the current project's UI state (type filter, selected file,
        /// group expansion) into <see cref="CurrentUIState"/> and saves to disk.
        /// </summary>
        public void CaptureAndSaveCurrentProjectUIState()
        {
            if (_restoringUIState) return;
            if (currentProject == null) return;
            CaptureProjectUIState(currentProject);
            SaveUIStateAsync();
        }

        /// <summary>
        /// Writes the live state of <paramref name="project"/> into the in-memory bag.
        /// Does NOT save to disk — call <see cref="SaveUIStateAsync"/> afterwards.
        /// </summary>
        public void CaptureProjectUIState(ProjectData project)
        {
            if (!CurrentUIState.Projects.TryGetValue(project.Namn, out var slot))
            {
                slot = new ProjectUIState();
                CurrentUIState.Projects[project.Namn] = slot;
            }

            slot.ActiveType = type;
            slot.SelectedFileName = CurrentFile?.Namn;

            // Snapshot expansion state for every group/parent in this project
            slot.GroupExpansion.Clear();
            foreach (var file in project.StoredFiles)
            {
                if (file.IsGroup || file.HasChildren)
                    slot.GroupExpansion[file.Namn] = file.IsExpanded;
            }
        }

        /// <summary>
        /// Restores the type filter, group expansion, and selected file for
        /// <paramref name="project"/> from the in-memory bag.
        /// Safe to call when no slot exists (no-op).
        /// </summary>
        public void ApplyProjectUIState(ProjectData project)
        {
            ApplyProjectExpansion(project);

            if (!CurrentUIState.Projects.TryGetValue(project.Namn, out var slot))
                return;

            // Apply the type filter (falls back to All Types if no longer valid)
            if (!string.IsNullOrEmpty(slot.ActiveType)
                && project.Filetypes.Contains(slot.ActiveType))
            {
                type = slot.ActiveType;
            }
            else
            {
                type = ALL_TYPES;
            }
        }

        /// <summary>
        /// Restores only group expansion state for <paramref name="project"/>.
        /// Used by <see cref="NavigateTo"/> which supplies its own explicit type.
        /// </summary>
        public void ApplyProjectExpansion(ProjectData project)
        {
            if (!CurrentUIState.Projects.TryGetValue(project.Namn, out var slot))
                return;

            foreach (var file in project.StoredFiles)
            {
                if ((file.IsGroup || file.HasChildren)
                    && slot.GroupExpansion.TryGetValue(file.Namn, out bool expanded))
                {
                    file.IsExpanded = expanded;
                }
            }
        }

        /// <summary>
        /// Called after projects are loaded from disk.  Applies the last active
        /// project and its per-project UI state.
        /// </summary>
        public void RestoreUIState()
        {
            if (CurrentUIState == null) return;

            var target = CurrentUIState.LastActiveProject != null
                ? Storage.StoredProjects.FirstOrDefault(
                      p => p.Namn == CurrentUIState.LastActiveProject)
                : null;

            target ??= Storage.StoredProjects.FirstOrDefault();
            if (target == null) return;

            // Apply expansion + type before navigating so UpdateFilter sees them
            ApplyProjectUIState(target);

            currentProject = target;
            InvalidateAvailableParentsCache();
            UpdateFilter();
            OnPropertyChanged(nameof(CurrentProject));
            OnPropertyChanged(nameof(IsSearchResult));
            OnPropertyChanged(nameof(Type));

            // Restore selected file after the filter is built
            if (CurrentUIState.Projects.TryGetValue(target.Namn, out var slot)
                && slot.SelectedFileName != null)
            {
                var file = FilteredFiles.FirstOrDefault(
                    f => f.Namn == slot.SelectedFileName);
                if (file != null)
                {
                    _restoringUIState = true;
                    try   { CurrentFiles = new System.Collections.Generic.List<FileData> { file }; }
                    finally { _restoringUIState = false; }
                }
            }
        }
    }
}
