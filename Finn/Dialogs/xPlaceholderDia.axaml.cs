using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Finn.Dialogs;

public partial class xPlaceholderDia : Window
{
    /// <summary>
    /// After the dialog closes, contains the entered name (if confirmed).
    /// Used by callers that need the name without calling AddPlaceholderFile directly.
    /// </summary>
    public string? ResultName { get; private set; }

    public xPlaceholderDia()
    {
        InitializeComponent();

        KeyDown += CloseKey;
        Opened += (_, _) => NewFileName.Focus();
    }

    private void OnAddPlaceholder(object sender, RoutedEventArgs e)
    {
        if (NewFileName.Text != null)
            ResultName = NewFileName.Text;

        this.Close();
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            this.Close();
        else if (e.Key == Key.Enter)
            OnAddPlaceholder(sender, e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

}