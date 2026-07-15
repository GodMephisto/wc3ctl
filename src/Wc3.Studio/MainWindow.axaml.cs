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
    private readonly HashSet<IMapPanel> _loadedPanels = new();

    public MainWindow()
    {
        InitializeComponent();
    }

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

            // Lazy loading: only the visible tab refreshes now; the other
            // panels load on first selection (see OnPanelTabsSelectionChanged).
            _loadedPanels.Clear();
            LoadSelectedPanel();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error opening {path}: {ex.Message}";
        }
    }

    private void OnPanelTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles up from selectors inside tab content too.
        if (!ReferenceEquals(e.Source, PanelTabs) || _session.Current is null)
        {
            return;
        }

        try
        {
            LoadSelectedPanel();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading panel: {ex.Message}";
        }
    }

    private void LoadSelectedPanel()
    {
        if (PanelTabs.SelectedItem is TabItem { Content: IMapPanel panel } && _loadedPanels.Add(panel))
        {
            panel.ShowMap(_session);
        }
    }
}
