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
    /// Builds a stable comparison key for shared merge operations.
    /// Prefer persisted path-based identity; fall back to structure-aware
    /// metadata for groups/placeholders that may not have a real file path.
    /// </summary>
    public static string GetFileKey(FileData file)
    {
        if (!string.IsNullOrWhiteSpace(file.Sökväg))
            return "PATH|" + file.Sökväg.Trim().ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(file.OriginalPath))
            return "ORIGINAL|" + file.OriginalPath.Trim().ToUpperInvariant();

        return string.Join("|",
            "META",
            file.Namn.Trim().ToUpperInvariant(),
            (file.ParentNamn ?? string.Empty).Trim().ToUpperInvariant(),
            (file.Filtyp ?? string.Empty).Trim().ToUpperInvariant(),
            file.IsGroup ? "GROUP" : "FILE");
    }

    /// <summary>
    /// Decides whether a specific (file, category) should keep the local value.
    /// </summary>
    public static bool ShouldKeepLocal(HashSet<(string File, string Category)>? keepLocal,
        string fileKey, string category)
    {
        return keepLocal != null && keepLocal.Contains((fileKey, category));
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
        if (local.ParentNamn != server.ParentNamn) { local.ParentNamn = server.ParentNamn; changed = true; }
        if (local.IsGroup != server.IsGroup) { local.IsGroup = server.IsGroup; changed = true; }
        if (local.Sökväg != server.Sökväg) { local.Sökväg = server.Sökväg; changed = true; }
        if (local.Filtyp != server.Filtyp) { local.Filtyp = server.Filtyp; changed = true; }
        if (local.CurrentVersion != server.CurrentVersion) { local.CurrentVersion = server.CurrentVersion; changed = true; }
        if (local.Tagg != server.Tagg) { local.Tagg = server.Tagg; changed = true; }
        if (local.Färg != server.Färg) { local.Färg = server.Färg; changed = true; }
        if (local.Handling != server.Handling) { local.Handling = server.Handling; changed = true; }
        if (local.Status != server.Status) { local.Status = server.Status; changed = true; }
        if (local.Datum != server.Datum) { local.Datum = server.Datum; changed = true; }
        if (local.Ritningstyp != server.Ritningstyp) { local.Ritningstyp = server.Ritningstyp; changed = true; }
        if (local.Beskrivning1 != server.Beskrivning1) { local.Beskrivning1 = server.Beskrivning1; changed = true; }
        if (local.Beskrivning2 != server.Beskrivning2) { local.Beskrivning2 = server.Beskrivning2; changed = true; }
        if (local.Beskrivning3 != server.Beskrivning3) { local.Beskrivning3 = server.Beskrivning3; changed = true; }
        if (local.Beskrivning4 != server.Beskrivning4) { local.Beskrivning4 = server.Beskrivning4; changed = true; }
        if (local.Revidering != server.Revidering) { local.Revidering = server.Revidering; changed = true; }
        if (local.DefaultPage != server.DefaultPage) { local.DefaultPage = server.DefaultPage; changed = true; }
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

        // Skip if layer count and content are identical
        if (local.AnnotationLayers.Count == server.AnnotationLayers.Count)
        {
            bool same = true;
            for (int i = 0; i < local.AnnotationLayers.Count; i++)
            {
                var ll = local.AnnotationLayers[i];
                var sl = server.AnnotationLayers[i];
                if (ll.Name != sl.Name || ll.TotalCount != sl.TotalCount)
                { same = false; break; }
            }
            if (same) return false;
        }

        local.AnnotationLayers = new ObservableCollection<AnnotationLayer>(server.AnnotationLayers.Select(Clone));
        foreach (var layer in local.AnnotationLayers)
            layer.RecalculateCounts();
        return true;
    }

    /// <summary>
    /// Viewer-mode annotation merge: replaces server-owned layers with the
    /// latest server versions while preserving any viewer-local layers
    /// (layers whose name starts with "Viewer:").
    /// </summary>
    public static bool MergeAnnotationsForViewer(FileData local, FileData server)
    {
        // Separate viewer-local layers from server-owned layers
        var viewerLayers = local.AnnotationLayers
            .Where(l => l.Name.StartsWith(ViewerLayerPrefix, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        var currentServerLayers = local.AnnotationLayers
            .Where(l => !l.Name.StartsWith(ViewerLayerPrefix, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Check if server layers actually changed
        if (currentServerLayers.Count == server.AnnotationLayers.Count)
        {
            bool same = true;
            for (int i = 0; i < currentServerLayers.Count; i++)
            {
                var cl = currentServerLayers[i];
                var sl = server.AnnotationLayers[i];
                if (cl.Name != sl.Name || cl.TotalCount != sl.TotalCount)
                { same = false; break; }
            }
            if (same) return false;
        }

        // Rebuild: server layers first, then viewer layers
        var merged = new ObservableCollection<AnnotationLayer>(server.AnnotationLayers.Select(Clone));
        foreach (var layer in merged)
            layer.RecalculateCounts();

        foreach (var vl in viewerLayers)
            merged.Add(vl);

        local.AnnotationLayers = merged;
        return true;
    }

    /// <summary>Prefix used to identify viewer-local annotation layers.</summary>
    internal const string ViewerLayerPrefix = "Viewer:";

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

        local.FavPages = new ObservableCollection<PageData>(server.FavPages.Select(Clone));
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
            local.OtherFiles.Add(Clone(item));
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

        local.Versions = new ObservableCollection<FileVersionData>(server.Versions.Select(Clone));
        return true;
    }

    private static AnnotationLayer Clone(AnnotationLayer source)
    {
        var clone = new AnnotationLayer
        {
            Name = source.Name,
            IsVisible = source.IsVisible,
            IsLocked = source.IsLocked,
            Color = source.Color
        };

        clone.PageStrokes = source.PageStrokes.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(Clone).ToList());

        clone.PageShapes = source.PageShapes.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(Clone).ToList());

        clone.PageTexts = source.PageTexts.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(Clone).ToList());

        clone.PageMeasurements = source.PageMeasurements.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(Clone).ToList());

        clone.RecalculateCounts();
        return clone;
    }

    private static Finn.Controls.InkStroke Clone(Finn.Controls.InkStroke source) => new()
    {
        Points = source.Points.ToList(),
        Color = source.Color,
        Width = source.Width,
        Opacity = source.Opacity,
        IsHighlighter = source.IsHighlighter,
        IsPolyline = source.IsPolyline,
        IsClosed = source.IsClosed,
        DashPattern = source.DashPattern,
        CornerRadius = source.CornerRadius
    };

    private static ShapeAnnotation Clone(ShapeAnnotation source) => new()
    {
        ShapeType = source.ShapeType,
        Start = source.Start,
        End = source.End,
        Color = source.Color,
        StrokeWidth = source.StrokeWidth,
        Opacity = source.Opacity,
        IsFilled = source.IsFilled,
        DashPattern = source.DashPattern,
        CornerRadius = source.CornerRadius
    };

    private static TextAnnotation Clone(TextAnnotation source) => new()
    {
        Position = source.Position,
        Text = source.Text,
        FontSize = source.FontSize,
        Color = source.Color,
        Opacity = source.Opacity,
        FontFamily = source.FontFamily,
        ArrowOrigin = source.ArrowOrigin,
        MaxWidth = source.MaxWidth,
        IsStickyNote = source.IsStickyNote,
        IsLabel = source.IsLabel
    };

    private static MeasurementAnnotation Clone(MeasurementAnnotation source) => new()
    {
        Points = source.Points.ToList(),
        Color = source.Color,
        Scale = source.Scale
    };

    private static PageData Clone(PageData source) => new()
    {
        PageNr = source.PageNr,
        PageName = source.PageName
    };

    private static OtherData Clone(OtherData source) => new()
    {
        IsLink = source.IsLink,
        Name = source.Name,
        Filepath = source.Filepath,
        Type = source.Type,
        IsFromFolder = source.IsFromFolder,
        FromFolder = source.FromFolder,
        SyncFolder = source.SyncFolder,
        IconBytes = source.IconBytes?.ToArray() ?? Array.Empty<byte>()
    };

    private static FileVersionData Clone(FileVersionData source) => new()
    {
        Sökväg = source.Sökväg,
        Label = source.Label,
        AddedDate = source.AddedDate
    };
}
