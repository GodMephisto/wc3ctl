using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Studio.Panels;

public partial class ScriptView : UserControl, IMapPanel
{
    /// <summary>ListBox item wrapping a function; ToString is the display text.</summary>
    private sealed record FunctionItem(JassFunction Fn)
    {
        public override string ToString() => $"{Fn.Name}  (lines {Fn.StartLine}–{Fn.EndLine})";
    }

    private MapSession? _session;
    private string? _scriptFile;                      // file the source came from (write-back target)
    private int _unsavedEdits;                        // applied-but-not-saved edit count
    private List<JassFunction> _functions = new();
    private string _source = string.Empty;            // full script; never shown whole in SourceBox
    private int[] _lineStarts = Array.Empty<int>();   // char offset of each 1-based line's start

    public ScriptView()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        FunctionList.SelectionChanged += (_, _) => OnFunctionSelected();
    }

    public void ShowMap(MapSession session)
    {
        // Rebuild from scratch on every call.
        _session = session;
        _scriptFile = null;
        _unsavedEdits = 0;
        _functions = new List<JassFunction>();
        _source = string.Empty;
        _lineStarts = Array.Empty<int>();
        SearchBox.Text = string.Empty;
        FunctionList.ItemsSource = null;
        SourceBox.Text = string.Empty;
        SignatureText.Text = string.Empty;
        StatusText.Text = string.Empty;
        ApplyButton.IsEnabled = false;
        SaveButton.IsEnabled = false;

        var doc = session.Current;
        if (doc is null)
        {
            HeaderText.Text = "No script in map";
            return;
        }

        RefreshFromDoc(doc, keepFunctionName: null);
    }

    /// <summary>
    /// (Re)loads source + function list from the doc's current script bytes (pending
    /// in-memory edits included), optionally re-selecting a function by name.
    /// </summary>
    private void RefreshFromDoc(MapDocument doc, string? keepFunctionName)
    {
        ScriptFunctionsResult result;
        try
        {
            result = ScriptCommand.Functions(doc);
        }
        catch (FileNotFoundException)
        {
            HeaderText.Text = "No script in map";
            return;
        }

        _scriptFile = result.ScriptFile;
        var entry = doc.GetFile(result.ScriptFile);
        var raw = entry?.OverrideBytes ?? entry?.RawBytes ?? Array.Empty<byte>();
        _source = Encoding.UTF8.GetString(raw);
        _lineStarts = ScriptCommand.ComputeLineStarts(_source);
        _functions = result.Functions.OrderBy(f => f.StartLine).ToList();

        // The full source stays in _source only; SourceBox gets one function at a
        // time in OnFunctionSelected, so the TextBox never lays out megabytes.
        HeaderText.Text = $"{_functions.Count} functions in {result.ScriptFile}";
        ApplyFilter();

        if (keepFunctionName is not null && FunctionList.ItemsSource is List<FunctionItem> items)
        {
            var again = items.FirstOrDefault(i => i.Fn.Name == keepFunctionName);
            if (again is not null)
                FunctionList.SelectedItem = again;   // reloads SourceBox via OnFunctionSelected
        }
    }

    private void ApplyFilter()
    {
        var filter = SearchBox.Text?.Trim() ?? string.Empty;
        FunctionList.ItemsSource = _functions
            .Where(f => filter.Length == 0 ||
                        f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(f => new FunctionItem(f))
            .ToList();
    }

    private void OnFunctionSelected()
    {
        if (FunctionList.SelectedItem is not FunctionItem item)
        {
            ApplyButton.IsEnabled = false;
            return;
        }

        SignatureText.Text = item.Fn.Signature;
        ApplyButton.IsEnabled = true;

        // Show only this function's lines so the TextBox holds a few dozen
        // lines instead of the whole (potentially multi-MB) script.
        SourceBox.Text = ScriptCommand.SliceFunction(
            _source, _lineStarts, item.Fn.StartLine, item.Fn.EndLine);
        SourceBox.CaretIndex = 0;
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc || _scriptFile is null)
        {
            StatusText.Text = "No map open.";
            return;
        }
        if (FunctionList.SelectedItem is not FunctionItem item)
        {
            StatusText.Text = "Select a function to apply the edit to.";
            return;
        }

        var newSource = ScriptCommand.ReplaceFunction(
            _source, _lineStarts, item.Fn, SourceBox.Text ?? string.Empty);
        if (newSource == _source)
        {
            StatusText.Text = "No changes to apply.";
            return;
        }

        // Write the full edited source back into the in-memory doc, then re-run the
        // function list against it (ScriptCommand.Functions reads pending edits).
        doc.AddOrReplaceRawFile(_scriptFile, Encoding.UTF8.GetBytes(newSource));
        _unsavedEdits++;
        RefreshFromDoc(doc, keepFunctionName: item.Fn.Name);
        SaveButton.IsEnabled = true;
        StatusText.Text = $"Applied edit to {item.Fn.Name} - {_unsavedEdits} unsaved edit(s).";
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc)
        {
            StatusText.Text = "No map open.";
            return;
        }
        if (_session.MapPath is not { } mapPath)
        {
            StatusText.Text = "The map has no file path to save next to.";
            return;
        }
        if (_unsavedEdits == 0)
        {
            StatusText.Text = "No applied edits to save.";
            return;
        }

        // Never overwrite the original map: save next to it as <name>.edited<ext>
        // (same convention as the Object editor).
        var editedPath = EditedPath(mapPath);
        try
        {
            doc.Save(editedPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Saving failed: {ex.Message}";
            return;
        }

        StatusText.Text = $"Saved {editedPath} - {_unsavedEdits} edit(s) written.";
        _unsavedEdits = 0;
        SaveButton.IsEnabled = false;
    }

    private static string EditedPath(string mapPath)
    {
        var dir = Path.GetDirectoryName(mapPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(mapPath);
        var ext = Path.GetExtension(mapPath);
        return Path.Combine(dir, $"{stem}.edited{ext}");
    }
}
