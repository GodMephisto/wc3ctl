using Wc3.Commands;

namespace Wc3.Tests;

/// <summary>
/// Pins the shared rule that separates a hero's real dependencies from the script closure
/// over-carry, and the CLI renderer that consumes it. Both front ends (the Studio panel and
/// this CLI) must agree, so the one definition lives in Wc3.Commands and is exercised here.
/// </summary>
public class BundleStructureAndRenderTests
{
    // A small closure. The root H001 reaches two abilities through real fields (A001 owning
    // buff B001), and three more objects only through the script closure seed edge (A004
    // owning buff B002). So four objects are carried, A003, A004, B002, and U002.
    private static UnitBundle SampleBundle()
    {
        var objects = new List<BundleNode>
        {
            new("H001", ObjectKind.Unit, "Test Hero", true),
            new("A001", ObjectKind.Ability, "Real Skill One", true),
            new("A002", ObjectKind.Ability, "Real Skill Two", true),
            new("B001", ObjectKind.Buff, "Real Buff", true),
            new("A003", ObjectKind.Ability, "Foreign Skill", true),
            new("A004", ObjectKind.Ability, "Foreign Skill With Buff", true),
            new("B002", ObjectKind.Buff, "Foreign Buff", true),
            new("U002", ObjectKind.Unit, "Foreign Dummy", true),
        };
        var edges = new List<BundleEdge>
        {
            new("H001", "A001", "uhab"),
            new("H001", "A002", "uhab"),
            new("A001", "B001", "abuf"),
            new("H001", "A003", "script closure"),
            new("H001", "A004", "script closure"),
            new("H001", "U002", "script closure"),
            new("A004", "B002", "abuf"),
        };
        return new UnitBundle("H001", "Test Hero", objects,
            Array.Empty<BundleFile>(), Array.Empty<string>(), edges,
            Array.Empty<string>(), Array.Empty<BundleFunction>());
    }

    [Fact]
    public void CarriedByScriptClosure_is_the_objects_reached_only_through_the_seed_edge()
    {
        var carried = BundleStructure.CarriedByScriptClosure(SampleBundle());
        Assert.Equal(new[] { "A003", "A004", "B002", "U002" }, carried.OrderBy(c => c).ToArray());
    }

    [Fact]
    public void RealAdjacency_keeps_field_edges_and_drops_the_script_closure_seed()
    {
        var adjacency = BundleStructure.RealAdjacency(SampleBundle());
        Assert.Equal(new[] { "A001", "A002" }, adjacency["H001"].OrderBy(c => c).ToArray());
        Assert.Equal(new[] { "B001" }, adjacency["A001"].ToArray());
        Assert.Equal(new[] { "B002" }, adjacency["A004"].ToArray());
        Assert.False(adjacency.ContainsKey("U002")); // a leaf, and only ever reached by a seed edge
    }

    [Fact]
    public void BundleUnit_shows_the_hero_s_real_children_then_a_labelled_carried_group()
    {
        var lines = Wc3Ctl.Render.BundleUnit(SampleBundle())
            .Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        // The hero, then its two real abilities at depth one, the real buff nested under A001.
        Assert.StartsWith("H001", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("  A001"));
        Assert.Contains(lines, l => l.StartsWith("  A002"));
        Assert.Contains(lines, l => l.StartsWith("    B001"));

        // The over-carry sits under one labelled group with the count, never as the hero's own.
        int group = lines.FindIndex(l => l.StartsWith("Carried by the script closure (4)"));
        Assert.True(group > 0);
        Assert.Contains(lines, l => l.StartsWith("  A003"));   // a carried root at depth one in the group
        Assert.Contains(lines, l => l.StartsWith("    B002"));  // its buff nested under A004

        // Nothing carried appears above the group, so the hero's own children are only the reals.
        var beforeGroup = lines.Take(group);
        Assert.DoesNotContain(beforeGroup, l => l.Contains("A003") || l.Contains("A004") || l.Contains("U002"));
    }
}
