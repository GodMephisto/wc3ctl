using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// Drives the dependency graph panel headlessly, exactly like a user: push an
/// object at it (the Objects-tab path), wait for the off-thread resolve to land,
/// then exercise the interactive canvas. The pipeline is two background hops
/// (object list, then closure resolve), so the test pumps the headless dispatcher
/// in a bounded loop until the canvas gains children. Corpus-gated: silently
/// passes when the map isn't on this machine.
/// </summary>
public class DependencyGraphViewTests
{
    private const string MapPath =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\Anime_WOS2_0.27d3.w3x";

    /// <summary>A real hero closure runs into the hundreds of objects, used here to prove
    /// the exclusion toggle and the nested trigger-function tree hold up at that size, not
    /// just on a hand-built fixture.</summary>
    private const string GggaPath =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\GGGA_V0.02b.w3x";

    /// <summary>The first load may open CASC (seconds); bounded so a hang still fails.</summary>
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(120);

    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Showing_a_unit_renders_graph_nodes_and_zoom_scales_the_canvas()
    {
        if (!File.Exists(MapPath)) return;

        var view = new DependencyGraphView();
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();

        var session = new MapSession { Current = MapDocument.Load(MapPath), MapPath = MapPath };
        view.ShowObject(session, ObjectKind.Unit, "H000");

        var canvas = Field<Canvas>(view, "GraphCanvas");
        PumpUntilRendered(canvas);

        Assert.Equal("H000", view.SelectedRawcode);
        Assert.True(canvas.Children.OfType<Border>().Any(),
            "resolving a unit should render node borders on the canvas");

        // Picker rows carry the kind, e.g. "Name · unit (H000)".
        var items = Field<SearchableComboBox>(view, "ObjectCombo").Items;
        Assert.True(items.Count > 0, "object picker should be populated");
        Assert.All(items, i => Assert.Contains(" · unit", i.Display));

        // Zooming via the toolbar button scales the canvas render transform.
        var zoom = Field<ScaleTransform>(view, "_zoomTransform");
        Assert.Equal(1.0, zoom.ScaleX, 3);
        Field<Button>(view, "ZoomInButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(zoom.ScaleX > 1.0,
            $"zoom in should raise the scale above 1.0 (saw {zoom.ScaleX})");

        // Reset puts the view back at 100%.
        Field<Button>(view, "ZoomResetButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.0, zoom.ScaleX, 3);
    }

    /// <summary>
    /// GGGA's H001 (Tohno Shiki) is a real, large hero closure (hundreds of objects,
    /// hundreds of script functions). Proves the tree nests trigger functions under an
    /// ability (or the labelled catch-all) rather than a flat parallel list, and that a
    /// node click toggles its excluded state and restyles, at real closure size, not just
    /// on a small hand-built fixture.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Large_hero_closure_nests_trigger_functions_and_supports_click_to_exclude()
    {
        if (!File.Exists(GggaPath)) return;

        var view = new DependencyGraphView();
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();

        var session = new MapSession { Current = MapDocument.Load(GggaPath), MapPath = GggaPath };
        view.ShowObject(session, ObjectKind.Unit, "H001");

        var canvas = Field<Canvas>(view, "GraphCanvas");
        PumpUntilRendered(canvas);

        Assert.Equal("H001", view.SelectedRawcode);

        int nodeCount = canvas.Children.OfType<Border>().Count();
        Assert.True(nodeCount > 100, $"a real hero closure should render >100 nodes, saw {nodeCount}");

        // The tree nests trigger functions under the ability that pulled them in (or
        // the labelled catch-all for anything unattributed), never a flat list.
        var depTree = Field<TreeView>(view, "DepTree");
        Assert.True(depTree.Items.Count > 0, "tree should have the root hero item");
        var headers = AllHeaders(depTree).ToList();
        Assert.Contains(headers, h => h.Contains("Trigger functions") || h.Contains("Other triggers"));

        // A left click on a node (exercised directly on the handler, simulating a full
        // pointer gesture headlessly is its own project, the click-vs-drag distance
        // check above it is simple enough to trust from review) toggles its excluded
        // state, restyles it, and is reported through the public ExcludedKeys surface.
        var objVisuals = Field<Dictionary<string, Border>>(view, "_objVisuals");
        string anAbility = objVisuals.Keys.First(k => k != "H001");
        var toggle = typeof(DependencyGraphView).GetMethod(
            "ToggleExcluded", BindingFlags.NonPublic | BindingFlags.Instance)!;

        toggle.Invoke(view, new object[] { objVisuals[anAbility] });
        Assert.Contains(anAbility, view.ExcludedKeys);
        Assert.True(objVisuals[anAbility].Opacity < 1.0, "an excluded node should render dimmed");

        toggle.Invoke(view, new object[] { objVisuals[anAbility] }); // click again restores it
        Assert.DoesNotContain(anAbility, view.ExcludedKeys);
        Assert.Equal(1.0, objVisuals[anAbility].Opacity);
    }

    /// <summary>Every TreeViewItem header text under <paramref name="root"/>, depth first.</summary>
    private static IEnumerable<string> AllHeaders(ItemsControl root)
    {
        foreach (var item in root.Items)
        {
            if (item is not TreeViewItem t) continue;
            if (t.Header is TextBlock tb) yield return tb.Text ?? "";
            foreach (var nested in AllHeaders(t)) yield return nested;
        }
    }

    /// <summary>Pump the headless dispatcher until the off-thread resolve posts its
    /// result (the canvas gains children), failing after a bounded wait.</summary>
    private static void PumpUntilRendered(Canvas canvas)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < LoadTimeout)
        {
            Dispatcher.UIThread.RunJobs();
            if (canvas.Children.Count > 0) return;
            Thread.Sleep(50); // background resolve still running; let it progress
        }
        throw new TimeoutException(
            $"dependency graph did not render within {LoadTimeout.TotalSeconds:F0}s");
    }

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;
}
