using System.Text;
using Avalonia.Controls;
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

    private List<JassFunction> _functions = new();
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
        _functions = new List<JassFunction>();
        _lineStarts = Array.Empty<int>();
        SearchBox.Text = string.Empty;
        FunctionList.ItemsSource = null;
        SourceBox.Text = string.Empty;
        SignatureText.Text = string.Empty;

        var doc = session.Current;
        if (doc is null)
        {
            HeaderText.Text = "No script in map";
            return;
        }

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

        var raw = doc.GetFile(result.ScriptFile)?.RawBytes ?? Array.Empty<byte>();
        var source = Encoding.UTF8.GetString(raw);
        _lineStarts = ComputeLineStarts(source);
        _functions = result.Functions.OrderBy(f => f.StartLine).ToList();

        HeaderText.Text = $"{_functions.Count} functions in {result.ScriptFile}";
        SourceBox.Text = source;
        ApplyFilter();
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
        if (FunctionList.SelectedItem is not FunctionItem item) return;

        SignatureText.Text = item.Fn.Signature;

        // Best-effort: move the caret to the function's first line and select it.
        var line = item.Fn.StartLine;                 // 1-based (JassFunctionIndex)
        if (line < 1 || line > _lineStarts.Length) return;

        var start = _lineStarts[line - 1];
        var end = line < _lineStarts.Length
            ? _lineStarts[line] - 1                   // up to (not including) the '\n'
            : SourceBox.Text?.Length ?? start;
        var text = SourceBox.Text ?? string.Empty;
        if (end > start && end <= text.Length && end > 0 && text[end - 1] == '\r') end--;
        if (end < start) end = start;

        SourceBox.CaretIndex = start;
        SourceBox.SelectionStart = start;
        SourceBox.SelectionEnd = end;
    }

    // Offsets follow JassFunctionIndex line numbering: lines are '\n'-separated.
    private static int[] ComputeLineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++)
            if (source[i] == '\n') starts.Add(i + 1);
        return starts.ToArray();
    }
}
