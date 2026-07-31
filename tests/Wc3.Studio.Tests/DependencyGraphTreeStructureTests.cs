using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// Pins the shape of the dependency tree (DepTree). A hero in these tightly coupled
/// maps carries a very large script closure, hundreds of objects including other
/// heroes' whole kits. That over-carry is deliberate and stays in the bundle, but the
/// tree must not present it as if the hero owns it. The hero's DIRECT children are only
/// the objects it references through real object-data fields (uhab, uabi, abuf and the
/// like), never the objects attached to the root by the synthetic "script closure"
/// edge. Everything reachable only through that edge lives under one clearly labelled
/// "Carried by the script closure" group.
/// </summary>
public class DependencyGraphTreeStructureTests
{
    private const string AstaMap =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\Anime_WOS2_0.28a2.w3x";
    private const string ChoiceArenaMap =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\Anime Choice Arena V0.31C.w3x";

    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(180);

    // An object row header starts with the four character rawcode followed by a space
    // hyphen space and the name. Group headers ("Carried by the script closure (N)",
    // "Other triggers", "Trigger functions (N)") never begin that way, so this pattern
    // selects object rows and skips the group nodes.
    private static readonly Regex ObjectRow = new(@"^.{4} - ", RegexOptions.Compiled);

    /// <summary>
    /// Hermetic, no map or game data needed. A synthetic bundle where the root reaches
    /// two abilities through real fields (one of them owning a buff) and three more
    /// objects only through the "script closure" edge (one of those owning a buff of its
    /// own). The tree must show exactly the two real abilities directly under the hero,
    /// nest the real buff under its ability, and park the three closure objects (plus the
    /// nested buff) under the carried group.
    /// </summary>
    [AvaloniaFact]
    public void Real_references_are_the_hero_s_direct_children_closure_objects_are_grouped()
    {
        var bundle = SampleBundle();

        var view = new DependencyGraphView();
        SetField(view, "_hideCarried", false); // this test inspects the carried group, so reveal it
        InvokeBuildTree(view, bundle);
        var root = RootItem(view);

        var directRows = ObjectRows(root);
        Assert.Equal(new[] { "A001", "A002" }, directRows.Select(RawcodeOf).OrderBy(r => r).ToArray());

        // The real buff hangs under its ability, not at the top and not in the closure group.
        var a001 = directRows.Single(i => RawcodeOf(i) == "A001");
        Assert.Contains(ObjectRows(a001), i => RawcodeOf(i) == "B001");

        var carried = root.Items.OfType<TreeViewItem>()
            .Single(i => HeaderText(i).StartsWith("Carried by the script closure", StringComparison.Ordinal));
        Assert.Contains("(4)", HeaderText(carried));

        var carriedTop = ObjectRows(carried).Select(RawcodeOf).OrderBy(r => r).ToArray();
        Assert.Equal(new[] { "A003", "A004", "U002" }, carriedTop);

        // The foreign buff nests under the foreign ability that pulls it in, inside the group.
        var a004 = ObjectRows(carried).Single(i => RawcodeOf(i) == "A004");
        Assert.Contains(ObjectRows(a004), i => RawcodeOf(i) == "B002");

        // No object row anywhere claims the hero directly owns a closure object.
        Assert.DoesNotContain(directRows, i => RawcodeOf(i) is "A003" or "A004" or "U002" or "B002");
    }

    /// <summary>
    /// The reported repro. Asta (H028) in Anime_WOS2_0.28a2 resolves to 193 abilities in
    /// the closure, but only 6 are real references of the hero. The old tree listed all
    /// 193 flat under the hero. The tree must now show 6 direct abilities and carry the
    /// remaining 204 objects in the labelled group, while the bundle itself keeps the full
    /// over-carry untouched.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Asta_H028_shows_six_direct_abilities_not_the_whole_closure()
    {
        if (!File.Exists(AstaMap)) return;
        AssertHeroDirectStructure(AstaMap, "H028",
            expectedDirectRows: 6, expectedTotalAbilities: 193, expectedCarried: 204);
    }

