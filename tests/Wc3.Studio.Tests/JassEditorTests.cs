// tests/Wc3.Studio.Tests/JassEditorTests.cs
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Wc3.Model;
using Wc3.Studio.Controls;
using Xunit.Abstractions;

namespace Wc3.Studio.Tests;

/// <summary>
/// The JASS editor control. The Script panel used to show one function's lines in a bare TextBox
/// with the default foreground, which is where "white text so it is terrible to eyes" came from
/// and why there were no line numbers, no find and no way to reach a definition.
///
/// The design change these pin is that the document now holds the WHOLE script. That is what makes
/// a true line number, a script-wide find and go to definition possible at all, and it is only
/// viable if a real merged arena script loads fast enough, which the last test measures rather
/// than assumes.
/// </summary>
public class JassEditorTests
{
    private readonly ITestOutputHelper _out;
    public JassEditorTests(ITestOutputHelper output) => _out = output;

    private static JassEditorView Shown()
    {
        var view = new JassEditorView();
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        return view;
    }

    private const string Sample = """
        globals
            integer udg_Count = 0
        endglobals

        function Trig_Hero_Conditions takes nothing returns boolean
            return GetSpellAbilityId() == 'A00R'
        endfunction

        function Trig_Hero_Actions takes nothing returns nothing
            local unit u = GetTriggerUnit()
            call BJDebugMsg("cast")   // a comment
            set u = null
        endfunction
        """;

    [AvaloniaFact]
    public void The_document_holds_the_whole_script_so_line_numbers_are_the_files_own()
    {
        var view = Shown();
        view.LoadScript(Sample, natives: null);

        // 15 lines in the sample. If the panel sliced a function out instead, the gutter would
        // say line 1 where the file says line 9, and every diagnostic line number would be a lie.
        Assert.Equal(Sample.Split('\n').Length, view.LineCount);
        Assert.Contains("Trig_Hero_Actions", view.Text);
        Assert.Contains("globals", view.Text);
    }

    [AvaloniaFact]
    public void GoToLine_puts_the_caret_on_the_line_asked_for()
    {
        var view = Shown();
        view.LoadScript(Sample, natives: null);

        var fns = JassFunctionIndex.Parse(Sample);
        var target = fns.Single(f => f.Name == "Trig_Hero_Actions");

        view.GoToLine(target.StartLine, target.EndLine);
        Assert.Equal(target.StartLine, view.CaretLine);
    }

    [AvaloniaFact]
    public void GoToLine_clamps_rather_than_throwing_on_a_line_past_the_end()
    {
        // Diagnostics and search hits arrive as line numbers from other passes. One of them
        // being stale must not take the panel down.
        var view = Shown();
        view.LoadScript(Sample, natives: null);

        view.GoToLine(100000);
        Assert.Equal(view.LineCount, view.CaretLine);
        view.GoToLine(-5);
        Assert.Equal(1, view.CaretLine);
    }

    [AvaloniaFact]
    public void GoToLine_on_an_empty_document_does_nothing()
    {
        var view = Shown();
        view.LoadScript("", natives: null);
        view.GoToLine(1);      // must not throw
        view.ClearMarks();
        view.MarkLines(new[] { 1, 2 }, Colors.Red);
    }

    [AvaloniaFact]
    public void The_word_at_the_caret_is_the_identifier_go_to_definition_uses()
    {
        var view = Shown();
        view.LoadScript(Sample, natives: null);

        var fns = JassFunctionIndex.Parse(Sample);
        var target = fns.Single(f => f.Name == "Trig_Hero_Conditions");
        view.GoToLine(target.StartLine);

        // Caret lands at column 1 of "function Trig_Hero_Conditions takes ...", so the word
        // under it is the keyword. That is correct, and it is what the panel then looks up and
        // does not find, which is the behaviour a lookup miss must have.
        Assert.Equal("function", view.WordAtCaret());
    }

    [AvaloniaFact]
    public void Highlighting_is_built_from_the_shared_vocabulary()
    {
        // Not a rendering test. It pins that the definition builds at all from JassSyntax, since
        // a malformed generated xshd throws on load and would take the whole panel with it.
        var def = JassHighlighting.For(natives: null);
        Assert.Equal("JASS", def.Name);

        var withNatives = JassHighlighting.For(
            new HashSet<string> { "CreateUnit", "GetTriggerUnit", "BJDebugMsg" });
        Assert.Equal("JASS", withNatives.Name);
    }

