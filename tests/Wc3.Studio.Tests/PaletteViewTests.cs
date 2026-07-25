using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// Drives the palette panel headlessly, exactly like a user: open a map, wait for the
/// async catalog load to land, then arm a tile. The catalog builds off the UI thread
/// (Task.Run + a dispatcher post), so the tests pump the headless dispatcher in a
/// bounded loop until groups appear. Corpus-gated: silently passes when the map isn't
/// on this machine.
/// </summary>
public class PaletteViewTests
{
    private const string MapPath =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\Anime_WOS2_0.27d3.w3x";

    /// <summary>The first load may open CASC (seconds); bounded so a hang still fails.</summary>
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(120);

    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Opening_a_map_loads_grouped_entries_asynchronously()
    {
        if (!File.Exists(MapPath)) return;

        var view = new PaletteView();
        var window = new Window { Width = 900, Height = 650, Content = view };
        window.Show();

        view.ShowMap(new MapSession { Current = MapDocument.Load(MapPath), MapPath = MapPath });

        // ShowMap returns immediately (load is off-thread); pump until results land.
        var groups = PumpUntilLoaded(view);

        Assert.True(groups.Any(), "palette should build groups");
        Assert.True(groups.All(g => !string.IsNullOrWhiteSpace(g.Title)), "each group has a visible title");
        var tiles = groups.SelectMany(g => g.Tiles).ToList();
        Assert.True(tiles.Any(), "palette should list placeable tiles");
        // Units exist even map-only (the map's own w3u); doodads need an install.
        Assert.True(tiles.Any(r => r.Kind == ObjectKind.Unit), "palette should list at least one unit");
    }

    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Selecting_a_tile_arms_placement()
    {
        if (!File.Exists(MapPath)) return;

        var view = new PaletteView();
        var window = new Window { Width = 900, Height = 650, Content = view };
        window.Show();

        view.ShowMap(new MapSession { Current = MapDocument.Load(MapPath), MapPath = MapPath });
        var groups = PumpUntilLoaded(view);

        PaletteRow? observed = null;
        int raised = 0;
        view.PlacementChanged += (_, row) => { observed = row; raised++; };

        var target = groups.SelectMany(g => g.Tiles).First();
        // Arm the tile through the same private path a tile click uses.
        typeof(PaletteView).GetMethod("Select", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, new object?[] { target });
        Dispatcher.UIThread.RunJobs();

        Assert.True(raised > 0, "arming a tile must raise PlacementChanged");
        Assert.Same(target, observed);
        Assert.Same(target, view.SelectedPlacement);
        Assert.True(target.IsSelected, "the armed tile is highlighted");
    }

    /// <summary>Pump the headless dispatcher until the async catalog load posts its
    /// result (groups appear), failing after a bounded wait.</summary>
    private static List<PaletteGroup> PumpUntilLoaded(PaletteView view)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < LoadTimeout)
        {
            Dispatcher.UIThread.RunJobs();
            var groups = Groups(view);
            if (groups.Count > 0) return groups;
            Thread.Sleep(50); // background load still running; let it progress
        }
        throw new TimeoutException($"palette did not load within {LoadTimeout.TotalSeconds:F0}s");
    }

    private static List<PaletteGroup> Groups(PaletteView view) =>
        Field<List<PaletteGroup>>(view, "_groups") ?? new List<PaletteGroup>();

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;
}