    /// <summary>Second hero, so the fix is not overfitted to Asta. Shadow Nanaya (H0DA)
    /// in Anime Choice Arena V0.31C has 9 real direct abilities out of 40 in the closure,
    /// with the deeper real structure (buffs and spellbook entries) hanging under them.</summary>
    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Second_hero_H0DA_shows_nine_direct_abilities()
    {
        if (!File.Exists(ChoiceArenaMap)) return;
        AssertHeroDirectStructure(ChoiceArenaMap, "H0DA",
            expectedDirectRows: 9, expectedTotalAbilities: 40, expectedCarried: 66);
    }

    /// <summary>
    /// The toggle is a view change only, and it defaults to hidden. On a fresh resolve the
    /// carried group is absent from the tree and its nodes are off the graph, with the count
    /// still shown on the checkbox. Revealing then hiding never changes the port exclusion
    /// set. A node the user excluded from the port stays excluded while it is hidden, hiding
    /// is not excluding.
    /// </summary>
    [AvaloniaFact]
    public void Hiding_the_carried_group_is_view_only_and_keeps_exclusions()
    {
        var view = new DependencyGraphView();
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        InvokeRenderBundle(view, SampleBundle());

        var check = Field<CheckBox>(view, "HideCarriedCheck");
        var objVisuals = Field<Dictionary<string, Border>>(view, "_objVisuals");

        // Hidden by default. The group is absent and the carried node is not drawn, but the
        // count is still visible so the user can see how much is tucked away.
        Assert.True(check.IsVisible);
        Assert.True(check.IsChecked == false);
        Assert.Contains("(4)", check.Content as string ?? "");
        Assert.False(HasCarriedGroup(RootItem(view)));
        Assert.DoesNotContain("A003", objVisuals.Keys);
        Assert.Contains("A001", objVisuals.Keys);

        // Reveal, the group and its nodes appear.
        check.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(HasCarriedGroup(RootItem(view)));
        Assert.Contains("A003", objVisuals.Keys);

        // The user excludes a carried node from the port, then hides again.
        var excluded = Field<HashSet<string>>(view, "_excluded");
        excluded.Add("A003");
        check.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        // Both surfaces drop the carried objects, the real abilities remain.
        Assert.False(HasCarriedGroup(RootItem(view)));
        Assert.DoesNotContain("A003", objVisuals.Keys);
        Assert.DoesNotContain("A004", objVisuals.Keys);
        Assert.DoesNotContain("U002", objVisuals.Keys);
        Assert.Contains("H001", objVisuals.Keys);
        Assert.Contains("A001", objVisuals.Keys);
        Assert.Contains("A002", objVisuals.Keys);

        // Hiding never touched the exclusion set, the hidden node is still excluded.
        Assert.Contains("A003", view.ExcludedKeys);

        // Reveal once more, the exclusion is still intact.
        check.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(HasCarriedGroup(RootItem(view)));
        Assert.Contains("A003", objVisuals.Keys);
        Assert.Contains("A003", view.ExcludedKeys);
    }

    /// <summary>
    /// End to end on the reported map, the default. Asta (H028) opens with the closure
    /// hidden, so the hero shows its 6 real abilities and the graph is just the 7 real nodes
    /// (the hero plus 6), with the "(204)" count on the checkbox and nothing excluded.
    /// Revealing brings the whole closure back. The checkbox declutters without touching the
    /// bundle or the exclusion set.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Hiding_the_carried_group_declutters_Asta_H028_without_excluding()
    {
        if (!File.Exists(AstaMap)) return;
        var doc = MapDocument.Load(AstaMap);
        var view = new DependencyGraphView();
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        view.ShowObject(new MapSession { Current = doc, MapPath = AstaMap }, ObjectKind.Unit, "H028");
        var tree = Field<TreeView>(view, "DepTree");
        PumpUntilPopulated(tree);

        // Hidden by default, decluttered.
        var check = Field<CheckBox>(view, "HideCarriedCheck");
        Assert.True(check.IsVisible);
        Assert.True(check.IsChecked == false);
        Assert.Contains("(204)", check.Content as string ?? "");
        var root = RootItem(view);
        Assert.False(HasCarriedGroup(root));
        Assert.Equal(6, ObjectRows(root).Count);
        var objVisuals = Field<Dictionary<string, Border>>(view, "_objVisuals");
        Assert.Equal(7, objVisuals.Count);
        Assert.Contains("H028", objVisuals.Keys);
        Assert.Empty(view.ExcludedKeys);

        // Reveal, the whole closure comes back and still nothing is excluded.
        check.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(HasCarriedGroup(RootItem(view)));
        Assert.True(objVisuals.Count > 7);
        Assert.Empty(view.ExcludedKeys);
    }

