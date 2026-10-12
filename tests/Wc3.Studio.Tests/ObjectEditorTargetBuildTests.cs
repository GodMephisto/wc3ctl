// tests/Wc3.Studio.Tests/ObjectEditorTargetBuildTests.cs
using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

using Wc3.Tests;

namespace Wc3.Studio.Tests;

/// <summary>
/// The object editor's target-build selector and bounds hint, driven headlessly over temp schema
/// folders so no game install is needed. The behaviour worth pinning is that a build with no object
/// data is reported rather than silently showing the installed build's fields, and that the field
/// under edit shows its type and bounds from the chosen build's schema.
/// </summary>
public class ObjectEditorTargetBuildTests
{
    private const string ObjectsBuild = "TFT-v1.30.0.00001";
    private const string ScriptsOnlyBuild = "TFT-v1.35.0.00001";

    private static string TempSchemas()
    {
        var root = Path.Combine(Path.GetTempPath(), "wc3-studio-objtarget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        GamedataFixtures.Save(root, ObjectsBuild, slk: "units/unitmetadata.slk",
            fields: new[] { GamedataFixtures.Field("ugol", "int", "1", "300", true) });
        GamedataFixtures.Save(root, ScriptsOnlyBuild, natives: new[] { "NewNative" });
        return root;
    }

    private static MapDocument MapWithOneUnit()
    {
        var doc = BlankMap.Create();
        var made = ObjectNewCommand.Execute(doc, ObjectKind.Unit, "hfoo");
        Assert.True(made.Ok, made.Message);
        Assert.True(ObjectSetCommand.Execute(doc, ObjectKind.Unit, made.NewRawcode!, "ugol", "123").Ok);
        return doc;
    }

    private static ObjectEditorView Shown(MapDocument doc, string schemasDir)
    {
        var view = new ObjectEditorView();
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        view.ShowMap(new MapSession { Current = doc, SchemasDir = schemasDir });
        window.UpdateLayout();
        return view;
    }

    private static int FieldRowCount(ObjectEditorView view) =>
        (Field<ListBox>(view, "FieldList").ItemsSource as IEnumerable)?.Cast<object>().Count() ?? 0;

    private static string TextOf(Control root, string name) => Field<TextBlock>(root, name).Text ?? "";

    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;

    [AvaloniaFact]
    public void A_build_with_no_object_data_says_so_instead_of_showing_installed_fields()
    {
        var root = TempSchemas();
        try
        {
            var view = Shown(MapWithOneUnit(), root);
            // Baseline, the installed/objects view shows the object's field rows.
            Assert.True(FieldRowCount(view) > 0);

            var target = Field<SearchableComboBox>(view, "TargetCombo");
            Assert.True(target.Select(ScriptsOnlyBuild, raiseEvent: true));

            Assert.Contains("no object data", TextOf(view, "SelectedHeader"),
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, FieldRowCount(view));
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void The_field_under_edit_shows_type_and_bounds_from_the_chosen_build()
    {
        var root = TempSchemas();
        try
        {
            var view = Shown(MapWithOneUnit(), root);

            var target = Field<SearchableComboBox>(view, "TargetCombo");
            Assert.True(target.Select(ObjectsBuild, raiseEvent: true));

            var fieldList = Field<ListBox>(view, "FieldList");
            var rows = (fieldList.ItemsSource as IEnumerable)!.Cast<ObjectEditorView.FieldRow>().ToList();
            var ugol = rows.First(r => r.IsSelectable && r.Code == "ugol");
            fieldList.SelectedItem = ugol;   // raises the selection handler that writes the hint

            var bounds = TextOf(view, "FieldBoundsText");
            Assert.Contains("int", bounds);
            Assert.Contains("1..300", bounds);
            Assert.Contains("blank allowed", bounds);
        }
        finally { Directory.Delete(root, true); }
    }
}
