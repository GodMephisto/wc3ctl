using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// Drives the object editor's reference-list builder headlessly, exactly like a user:
/// open a map, select a unit, select an ability-LIST field (the builder editor), pick
/// a candidate in the Add dropdown and press Add - then check the value the Apply
/// path would write. Corpus-gated on the map AND on game data (field metadata needs
/// a WC3 install): silently passes when either is missing on this machine.
/// </summary>
public class ObjectEditorListEditTests
{
    private const string MapPath =
        @"C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\Anime_WOS2_0.27d3.w3x";

    /// <summary>Unit ability-list field codes ('uabi' abilList, 'uhab' heroAbilList) -
    /// the canonical object-reference LIST fields the builder targets.</summary>
    private static readonly string[] AbilityListCodes = { "uabi", "uhab" };

    [AvaloniaFact]
    [Trait("Category", "Corpus")]
    public void Adding_a_reference_list_entry_reaches_the_editor_value()
    {
        if (!File.Exists(MapPath)) return;
        // Without game data the field never classifies as a reference list and the
        // editor falls back to free text by design - nothing to drive here.
        if (ObjectFieldOptionsCommand.Execute(ObjectKind.Unit, "uabi", null).Type.Length == 0)
            return;

        var view = new ObjectEditorView();
        var window = new Window { Width = 900, Height = 650, Content = view };
        window.Show();
        view.ShowMap(new MapSession { Current = MapDocument.Load(MapPath), MapPath = MapPath });

        var objectList = Field<ListBox>(view, "ObjectList");
        int units = (objectList.ItemsSource as IEnumerable)?.Cast<object>().Count() ?? 0;
        Assert.True(units > 0, "the corpus map should list units");

        // Walk units until one shows an ability-list field that configures the
        // builder (in practice the very first unit carries 'uabi').
        Assert.True(SelectRefListField(view, objectList, Math.Min(units, 50)),
            "no unit within the first 50 exposed an ability-list field with the RefList editor");

        var addCombo = Field<SearchableComboBox>(view, "RefListAddCombo");
        Assert.True(addCombo.Items.Count > 0,
            "the Add picker should list the map's abilities as candidates");

        // Prefer a rawcode not already in the list so the add is unambiguous.
        var before = CurrentEditorValue(view);
        var existing = before.Split(',', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        var candidate = addCombo.Items.Select(i => i.Id)
            .FirstOrDefault(id => !existing.Contains(id, StringComparer.Ordinal))
            ?? addCombo.Items[0].Id;

        Assert.True(addCombo.Select(candidate), $"picker should select {candidate}");
        Field<Button>(view, "RefListAddButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var after = CurrentEditorValue(view);
        Assert.Contains(candidate, after.Split(','));
        // The new entry appends at the end, preserving the existing order.
        Assert.Equal(before.Length == 0 ? candidate : $"{before},{candidate}", after);
    }

    /// <summary>Select units in turn and, on each, select the first ability-list field
    /// row; true once a selection lands the private editor in RefList mode.</summary>
    private static bool SelectRefListField(ObjectEditorView view, ListBox objectList, int maxUnits)
    {
        var fieldList = Field<ListBox>(view, "FieldList");
        for (int u = 0; u < maxUnits; u++)
        {
            objectList.SelectedIndex = u; // refreshes the field pane
            var rows = (fieldList.ItemsSource as IEnumerable)
                ?.Cast<ObjectEditorView.FieldRow>()
                .Where(r => !r.IsSubRow && AbilityListCodes.Contains(BareCode(r.Code)))
                .ToList() ?? new List<ObjectEditorView.FieldRow>();
            foreach (var row in rows)
            {
                fieldList.SelectedItem = row; // runs ConfigureEditor
                if (EditorModeName(view) == "RefList")
                    return true;
            }
        }
        return false;
    }

    /// <summary>Leveled field codes ("code:N") resolve by their bare code.</summary>
    private static string BareCode(string code)
    {
        int colon = code.IndexOf(':');
        return colon < 0 ? code : code[..colon];
    }

    private static string EditorModeName(ObjectEditorView view) => view.GetType()
        .GetField("_editorMode", BindingFlags.NonPublic | BindingFlags.Instance)!
        .GetValue(view)!.ToString()!;

    private static string CurrentEditorValue(ObjectEditorView view) => (string)view.GetType()
        .GetMethod("CurrentEditorValue", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(view, null)!;

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;
}
