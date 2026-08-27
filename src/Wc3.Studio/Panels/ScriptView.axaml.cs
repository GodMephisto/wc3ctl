// src/Wc3.Studio/Panels/ScriptView.axaml.cs
using System.Diagnostics;
using System.Text;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>
/// The map's script, as an editor rather than a viewer. The whole script in a
/// <see cref="JassEditorView"/>, a filterable symbol list beside it, go to definition, a compile
/// check whose findings are clickable, and Apply / Revert / Save.
/// </summary>
/// <remarks>
/// What changed and why. The panel used to slice one function's lines into a bare TextBox with the
/// default foreground, which is where "white text so it is terrible to eyes" came from. Slicing
/// also ruled out everything else, a gutter cannot show a true line number when line 1 of the box
/// is line 4,912 of the file, find can only search the forty lines on screen, and go to definition
/// has nowhere to go.
///
/// So the document now holds the whole script and the panel scrolls to a function instead of
/// cutting it out. That was measured before it was chosen. A real merged arena script, 8.4 MB and
/// 113,387 lines with 3,009 functions, loads into the editor in 275ms, indexes in 103ms, and
/// jumping to the very last function costs 398ms. Holding it whole is affordable, and slicing was
/// buying nothing.
/// </remarks>
public partial class ScriptView : UserControl, IMapPanel
{
    /// <summary>ListBox item wrapping a function. ToString is the display text.</summary>
    private sealed record FunctionItem(JassFunction Fn)
    {
        public override string ToString() => $"{Fn.StartLine,7}  {Fn.Name}";
    }

    /// <summary>A problems-strip row that jumps to its line when clicked.</summary>
    private sealed class JumpCommand : ICommand
    {
        private readonly Action _go;
        public JumpCommand(Action go) => _go = go;
        // Required by ICommand. A jump target never stops being valid, so nothing
        // ever raises it.
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _go();
    }

    private static readonly Color ProblemTint = Color.FromArgb(0x40, 0xC0, 0x30, 0x30);

    private MapSession? _session;
    private string? _scriptFile;                      // write-back target
    private int _unsavedEdits;                        // applied but not yet written to disk
    private List<JassFunction> _functions = new();
    private string _loaded = string.Empty;            // the text the editor was loaded with
    private bool _dirty;                              // editor text differs from _loaded
    private bool _selecting;                          // suppress reentry while jumping
    private int _generation;                          // invalidates an in-flight index

