
using Finn.Model;
using Finn.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.IO;
using System.Reflection;

namespace Finn.Dialog;

public partial class xProgDia : Window
{
    public xProgDia()
    {
        InitializeComponent();

        Loaded += SetupInfo;
    }

    private void SetupInfo(object sender, RoutedEventArgs e)
    {

        MainViewModel ctx = (MainViewModel)this.DataContext;

        CompiledDate.Content = File.GetLastWriteTime(Assembly.GetExecutingAssembly().CodeBase.Substring(8));

        LastSaved.Content = File.GetLastWriteTime("C:\\FIlePathManager\\Projects.json");


        int nrFiles = 0;

        foreach (ProjectData project in ctx.Storage.StoredProjects)
        {
            nrFiles = nrFiles + project.StoredFiles.Count;
        }

        NrFiles.Content = nrFiles;


    }


}