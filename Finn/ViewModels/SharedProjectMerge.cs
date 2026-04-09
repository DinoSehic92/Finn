using Finn.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Finn.ViewModels;

/// <summary>
/// Per-file merge helpers for shared project pull operations.
/// Default behavior: server value wins (overwrites local).
/// When <c>keepLocal</c> is true, the local value is preserved instead.
/// </summary>
internal static class SharedProjectMerge
{
    /// <summary>
    /// Decides whether a specific (file, category) should keep the local value.
    /// </summary>
    public static bool ShouldKeepLocal(HashSet<(string File, string Category)>? keepLocal,
        string fileName, string category)
    {
        return keepLocal != null && keepLocal.Contains((fileName, category));
    }

    /// <summary>
    /// Applies file metadata from server to local.
    /// Default: server overwrites. KeepLocal: no change.
    /// Returns true if any field changed.
    /// </summary>
    public static bool MergeFileMetadata(FileData local, FileData server, bool keepLocal)
    {
        if (keepLocal) return false;

        bool changed = false;
        if (local.Sökväg != server.Sökväg) { local.Sökväg = server.Sökväg; changed = true; }
        if (local.Filtyp != server.Filtyp) { local.Filtyp = server.Filtyp; changed = true; }
        if (local.CurrentVersion != server.CurrentVersion) { local.CurrentVersion = server.CurrentVersion; changed = true; }
        if (local.Tagg != server.Tagg) { local.Tagg = server.Tagg; changed = true; }
        if (local.Färg != server.Färg) { local.Färg = server.Färg; changed = true; }
        return changed;
    }

    /// <summary>
    /// Merges note from server into local file.
    /// Default: server overwrites. KeepLocal: no change.
    /// </summary>
    public static bool MergeNote(FileData local, FileData server, bool keepLocal)
    {
        if (keepLocal) return false;
        if (local.Note != server.Note)
        {
            local.Note = server.Note;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Merges annotation layers from server into local file.
    /// Default: server overwrites. KeepLocal: no change.
    /// </summary>
    public static bool MergeAnnotations(FileData local, FileData server, bool keepLocal)
    {
        if (keepLocal) return false;

        local.AnnotationLayers = new ObservableCollection<AnnotationLayer>(server.AnnotationLayers);
        foreach (var layer in local.AnnotationLayers)
            layer.RecalculateCounts();
        return true;
    }

    /// <summary>
    /// Merges bookmarks from server into local file.
    /// Default: server overwrites. KeepLocal: no change.
    /// </summary>
    public static bool MergeBookmarks(FileData local, FileData server, bool keepLocal)
    {
        if (keepLocal) return false;

        if (server.FavPages == null || server.FavPages.Count == 0)
        {
            if (local.FavPages?.Count > 0)
            {
                local.FavPages.Clear();
                return true;
            }
            return false;
        }

        local.FavPages = new ObservableCollection<PageData>(server.FavPages);
        return true;
    }

    /// <summary>
    /// Merges other file attachments from server into local file.
    /// Default: server overwrites. KeepLocal: no change.
    /// </summary>
    public static bool MergeOtherFiles(FileData local, FileData server, bool keepLocal)
    {
        if (keepLocal) return false;

        if (server.OtherFiles == null || server.OtherFiles.Count == 0)
        {
            if (local.OtherFiles?.Count > 0)
            {
                local.OtherFiles.Clear();
                return true;
            }
            return false;
        }

        if (local.OtherFiles != null)
            local.OtherFiles.Clear();
        else
            local.OtherFiles = new();
        foreach (var item in server.OtherFiles)
            local.OtherFiles.Add(item);
        return true;
    }

    /// <summary>
    /// Merges file versions from server into local file.
    /// Default: server overwrites. KeepLocal: no change.
    /// </summary>
    public static bool MergeVersions(FileData local, FileData server, bool keepLocal)
    {
        if (keepLocal) return false;

        if (server.Versions == null || server.Versions.Count == 0)
        {
            if (local.Versions?.Count > 0)
            {
                local.Versions.Clear();
                return true;
            }
            return false;
        }

        local.Versions = new ObservableCollection<FileVersionData>(server.Versions);
        return true;
    }
}
