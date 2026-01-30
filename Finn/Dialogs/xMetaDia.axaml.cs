using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.Model;
using Finn.ViewModels;
using Finn.Views;
using System.Linq;

namespace Finn.Dialog;

public partial class xMetaDia : TemplateWindow
{
    public xMetaDia()
    {
        InitializeComponent();

        MainGrid.AddHandler(DataGrid.LoadedEvent, SetupMetadata);

        KeyDown += CloseKey;

    }

    private void SetupMetadata(object sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;

        string val1 = ctx.CurrentFile.Namn;
        if (ctx.CurrentFiles.Where(x => x.Namn == val1).Count() == ctx.CurrentFiles.Count())
        { 
            FileNameInp.Text = val1; 
        }

        string val2 = ctx.CurrentFile.Filtyp;
        if (ctx.CurrentFiles.Where(x => x.Filtyp == val2).Count() == ctx.CurrentFiles.Count())
        { 
            FileTypeInp.Text = val2; 
        }

        string val3 = ctx.CurrentFile.Uppdrag;
        if (ctx.CurrentFiles.Where(x => x.Uppdrag == val3).Count() == ctx.CurrentFiles.Count())
        { 
            ProjectInp.Text = val3; }

        string val4 = ctx.CurrentFile.Tagg;
        if (ctx.CurrentFiles.Where(x => x.Tagg == val4).Count() == ctx.CurrentFiles.Count())
        { 
            TagInp.Text = val4; }

        string val5 = ctx.CurrentFile.Färg;
        if (ctx.CurrentFiles.Where(x => x.Färg == val5).Count() == ctx.CurrentFiles.Count())
        { 
            ColorInp.Text = val5; }

        string val6 = ctx.CurrentFile.Handling; 
        HandlingCheck.IsChecked = false;
        if (ctx.CurrentFiles.Where(x => x.Handling == val6).Count() == ctx.CurrentFiles.Count())
        { 
            HandlingInp.Text = val6;

            if (val6 != null && val6 != "")
            {
                HandlingCheck.IsChecked = true;
            }
        }

        string val7 = ctx.CurrentFile.Status; 
        StatusCheck.IsChecked = false;
        if (ctx.CurrentFiles.Where(x => x.Status == val7).Count() == ctx.CurrentFiles.Count())
        {
            StatusInp.Text = val7; 
            if (val7 != null && val7 != "")
            {
                StatusCheck.IsChecked = true;
            }
        }

        string val8 = ctx.CurrentFile.Datum; 
        DatumCheck.IsChecked = false;
        if (ctx.CurrentFiles.Where(x => x.Datum == val8).Count() == ctx.CurrentFiles.Count())
        { 
            DatumInp.Text = val8; 
            if (val8 != null && val8 != "")
            {
                DatumCheck.IsChecked = true;
            }
        }

        string val9 = ctx.CurrentFile.Ritningstyp; 
        RitningCheck.IsChecked = false;
        if (ctx.CurrentFiles.Where(x => x.Ritningstyp == val9).Count() == ctx.CurrentFiles.Count())
        { 
            RitningInp.Text = val9; 
            if (val9 != null && val9 != "")
            {
                RitningCheck.IsChecked = true;
            }
        }

        string val10 = ctx.CurrentFile.Beskrivning1; 
        Besk1Check.IsChecked = false;
        if (ctx.CurrentFiles.Where(x => x.Beskrivning1 == val10).Count() == ctx.CurrentFiles.Count())
        { 
            Besk1Inp.Text = val10; 
            if (val10 != null && val10 != "")
            {
                Besk1Check.IsChecked = true;
            }
        }

        string val11 = ctx.CurrentFile.Beskrivning2; 
        Besk2Check.IsChecked = false;
        if (ctx.CurrentFiles.Where(x => x.Beskrivning2 == val11).Count() == ctx.CurrentFiles.Count())
        { 
            Besk2Inp.Text = val11; 
            if (val11 != null && val11 != "")
            {
                Besk2Check.IsChecked = true;
            }
        }

        string val12 = ctx.CurrentFile.Beskrivning3; 
        Besk3Check.IsChecked = false;
        if (ctx.CurrentFiles.Where(x => x.Beskrivning3 == val12).Count() == ctx.CurrentFiles.Count())
        { 
            Besk3Inp.Text = val12; 
            if (val12 != null && val12 != "")
            {
                Besk3Check.IsChecked = true;
            }
        }

        string val13 = ctx.CurrentFile.Beskrivning4; 
        Besk4Check.IsChecked = false;
        if (ctx.CurrentFiles.Where(x => x.Beskrivning4 == val13).Count() == ctx.CurrentFiles.Count())
        { 
            Besk4Inp.Text = val13; 
            if (val13 != null && val13 != "")
            {
                Besk4Check.IsChecked = true;
            }
        }

        string val14 = ctx.CurrentFile.Revidering; 
        RevCheck.IsChecked = false;
        if (ctx.CurrentFiles.Where(x => x.Revidering == val14).Count() == ctx.CurrentFiles.Count())
        { 
            RevInp.Text = val14; 
            if (val14 != null && val14 != "")
            {
                RevCheck.IsChecked = true;
            }
        }

        string val15 = ctx.CurrentFile.Sökväg;
        if (ctx.CurrentFiles.Where(x => x.Sökväg == val15).Count() == ctx.CurrentFiles.Count())
        { 
            PathInp.Text = val15; 
        }

    }

    private void OnEditProject(object sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;

        foreach (FileData file in ctx.CurrentFiles)
        {
            if(HandlingCheck.IsChecked == true)
            {
                file.Handling = HandlingInp.Text;
            }

            if (StatusCheck.IsChecked == true)
            {
                file.Status = StatusInp.Text;
            }

            if (DatumCheck.IsChecked == true)
            {
                file.Datum = DatumInp.Text;
            }

            if (RitningCheck.IsChecked == true)
            {
                file.Ritningstyp = RitningInp.Text;
            }

            if (Besk1Check.IsChecked == true)
            {
                file.Beskrivning1 = Besk1Inp.Text;
            }

            if (Besk2Check.IsChecked == true)
            {
                file.Beskrivning2 = Besk2Inp.Text;
            }

            if (Besk3Check.IsChecked == true)
            {
                file.Beskrivning3 = Besk3Inp.Text;
            }

            if (Besk4Check.IsChecked == true)
            {
                file.Beskrivning4 = Besk4Inp.Text;
            }

            if (RevCheck.IsChecked == true)
            {
                file.Revidering = RevInp.Text;
            }
        }
        this.Close();
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }

}