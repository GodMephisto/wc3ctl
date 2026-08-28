// src/Wc3.Studio/Controls/JassEditorView.axaml.cs
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using Wc3.Model;

namespace Wc3.Studio.Controls;

/// <summary>
/// A JASS source editor. Line numbers, syntax highlighting, find and replace, go to definition,
/// current-line highlight, and a problems strip whose rows jump to their line.
/// </summary>
/// <remarks>
/// One control, composed by both the Script panel and the Triggers panel, because the alternative
/// was each of them re-declaring a monospace TextBox and neither of them getting any of this. The
/// Script panel's editor was a bare TextBox with default foreground, which is where "white text so
/// it is terrible to eyes" came from, and a bare TextBox cannot be made to show a line number no
/// matter how it is styled.
///
/// The document holds the WHOLE script rather than one function's lines. That is the change that
/// makes the rest work. A gutter can only show a true line number if line 1 of the document is
/// line 1 of the file, find can only be useful if it searches the file rather than the forty lines
/// on screen, and go to definition needs the definition to be in the document. The panel scrolls
/// to a function instead of slicing it out.
/// </remarks>
public partial class JassEditorView : UserControl
{
    /// <summary>One row of the problems strip.</summary>
    public sealed class Problem
    {
        public required string Badge { get; init; }
        public required string Where { get; init; }
        public required string Message { get; init; }
        public required IBrush Accent { get; init; }
        public required ICommand Jump { get; init; }
    }

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<JassEditorView, bool>(nameof(IsReadOnly));

    /// <summary>Raised when the caret moves to a different line (1-based).</summary>
    public event EventHandler<int>? CaretLineChanged;

    /// <summary>Raised when the user asks to go to a definition (F12, or Ctrl+click) on a word.
    /// The panel owns the lookup, since only it knows the function index.</summary>
    public event EventHandler<string>? GoToDefinitionRequested;

    /// <summary>Raised when the user asks who uses the word at the caret (Shift+F12, the same
    /// binding every IDE uses for it). The panel owns the search.</summary>
    public event EventHandler<string>? FindReferencesRequested;

    /// <summary>Raised on every text change while editable, so a panel can enable its Apply.</summary>
    public event EventHandler? TextEdited;

    private readonly LineHighlighter _lineMarks = new();
    private int _lastCaretLine = -1;
    private bool _suppressEdited;

    public JassEditorView()
    {
        InitializeComponent();

        Editor.Options.ConvertTabsToSpaces = false;   // war3map.j is tab-indented, keep it
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.ShowBoxForControlCharacters = true;
        Editor.Options.AllowScrollBelowDocument = true;

        // Ctrl+F / Ctrl+H, and F3 for next. Free, and the panel had no find at all before.
        SearchPanel.Install(Editor);

        Editor.TextArea.TextView.BackgroundRenderers.Add(_lineMarks);
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            int line = Editor.TextArea.Caret.Line;
            if (line == _lastCaretLine) return;
            _lastCaretLine = line;
            CaretLineChanged?.Invoke(this, line);
        };
        Editor.TextChanged += (_, _) =>
        {
            if (!_suppressEdited) TextEdited?.Invoke(this, EventArgs.Empty);
        };
        Editor.AddHandler(KeyDownEvent, OnEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Editor.TextArea.TextView.PointerPressed += OnTextViewPointerPressed;

        ApplyTheme();
    }

    /// <summary>Whether the source can be typed into.</summary>
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>The editor's current text. Reading it is how a panel gets the edited source.</summary>
    public string Text => Editor.Document?.Text ?? string.Empty;

    /// <summary>The caret's 1-based line.</summary>
    public int CaretLine => Editor.TextArea.Caret.Line;

    /// <summary>Total lines in the document.</summary>
    public int LineCount => Editor.Document?.LineCount ?? 0;

    /// <summary>
    /// Loads a whole script, with the natives set used for highlighting. Replacing the document
    /// rather than assigning Text keeps the undo stack from carrying the previous map's script.
    /// </summary>
    public void LoadScript(string source, IReadOnlySet<string>? natives)
    {
        _suppressEdited = true;
        try
        {
            Editor.SyntaxHighlighting = JassHighlighting.For(natives);
            Editor.Document = new TextDocument(source ?? string.Empty);
            Editor.IsReadOnly = IsReadOnly;
            ClearProblems();
            _lastCaretLine = -1;
        }
        finally { _suppressEdited = false; }
    }

    /// <summary>
    /// Upgrades the highlighting once the externals are known, without touching the document.
    /// </summary>
    /// <remarks>
    /// Exists so a panel can paint the script before it has finished analysing it. Deriving the
    /// externals of an 8.4 MB script costs 161ms and indexing it another 131ms, and a reader does
    /// not need either to start reading. The definition itself takes 30ms to build for 634 words,
    /// and reassigning it recolours only the visible lines.
    /// </remarks>
    public void SetNatives(IReadOnlySet<string>? natives)
    {
        if (Editor.Document is null) return;
        Editor.SyntaxHighlighting = JassHighlighting.For(natives);
    }