    public ScriptView()
    {
        InitializeComponent();
        // Observe the property rather than the TextChanged event. TextChanged tracks typing
        // but does not fire when Text is assigned in code, so anything that set the filter
        // programmatically (a test, or a future "search for this symbol" from another
        // panel) would leave the list showing everything while the box read as filtered.
        SearchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ApplyFilter();
        };
        SearchBodiesCheck.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty) ApplyFilter();
        };
        FunctionList.SelectionChanged += (_, _) => OnFunctionSelected();
        Editor.GoToDefinitionRequested += (_, word) => GoToDefinition(word);
        Editor.CaretLineChanged += (_, line) => OnCaretMoved(line);
        Editor.TextEdited += (_, _) => OnTextEdited();
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        _scriptFile = null;
        _unsavedEdits = 0;
        _dirty = false;
        _functions = new List<JassFunction>();
        _loaded = string.Empty;
        SearchBox.Text = string.Empty;
        FunctionList.ItemsSource = null;
        SignatureText.Text = string.Empty;
        StatusText.Text = string.Empty;
        CaretText.Text = string.Empty;
        ListCountText.Text = string.Empty;
        Editor.LoadScript("", natives: null);
        ApplyButton.IsEnabled = false;
        RevertButton.IsEnabled = false;
        CheckButton.IsEnabled = false;
        SaveButton.IsEnabled = false;

        if (session.Current is not { } doc)
        {
            HeaderText.Text = "No script in map";
            return;
        }

        LoadFromDoc(doc, keepFunctionName: null);
    }

    /// <summary>
    /// Loads the script and its symbol list from the document's CURRENT bytes, so an edit another
    /// panel made is visible here, optionally re-selecting a function by name.
    /// </summary>
    private void LoadFromDoc(MapDocument doc, string? keepFunctionName)
    {
        string file, source;
        try
        {
            (file, source) = ScriptCommand.Read(doc);
        }
        catch (FileNotFoundException)
        {
            HeaderText.Text = "No script in map";
            return;
        }

        _scriptFile = file;
        _loaded = source;
        _dirty = false;

        // Paint the script FIRST, with keywords and types only. Putting 8.4 MB into the document
        // costs 252ms and there is no way around that, but indexing it (131ms) and deriving its
        // externals (161ms) are pure computation that nobody has to wait through to start reading.
        // Doing all three before the first paint is what made opening this tab cost 606ms.
        Editor.LoadScript(source, natives: null);

        _functions = new List<JassFunction>();
        FunctionList.ItemsSource = null;
        HeaderText.Text = $"{Editor.LineCount:N0} lines in {file}, indexing…";
        ListCountText.Text = "…";
        CheckButton.IsEnabled = true;

        // A generation counter, because the user can open another map while this runs. Without it
        // a slow index would land on top of the map that replaced it.
        int generation = ++_generation;
        _ = IndexInBackgroundAsync(source, keepFunctionName, file, generation);
    }

    /// <summary>
    /// Indexes the script off the UI thread and fills in the symbol list and the native
    /// highlighting when it lands. Anything that throws is reported in the header rather than
    /// escaping onto the thread pool, where it would take the process down.
    /// </summary>
    private async Task IndexInBackgroundAsync(
        string source, string? keepFunctionName, string file, int generation)
    {
        List<JassFunction> functions;
        IReadOnlySet<string>? externals;
        try
        {
            (functions, externals) = await Task.Run(() =>
            {
                var fns = JassFunctionIndex.Parse(source).OrderBy(f => f.StartLine).ToList();
                IReadOnlySet<string>? ext;
                try { ext = JassSyntax.ExternalCalls(source, fns.Select(f => f.Name)); }
                catch { ext = null; }   // highlighting is never worth failing a load over
                return (fns, ext);
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (generation == _generation)
                HeaderText.Text = $"{Editor.LineCount:N0} lines in {file}, "
                                + $"could not be indexed ({ex.Message})";
            return;
        }

        if (generation != _generation) return;   // a different map is showing now

        _functions = functions;
        Editor.SetNatives(externals);
        HeaderText.Text = $"{_functions.Count:N0} functions, "
                        + $"{Editor.LineCount:N0} lines in {file}";
        ApplyFilter();

        if (keepFunctionName is not null && FunctionList.ItemsSource is List<FunctionItem> items)
        {
            var again = items.FirstOrDefault(i => i.Fn.Name == keepFunctionName);
            if (again is not null)
                FunctionList.SelectedItem = again;
        }
    }

    private void ApplyFilter()
    {
        var filter = SearchBox.Text?.Trim() ?? string.Empty;
        bool bodies = SearchBodiesCheck.IsChecked == true;

        IEnumerable<JassFunction> matches = _functions;
        if (filter.Length > 0)
        {
            matches = bodies
                ? _functions.Where(f => f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                        || BodyContains(f, filter))
                : _functions.Where(f => f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var list = matches.Select(f => new FunctionItem(f)).ToList();
        FunctionList.ItemsSource = list;
        ListCountText.Text = filter.Length == 0
            ? $"{list.Count:N0}"
            : $"{list.Count:N0} of {_functions.Count:N0}";
    }

    /// <summary>Whether a function's body contains the text. Reads from the loaded source rather
    /// than re-slicing, so a body search is a span comparison inside one string.</summary>
    private bool BodyContains(JassFunction f, string text)
    {
        var source = _loaded;
        if (source.Length == 0) return false;

        int from = LineOffset(source, f.StartLine);
        if (from < 0) return false;
        int to = LineOffset(source, f.EndLine + 1);
        if (to < 0 || to > source.Length) to = source.Length;
        if (to <= from) return false;

        return source.AsSpan(from, to - from)
            .Contains(text.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static int LineOffset(string source, int line)
    {
        if (line <= 1) return 0;
        int seen = 1;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != '\n') continue;
            if (++seen == line) return i + 1;
        }
        return -1;
    }

    private void OnFunctionSelected()
    {
        if (_selecting) return;
        if (FunctionList.SelectedItem is not FunctionItem item) return;

        SignatureText.Text = item.Fn.Signature;
        Editor.GoToLine(item.Fn.StartLine, item.Fn.EndLine);
    }

    private void OnCaretMoved(int line)
    {
        CaretText.Text = $"line {line:N0} of {Editor.LineCount:N0}";

        // Keep the symbol list following the caret, so scrolling through the script tells you
        // which function you are in. This is the half of "go to function" that reads the other
        // way, and on a merged script with 3,000 functions it is the more useful half.
        var owner = _functions.LastOrDefault(f => f.StartLine <= line && line <= f.EndLine);
        if (owner is null) return;
        if (FunctionList.SelectedItem is FunctionItem cur && cur.Fn.StartLine == owner.StartLine)
            return;
        if (FunctionList.ItemsSource is not List<FunctionItem> items) return;

        var match = items.FirstOrDefault(i => i.Fn.StartLine == owner.StartLine);
        if (match is null) return;

        _selecting = true;
        try
        {
            FunctionList.SelectedItem = match;
            SignatureText.Text = owner.Signature;
        }
        finally { _selecting = false; }
    }

    private void OnTextEdited()
    {
        _dirty = !string.Equals(Editor.Text, _loaded, StringComparison.Ordinal);
        ApplyButton.IsEnabled = _dirty;
        RevertButton.IsEnabled = _dirty;
    }

    /// <summary>F12 or Ctrl+click. Jumps to the function of that name, or says what it is.</summary>
    private void GoToDefinition(string word)
    {
        var target = _functions.FirstOrDefault(
            f => string.Equals(f.Name, word, StringComparison.Ordinal));
        if (target is null)
        {
            // A keyword, a native or a global, not a function this script declares. Saying which
            // is more useful than saying nothing, because "it is a native" is itself the answer.
            StatusText.Text = JassSyntax.IsReserved(word)
                ? $"'{word}' is a JASS keyword or type."
                : $"'{word}' is not a function this script declares (a native, a global, or a typo).";
            return;
        }

        Editor.GoToLine(target.StartLine, target.EndLine);
        SignatureText.Text = target.Signature;
        StatusText.Text = $"{target.Name} at line {target.StartLine:N0}.";
    }

    private void OnCheckClick(object? sender, RoutedEventArgs e) => RunCheck(announceClean: true);

    /// <summary>
    /// Runs the compile check over the editor's text and lists what it finds.
    /// </summary>
    /// <remarks>
    /// The single most valuable thing this panel can do. In JASS one undeclared variable fails the
    /// whole war3map.j, so config() never runs and the map shows an empty host lobby with no
    /// message anywhere. Before this, the only way to learn that an edit had broken the script was
    /// to save the map, launch the game and see an empty lobby.
    /// </remarks>
    private bool RunCheck(bool announceClean)
    {
        var sw = Stopwatch.StartNew();
        IReadOnlyList<JassIssue> issues;
        try
        {
            issues = JassScriptCheck.Check(Editor.Text);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"The check itself failed: {ex.Message}";
            return false;
        }
        var took = sw.ElapsedMilliseconds;

        if (issues.Count == 0)
        {
            Editor.ClearProblems();
            if (announceClean)
                StatusText.Text = $"No problems found ({took}ms).";
            return true;
        }

        var rows = issues
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Line)
            .Select(i => new JassEditorView.Problem
            {
                Badge = i.Severity.ToString().ToLowerInvariant(),
                Where = i.Line > 0 ? $"line {i.Line:N0}" : "script",
                Message = i.Message,
                Accent = new SolidColorBrush(i.Severity == DiagnosticSeverity.Error
                    ? Color.FromRgb(0xF1, 0x70, 0x70)
                    : Color.FromRgb(0xE0, 0xC0, 0x70)),
                Jump = new JumpCommand(() => Editor.GoToLine(i.Line)),
            })
            .ToList();

        Editor.ShowProblems(rows);
        Editor.MarkLines(issues.Where(i => i.Line > 0).Select(i => i.Line), ProblemTint);

        int errors = issues.Count(i => i.Severity == DiagnosticSeverity.Error);
        StatusText.Text = errors > 0
            ? $"{errors} error(s) would stop this script compiling, so the map would not host."
            : $"{issues.Count} warning(s).";
        return errors == 0;
    }

    private void OnRevertClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc) return;
        var keep = (FunctionList.SelectedItem as FunctionItem)?.Fn.Name;
        LoadFromDoc(doc, keep);
        ApplyButton.IsEnabled = false;
        RevertButton.IsEnabled = false;
        StatusText.Text = "Reverted to the script in the map.";
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc || _scriptFile is null)
        {
            StatusText.Text = "No map open.";
            return;
        }

        var edited = Editor.Text;
        if (string.Equals(edited, _loaded, StringComparison.Ordinal))
        {
            StatusText.Text = "No changes to apply.";
            return;
        }

        var keep = (FunctionList.SelectedItem as FunctionItem)?.Fn.Name;
        int caret = Editor.CaretLine;

        doc.AddOrReplaceRawFile(_scriptFile, Encoding.UTF8.GetBytes(edited));
        _unsavedEdits++;
        LoadFromDoc(doc, keep);
        Editor.GoToLine(caret);
        SaveButton.IsEnabled = true;

        // Check AFTER applying, and say so either way. Applying a script that will not compile is
        // allowed, since an edit can be half finished, but it must never be silent.
        bool ok = RunCheck(announceClean: false);
        StatusText.Text = ok
            ? $"Applied, {_unsavedEdits} unsaved edit(s), script still compiles."
            : $"Applied, {_unsavedEdits} unsaved edit(s). {StatusText.Text}";
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

        // Never overwrite the original map, save next to it as <name>.edited<ext> (the same
        // convention as the Object editor).
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

        StatusText.Text = $"Saved {editedPath}, {_unsavedEdits} edit(s) written.";
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
