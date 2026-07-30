using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// Drives the hero wiring audit panel headlessly against real, previously-verified maps.
/// Corpus-gated, silently passes when a map is not on this machine.
/// </summary>
public class HeroWiringViewTests
{
    private const string TohnoV3 =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\1\1\Shiki_Tohno_v3.w3x";
    private const string ShikiArena =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\1\ShikiArena.w3x";

    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(60);

    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void A_clean_hero_reports_every_castable_wired_and_zero_problems()
    {
        if (!File.Exists(TohnoV3)) return;

        var view = new HeroWiringView();
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();

        view.ShowMap(new MapSession { Current = MapDocument.Load(TohnoV3), MapPath = TohnoV3 });
        var results = PumpUntilAudited(view);

        // The v3 rebuild's whole point was H001 wired end to end, so this is the regression
        // net for that fix, not just a fresh assertion.
        var hero = Assert.Single(results);
        Assert.Equal("H001", hero.Hero);
        Assert.Equal(8, hero.Wired);
        Assert.Equal(8, hero.Abilities.Count - hero.NotCastable);
        Assert.Equal(3, hero.NotCastable);
        Assert.Empty(hero.Problems);

        // The card list actually rendered something, not just that the data came back clean.
        var catalog = Field<CatalogEditorView>(view, "Catalog");
        var itemHost = Field<StackPanel>(catalog, "ItemHost");
        Assert.True(itemHost.Children.Count > 0, "the hero's card should be in the visual tree");
    }

    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Auditing_every_placed_hero_needs_no_input_and_covers_all_of_them()
    {
        if (!File.Exists(ShikiArena)) return;

        var view = new HeroWiringView();
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();

        // ShowMap alone, no rawcode and no owner, is the whole ask, audit everything placed.
        view.ShowMap(new MapSession { Current = MapDocument.Load(ShikiArena), MapPath = ShikiArena });
        var results = PumpUntilAudited(view);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Empty(r.Problems));

        var focus = Field<SearchableComboBox>(view, "_focus");
        // "All heroes" plus one row per audited hero.
        Assert.Equal(4, focus.Items.Count);
    }

    private static IReadOnlyList<HeroWiringResult> PumpUntilAudited(HeroWiringView view)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < LoadTimeout)
        {
            Dispatcher.UIThread.RunJobs();
            if (Field<IReadOnlyList<HeroWiringResult>?>(view, "_results") is { } results)
                return results;
            Thread.Sleep(50);
        }
        throw new TimeoutException($"hero wiring audit did not complete within {LoadTimeout.TotalSeconds:F0}s");
    }

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;
}
