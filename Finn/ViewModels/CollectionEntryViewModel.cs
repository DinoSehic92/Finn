using CommunityToolkit.Mvvm.ComponentModel;
using Finn.Model;
using System.ComponentModel;
using System.Collections.ObjectModel;

namespace Finn.ViewModels;

public sealed class CollectionEntryViewModel : ObservableObject
{
    public CollectionEntryViewModel(FileData file, FileVersionData? version = null)
    {
        File = file;
        Version = version;
        File.PropertyChanged += File_PropertyChanged;
        if (Version != null)
            Version.PropertyChanged += Version_PropertyChanged;
    }

    public FileData File { get; }
    public FileVersionData? Version { get; }
    public bool IsVersion => Version != null;
    public string Id => Version == null ? File.Id : GetVersionId(File.Id, Version.Id);
    public string DisplayName => Version == null ? File.Namn : $"{File.Namn} — {Version.Label}";
    public string Path => Version?.Sökväg ?? File.Sökväg;
    public ObservableCollection<string> PartOfCollections => Version?.PartOfCollections ?? File.PartOfCollections;

    public static string GetVersionId(string fileId, string versionId) => $"version:{fileId}:{versionId}";

    public void Detach()
    {
        File.PropertyChanged -= File_PropertyChanged;
        if (Version != null)
            Version.PropertyChanged -= Version_PropertyChanged;
    }

    private void File_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileData.Namn))
            OnPropertyChanged(nameof(DisplayName));
        else if (e.PropertyName == nameof(FileData.Sökväg))
            OnPropertyChanged(nameof(Path));
    }

    private void Version_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileVersionData.Label) or nameof(FileVersionData.Sökväg))
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Path));
        }
    }
}
