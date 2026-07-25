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

    // Current preview state (wave 2: hex toggle, model preview, text edit).
    private FileRow? _previewRow;
    private byte[]? _previewBytes;      // override-aware bytes of the previewed file
    private FilePreview? _preview;      // the auto preview (null for model files)
    private bool _hexMode;
    private bool _editing;

    public FilesView()
    {
        InitializeComponent();
        _audio.PlaybackStopped += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SetPlayState(false));
        // Best-effort cleanup: stop playback (releases the audio device + temp file) when
        // this panel leaves the visual tree, e.g. on window close or tab teardown.
        DetachedFromVisualTree += (_, _) => _audio.Stop();
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
        _rows = BuildRows(doc);
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
            var entry = doc.Files[row.Ordinal];
            _previewRow = row;
            _previewBytes = entry.OverrideBytes ?? entry.RawBytes;   // reflect in-session edits
            _audioBytes = _previewBytes;
            _audioExt = Path.GetExtension(row.Name ?? "");
            _hexMode = false;
            _editing = false;
            HexButton.Content = "Hex";
            EditButton.Content = "Edit";
            SaveTextButton.IsEnabled = false;

            var ext = _audioExt.ToLowerInvariant();
            if (ext is ".mdx" or ".mdl")
            {
                _preview = null;
                ShowModelPreview(row, _previewBytes);
                EditButton.IsEnabled = false;
            }
            else
            {
                _preview = row.Name is not null
                    ? FilePreviewCommand.Execute(doc, row.Name)
                    : FilePreviewCommand.Of(_previewBytes, null);
                ShowPreview(_preview);
                bool isText = _preview.Kind is not "image" and not "audio";
                EditButton.IsEnabled = row.Name is not null && isText;
            }
            PreviewActions.IsVisible = true;
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

    // --- wave 2: model preview, hex view, text edit, import/replace ---

    /// <summary>Render an .mdx/.mdl model as a lightweight orthographic wireframe PNG.</summary>
    private void ShowModelPreview(FileRow row, byte[] bytes)
    {
        _audio.Stop();
        SetPlayState(false);
        HidePreviewBodies();
        PreviewHeader.Text = $"{row.DisplayName}  (model preview - wireframe)";
        try
        {
            var png = ModelPreviewCommand.RenderPreview(bytes);
            using var ms = new MemoryStream(png);
            var bmp = new Bitmap(ms);
            var old = PreviewImage.Source as Bitmap;
            PreviewImage.Source = bmp;
            old?.Dispose();
            PreviewImage.IsVisible = true;
        }
        catch (Exception ex)
        {
            PreviewHeader.Text = $"Model preview failed: {ex.Message}";
        }
    }

    private void OnHexToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_previewBytes is null) return;
        _hexMode = !_hexMode;
        HexButton.Content = _hexMode ? "Auto" : "Hex";
        if (_hexMode) ShowHex();
        else RestoreAutoPreview();
    }

    private void ShowHex()
    {
        _audio.Stop();
        SetPlayState(false);
        HidePreviewBodies();
        const int cap = 256 * 1024; // the preview TextBox is not virtualized - cap the dump
        var bytes = _previewBytes!;
        int len = Math.Min(bytes.Length, cap);
        var text = string.Join("\n", HexDumpCommand.Format(bytes, 0, len));
        if (len < bytes.Length)
            text += $"\n... ({bytes.Length - len:N0} more bytes not shown)";
        PreviewText.Text = text;
        PreviewText.IsReadOnly = true;
        PreviewText.IsVisible = true;
    }

    private void RestoreAutoPreview()
    {
        if (_previewRow is not { } row) return;
        var ext = Path.GetExtension(row.Name ?? "").ToLowerInvariant();
        if (ext is ".mdx" or ".mdl") ShowModelPreview(row, _previewBytes!);
        else if (_preview is not null) ShowPreview(_preview);
        // Leaving hex returns to the read-only auto view; editing must be re-enabled.
        PreviewText.IsReadOnly = true;
        _editing = false;
        EditButton.Content = "Edit";
        SaveTextButton.IsEnabled = false;
    }

    private void OnEditToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_hexMode || _preview is null) return;
        _editing = !_editing;
        PreviewText.IsReadOnly = !_editing;
        EditButton.Content = _editing ? "Editing..." : "Edit";
        SaveTextButton.IsEnabled = _editing;
        if (_editing) PreviewText.Focus();
    }

    private void OnSaveTextClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc || _previewRow?.Name is not { } name) return;
        try
        {
            FileEditCommand.WriteText(doc, name, PreviewText.Text ?? "");
            RefreshList();
            StatusText.Text = $"Saved edits to {name} in the map. Use Save Map As... to write to disk.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save to map failed: {ex.Message}";
        }
    }

    private async void OnImportReplaceClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc) return;
        var path = await Controls.FilePicker.PickOpenAsync(this, "Import or replace a file in the map");
        if (path is null) return;

        // Replace the selected file if one is highlighted; otherwise add under the disk name.
        var selectedName = FileList.SelectedItems?.OfType<FileRow>().FirstOrDefault()?.Name;
        var targetName = selectedName ?? Path.GetFileName(path);
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var result = FileEditCommand.AddOrReplace(doc, targetName, bytes);
            RefreshList();
            StatusText.Text =
                $"{(result.Replaced ? "Replaced" : "Added")} {targetName} ({bytes.Length:N0} bytes). Save Map As... to persist.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Import failed: {ex.Message}";
        }
    }

    private async void OnSaveMapClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc) return;

        var mapPath = _session.MapPath;
        var ext = mapPath is not null ? Path.GetExtension(mapPath) : ".w3x";
        var suggested = mapPath is not null
            ? Path.GetFileNameWithoutExtension(mapPath) + ".edited" + ext
            : "map.edited.w3x";
        var dest = await Controls.FilePicker.PickSaveAsync(this, "Save map with edits", suggested, ext.TrimStart('.'));
        if (dest is null) return;
        try
        {
            await Task.Run(() => doc.Save(dest));
            StatusText.Text = $"Saved map to {Path.GetFileName(dest)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save map failed: {ex.Message}";
        }
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e) => ApplySort();

    /// <summary>Rebuild the row list from the current doc (override-aware sizes), preserving the preview.</summary>
    private void RefreshList()
    {
        if (_session?.Current is not { } doc) return;
        _rows = BuildRows(doc);
        SummaryText.Text = $"{_rows.Count} file(s), {_rows.Sum(r => (long)r.SizeBytes):N0} bytes";
        ApplySort();
    }

    /// <summary>Rows from ListCommand, with byte sizes taken override-aware from doc.Files.</summary>
    private static List<FileRow> BuildRows(Wc3.Model.MapDocument doc)
    {
        var files = ListCommand.Execute(doc).Files;
        var rows = new List<FileRow>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            int size = f.SizeBytes;
            if (i < doc.Files.Count)
            {
                var entry = doc.Files[i];
                size = (entry.OverrideBytes ?? entry.RawBytes)?.Length ?? f.SizeBytes;
            }
            rows.Add(new FileRow
            {
                Ordinal = i, Name = f.Name, SizeBytes = size, Known = f.Known, Parsed = f.Parsed,
            });
        }
        return rows;
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
        PreviewText.IsReadOnly = true;
        var old = PreviewImage.Source as Bitmap;
        PreviewImage.Source = null;
        old?.Dispose();
        HidePreviewBodies();
        PreviewActions.IsVisible = false;
        _previewRow = null;
        _previewBytes = null;
        _preview = null;
        _hexMode = false;
        _editing = false;
        HexButton.Content = "Hex";
        EditButton.Content = "Edit";
        EditButton.IsEnabled = false;
        SaveTextButton.IsEnabled = false;
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
        IEnumerable<FileRow> src = _rows;
        var query = FilterBox?.Text;
        if (!string.IsNullOrWhiteSpace(query))
            src = src.Where(r => DropdownFilter.Matches(query!, r.DisplayName, r.Name ?? ""));

        IEnumerable<FileRow> sorted = _sortColumn switch
        {
            SortColumn.Size => src.OrderBy(r => r.SizeBytes),
            SortColumn.Known => src.OrderBy(r => r.Known),
            SortColumn.Parsed => src.OrderBy(r => r.Parsed),
            _ => src.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase),
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
