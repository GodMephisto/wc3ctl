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
        _functions = new List<JassFunction>();
        _source = string.Empty;
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
        _source = Encoding.UTF8.GetString(raw);
        _lineStarts = ComputeLineStarts(_source);
        _functions = result.Functions.OrderBy(f => f.StartLine).ToList();

        // The full source stays in _source only; SourceBox gets one function at a
        // time in OnFunctionSelected, so the TextBox never lays out megabytes.
        HeaderText.Text = $"{_functions.Count} functions in {result.ScriptFile}";
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

        // Show only this function's lines so the TextBox holds a few dozen
        // lines instead of the whole (potentially multi-MB) script.
        var startLine = item.Fn.StartLine;            // 1-based (JassFunctionIndex)
        if (startLine < 1 || startLine > _lineStarts.Length)
        {
            SourceBox.Text = string.Empty;
            return;
        }

        var endLine = Math.Clamp(item.Fn.EndLine, startLine, _lineStarts.Length);
        var start = _lineStarts[startLine - 1];
        var end = endLine < _lineStarts.Length
            ? _lineStarts[endLine] - 1                // up to (not including) the '\n'
            : _source.Length;                         // last line runs to EOF
        if (end > start && _source[end - 1] == '\r') end--;
        if (end < start) end = start;

        SourceBox.Text = _source.Substring(start, end - start);
        SourceBox.CaretIndex = 0;
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
