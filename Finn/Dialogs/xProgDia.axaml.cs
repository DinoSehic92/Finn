
using Avalonia.Controls;
using Avalonia.Interactivity;
using Finn.Model;
using Finn.ViewModels;
using Finn.Views;
using System.IO;
using System.Reflection;

namespace Finn.Dialogs;

public partial class xProgDia : Window
{
    public xProgDia()
    {
        InitializeComponent();

        Loaded += SetupInfo;
    }

    private void SetupInfo(object? sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext!;

        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        CompiledDate.Content = !string.IsNullOrEmpty(assemblyPath)
            ? File.GetLastWriteTime(assemblyPath)
            : "N/A";

        var projectsPath = System.IO.Path.Combine(MainViewModel.SavePath, "Projects.json");
        LastSaved.Content = File.Exists(projectsPath)
            ? File.GetLastWriteTime(projectsPath)
            : "Not saved";

        int nrFiles = 0;

        foreach (ProjectData project in ctx.Storage.StoredProjects)
        {
            nrFiles = nrFiles + project.StoredFiles.Count;
        }

        NrFiles.Content = nrFiles;

        UpdateCacheLabels(ctx);
    }

    private void UpdateCacheLabels(MainViewModel ctx)
    {
        int count = ctx.PreviewVM.CacheFileCount;
        long bytes = ctx.PreviewVM.CacheTotalBytes;

        var countLabel = this.FindControl<Label>("CacheCountLabel");
        var sizeLabel = this.FindControl<Label>("CacheSizeLabel");

        if (countLabel != null)
            countLabel.Content = count == 0 ? "None" : count.ToString();

        if (sizeLabel != null)
        {
            sizeLabel.Content = bytes switch
            {
                0 => "0 B",
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
                < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
                _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
            };
        }
    }

    private void OnClearCache(object? sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext!;
        ctx.PreviewVM.ClearCache();
        UpdateCacheLabels(ctx);
    }
}