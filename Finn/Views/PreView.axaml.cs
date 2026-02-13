using Finn.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MuPDFCore.MuPDFRenderer;
using System.ComponentModel;

namespace Finn.Views;

public partial class PreView : UserControl
{
    public PreView()
    {
        InitializeComponent();

        ScrollSlider.AddHandler(Slider.ValueChangedEvent, PageNrSlider);
        ScrollSliderSecondary.AddHandler(Slider.ValueChangedEvent, SecondaryPageNrSlider);
        PreviewGrid.AddHandler(Grid.SizeChangedEvent, PreviewSizeChanged);

        this.AddHandler(LoadedEvent, InitSetup);
    }

    public MainViewModel ctx = null;
    public PreviewViewModel pwr = null;
    public RotateTransform rotation = new RotateTransform(0);
    private bool ZoomMode = false;

    public void InitSetup(object sender, RoutedEventArgs e)
    {
        ctx = (MainViewModel)this.DataContext;
        pwr = ctx.PreviewVM;

        pwr.PropertyChanged += OnBindingPwr;

        SetRenderer();

        if (Avalonia.Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += (_, _) =>
            {
                var color = ctx.Storage.General.DarkMode
                    ? ctx.Storage.General.Color1
                    : ctx.Storage.General.Color3;
                pwr.UpdateThemeRegionColor(color);
            };
        }
    }

    public void OnBindingPwr(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "SearchMode") { SetSearchFocus(); }
    }


    public void SetRenderer()
    {
        MuPDFRenderer.ActivateLinks = false;
        MuPDFRenderer.DrawLinks = false;

        MuPDFRendererSecondary.ActivateLinks = false;
        MuPDFRendererSecondary.DrawLinks = false;

        ctx.PreviewVM.GetRenderControl(MuPDFRenderer, MuPDFRendererSecondary);
    }

    private void OnSeachRegex(object sender, RoutedEventArgs e)
    {
        string text = SearchRegex.Text;
        pwr.Search(text);
    }

    private void SetSearchFocus()
    {
        SearchRegex.Clear();
        SearchRegex.Focus();
    }

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        if (pwr.SearchBusy)
        {
            pwr.StopSearch();
        }
        else
        {
            pwr.ClearSearch();
            SearchRegex.Clear();
        }
    }

    private void OnStartSearhRegex(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSeachRegex(null, null);
        }
    }

    private void PageNrSlider(object sender, RoutedEventArgs e)
    {
        if (ScrollSlider.IsFocused)
        {
            if ((int)ScrollSlider.Value - 1 != pwr.RequestPage1)
            {
                pwr.RequestPage1 = (int)ScrollSlider.Value - 1;
            }
        }
    }

    private void SecondaryPageNrSlider(object sender, RoutedEventArgs e)
    {
        if (ScrollSliderSecondary.IsFocused)
        {
            if ((int)ScrollSliderSecondary.Value - 1 != pwr.RequestPage2)
            {
                pwr.RequestPage2 = (int)ScrollSliderSecondary.Value - 1;
            }
        }
    }


    private void PreviewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (true)//previewMode)
        {
            ResetView(null, null);
        }
    }

    private void ResetView(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.Contain();
        MuPDFRendererSecondary.Contain();
    }

    public void RotateRight(object sender, RoutedEventArgs e)
    {
        pwr.Rotation = pwr.Rotation + 90;
        MuPDFRenderer.UpdateLayout();
        MuPDFRenderer.Contain();
    }

    public void RotateLeft(object sender, RoutedEventArgs e)
    {
        pwr.Rotation = pwr.Rotation - 90;
        MuPDFRenderer.UpdateLayout();
        MuPDFRenderer.Contain();
    }

    public void RotateNull()
    {
        pwr.Rotation = 0;
        MuPDFRenderer.UpdateLayout();
        MuPDFRenderer.Contain();
    }

    private void ModifiedControlPointerWheelChanged(object sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ZoomMode = true;
        }
        else
        {
            ZoomMode = false;
        }

        PDFRenderer currentSender = (PDFRenderer)sender;

        bool secondPage = false;

        if (currentSender.Name.ToString() == "MuPDFRendererSecondary")
        {
            secondPage = true;
        }

        MuPDFRenderer.ZoomEnabled = ZoomMode;
        MuPDFRendererSecondary.ZoomEnabled = ZoomMode;

        if (!ZoomMode && pwr.Pagecount > 0)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Avalonia.Vector mode = e.Delta;

                if (mode.Y > 0)
                {
                    pwr.PrevPage(secondPage);
                }

                if (mode.Y < 0)
                {
                    pwr.NextPage(secondPage);
                }
            }
        }
    }
}