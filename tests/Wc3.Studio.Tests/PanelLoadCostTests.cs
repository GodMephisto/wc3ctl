// tests/Wc3.Studio.Tests/PanelLoadCostTests.cs
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Panels;
using Xunit.Abstractions;

namespace Wc3.Studio.Tests;

/// <summary>
/// What each panel costs to open, on a real map. Measurement first, because "the Studio is laggy"
/// names a symptom and not a panel, and the panels are lazy so only the one you click pays.
///
/// This is a measurement harness as much as a test. It prints a per-panel table so a regression is
/// visible as a number, and it fails only on a budget generous enough to catch an order of
/// magnitude rather than to police a machine's mood.
/// </summary>
public class PanelLoadCostTests
{
    private readonly ITestOutputHelper _out;
    public PanelLoadCostTests(ITestOutputHelper output) => _out = output;

    /// <summary>Panels a click on a tab opens, in the order the tabs sit in.</summary>
    private static IEnumerable<(string Name, Func<UserControl> Make)> Panels()
    {
        yield return ("MapInfo", () => new MapInfoView());
        yield return ("Files", () => new FilesView());
        yield return ("Script", () => new ScriptView());
        yield return ("Triggers", () => new TriggerView());
        yield return ("Strings", () => new StringImportView());
        yield return ("Regions", () => new RegionsView());
        yield return ("Cameras", () => new CamerasView());
        yield return ("Sounds", () => new SoundsView());
        yield return ("Players", () => new PlayerForceView());
        // The heavy ones. Objects opens the whole object-data model, Terrain renders a heightmap,
        // Palette loads art, and the two audit panels walk the script. Leaving them out of the
        // table would have measured only the panels that were already fast.
        yield return ("Objects", () => new ObjectEditorView());
        yield return ("Terrain", () => new TerrainView());
        yield return ("Palette", () => new PaletteView());
        yield return ("Dependencies", () => new DependencyGraphView());
        yield return ("HeroWiring", () => new HeroWiringView());
    }

    private static string RealMapOrEmpty()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                               "Warcraft III", "Maps", "Download");
        if (!Directory.Exists(dir)) return "";
        return Directory.EnumerateFiles(dir, "Anime_WOS2_*.w3x")
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "";
    }

    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Every_panel_opens_within_a_budget_on_a_real_map()
    {
        var path = RealMapOrEmpty();
        if (path.Length == 0) { _out.WriteLine("no real map, skipped"); return; }

        // The Studio warms the game data at startup, so a panel never pays the CASC open on the
        // UI thread. Do the same here, or the table measures a cost the app no longer has and
        // attributes it to whichever panel happens to be measured first.
        GameDataWarmup.Begin(null);
        var warm = Stopwatch.StartNew();
        GameDataWarmup.Running?.Wait(TimeSpan.FromMinutes(2));
        _out.WriteLine($"game data warm-up {warm.ElapsedMilliseconds}ms (off the UI thread in "
                     + "the app, waited for here so the panel numbers are the panels')");

        var sw = Stopwatch.StartNew();
        var doc = MapDocument.Load(path);
        _out.WriteLine($"map            {Path.GetFileName(path)}");
        _out.WriteLine($"MapDocument.Load  {sw.ElapsedMilliseconds,6}ms  "
                     + $"{doc.Files.Count:N0} entries");
        _out.WriteLine("");
        // Two numbers per panel, and the difference matters. "blocked" is how long ShowMap holds
        // the UI thread, which is what reads as lag. "settled" adds any background work the panel
        // starts, which is when it is fully populated. A panel that paints in 250ms and finishes
        // in 550ms feels fast, one that blocks for 550ms does not.
        _out.WriteLine("panel         blocked   settled    reopen");

        var session = new MapSession { Current = doc, MapPath = path };
        var slow = new List<string>();

        foreach (var (name, make) in Panels())
        {
            var panel = make();
            var window = new Window { Width = 1280, Height = 800, Content = panel };
            window.Show();
            window.UpdateLayout();

            var panelClock = Stopwatch.StartNew();
            Open(panel, session);
            long blocked = panelClock.ElapsedMilliseconds;
            Settle();
            long settled = panelClock.ElapsedMilliseconds;
            window.UpdateLayout();

            long again = Time(() => { Open(panel, session); Settle(); });
            window.UpdateLayout();

            _out.WriteLine($"{name,-12} {blocked,7}ms {settled,7}ms {again,7}ms");
            if (blocked > 500) slow.Add($"{name} blocks {blocked}ms");
        }

        // Where the Script panel's cost goes, since it is the one panel that pays the same price
        // on every open rather than caching after the first.
        {
            var script = doc.GetFile("war3map.j");
            if (script is not null)
            {
                var text = System.Text.Encoding.UTF8.GetString(script.CurrentBytes);
                long decode = Time(() => System.Text.Encoding.UTF8.GetString(script.CurrentBytes));
                long index = Time(() => JassFunctionIndex.Parse(text));
                var fns = JassFunctionIndex.Parse(text);
                long ext = Time(() => JassSyntax.ExternalCalls(text, fns.Select(f => f.Name)));
                _out.WriteLine("");
                _out.WriteLine($"script {text.Length:N0} chars: decode {decode}ms, "
                             + $"index {index}ms, externals {ext}ms");
            }
        }

        // Where the Objects panel's cost goes. It is the biggest UI-thread block in the app, so
        // it matters whether that is the object-data parse (movable off the thread) or the list
        // rendering (not).
        _out.WriteLine("");
        // The Objects panel used to block for 1,704ms and none of it was the map. These two
        // guard that, because if either grew the panel would go back to looking hung and the
        // warm-up would take the blame.
        {
            var fresh = MapDocument.Load(path);
            foreach (var kind in Wc3.Commands.ObjectKinds.All)
            {
                var clock = Stopwatch.StartNew();
                var items = Wc3.Commands.ObjectListCommand.Execute(fresh, kind, null).Items;
                long took = clock.ElapsedMilliseconds;
                _out.WriteLine($"  cold ObjectListCommand {kind,-14} {took,5}ms  {items.Count,5} items");
                Assert.True(took < 400, $"listing {kind} took {took}ms");
            }

            var first = Wc3.Commands.ObjectListCommand
                .Execute(fresh, Wc3.Commands.ObjectKind.Unit, null).Items.FirstOrDefault();
            if (first is not null)
            {
                var clock = Stopwatch.StartNew();
                var form = Wc3.Commands.ObjectFormCommand.Execute(
                    fresh, Wc3.Commands.ObjectKind.Unit, first.Rawcode, null);
                long took = clock.ElapsedMilliseconds;
                _out.WriteLine($"  ObjectFormCommand {took}ms, {form.FieldCount} fields");
                Assert.True(took < 400, $"building one object's form took {took}ms");
            }
        }

        Assert.True(slow.Count == 0,
            "a panel holds the UI thread for over half a second, which reads as the app "
            + "hanging: " + string.Join(", ", slow));
    }

    private static void Open(UserControl panel, MapSession session)
    {
        // Every panel implements IMapPanel, but a couple take a wider entry point too. ShowMap is
        // the one a tab click uses, so it is the one measured.
        if (panel is IMapPanel p) p.ShowMap(session);
    }

    // Settle and Time moved to UiWork when FilesPanelNamelessCostTests needed the same
    // loop, so every measurement test agrees on what "settled" means.
    private static void Settle() => UiWork.Settle();

    private static long Time(Action a) => UiWork.Time(a);
}
