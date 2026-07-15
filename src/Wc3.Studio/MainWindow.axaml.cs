using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Studio;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private sealed record FileRow(string Name, string Size, string Known, string Parsed);

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
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
            PathBox.Text = path;
            OpenMap(path);
        }
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e) => OpenMap(PathBox.Text?.Trim() ?? "");

    private void OpenMap(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            StatusText.Text = "Enter a map file path first.";
            return;
        }

        try
        {
            var doc = MapDocument.Load(path);
            var info = InfoCommand.Execute(doc);
            var list = ListCommand.Execute(doc);

            NameText.Text = info.Name;
            AuthorText.Text = $"Author: {info.Author}";
            PlayersText.Text = $"Players: {info.Players}";
            SizeText.Text = info.Width is { } w && info.Height is { } h
                ? $"Playable area: {w} x {h}"
                : "Playable area: (unknown)";

            FileList.ItemsSource = list.Files
                .Select(f => new FileRow(
                    f.Name ?? "(unnamed)",
                    $"{f.SizeBytes:N0} B",
                    f.Known ? "known" : "",
                    f.Parsed ? "parsed" : ""))
                .ToList();

            StatusText.Text = info.Diagnostics.Count > 0
                ? $"Opened with {info.Diagnostics.Count} diagnostic(s): {string.Join("; ", info.Diagnostics)}"
                : $"Opened {list.Files.Count} file(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }
}