    [AvaloniaFact]
    public void A_native_name_that_is_not_an_identifier_cannot_break_the_definition()
    {
        // NativesIn reads the game's own common.j. A parse slip there must degrade highlighting,
        // never throw, so the words are filtered rather than trusted.
        var def = JassHighlighting.For(new HashSet<string>
        {
            "Fine", "has space", "<bad>", "", "1leading", "ok_2",
        });
        Assert.Equal("JASS", def.Name);
    }

    [AvaloniaFact]
    public void Problems_hide_themselves_when_there_are_none()
    {
        var view = Shown();
        view.LoadScript(Sample, natives: null);
        view.ShowProblems(Array.Empty<JassEditorView.Problem>());
        // A clean script must cost no vertical space, so the strip is collapsed and not merely
        // empty.
        var border = view.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Name == "ProblemsBorder");
        Assert.NotNull(border);
        Assert.False(border!.IsVisible);
    }

    /// <summary>
    /// The measurement the whole-document design stands on. A real merged arena script is 8.4 MB
    /// and about 250 thousand lines. If loading that blocks for seconds the design is wrong and
    /// the panel has to go back to slicing, so this measures rather than assumes.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void A_real_eight_megabyte_script_loads_fast_enough_to_hold_whole()
    {
        var path = RealScriptOrEmpty();
        if (path.Length == 0) { _out.WriteLine("no corpus script, skipped"); return; }

        var source = File.ReadAllText(path, System.Text.Encoding.Latin1);
        var view = Shown();

        // Split the load, because "LoadScript costs 275ms" does not say whether to attack the
        // document or the highlighting, and they have completely different fixes.
        var fns = JassFunctionIndex.Parse(source);
        var externals = JassSyntax.ExternalCalls(source, fns.Select(f => f.Name));

        var sw = Stopwatch.StartNew();
        var def = JassHighlighting.For(externals);
        var buildDef = sw.Elapsed;

        sw.Restart();
        _ = new AvaloniaEdit.Document.TextDocument(source);
        var buildDoc = sw.Elapsed;

        _out.WriteLine($"highlighting definition {buildDef.TotalMilliseconds:F0}ms "
                     + $"({externals.Count:N0} externals), TextDocument "
                     + $"{buildDoc.TotalMilliseconds:F0}ms");

        sw.Restart();
        view.LoadScript(source, natives: null);
        var load = sw.Elapsed;

        sw.Restart();
        _ = JassFunctionIndex.Parse(source);
        var index = sw.Elapsed;

        sw.Restart();
        view.GoToLine(fns[^1].StartLine, fns[^1].EndLine);
        var jump = sw.Elapsed;

        _out.WriteLine($"{source.Length:N0} chars, {view.LineCount:N0} lines, "
                     + $"{fns.Count:N0} functions");
        _out.WriteLine($"load {load.TotalMilliseconds:F0}ms  index {index.TotalMilliseconds:F0}ms  "
                     + $"jump to last function {jump.TotalMilliseconds:F0}ms");

        Assert.Equal(source.Split('\n').Length, view.LineCount);
        // Generous, because a CI machine is not this one. The point is to catch an order of
        // magnitude, a design that needs seconds per keystroke, not to police milliseconds.
        Assert.True(load.TotalMilliseconds < 2000,
            $"loading the whole script took {load.TotalMilliseconds:F0}ms, so holding it whole is "
            + "not viable and the panel must slice again");
        Assert.True(jump.TotalMilliseconds < 1000,
            $"jumping to a function took {jump.TotalMilliseconds:F0}ms");
    }

    private static string RealScriptOrEmpty()
    {
        // Extracted by the encoding sweep during this work, and left in the scratchpad. Falls
        // back to nothing rather than pretending, so a skip is visible in the output above.
        var candidates = new[]
        {
            Path.Combine(Path.GetTempPath(), "wc3ctl-corpus-war3map.j"),
        };
        return candidates.FirstOrDefault(File.Exists) ?? "";
    }
}
