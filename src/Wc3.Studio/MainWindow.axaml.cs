using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Wc3.Studio.Panels;

namespace Wc3.Studio;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceWorkspace.MapChanged += OnWorkspaceMapChanged;
        TargetWorkspace.MapChanged += OnWorkspaceMapChanged;
    }

    /// <summary>
    /// Both panes, for the porting wave to wire across (read source selection,
    /// write into the target session). Each workspace owns its map exclusively.
    /// </summary>
    public (MapWorkspaceView Source, MapWorkspaceView Target) Workspaces =>
        (SourceWorkspace, TargetWorkspace);

    private async void OnOpenSourceMapClick(object? sender, RoutedEventArgs e) =>
        await SourceWorkspace.PickAndOpenMapAsync();

    private async void OnOpenTargetMapClick(object? sender, RoutedEventArgs e) =>
        await TargetWorkspace.PickAndOpenMapAsync();

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

    /// <summary>Global status: which map sits in each pane (details live per pane).</summary>
    private void OnWorkspaceMapChanged(object? sender, EventArgs e) =>
        StatusText.Text = $"Source: {Describe(SourceWorkspace)}   |   Target: {Describe(TargetWorkspace)}";

    private static string Describe(MapWorkspaceView workspace) =>
        workspace.HasMap
            ? Path.GetFileName(workspace.Session.MapPath) ?? "(unnamed)"
            : "no map";
}
