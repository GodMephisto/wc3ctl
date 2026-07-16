using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Wc3.Commands;

namespace Wc3.Studio.Panels;

/// <summary>Row model for the file list (reflection-bound from XAML).</summary>
public sealed class FileRow
{
    public int Ordinal { get; init; }
    public string? Name { get; init; }
    public int SizeBytes { get; init; }
    public bool Known { get; init; }
    public bool Parsed { get; init; }

    public string DisplayName => Name ?? $"(unnamed #{Ordinal})";
    public string SizeText => SizeBytes.ToString("N0");
    public string KnownText => Known ? "yes" : "";
    public string ParsedText => Parsed ? "yes" : "";
}

public partial class FilesView : UserControl, IMapPanel
{
    private enum SortColumn { Name, Size, Known, Parsed }

    private MapSession? _session;
    private List<FileRow> _rows = new();
    private SortColumn _sortColumn = SortColumn.Name;
    private bool _sortAscending = true;

    public FilesView()
    {
        InitializeComponent();
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        Rebuild();
    }

    private void Rebuild()
    {
        StatusText.Text = "";
        if (_session?.Current is not { } doc)
        {
            _rows = new List<FileRow>();
            FileList.ItemsSource = null;
            SummaryText.Text = "";
            ContentRoot.IsVisible = false;
            PlaceholderText.IsVisible = true;
            return;
        }

        // Ordinal == position in doc.Files order, shared by ListCommand and ExtractCommand(All).
        _rows = ListCommand.Execute(doc).Files
            .Select((f, i) => new FileRow
            {
                Ordinal = i, Name = f.Name, SizeBytes = f.SizeBytes, Known = f.Known, Parsed = f.Parsed,
            })
            .ToList();
        SummaryText.Text = $"{_rows.Count} file(s), {_rows.Sum(r => (long)r.SizeBytes):N0} bytes";
        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
        ResetPreview();
        ApplySort();
    }

