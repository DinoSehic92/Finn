using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Finn.Dialogs;

public partial class xEditLinkDia : Window
{
    public string? ResultName { get; private set; }
    public string? ResultUrl  { get; private set; }
    public bool    Confirmed  { get; private set; }

    public xEditLinkDia()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        Opened  += (_, _) => LinkName.Focus();
    }

    /// <summary>Pre-populates the fields when editing an existing link.</summary>
    public void Populate(string name, string url)
    {
        LinkName.Text = name;
        LinkUrl.Text  = url;
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Commit();
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  Commit();
        if (e.Key == Key.Escape) Close();
    }

    private void Commit()
    {
        string? url = LinkUrl.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;

        // Auto-prefix bare hostnames
        bool hasScheme  = url.Contains("://") || (url.Contains(':') && url.IndexOf(':') <= 8);
        bool isLocalPath = url.Length >= 2 && (url[1] == ':' || url.StartsWith("\\\\") || url.StartsWith("//"));
        if (!hasScheme && !isLocalPath)
            url = "https://" + url;

        string name = LinkName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
            name = isLocalPath ? System.IO.Path.GetFileName(url) : url;
        if (string.IsNullOrEmpty(name)) name = url;

        ResultUrl  = url;
        ResultName = name;
        Confirmed  = true;
        Close();
    }
}
