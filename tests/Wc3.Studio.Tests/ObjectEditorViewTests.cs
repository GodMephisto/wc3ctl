using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// Drives the object editor headlessly, exactly like a user: open a map, then flip the
/// Type switcher. Reproduces (or clears) the "Abilities list is empty" report against the
/// real map. Corpus-gated: silently passes when the map isn't on this machine.
/// </summary>
public class ObjectEditorViewTests
{
    private const string MapPath =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\Anime_WOS2_0.27d3.w3x";

    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Switching_type_to_abilities_lists_them()
    {
        if (!File.Exists(MapPath)) return;

        var view = new ObjectEditorView();
        var window = new Window { Width = 900, Height = 650, Content = view };
        window.Show();

        var session = new MapSession { Current = MapDocument.Load(MapPath), MapPath = MapPath };
        view.ShowMap(session);
        int units = VisibleRowCount(view); // baseline: defaults to Units
        Assert.Equal("unit", SelectedKindName(view));

        Field<SearchableComboBox>(view, "KindCombo").Select("ability", raiseEvent: true);
        int abilities = VisibleRowCount(view);

        Assert.True(units > 0, $"units should list ({units})");
        Assert.Equal("ability", SelectedKindName(view));
        Assert.True(abilities > 0,
            $"abilities should list but got {abilities} (units listed {units})");
    }

    /// <summary>The real-world repro: type a search while on one kind, then switch kinds.
    /// If the search text carries over, the new kind's list is filtered to nothing and
    /// looks empty — the likely cause of the "can't see abilities" report.</summary>
    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Switching_type_clears_the_stale_search_filter()
    {
        if (!File.Exists(MapPath)) return;

        var view = new ObjectEditorView();
        var window = new Window { Width = 900, Height = 650, Content = view };
        window.Show();
        view.ShowMap(new MapSession { Current = MapDocument.Load(MapPath), MapPath = MapPath });

        // User searches while browsing Units...
        Field<TextBox>(view, "SearchBox").Text = "zzz_no_such_unit";
        // ...then switches to Abilities.
        Field<SearchableComboBox>(view, "KindCombo").Select("ability", raiseEvent: true);

        int abilities = VisibleRowCount(view);
        Assert.True(abilities > 0,
            $"switching Type must not keep the old search filter (saw {abilities} abilities)");
    }

    private static string SelectedKindName(ObjectEditorView view) =>
        Field<SearchableComboBox>(view, "KindCombo").SelectedId ?? "(none)";

    private static int VisibleRowCount(ObjectEditorView view) =>
        (Field<ListBox>(view, "ObjectList").ItemsSource as IEnumerable)?.Cast<object>().Count() ?? 0;

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;
}
