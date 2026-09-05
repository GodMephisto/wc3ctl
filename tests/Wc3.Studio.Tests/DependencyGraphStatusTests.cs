using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// The dependency panel's status line used to be one uniform grey described in the markup as
/// "unobtrusive", and every message went into it the same way, a resolver note and "this result
/// is incomplete" alike, joined by semicolons in the order the resolver happened to emit them.
///
/// That is the exact failure this repo keeps paying for, an incomplete result that reads as
/// exhaustive. Resolving one hero on GGGA_V0.05 DOES hit the carry cap, so this is not a
/// hypothetical. These tests pin that a truncated bundle says so first and in the problem colour,
/// and that an ordinary bundle is left alone.
/// </summary>
public class DependencyGraphStatusTests
{
    [AvaloniaFact]
    public void A_truncated_bundle_leads_with_the_warning_and_is_not_grey()
    {
        var view = new DependencyGraphView();
        InvokeRenderBundle(view, BundleWith(
            "note: carried 512 object(s) a handler spawns or grants at runtime",
            "note: script-spawned object cap (512) reached, 295 more not carried"));

        var status = Field<TextBlock>(view, "StatusText");
        Assert.StartsWith("INCOMPLETE", status.Text ?? "");
        Assert.Same(StudioPalette.Problem, status.Foreground);

        // The resolver's own notes still follow. Leading with the warning must not cost the
        // detail that says which cap and by how much.
        Assert.Contains("295 more not carried", status.Text ?? "");
    }

    [AvaloniaFact]
    public void An_ordinary_note_stays_a_note()
    {
        var view = new DependencyGraphView();
        InvokeRenderBundle(view, BundleWith("note: skipped 243 asset path(s) in 2 bank function(s)"));

        var status = Field<TextBlock>(view, "StatusText");
        Assert.DoesNotContain("INCOMPLETE", status.Text ?? "");
        Assert.Same(StudioPalette.Muted, status.Foreground);
    }

    [AvaloniaFact]
    public void A_clean_bundle_says_nothing()
    {
        var view = new DependencyGraphView();
        InvokeRenderBundle(view, BundleWith());

        Assert.Equal("", Field<TextBlock>(view, "StatusText").Text ?? "");
    }

    [AvaloniaFact]
    public void The_summary_names_whose_counts_it_is_showing()
    {
        // A hero whose bundle holds five hundred objects showing "2 objects" here reads like a
        // failed resolve. The line has to say the counts are deliberately the unit's own.
        var view = new DependencyGraphView();
        InvokeRenderBundle(view, BundleWith());

        var summary = Field<TextBlock>(view, "SummaryText").Text ?? "";
        Assert.StartsWith("2 objects", summary);
        Assert.Contains("this unit's own", summary);
    }

    /// <summary>Smallest bundle the panel will render, a root reaching one ability through a real
    /// field, plus whatever diagnostics the test needs.</summary>
    private static UnitBundle BundleWith(params string[] diagnostics) =>
        new("H001", "Test Hero",
            new List<BundleNode>
            {
                new("H001", ObjectKind.Unit, "Test Hero", true),
                new("A001", ObjectKind.Ability, "Real Skill", true),
            },
            new List<BundleFile>(),
            new List<string>(),
            new List<BundleEdge> { new("H001", "A001", "uhab") },
            diagnostics,
            Array.Empty<BundleFunction>());

    private static void InvokeRenderBundle(DependencyGraphView view, UnitBundle bundle) =>
        typeof(DependencyGraphView)
            .GetMethod("RenderBundle", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, new object[] { bundle });

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;
}
