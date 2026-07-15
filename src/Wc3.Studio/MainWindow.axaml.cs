using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Wc3.Studio;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Workspace.MapChanged += OnWorkspaceMapChanged;
    }

    private async void OnOpenMapClick(object? sender, RoutedEventArgs e) =>
        await Workspace.PickAndOpenMapAsync();

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "About wc3ctl Studio",
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Text = "wc3ctl Studio\n\nA CLI-first Warcraft III map editor.",
                Margin = new Avalonia.Thickness(16),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        await dialog.ShowDialog(this);
    }

    private void OnWorkspaceMapChanged(object? sender, EventArgs e)
    {
        StatusText.Text = Workspace.HasMap
            ? $"Open: {Workspace.Session.MapPath}"
            : "No map open.";
    }
}
