using Finn.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Finn.ViewModels;

/// <summary>
/// Per-file merge helpers for shared project pull operations.
/// Extracted from MainViewModel.Projects.cs to keep the shared region manageable.
/// </summary>
internal static class SharedProjectMerge
{
    /// <summary>
    /// Decides whether a specific (file, category) should use server-replace
    /// instead of the default additive merge.
    /// </summary>
    public static bool ShouldAccept(HashSet<(string File, string Category)>? accepted,
        string fileName, string category)
    {
        return accepted != null && accepted.Contains((fileName, category));
    }

    /// <summary>
    /// Merges or replaces the note on a local file from the server file.
    /// Default: server fills empty local note. Accept-incoming: server overwrites.
    /// </summary>
    public static bool MergeNote(FileData local, FileData server, bool acceptIncoming)
    {
        if (acceptIncoming && local.Note != server.Note)
        {
            local.Note = server.Note;
            return true;
        }
        if (string.IsNullOrEmpty(local.Note) && !string.IsNullOrEmpty(server.Note))
        {
            local.Note = server.Note;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Merges or replaces annotation layers from server into local file.
    /// Default: additive (server-only layers/pages added). Accept-incoming: full replace.
    /// </summary>
    public static bool MergeAnnotations(FileData local, FileData server, bool acceptIncoming)
    {
        if (server.AnnotationLayers.Count == 0 && !acceptIncoming) return false;

        if (acceptIncoming)
        {
            local.AnnotationLayers = new ObservableCollection<AnnotationLayer>(server.AnnotationLayers);
            foreach (var layer in local.AnnotationLayers)
                layer.RecalculateCounts();
            return true;
        }

        bool changed = false;
        var localLayerMap = new Dictionary<string, AnnotationLayer>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in local.AnnotationLayers)
            localLayerMap.TryAdd(l.Name, l);

        foreach (var serverLayer in server.AnnotationLayers)
        {
            if (serverLayer.TotalCount == 0) continue;

            if (!localLayerMap.TryGetValue(serverLayer.Name, out var localLayer))
            {
                serverLayer.RecalculateCounts();
                local.AnnotationLayers.Add(serverLayer);
                changed = true;
            }
            else
            {
                bool layerChanged = false;
                layerChanged |= MergePageDict(localLayer.PageStrokes, serverLayer.PageStrokes);
                layerChanged |= MergePageDict(localLayer.PageShapes, serverLayer.PageShapes);
                layerChanged |= MergePageDict(localLayer.PageTexts, serverLayer.PageTexts);
                layerChanged |= MergePageDict(localLayer.PageMeasurements, serverLayer.PageMeasurements);
                if (layerChanged) localLayer.RecalculateCounts();
                changed |= layerChanged;
            }
        }
        return changed;
    }

    /// <summary>
    /// Merges or replaces bookmarks from server into local file.
    /// Default: additive (server-only pages added). Accept-incoming: full replace.
    /// </summary>
    public static bool MergeBookmarks(FileData local, FileData server, bool acceptIncoming)
    {
        if (server.FavPages == null || server.FavPages.Count == 0)
        {
            if (acceptIncoming && local.FavPages?.Count > 0)
            {
                local.FavPages.Clear();
                return true;
            }
            return false;
        }

        if (acceptIncoming)
        {
            local.FavPages = new ObservableCollection<PageData>(server.FavPages);
            return true;
        }

        var localPages = new HashSet<int>(local.FavPages?.Select(p => p.PageNr) ?? []);
        bool changed = false;
        foreach (var serverBookmark in server.FavPages)
        {
            if (!localPages.Contains(serverBookmark.PageNr))
            {
                local.FavPages ??= new();
                local.FavPages.Add(serverBookmark);
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// Merges or replaces other file attachments from server into local file.
    /// Default: additive (server-only names added). Accept-incoming: full replace.
    /// </summary>
    public static bool MergeOtherFiles(FileData local, FileData server, bool acceptIncoming)
    {
        if (server.OtherFiles == null || server.OtherFiles.Count == 0)
        {
            if (acceptIncoming && local.OtherFiles?.Count > 0)
            {
                local.OtherFiles.Clear();
                return true;
            }
            return false;
        }

        if (acceptIncoming)
        {
            if (local.OtherFiles != null)
                local.OtherFiles.Clear();
            else
                local.OtherFiles = new();
            foreach (var item in server.OtherFiles)
                local.OtherFiles.Add(item);
            return true;
        }

        var localNames = new HashSet<string>(
            local.OtherFiles?.Select(o => o.Name ?? "") ?? [],
            StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (var serverOther in server.OtherFiles)
        {
            if (!localNames.Contains(serverOther.Name ?? ""))
            {
                local.OtherFiles ??= new();
                local.OtherFiles.Add(serverOther);
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// Merges or replaces file versions from server into local file.
    /// Default: additive (server-only paths added). Accept-incoming: full replace.
    /// </summary>
    public static bool MergeVersions(FileData local, FileData server, bool acceptIncoming)
    {
        if (server.Versions == null || server.Versions.Count == 0)
        {
            if (acceptIncoming && local.Versions?.Count > 0)
            {
                local.Versions.Clear();
                return true;
            }
            return false;
        }

        if (acceptIncoming)
        {
            local.Versions = new ObservableCollection<FileVersionData>(server.Versions);
            return true;
        }

        var localPaths = new HashSet<string>(
            local.Versions?.Select(v => v.Sökväg ?? "") ?? [],
            StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (var serverVersion in server.Versions)
        {
            if (!localPaths.Contains(serverVersion.Sökväg ?? ""))
            {
                local.Versions ??= new();
                local.Versions.Add(serverVersion);
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// Overwrites simple metadata fields on a local file from the server file.
    /// Called when Accept-incoming is checked for the "File" category.
    /// </summary>
    public static void AcceptFileMetadata(FileData local, FileData server)
    {
        local.Sökväg = server.Sökväg;
        local.Filtyp = server.Filtyp;
        local.CurrentVersion = server.CurrentVersion;
        local.Tagg = server.Tagg;
        local.Färg = server.Färg;
    }

    /// <summary>
    /// Adds page entries from server that don't exist in local.
    /// </summary>
    private static bool MergePageDict<T>(Dictionary<int, List<T>> local, Dictionary<int, List<T>> server)
    {
        bool changed = false;
        foreach (var (page, items) in server)
        {
            if (!local.ContainsKey(page) && items.Count > 0)
            {
                local[page] = items;
                changed = true;
            }
        }
        return changed;
    }
}
