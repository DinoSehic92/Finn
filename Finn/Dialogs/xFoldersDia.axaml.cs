using Finn.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.IO;
using System.Xml.XPath;

namespace Finn.Dialog;

public partial class xFoldersDia : Window
{
    public xFoldersDia()
    {
        InitializeComponent();

        FolderGrid.AddHandler(DragDrop.DropEvent, OnDrop);

        KeyDown += CloseKey;

    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;

        var items = e.Data.GetFiles();

        foreach (var item in items)
        {
            string path = item.Path.LocalPath;

            if (Directory.Exists(path))
            {
                ctx.CurrentFile.AppendedFolders.Add(new Model.FolderData()
                {
                    Name = Path.GetFileName(Path.GetDirectoryName(path)),
                    Path = path
                });
            }
        }
    }



    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }

}