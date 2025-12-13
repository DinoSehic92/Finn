using Finn.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.IO;

namespace Finn.Dialog;

public partial class xInfoDia : Window
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

            FileInfo fileInfo = new FileInfo(path);

            NameLabel.Content = fileInfo.Name;
            CreationLabel.Content = fileInfo.CreationTime;
            ReadLabel.Content = fileInfo.LastAccessTime;
            WriteLabel.Content = fileInfo.LastWriteTime;
            SizeLabel.Content = Math.Round((decimal)fileInfo.Length / 1000000, 2) + " Mb";
        }
    }


}