    private static void AssertHeroDirectStructure(
        string mapPath, string rawcode, int expectedDirectRows, int expectedTotalAbilities, int expectedCarried)
    {
        // Ground truth from the command layer, the over-carry must be intact.
        var doc = MapDocument.Load(mapPath);
        var bundle = BundleCommand.ResolveObject(doc, ObjectKind.Unit, rawcode, null);
        var objectCodes = bundle.Objects.Select(o => o.Rawcode).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedTotalAbilities, bundle.Objects.Count(o => o.Kind == ObjectKind.Ability));
        var realDirectAbilities = bundle.Edges
            .Where(e => e.From == rawcode && e.Via != "script closure" && objectCodes.Contains(e.To))
            .Select(e => e.To)
            .Distinct()
            .Count(to => bundle.Objects.First(o => o.Rawcode == to).Kind == ObjectKind.Ability);
        Assert.Equal(expectedDirectRows, realDirectAbilities);

        // The panel must reflect that, a handful of direct rows and the rest in the group.
        var view = new DependencyGraphView();
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        var session = new MapSession { Current = doc, MapPath = mapPath };
        view.ShowObject(session, ObjectKind.Unit, rawcode);

        var tree = Field<TreeView>(view, "DepTree");
        PumpUntilPopulated(tree);

        // The carried group is hidden by default, the hero's real children still show.
        var root = tree.Items.OfType<TreeViewItem>().First();
        Assert.Equal(expectedDirectRows, ObjectRows(root).Count);
        Assert.DoesNotContain(root.Items.OfType<TreeViewItem>(),
            i => HeaderText(i).StartsWith("Carried by the script closure", StringComparison.Ordinal));

        // Reveal it, the group appears with the full carried count and the real rows persist.
        Field<CheckBox>(view, "HideCarriedCheck").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        root = tree.Items.OfType<TreeViewItem>().First();
        Assert.Equal(expectedDirectRows, ObjectRows(root).Count);
        var carried = root.Items.OfType<TreeViewItem>()
            .Single(i => HeaderText(i).StartsWith("Carried by the script closure", StringComparison.Ordinal));
        Assert.Contains($"({expectedCarried})", HeaderText(carried));
    }

    // helpers

    /// <summary>A small closure the root reaches two abilities through real fields (A001
    /// owning buff B001), and three more objects only through the script closure edge (A004
    /// owning buff B002). Four objects are carried, A003, A004, U002, and B002.</summary>
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

    private static bool HasCarriedGroup(TreeViewItem root) =>
        root.Items.OfType<TreeViewItem>()
            .Any(i => HeaderText(i).StartsWith("Carried by the script closure", StringComparison.Ordinal));

    private static void InvokeRenderBundle(DependencyGraphView view, UnitBundle bundle) =>
        typeof(DependencyGraphView)
            .GetMethod("RenderBundle", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, new object[] { bundle });

    private static void InvokeBuildTree(DependencyGraphView view, UnitBundle bundle) =>
        typeof(DependencyGraphView)
            .GetMethod("BuildTree", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, new object[] { bundle });

    private static TreeViewItem RootItem(DependencyGraphView view) =>
        Field<TreeView>(view, "DepTree").Items.OfType<TreeViewItem>().First();

    private static List<TreeViewItem> ObjectRows(TreeViewItem item) =>
        item.Items.OfType<TreeViewItem>().Where(i => ObjectRow.IsMatch(HeaderText(i))).ToList();

    private static string HeaderText(TreeViewItem item) =>
        (item.Header as TextBlock)?.Text ?? "";

    private static string RawcodeOf(TreeViewItem item) => HeaderText(item)[..4];

    private static void PumpUntilPopulated(TreeView tree)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < LoadTimeout)
        {
            Dispatcher.UIThread.RunJobs();
            if (tree.Items.Count > 0) return;
            Thread.Sleep(50);
        }
        throw new TimeoutException(
            $"dependency tree did not populate within {LoadTimeout.TotalSeconds:F0}s");
    }

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;

    private static void SetField(object obj, string name, object? value) => obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .SetValue(obj, value);
}
