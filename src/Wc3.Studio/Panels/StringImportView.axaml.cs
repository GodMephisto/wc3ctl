using Avalonia.Controls;
using Avalonia.Interactivity;
using Wc3.Commands;

namespace Wc3.Studio.Panels;

/// <summary>
/// Trigger strings (war3map.wts) + imports panel. Thin shell over StringsCommand /
/// ImportsCommand: the list shows read-only rows and all editing happens in the single
/// dedicated editor + Apply (an editable TextBox inside a ListBox loses its value to
/// virtualization/focus recycling).
/// </summary>
public partial class StringImportView : UserControl, IMapPanel
{
    /// <summary>ListBox row for one string; ToString is the display text (id + first line).</summary>
    private sealed record StringItem(WtsEntry Entry)
    {
        public override string ToString()
        {
            var firstLine = Entry.Text;
            var nl = firstLine.IndexOf('\n');
            if (nl >= 0) firstLine = firstLine[..nl];
            return $"{Entry.Id}:  {firstLine.TrimEnd('\r')}";
        }
    }

    /// <summary>ListBox row for one import; read-only by design.</summary>
    private sealed record ImportItem(ImportEntry Entry)
    {
        public override string ToString()
        {
            var size = Entry.SizeBytes is { } b ? $"  ({b:N0} bytes)" : "";
            var note = Entry switch
            {
                { InArchive: false } => "  — in manifest, missing from archive",
                { InManifest: false } => "  — in archive, not in manifest",
                _ => "",
            };
            return $"{Entry.Path}{size}{note}";
        }
    }

    private MapSession? _session;
    private List<WtsEntry> _entries = new();

    public StringImportView()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        StringList.SelectionChanged += (_, _) => OnStringSelected();
    }

    public void ShowMap(MapSession session)
    {
        // Rebuild from scratch on every call.
        _session = session;
        _entries = new List<WtsEntry>();
        SearchBox.Text = string.Empty;
        StringList.ItemsSource = null;
        ImportsList.ItemsSource = null;
        StatusText.Text = string.Empty;
        ResetEditor();

        var doc = session.Current;
        PlaceholderText.IsVisible = doc is null;
        ContentRoot.IsVisible = doc is not null;
        if (doc is null) return;

        RefreshStrings(keepSelectionId: null);
        RefreshImports();
        SaveButton.IsEnabled = session.MapPath is not null;
    }

    private void RefreshStrings(int? keepSelectionId)
    {
        var doc = _session?.Current;
        if (doc is null) return;

        var result = StringsCommand.List(doc);
        _entries = result.Entries.ToList();
        HeaderText.Text = result.FileName is null
            ? "No war3map.wts in map"
            : $"{_entries.Count} strings in {result.FileName}";
        ApplyFilter();

        if (keepSelectionId is { } id && StringList.ItemsSource is List<StringItem> items)
            StringList.SelectedItem = items.FirstOrDefault(i => i.Entry.Id == id);
    }

    private void RefreshImports()
    {
        var doc = _session?.Current;
        if (doc is null) return;
        try
        {
            var result = ImportsCommand.Execute(doc);
            ImportsHeader.Text = result.HasManifest
                ? $"{result.Entries.Count} imports (war3map.imp)"
                : $"{result.Entries.Count} non-standard files (no parsed war3map.imp)";
            ImportsList.ItemsSource = result.Entries.Select(e => new ImportItem(e)).ToList();
        }
        catch (Exception ex)
        {
            ImportsHeader.Text = "Imports";
            StatusText.Text = $"Imports listing failed: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var filter = SearchBox.Text?.Trim() ?? string.Empty;
        StringList.ItemsSource = _entries
            .Where(e => filter.Length == 0
                        || e.Id.ToString().Contains(filter, StringComparison.Ordinal)
                        || e.Text.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(e => new StringItem(e))
            .ToList();
    }

    private void OnStringSelected()
    {
        if (StringList.SelectedItem is not StringItem item) { ResetEditor(); return; }
        EditorHeader.Text = $"STRING {item.Entry.Id}";
        EditorBox.Text = item.Entry.Text;
        EditorBox.IsEnabled = true;
        ApplyButton.IsEnabled = true;
    }

    private void ResetEditor()
    {
        EditorHeader.Text = "Select a string to edit";
        EditorBox.Text = string.Empty;
        EditorBox.IsEnabled = false;
        ApplyButton.IsEnabled = false;
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        var doc = _session?.Current;
        if (doc is null || StringList.SelectedItem is not StringItem item) return;
        try
        {
            StringsCommand.Set(doc, item.Entry.Id, EditorBox.Text ?? string.Empty);
            RefreshStrings(keepSelectionId: item.Entry.Id);   // re-parse so the row + editor show what was stored
            StatusText.Text = $"STRING {item.Entry.Id} updated in memory — Save Map… writes a .edited copy.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Apply failed: {ex.Message}";
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var doc = _session?.Current;
        var mapPath = _session?.MapPath;
        if (doc is null || mapPath is null) { StatusText.Text = "No map path available."; return; }
        try
        {
            // Same convention as the object editor: never clobber the original map.
            var editedPath = Path.Combine(
                Path.GetDirectoryName(mapPath) ?? ".",
                Path.GetFileNameWithoutExtension(mapPath) + ".edited" + Path.GetExtension(mapPath));
            doc.Save(editedPath);
            StatusText.Text = $"Saved: {editedPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }
}
