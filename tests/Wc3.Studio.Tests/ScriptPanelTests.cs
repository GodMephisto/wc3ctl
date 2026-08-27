// tests/Wc3.Studio.Tests/ScriptPanelTests.cs
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// The Script panel, end to end through its real controls. The behaviour worth pinning is not the
/// highlighting, it is that the whole script is in the editor so the gutter's line number is the
/// file's own, that the symbol list and the editor agree about which function you are looking at,
/// and that a broken edit is reported rather than saved silently.
/// </summary>
public class ScriptPanelTests
{
    private const string Script = """
        globals
            integer udg_Count = 0
        endglobals

        function Helper takes nothing returns nothing
            call BJDebugMsg("hi")
        endfunction

        function config takes nothing returns nothing
            call SetPlayers(2)
            call DefineStartLocation(0, 0., 0.)
        endfunction

        function main takes nothing returns nothing
            call Helper()
        endfunction
        """;

    private static ScriptView Shown()
    {
        var view = new ScriptView();
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.UpdateLayout();
        return view;
    }

    /// <summary>
    /// Opens a map on the panel and waits for its background index to land.
    /// </summary>
    /// <remarks>
    /// The panel paints the script before it indexes it, so the symbol list arrives a moment
    /// after the text. That is the point of the design, and it means a test that asserted
    /// immediately after ShowMap would race the index rather than measure it.
    /// </remarks>
    private static void Open(ScriptView view, MapDocument? doc)
    {
        view.ShowMap(new MapSession { Current = doc });
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (!TextOf(view, "HeaderText").EndsWith("indexing…", StringComparison.Ordinal))
                return;
            Thread.Sleep(5);
        }
        throw new TimeoutException("the script panel never finished indexing");
    }

    private static MapDocument MapWith(string script)
    {
        var doc = BlankMap.Create();
        doc.AddOrReplaceRawFile("war3map.j", Encoding.UTF8.GetBytes(script));
        return doc;
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static string TextOf(Control root, string name) =>
        Named<TextBlock>(root, name).Text ?? "";

    private static ListBox FunctionList(ScriptView view) => Named<ListBox>(view, "FunctionList");
    private static JassEditorView Editor(ScriptView view) =>
        view.GetVisualDescendants().OfType<JassEditorView>().First();

    [AvaloniaFact]
    public void The_panel_loads_the_whole_script_and_lists_its_functions()
    {
        var view = Shown();
        Open(view, MapWith(Script));

        var editor = Editor(view);
        Assert.Equal(Script.Split('\n').Length, editor.LineCount);

        var header = TextOf(view, "HeaderText");
        Assert.Contains("functions", header);
        Assert.Contains("war3map.j", header);

        // Helper, config and main.
        Assert.Equal(3, FunctionList(view).ItemCount);
    }

    [AvaloniaFact]
    public void Picking_a_function_scrolls_the_editor_to_its_real_line()
    {
        var view = Shown();
        Open(view, MapWith(Script));

        var fns = JassFunctionIndex.Parse(Script);
        var main = fns.Single(f => f.Name == "main");

        var list = FunctionList(view);
        list.SelectedIndex = fns.ToList().FindIndex(f => f.Name == "main");

        // The whole point of holding the document whole. The caret is on the file's line, not on
        // line 1 of a slice, so the header, the gutter and any diagnostic all agree.
        Assert.Equal(main.StartLine, Editor(view).CaretLine);
        Assert.Contains("main", TextOf(view, "SignatureText"));
        Assert.Contains($"line {main.StartLine:N0}", TextOf(view, "CaretText"));
    }

    [AvaloniaFact]
    public void The_symbol_list_follows_the_caret_as_well_as_leading_it()
    {
        // On a merged arena script with three thousand functions, "which function am I in" is
        // asked far more often than "take me to this one".
        var view = Shown();
        Open(view, MapWith(Script));

        var config = JassFunctionIndex.Parse(Script).Single(f => f.Name == "config");
        Editor(view).GoToLine(config.StartLine + 1);   // inside the body, not on the signature

        Assert.Contains("config", TextOf(view, "SignatureText"));
    }

    [AvaloniaFact]
    public void Filtering_narrows_the_list_and_says_by_how_much()
    {
        var view = Shown();
        Open(view, MapWith(Script));

        Named<TextBox>(view, "SearchBox").Text = "Help";
        Assert.Equal(1, FunctionList(view).ItemCount);
        Assert.Contains("1 of 3", TextOf(view, "ListCountText"));

        Named<TextBox>(view, "SearchBox").Text = "";
        Assert.Equal(3, FunctionList(view).ItemCount);
    }

    [AvaloniaFact]
    public void Searching_bodies_finds_a_call_that_no_function_name_contains()
    {
        var view = Shown();
        Open(view, MapWith(Script));

        // "SetPlayers" appears only inside config's body.
        Named<TextBox>(view, "SearchBox").Text = "SetPlayers";
        Assert.Equal(0, FunctionList(view).ItemCount);

        Named<CheckBox>(view, "SearchBodiesCheck").IsChecked = true;
        Assert.Equal(1, FunctionList(view).ItemCount);
    }

    [AvaloniaFact]
    public void Check_reports_a_clean_script_as_clean()
    {
        var view = Shown();
        Open(view, MapWith(Script));

        Invoke(view, "RunCheck", true);
        Assert.Contains("No problems", TextOf(view, "StatusText"));
    }

    [AvaloniaFact]
    public void Check_names_the_line_of_a_break_that_would_stop_the_map_hosting()
    {
        // The failure this panel exists to catch. In JASS one undeclared assignment target fails
        // the whole war3map.j, so config never runs and the map hosts an empty lobby with no
        // message anywhere. Before the check, the only way to find out was to launch the game.
        const string Broken = """
            function config takes nothing returns nothing
                set udg_NeverDeclared = 1
                call SetPlayers(2)
            endfunction

            function main takes nothing returns nothing
            endfunction
            """;

        var view = Shown();
        Open(view, MapWith(Broken));

        Invoke(view, "RunCheck", true);
        var status = TextOf(view, "StatusText");
        Assert.Contains("error", status);
        Assert.Contains("would not host", status);

        // And the finding is on screen where it can be clicked, not only in the status line.
        var problems = view.GetVisualDescendants().OfType<Border>()
            .First(b => b.Name == "ProblemsBorder");
        Assert.True(problems.IsVisible);
    }

    [AvaloniaFact]
    public void Reopening_the_panel_on_a_map_with_no_script_says_so()
    {
        var view = Shown();
        Open(view, MapWith(Script));
        Assert.Contains("war3map.j", TextOf(view, "HeaderText"));

        Open(view, null);
        Assert.Equal("No script in map", TextOf(view, "HeaderText"));
        // An empty document still reports one (empty) line, so the check is that the
        // previous map's script is gone, not that the count is zero.
        Assert.True(Editor(view).LineCount <= 1);
        Assert.DoesNotContain("Helper", Editor(view).Text);
    }

    private static void Invoke(ScriptView view, string method, params object[] args) =>
        view.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, args);
}
