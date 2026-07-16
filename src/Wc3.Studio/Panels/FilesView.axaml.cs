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

    private readonly AudioPlayer _audio = new();
    private byte[]? _audioBytes;
    private string _audioExt = "";

    public FilesView()
    {
        InitializeComponent();
        _audio.PlaybackStopped += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SetPlayState(false));
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
        ExportButton.IsEnabled = false; // re-sorting/reloading clears the selection
    }

    // --- content preview (double-click a file) ---

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_session?.Current is not { } doc || FileList.SelectedItem is not FileRow row)
            return;
        try
        {
            _audioBytes = doc.Files[row.Ordinal].RawBytes;
            _audioExt = Path.GetExtension(row.Name ?? "");
            var preview = row.Name is not null
                ? FilePreviewCommand.Execute(doc, row.Name)
                : FilePreviewCommand.Of(_audioBytes, null);
            ShowPreview(preview);
        }
        catch (Exception ex)
        {
            PreviewHeader.Text = $"Preview failed: {ex.Message}";
            HidePreviewBodies();
        }
    }

    private void ShowPreview(FilePreview preview)
    {
        PreviewHeader.Text = $"{preview.Name}  ({preview.Info})";
        _audio.Stop();
        SetPlayState(false);
        HidePreviewBodies();

        if (preview.Kind == "audio")
        {
            AudioControls.IsVisible = true; // bytes staged in OnFileDoubleTapped
        }
        else if (preview.Kind == "image" && preview.Png is { } png)
        {
            using var ms = new MemoryStream(png);
            var bmp = new Bitmap(ms);
            var old = PreviewImage.Source as Bitmap;
            PreviewImage.Source = bmp;
            old?.Dispose();
            PreviewImage.IsVisible = true;
        }
        else
        {
            PreviewText.Text = preview.Text ?? "";
            PreviewText.IsVisible = true;
        }
    }

    private void HidePreviewBodies()
    {
        PreviewText.IsVisible = false;
        PreviewImage.IsVisible = false;
        AudioControls.IsVisible = false;
    }

    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (_audioBytes is null) return;
        try { _audio.Play(_audioBytes, _audioExt); SetPlayState(true); }
        catch (Exception ex) { StatusText.Text = $"Cannot play {_audioExt}: {ex.Message}"; SetPlayState(false); }
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        _audio.Stop();
        SetPlayState(false);
    }

    private void SetPlayState(bool playing)
    {
        PlayButton.IsEnabled = !playing;
        StopButton.IsEnabled = playing;
    }

    private void ResetPreview()
    {
        _audio.Stop();
        SetPlayState(false);
        PreviewHeader.Text = "Double-click a file to preview its contents";
        PreviewText.Text = "";
        var old = PreviewImage.Source as Bitmap;
        PreviewImage.Source = null;
        old?.Dispose();
        HidePreviewBodies();
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

    // --- export (format conversion) ---

    private void OnFileSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        ExportButton.IsEnabled = FileList.SelectedItems?.Count >= 1;

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc) return;
        if (FileList.SelectedItems?.OfType<FileRow>().FirstOrDefault() is not { } row)
        {
            StatusText.Text = "No file selected.";
            return;
        }
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var ext = Path.GetExtension(row.Name ?? "").ToLowerInvariant();
        try
        {
            if (ext == ".blp") await ExportImageAsync(storage, doc, row);
            else if (ext is ".mdx" or ".mdl") await ExportModelAsync(storage, doc, row);
            else await ExportRawAsync(storage, doc, row);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>.blp → save dialog defaulting to &lt;name&gt;.png (the chosen extension picks the format).</summary>
    private async Task ExportImageAsync(IStorageProvider storage, Wc3.Model.MapDocument doc, FileRow row)
    {
        var baseName = Sanitize(Path.GetFileNameWithoutExtension(row.Name!));
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export image",
            SuggestedFileName = baseName + ".png",
            DefaultExtension = "png",
        });
        if (file?.TryGetLocalPath() is not { } dest) return;

        var toExt = Path.GetExtension(dest);
        if (string.IsNullOrEmpty(toExt)) { dest += ".png"; toExt = ".png"; }
        var raw = doc.Files[row.Ordinal].RawBytes;
        var converted = await Task.Run(() => ConvertCommand.ConvertImage(raw, ".blp", toExt));
        await File.WriteAllBytesAsync(dest, converted);
        StatusText.Text = $"Exported {Path.GetFileName(dest)} ({converted.Length:N0} bytes)";
    }

    /// <summary>.mdx/.mdl → folder pick, then &lt;name&gt;.obj + &lt;name&gt;.mtl + texture PNGs.</summary>
    private async Task ExportModelAsync(IStorageProvider storage, Wc3.Model.MapDocument doc, FileRow row)
    {
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Export model (OBJ + MTL + textures) to folder",
            AllowMultiple = false,
        });
        if (folders.Count != 1 || folders[0].TryGetLocalPath() is not { } dir) return;

        var export = await Task.Run(() => ConvertCommand.ExportModelToObj(doc, row.Name!));
        var baseName = Sanitize(Path.GetFileNameWithoutExtension(row.Name!));
        await File.WriteAllTextAsync(Path.Combine(dir, baseName + ".obj"), export.Obj);
        await File.WriteAllTextAsync(Path.Combine(dir, baseName + ".mtl"), export.Mtl);
        foreach (var (name, bytes) in export.Textures)
            await File.WriteAllBytesAsync(Path.Combine(dir, Sanitize(name)), bytes);
        StatusText.Text =
            $"Exported {baseName}.obj + {baseName}.mtl + {export.Textures.Count} texture(s) to {dir}";
    }

    /// <summary>Anything else → save dialog with the original name, raw bytes.</summary>
    private async Task ExportRawAsync(IStorageProvider storage, Wc3.Model.MapDocument doc, FileRow row)
    {
        var suggested = row.Name is null
            ? $"block_{row.Ordinal}.bin"
            : Sanitize(Path.GetFileName(row.Name));
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export file",
            SuggestedFileName = suggested,
        });
        if (file?.TryGetLocalPath() is not { } dest) return;

        var raw = doc.Files[row.Ordinal].RawBytes;
        await File.WriteAllBytesAsync(dest, raw);
        StatusText.Text = $"Exported {Path.GetFileName(dest)} ({raw.Length:N0} bytes)";
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