    /// <summary>Scrolls to a 1-based line and puts the caret on it. Optionally selects a span of
    /// lines, which is how a panel shows "this is the function you picked".</summary>
    public void GoToLine(int line, int throughLine = 0)
    {
        var doc = Editor.Document;
        if (doc is null || doc.LineCount == 0) return;

        line = Math.Clamp(line, 1, doc.LineCount);
        var start = doc.GetLineByNumber(line);

        // Select FIRST, then place the caret. Select moves the caret to the end of the
        // selection, so setting the caret first put it on the function's endfunction line and
        // the view scrolled to the wrong end of a long body.
        if (throughLine >= line)
        {
            var end = doc.GetLineByNumber(Math.Clamp(throughLine, 1, doc.LineCount));
            Editor.Select(start.Offset, end.EndOffset - start.Offset);
        }
        else
        {
            Editor.SelectionLength = 0;
        }

        Editor.TextArea.Caret.Line = line;
        Editor.TextArea.Caret.Column = 1;

        // Put the target a few lines below the top rather than flush against it, so the reader
        // keeps the context above it. Scrolling flush to the top hides the signature.
        Editor.ScrollToLine(Math.Max(1, line - 3));
        Editor.TextArea.Caret.BringCaretToView();
    }

    /// <summary>Tints a set of 1-based lines, for diagnostics or search hits.</summary>
    public void MarkLines(IEnumerable<int> lines, Color color)
    {
        _lineMarks.Set(lines, color);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    /// <summary>Removes every line tint.</summary>
    public void ClearMarks()
    {
        _lineMarks.Clear();
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    /// <summary>Shows the problems strip. An empty list hides it.</summary>
    public void ShowProblems(IReadOnlyList<Problem> problems)
    {
        ProblemsList.ItemsSource = problems;
        ProblemsBorder.IsVisible = problems.Count > 0;
    }

    /// <summary>Hides the problems strip and drops any line tints it put down.</summary>
    public void ClearProblems()
    {
        ProblemsList.ItemsSource = null;
        ProblemsBorder.IsVisible = false;
        ClearMarks();
    }

    /// <summary>The identifier under the caret, or empty when the caret is not on one.</summary>
    public string WordAtCaret()
    {
        var doc = Editor.Document;
        if (doc is null) return string.Empty;
        return WordAt(doc, Editor.CaretOffset);
    }

    private static string WordAt(TextDocument doc, int offset)
    {
        if (doc.TextLength == 0) return string.Empty;
        offset = Math.Clamp(offset, 0, doc.TextLength);

        int start = offset;
        while (start > 0 && IsWordChar(doc.GetCharAt(start - 1))) start--;
        int end = offset;
        while (end < doc.TextLength && IsWordChar(doc.GetCharAt(end))) end++;
        return end > start ? doc.GetText(start, end - start) : string.Empty;

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // Shift+F12 first, since F12 alone would otherwise swallow it.
        if (e.Key == Key.F12 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var word = WordAtCaret();
            if (word.Length > 0)
            {
                FindReferencesRequested?.Invoke(this, word);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.F12 || (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            var word = WordAtCaret();
            if (word.Length > 0)
            {
                GoToDefinitionRequested?.Invoke(this, word);
                e.Handled = true;
            }
        }
    }

    private void OnTextViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        var view = Editor.TextArea.TextView;
        var pos = view.GetPositionFloor(e.GetPosition(view) + view.ScrollOffset);
        if (pos is null || Editor.Document is null) return;

        int offset = Editor.Document.GetOffset(pos.Value.Location);
        var word = WordAt(Editor.Document, offset);
        if (word.Length == 0) return;

        GoToDefinitionRequested?.Invoke(this, word);
        e.Handled = true;
    }

    private void ApplyTheme()
    {
        // The shell is dark, and AvaloniaEdit's defaults are light. Left alone the editor would
        // be a bright panel in a dark app, which is the other half of "terrible to eyes".
        Editor.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Editor.Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
        Editor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x81));
        Editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x4F, 0x78));
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.TextView.CurrentLineBackground =
            new SolidColorBrush(Color.FromRgb(0x28, 0x2C, 0x34));
        Editor.TextArea.TextView.CurrentLineBorder =
            new Pen(new SolidColorBrush(Color.FromRgb(0x32, 0x38, 0x42)));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsReadOnlyProperty && Editor is not null)
            Editor.IsReadOnly = IsReadOnly;
    }

    /// <summary>
    /// Paints a background behind chosen lines. AvaloniaEdit has no per-line marker of its own,
    /// and a background renderer is the cheap way to get one, it draws only the visible lines.
    /// </summary>
    private sealed class LineHighlighter : IBackgroundRenderer
    {
        private readonly Dictionary<int, IBrush> _brushes = new();

        public KnownLayer Layer => KnownLayer.Background;

        public void Set(IEnumerable<int> lines, Color color)
        {
            var brush = new SolidColorBrush(color);
            foreach (var line in lines)
                if (line > 0) _brushes[line] = brush;
        }

        public void Clear() => _brushes.Clear();

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_brushes.Count == 0 || textView.Document is null) return;
            foreach (var visual in textView.VisualLines)
            {
                int number = visual.FirstDocumentLine.LineNumber;
                if (!_brushes.TryGetValue(number, out var brush)) continue;
                double top = visual.VisualTop - textView.ScrollOffset.Y;
                drawingContext.FillRectangle(brush,
                    new Rect(0, top, Math.Max(textView.Bounds.Width, 0), visual.Height));
            }
        }
    }
}
