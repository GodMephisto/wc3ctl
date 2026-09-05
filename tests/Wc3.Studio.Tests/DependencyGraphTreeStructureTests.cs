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
/// Pins what the dependency panel shows. A hero on one of these tightly coupled arena maps
/// carries a very large script closure, hundreds of objects including other heroes' whole
/// kits, because the shared trigger script needs them present to compile and run. That
/// over-carry stays in the bundle and still ports, but it is NOT this unit's dependency, so
/// this panel never lists it, in the tree, the graph, the file list or the string list. What
/// the unit shows is only what its own object data references (uhab, uabi, abuf and the like),
/// plus, for files, the textures its own models name.
///
/// The summary line does NOT report the closure's size, and the assertions below pin that. Adding
/// it was tried and reverted, because the panel lists none of those objects, so a count of them
/// describes something the reader cannot see. The over-carry's cost is accounted for in the port
/// report, which is where it is actually paid. What the status line does carry is whether a carry
/// cap truncated the bundle, see DependencyGraphStatusTests.
/// </summary>
public class DependencyGraphTreeStructureTests
{
    private const string AstaMap =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\Anime_WOS2_0.28a2.w3x";
    private const string ChoiceArenaMap =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\Anime Choice Arena V0.31C.w3x";

    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(180);

    // An object row header starts with the four character rawcode followed by a space hyphen
    // space and the name. Group headers ("Other triggers", "Trigger functions (N)") never begin
    // that way, so this pattern selects object rows and skips group nodes.
    private static readonly Regex ObjectRow = new(@"^.{4} - ", RegexOptions.Compiled);

    /// <summary>
    /// Hermetic, no map or game data needed. A synthetic bundle where the root reaches two
    /// abilities through real fields (one owning a buff) and three more objects only through the
    /// "script closure" edge (one of those owning a buff of its own, and one owning an icon
    /// through an ordinary art field). The tree shows exactly the two real abilities under the
    /// hero with the real buff nested, and the closure objects appear nowhere.
    /// </summary>
    [AvaloniaFact]
    public void Only_real_references_appear_and_the_closure_is_absent_entirely()
    {
        var view = new DependencyGraphView();
        InvokeBuildTree(view, SampleBundle());
        var root = RootItem(view);

        var directRows = ObjectRows(root);
        Assert.Equal(new[] { "A001", "A002" }, directRows.Select(RawcodeOf).OrderBy(r => r).ToArray());

        // The real buff hangs under its ability.
        var a001 = directRows.Single(i => RawcodeOf(i) == "A001");
        Assert.Contains(ObjectRows(a001), i => RawcodeOf(i) == "B001");

        // No carried group, and no closure object anywhere in the tree. The group header is what
        // is being ruled out, not the word, a REAL child legitimately lists "script closure" among
        // its vias when the closure seed also names an object its owner already references.
        Assert.DoesNotContain(root.Items.OfType<TreeViewItem>(), IsCarriedGroup);
        foreach (var code in new[] { "A003", "A004", "U002", "B002" })
            Assert.DoesNotContain(code, AllHeaders(root));
    }

    /// <summary>The file and string lists follow the same rule. The root's own icon and its
    /// model's texture show, the foreign object's icon does not, and there is no second list for
    /// the closure to hide in.</summary>
    [AvaloniaFact]
    public void Files_and_strings_show_only_the_root_s_own()
    {
        var view = new DependencyGraphView();
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        InvokeRenderBundle(view, SampleBundle());

        Assert.Equal("Files (3)", Field<Expander>(view, "FilesExpander").Header);
        var fileRows = Field<StackPanel>(view, "FilesList").Children
            .OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.Contains(fileRows, t => t.Contains("BTNOwn.blp"));
        Assert.Contains(fileRows, t => t.Contains("hero0.blp")); // through the root's own model
        Assert.DoesNotContain(fileRows, t => t.Contains("BTNForeign.blp"));
        Assert.DoesNotContain(fileRows, t => t.Contains("foreign.mdl"));

        Assert.Equal("Strings (2)", Field<Expander>(view, "StringsExpander").Header);
        var stringRows = Field<StackPanel>(view, "StringsList").Children
            .OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.Contains("Test Hero", stringRows);
        Assert.DoesNotContain("Foreign Skill", stringRows);

        // Every count on the summary is the unit's own. The over-carry is not mentioned at all,
        // it is accounted for in the port report, which is where it actually costs something.
        var summary = Field<TextBlock>(view, "SummaryText").Text ?? "";
        Assert.StartsWith("4 objects, 3 files, 2 strings", summary);
        Assert.DoesNotContain("closure", summary);
        Assert.DoesNotContain("carried", summary);
    }

    /// <summary>The graph lays out only the real dependencies, and a node the user excludes from
    /// the port stays excluded. Not listing the closure is a view decision, it never touches the
    /// bundle or the exclusion set.</summary>
    [AvaloniaFact]
    public void Graph_lays_out_real_dependencies_only_and_exclusions_still_work()
    {
        var view = new DependencyGraphView();
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        InvokeRenderBundle(view, SampleBundle());

        var objVisuals = Field<Dictionary<string, Border>>(view, "_objVisuals");
        Assert.Equal(new[] { "A001", "A002", "B001", "H001" }, objVisuals.Keys.OrderBy(k => k).ToArray());
        Assert.Empty(view.ExcludedKeys);

        var excluded = Field<HashSet<string>>(view, "_excluded");
        excluded.Add("A002");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("A002", view.ExcludedKeys);
    }