    // --- content preview (double-click a file) ---

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_session?.Current is not { } doc || FileList.SelectedItem is not FileRow row)
            return;
        try
        {
            var preview = row.Name is not null
                ? FilePreviewCommand.Execute(doc, row.Name)
                : FilePreviewCommand.Of(doc.Files[row.Ordinal].RawBytes, null);
            ShowPreview(preview);
        }
        catch (Exception ex)
        {
            PreviewHeader.Text = $"Preview failed: {ex.Message}";
            PreviewText.IsVisible = false;
            PreviewImage.IsVisible = false;
        }
    }

    private void ShowPreview(FilePreview preview)
    {
        PreviewHeader.Text = $"{preview.Name}  ({preview.Info})";
        if (preview.Kind == "image" && preview.Png is { } png)
        {
            using var ms = new MemoryStream(png);
            var bmp = new Bitmap(ms);
            var old = PreviewImage.Source as Bitmap;
            PreviewImage.Source = bmp;
            old?.Dispose();
            PreviewImage.IsVisible = true;
            PreviewText.IsVisible = false;
        }
        else
        {
            PreviewText.Text = preview.Text ?? "";
            PreviewText.IsVisible = true;
            PreviewImage.IsVisible = false;
        }
    }

    private void ResetPreview()
    {
        PreviewHeader.Text = "Double-click a file to preview its contents";
        PreviewText.Text = "";
        PreviewText.IsVisible = false;
        var old = PreviewImage.Source as Bitmap;
        PreviewImage.Source = null;
        old?.Dispose();
        PreviewImage.IsVisible = false;
    }

    // --- sorting ---

    private void OnSortByName(object? sender, RoutedEventArgs e) => ToggleSort(SortColumn.Name);
    private void OnSortBySize(object? sender, RoutedEventArgs e) => ToggleSort(SortColumn.Size);
    private void OnSortByKnown(object? sender, RoutedEventArgs e) => ToggleSort(SortColumn.Known);
    private void OnSortByParsed(object? sender, RoutedEventArgs e) => ToggleSort(SortColumn.Parsed);

    private void ToggleSort(SortColumn column)
    {
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else (_sortColumn, _sortAscending) = (column, true);
        ApplySort();
    }

    private void ApplySort()
    {
        IEnumerable<FileRow> sorted = _sortColumn switch
        {
            SortColumn.Size => _rows.OrderBy(r => r.SizeBytes),
            SortColumn.Known => _rows.OrderBy(r => r.Known),
            SortColumn.Parsed => _rows.OrderBy(r => r.Parsed),
            _ => _rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase),
        };
        if (!_sortAscending) sorted = sorted.Reverse();
        FileList.ItemsSource = sorted.ToList();

        var arrow = _sortAscending ? " ▲" : " ▼";
        NameHeader.Content = "Name" + (_sortColumn == SortColumn.Name ? arrow : "");
        SizeHeader.Content = "Size (bytes)" + (_sortColumn == SortColumn.Size ? arrow : "");
        KnownHeader.Content = "Known" + (_sortColumn == SortColumn.Known ? arrow : "");
        ParsedHeader.Content = "Parsed" + (_sortColumn == SortColumn.Parsed ? arrow : "");
    }

    // --- extraction ---

    private async void OnExtractAllClick(object? sender, RoutedEventArgs e) => await ExtractAsync(all: true);
    private async void OnExtractSelectedClick(object? sender, RoutedEventArgs e) => await ExtractAsync(all: false);

    private async Task ExtractAsync(bool all)
    {
        if (_session?.Current is not { } doc) return;

        HashSet<int>? selected = null;
        if (!all)
        {
            selected = FileList.SelectedItems?.OfType<FileRow>().Select(r => r.Ordinal).ToHashSet();
            if (selected is null || selected.Count == 0)
            {
                StatusText.Text = "No files selected.";
                return;
            }
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = all ? "Extract all files to folder" : "Extract selected files to folder",
            AllowMultiple = false,
        });
        if (folders.Count != 1 || folders[0].TryGetLocalPath() is not { } dir) return;

        try
        {
            var result = ExtractCommand.Execute(doc, new ExtractSelector { All = true });
            var items = selected is null
                ? result.Items
                : selected.Where(i => i < result.Items.Count).OrderBy(i => i)
                    .Select(i => result.Items[i]).ToList();
            var (count, bytes) = await Task.Run(() => WriteItems(items, dir));
            StatusText.Text = $"Extracted {count} file(s) ({bytes:N0} bytes) to {dir}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Extract failed: {ex.Message}";
        }
    }

    // File writing mirrors the CLI's ExtractWriter (src/wc3ctl), which Studio cannot
    // reference; internal folder structure is preserved and writes never escape outDir.

    private static (int Count, long Bytes) WriteItems(IReadOnlyList<ExtractedItem> items, string outDir)
    {
        var root = Path.GetFullPath(outDir);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        long total = 0;
        foreach (var item in items)
        {
            var dest = Path.GetFullPath(Path.Combine(root, SafeRelativePath(item.Name, item.BlockIndex)));
            if (!dest.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"refusing to write outside output directory: {item.Name}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, item.Bytes);
            total += item.Bytes.Length;
        }
        return (items.Count, total);
    }

    /// <summary>
    /// Maps an internal name to a safe relative path: splits on '\'/'/', drops
    /// '.'/'..'/empty segments, replaces invalid characters. Unnamed entries
    /// (Name == null) go to _unnamed\block_&lt;n&gt;.bin.
    /// </summary>
    private static string SafeRelativePath(string? name, int blockIndex)
    {
        if (name is null) return Path.Combine("_unnamed", $"block_{blockIndex}.bin");
        var segments = name.Split('\\', '/')
            .Where(s => s.Length > 0 && s != "." && s != "..")
            .Select(Sanitize)
            .Where(s => s.Length > 0)
            .ToArray();
        return segments.Length == 0
            ? Path.Combine("_unnamed", $"block_{blockIndex}.bin")
            : Path.Combine(segments);
    }

    private static string Sanitize(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(segment.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.TrimEnd(' ', '.');   // Windows rejects trailing dots/spaces
    }
}
