using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// Reference-field readability: an object-reference LIST field (e.g. a unit's ability
/// list) must expand into read-only sub-rows showing each referenced object's name
/// beside its rawcode. Corpus-gated: silently passes when the map (or the WC3 install
/// that provides the field metadata types) isn't on this machine.
/// </summary>
public class ObjectEditorRefFieldsTests
{
    private const string MapPath =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\Anime_WOS2_0.27d3.w3x";

    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Unit_ability_list_field_expands_into_named_sub_rows()
    {
        if (!File.Exists(MapPath)) return;
        // Reference detection reads the base metadata types; skip without an install.
        if (ObjectFieldOptionsCommand.Execute(ObjectKind.Unit, "uabi", null).Type.Length == 0) return;

        var view = new ObjectEditorView();
        var window = new Window { Width = 900, Height = 650, Content = view };
        window.Show();
        view.ShowMap(new MapSession { Current = MapDocument.Load(MapPath), MapPath = MapPath });

        var objects = Field<ListBox>(view, "ObjectList");
        var rows = (objects.ItemsSource as IEnumerable)?.Cast<object>().ToList() ?? new();
        Assert.True(rows.Count > 0, "the corpus map should list units");

        // Walk the units until one carries a reference-list entry that resolved to a
        // name: custom heroes reference map-defined abilities (A000-style), which the
        // name cache resolves; "rawcode - name" is the resolved sub-row format, while
        // unresolved entries stay a bare 4-char rawcode.
        bool found = false;
        for (int i = 0; i < rows.Count && !found; i++)
        {
            objects.SelectedIndex = i;
            found = FieldRows(view).Any(f =>
                f.IsSubRow && f.DisplayValue.Contains(" - ", StringComparison.Ordinal));
        }
        Assert.True(found,
            "no unit produced a reference-list sub-row with a resolved object name");
    }

    private static IEnumerable<ObjectEditorView.FieldRow> FieldRows(ObjectEditorView view) =>
        (Field<ListBox>(view, "FieldList").ItemsSource as IEnumerable)
            ?.OfType<ObjectEditorView.FieldRow>() ?? Enumerable.Empty<ObjectEditorView.FieldRow>();

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;
}
