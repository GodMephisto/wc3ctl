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
