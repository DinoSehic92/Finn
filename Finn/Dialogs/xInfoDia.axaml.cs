using Avalonia.Controls;
using Avalonia.Interactivity;
using Finn.ViewModels;
using Finn.Views;
using System;
using System.IO;

namespace Finn.Dialog;

public partial class xInfoDia : TemplateWindow
{
    public xInfoDia()
    {
        InitializeComponent();

        Loaded += SetupInfo;
    }

    private void SetupInfo(object sender, RoutedEventArgs e)
    {

        MainViewModel ctx = (MainViewModel)this.DataContext;

        if(ctx.CurrentFile != null)
        {
            string path = ctx.CurrentFile.Sökväg;

            if (File.Exists(path))
            {
                FileInfo fileInfo = new FileInfo(path);

                NameLabel.Content = fileInfo.Name;
                CreationLabel.Content = fileInfo.CreationTime;
                ReadLabel.Content = fileInfo.LastAccessTime;
                WriteLabel.Content = fileInfo.LastWriteTime;
                SizeLabel.Content = Math.Round((decimal)fileInfo.Length / 1000000, 2) + " Mb";
            }
        }
    }


}