using Avalonia.Data.Converters;
using Finn.Model;
using FluentIcons.Common;
using System;
using System.Globalization;

namespace Finn.Converters;

/// <summary>
/// Converts a <see cref="SyncFolderMode"/> value to the corresponding
/// <see cref="Symbol"/> for use with <c>ic:SymbolIcon</c> in the folder grid.
/// </summary>
public class FolderModeIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is SyncFolderMode mode ? mode switch
        {
            SyncFolderMode.ProjectFiles => Symbol.Folder,
            SyncFolderMode.AttachedFiles => Symbol.Attach,
            SyncFolderMode.OtherFiles => Symbol.Document,
            SyncFolderMode.VersionDelivery => Symbol.History,
            _ => Symbol.Folder
        } : Symbol.Folder;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
