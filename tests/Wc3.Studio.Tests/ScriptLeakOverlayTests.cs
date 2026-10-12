// tests/Wc3.Studio.Tests/ScriptLeakOverlayTests.cs
using System.Collections;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// The script view's leak overlay. Feeding the editor a known leak and pressing Leaks must put the
/// finding on screen (the problems strip, ranked by heat) rather than only in the status line.
/// </summary>
public class ScriptLeakOverlayTests
{
    // Leaky is reached from a periodic timer (hot). Its local location is never destroyed, so
    // ScriptLeaksCommand reports one never-destroyed location leak in hot code.
    private const string LeakyScript = """
        function Leaky takes nothing returns nothing
            local location l = Location(0., 0.)
            call BJDebugMsg(GetUnitName(GetTriggerUnit()))
        endfunction

        function Init takes nothing returns nothing
            call TimerStart(CreateTimer(), 0.03, true, function Leaky)
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

    private static MapDocument MapWith(string script)
    {
        var doc = BlankMap.Create();
        doc.AddOrReplaceRawFile("war3map.j", Encoding.UTF8.GetBytes(script));
        return doc;
    }

    private static void Open(ScriptView view, MapDocument doc)
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

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;

    private static string TextOf(Control root, string name) => Field<TextBlock>(root, name).Text ?? "";

    [AvaloniaFact]
    public void Leaks_marks_the_leaking_lines_and_lists_them_by_heat()
    {
        var view = Shown();
        Open(view, MapWith(LeakyScript));

        Field<Button>(view, "LeaksButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // The finding is on screen where it can be clicked, not only in the status line.
        var editor = view.GetVisualDescendants().OfType<JassEditorView>().First();
        var problems = editor.GetVisualDescendants().OfType<ItemsControl>()
            .First(c => c.Name == "ProblemsList");
        var rows = (problems.ItemsSource as IEnumerable)?.Cast<JassEditorView.Problem>().ToList()
            ?? new List<JassEditorView.Problem>();

        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => r.Badge == "hot");
        Assert.Contains("leak", TextOf(view, "StatusText"), StringComparison.OrdinalIgnoreCase);

        var border = editor.GetVisualDescendants().OfType<Border>()
            .First(b => b.Name == "ProblemsBorder");
        Assert.True(border.IsVisible);
    }
}