    /// <summary>
    /// The reported repro. Asta (H028) once resolved to 193 abilities and 498 files, almost all of
    /// them other heroes' kits pulled in by one roster registry function. With that registry's
    /// grants excluded the closure is 17 abilities, all Asta's own (his 5 plus the sub-abilities his
    /// handlers grant at runtime, Q2, W2, TR, T2 and the two swords), and 6 of them are direct.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Asta_H028_shows_only_his_own_six_abilities_and_seventeen_files()
    {
        if (!File.Exists(AstaMap)) return;
        var view = ShowHero(AstaMap, "H028", expectedDirectRows: 6,
            expectedTotalAbilities: 17, expectedCarried: 18);

        Assert.Equal("Files (17)", Field<Expander>(view, "FilesExpander").Header);
        Assert.Equal("Strings (88)", Field<Expander>(view, "StringsExpander").Header);

        // The registry exclusion must never cost Asta a sub-ability his own handlers grant at
        // runtime. A0DS (Q2) and A0DT (W2) are the dangerous pair, named by no grant statement at
        // all, only passed as arguments inside his own handler, so a naive rule drops them and
        // silently breaks Q1 into Q2.
        var doc = MapDocument.Load(AstaMap);
        var carried = BundleCommand.ResolveObject(doc, ObjectKind.Unit, "H028", null)
            .Objects.Select(o => o.Rawcode).ToHashSet(StringComparer.Ordinal);
        foreach (var own in new[] { "A0DQ", "A0DR", "A0DS", "A0DT", "A0DU", "A0DV", "A0DW", "A0DX" })
            Assert.Contains(own, carried);
        // And no other hero's kit survives. These are Akainu, Kyoraku, Barragan and Raiden Ei.
        foreach (var foreign in new[] { "A06V", "A050", "A0B1", "A000" })
            Assert.DoesNotContain(foreign, carried);
        // The hero plus his 6 real abilities, nothing else on the canvas.
        Assert.Equal(7, Field<Dictionary<string, Border>>(view, "_objVisuals").Count);
        Assert.Empty(view.ExcludedKeys);
    }

    /// <summary>Second hero, so the rule is not overfitted to Asta. Shadow Nanaya (H0DA) in
    /// Anime Choice Arena V0.31C has 9 real direct abilities out of 40 in the closure, with the
    /// deeper real structure (buffs and spellbook entries) hanging under them.</summary>
    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Second_hero_H0DA_shows_nine_direct_abilities()
    {
        if (!File.Exists(ChoiceArenaMap)) return;
        ShowHero(ChoiceArenaMap, "H0DA", expectedDirectRows: 9,
            expectedTotalAbilities: 40, expectedCarried: 66);
    }

    /// <summary>Resolves a hero on a real map, checks the command layer still holds the whole
    /// over-carry, and that the panel shows only the hero's own with no closure group anywhere.</summary>
    private static DependencyGraphView ShowHero(
        string mapPath, string rawcode, int expectedDirectRows, int expectedTotalAbilities, int expectedCarried)
    {
        // Ground truth from the command layer, the over-carry must still be intact in the bundle.
        var doc = MapDocument.Load(mapPath);
        var bundle = BundleCommand.ResolveObject(doc, ObjectKind.Unit, rawcode, null);
        Assert.Equal(expectedTotalAbilities, bundle.Objects.Count(o => o.Kind == ObjectKind.Ability));
        Assert.Equal(expectedCarried, BundleStructure.CarriedByScriptClosure(bundle).Count);

        var view = new DependencyGraphView();
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        view.ShowObject(new MapSession { Current = doc, MapPath = mapPath }, ObjectKind.Unit, rawcode);
        PumpUntilPopulated(Field<TreeView>(view, "DepTree"));

        var root = RootItem(view);
        Assert.Equal(expectedDirectRows, ObjectRows(root).Count);
        Assert.DoesNotContain(root.Items.OfType<TreeViewItem>(), IsCarriedGroup);

        // No group holds the unattributable remainder either, and nothing in the panel reports the
        // over-carry. On a real map this is where "Other triggers" used to park other heroes' logic.
        Assert.DoesNotContain(root.Items.OfType<TreeViewItem>(),
            i => HeaderText(i).StartsWith("Other triggers", StringComparison.Ordinal));
        var summary = Field<TextBlock>(view, "SummaryText").Text ?? "";
        Assert.DoesNotContain("closure", summary);
        Assert.DoesNotContain("carried", summary);

        // Every via label names a real object-data field, never the synthetic closure seed.
        Assert.DoesNotContain(AllHeaders(root), h => h.Contains("script closure", StringComparison.Ordinal));
        return view;
    }

    // helpers

    /// <summary>A small closure. The root reaches two abilities through real fields (A001 owning
    /// buff B001) and three more objects only through the script closure edge (A004 owning buff
    /// B002). Four objects are carried, A003, A004, B002 and U002. Assets matter as much, the
    /// root's model names its OWN texture, and the foreign ability owns an icon through "aart",
    /// an ordinary art field, which is why the field code can never tell them apart.</summary>
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

    /// <summary>The group node the panel used to add for the over-carry, identified by its header
    /// form rather than by the phrase, which also appears as an ordinary via label.</summary>
    private static bool IsCarriedGroup(TreeViewItem item) =>
        HeaderText(item).StartsWith("Carried by the script closure", StringComparison.Ordinal);

    /// <summary>Every header in the subtree, so a test can assert a rawcode appears nowhere.</summary>
    private static IEnumerable<string> AllHeaders(TreeViewItem item)
    {
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            yield return HeaderText(child);
            foreach (var deeper in AllHeaders(child)) yield return deeper;
        }
    }

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
}
