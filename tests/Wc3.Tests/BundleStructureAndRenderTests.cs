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
    //
    // Assets matter as much as objects here. The root owns a model whose OWN texture hangs off
    // the model path, not off the root, and the foreign ability owns an icon through "aart", a
    // perfectly ordinary art field. That is the whole trap, foreign art never looks foreign at
    // the edge, only the object that asked for it is foreign.
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
        var files = new List<BundleFile>
        {
            new(@"war3mapImported\hero.mdl", "model", PresentInMap: true),
            new(@"war3mapImported\hero0.blp", "texture", PresentInMap: true),
            new(@"ReplaceableTextures\CommandButtons\BTNOwn.blp", "icon", PresentInMap: true),
            new(@"ReplaceableTextures\CommandButtons\BTNForeign.blp", "icon", PresentInMap: true),
            new(@"war3mapImported\foreign.mdl", "model", PresentInMap: true),
        };
        var strings = new List<string> { "Test Hero", "Real Skill One", "Foreign Skill" };
        var edges = new List<BundleEdge>
        {
            new("H001", "A001", "uhab"),
            new("H001", "A002", "uhab"),
            new("A001", "B001", "abuf"),
            new("H001", "A003", "script closure"),
            new("H001", "A004", "script closure"),
            new("H001", "U002", "script closure"),
            new("A004", "B002", "abuf"),

            new("H001", @"war3mapImported\hero.mdl", "umdl"),
            new(@"war3mapImported\hero.mdl", @"war3mapImported\hero0.blp", "texture"),
            new("A001", @"ReplaceableTextures\CommandButtons\BTNOwn.blp", "aart"),
            new("A003", @"ReplaceableTextures\CommandButtons\BTNForeign.blp", "aart"),
            new("U002", @"war3mapImported\foreign.mdl", "umdl"),

            new("H001", "Test Hero", "string"),
            new("A001", "Real Skill One", "string"),
            new("A003", "Foreign Skill", "string"),
        };
        return new UnitBundle("H001", "Test Hero", objects, files,
            strings, edges, Array.Empty<string>(), Array.Empty<BundleFunction>());
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

    /// <summary>The root's files are its own art plus, transitively, the textures its model
    /// names. A foreign object's icon arrives through the same kind of art field, so only the
    /// asking object tells them apart.</summary>
    [Fact]
    public void RealFiles_follows_model_textures_and_excludes_a_foreign_object_s_art()
    {
        var real = BundleStructure.RealFiles(SampleBundle());
        Assert.Equal(new[]
        {
            @"ReplaceableTextures\CommandButtons\BTNOwn.blp",
            @"war3mapImported\hero.mdl",
            @"war3mapImported\hero0.blp", // reached through the model, not through the root
        }, real.OrderBy(f => f, StringComparer.Ordinal).ToArray());

        Assert.DoesNotContain(@"ReplaceableTextures\CommandButtons\BTNForeign.blp", real);
        Assert.DoesNotContain(@"war3mapImported\foreign.mdl", real);
    }

    [Fact]
    public void RealStrings_are_the_root_s_own_not_the_closure_s()
    {
        var real = BundleStructure.RealStrings(SampleBundle());
        Assert.Equal(new[] { "Real Skill One", "Test Hero" },
            real.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("Foreign Skill", real);
    }

    /// <summary>A string edge's To is prose, so a four character string must never be mistaken
    /// for a rawcode and forge an object edge. This is the guard that makes the string edges safe
    /// to add to a shared edge list.</summary>
    [Fact]
    public void A_string_that_looks_like_a_rawcode_does_not_become_an_object_edge()
    {
        var bundle = SampleBundle();
        // "A003" as literal display text, owned by the root. It names a real object in this bundle.
        bundle = bundle with
        {
            Strings = bundle.Strings.Append("A003").ToList(),
            Edges = bundle.Edges.Append(new BundleEdge("H001", "A003", "string")).ToList(),
        };

        // The carried object stays carried. Without the guard the root would "really" reach A003.
        Assert.Contains("A003", BundleStructure.CarriedByScriptClosure(bundle));
        Assert.DoesNotContain("A003", BundleStructure.RealAdjacency(bundle)["H001"]);
        // The string itself is still the root's own, it is only barred from acting as an object.
        Assert.Contains("A003", BundleStructure.RealStrings(bundle));
    }

    [Fact]
    public void BundleUnit_lists_the_root_s_own_files_and_counts_the_carried_ones()
    {
        var lines = Wc3Ctl.Render.BundleUnit(SampleBundle())
            .Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        Assert.Contains(lines, l => l.StartsWith("Files (3)"));
        Assert.Contains(lines, l => l.Contains("BTNOwn.blp"));
        Assert.Contains(lines, l => l.Contains(@"war3mapImported\hero0.blp"));

        // The over-carry is counted and categorized, never dumped, and the escape hatch is named.
        Assert.Contains(lines, l =>
            l.Contains("plus 2 carried by the script closure (1 icon, 1 model)")
            && l.Contains("--json"));
        Assert.DoesNotContain(lines, l => l.Contains("BTNForeign.blp"));

        // A string row is the quoted text alone, which is how it is told apart from an object row
        // that merely shows the same text as its name ("Foreign Skill" is both here, on purpose).
        Assert.Contains(lines, l => l.StartsWith("Strings (2)"));
        Assert.Contains(lines, l => l.Trim() == "\"Test Hero\"");
        Assert.DoesNotContain(lines, l => l.Trim() == "\"Foreign Skill\"");
        Assert.Contains(lines, l => l.Contains("plus 1 carried by the script closure"));
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
