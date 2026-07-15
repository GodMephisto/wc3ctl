using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Studio;

public partial class MainWindow : Window
{
    private readonly MapSession _session = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private IMapPanel[] Panels => new IMapPanel[] { TerrainPanel, ObjectsPanel, FilesPanel, ScriptPanel };

    private async void OnOpenMapClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Warcraft III map",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Warcraft III maps") { Patterns = new[] { "*.w3x", "*.w3m" } },
                FilePickerFileTypes.All,
            },
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
        {
            OpenMap(path);
        }
    }

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

    private void OpenMap(string path)
    {
        try
        {
            var doc = MapDocument.Load(path);
            _session.Current = doc;
            _session.MapPath = path;
            // GameDir stays null — panels auto-detect the game install.

            var info = InfoCommand.Execute(doc);
            var list = ListCommand.Execute(doc);
            var name = string.IsNullOrEmpty(info.Name) ? "(unnamed)" : info.Name;
            StatusText.Text = info.Diagnostics.Count > 0
                ? $"{path} — {name} — {list.Files.Count} file(s) — {info.Diagnostics.Count} diagnostic(s): {string.Join("; ", info.Diagnostics)}"
                : $"{path} — {name} — {list.Files.Count} file(s)";

            foreach (var panel in Panels)
            {
                panel.ShowMap(_session);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error opening {path}: {ex.Message}";
        }
    }
}
