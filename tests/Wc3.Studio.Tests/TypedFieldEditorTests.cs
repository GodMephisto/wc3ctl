using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio;
using Wc3.Studio.Controls;
using Wc3.Studio.Panels;

namespace Wc3.Studio.Tests;

/// <summary>
/// The bounded numeric editor's rules, tested as the pure functions they are. The point of
/// the editor is that an out-of-range value can no longer be ENTERED, where before it was
/// only rejected on Apply, so these pin the clamp, the rounding and the invariant format.
/// </summary>
public class NumericFieldEditorTests
{
    private static FormField Field(string type, string? min, string? max,
        bool forceNonNegative = false) =>
        new("utst", "Test Field", "0", "0", "base", type, min, max,
            forceNonNegative, CanBeEmpty: true, MultiLine: false,
            ObjectLayer.Map, LayerIsAuthoritative: true);

    [Fact]
    public void Numeric_types_are_int_real_and_unreal()
    {
        Assert.True(NumericFieldEditor.IsNumericType("int"));
        Assert.True(NumericFieldEditor.IsNumericType("real"));
        Assert.True(NumericFieldEditor.IsNumericType("unreal"));
        Assert.False(NumericFieldEditor.IsNumericType("string"));
        Assert.False(NumericFieldEditor.IsNumericType("icon"));
        Assert.False(NumericFieldEditor.IsNumericType(""));
        Assert.True(NumericFieldEditor.IsIntType("int"));
        Assert.False(NumericFieldEditor.IsIntType("real"));
    }

    [Fact]
    public void Bounds_come_from_the_metadata()
    {
        var (min, max) = NumericFieldEditor.Bounds(Field("int", "0", "100"), storedValue: 50m);
        Assert.Equal(0m, min);
        Assert.Equal(100m, max);
    }

    [Fact]
    public void Bounds_widen_to_keep_an_out_of_range_stored_value_visible()
    {
        // A stored -1 under a declared minimum of 0 must stay visible, opening the
        // editor never changes data by itself.
        var (min, max) = NumericFieldEditor.Bounds(Field("int", "0", "100"), storedValue: -1m);
        Assert.Equal(-1m, min);
        Assert.Equal(100m, max);
    }

    [Fact]
    public void ForceNonNegative_raises_the_minimum_to_zero()
    {
        var (min, _) = NumericFieldEditor.Bounds(
            Field("int", "-5", "10", forceNonNegative: true), storedValue: null);
        Assert.Equal(0m, min);
    }

    [Fact]
    public void Missing_metadata_means_an_unbounded_editor()
    {
        var (min, max) = NumericFieldEditor.Bounds(null, storedValue: null);
        Assert.Equal(decimal.MinValue, min);
        Assert.Equal(decimal.MaxValue, max);
    }

    [Fact]
    public void ValueText_clamps_into_the_bounds()
    {
        Assert.Equal("100", NumericFieldEditor.ValueText("500", null, 0m, 100m, isInt: true));
        Assert.Equal("0", NumericFieldEditor.ValueText("-3", null, 0m, 100m, isInt: true));
        Assert.Equal("50", NumericFieldEditor.ValueText("50", null, 0m, 100m, isInt: true));
    }

    [Fact]
    public void ValueText_writes_whole_numbers_for_int_fields()
    {
        Assert.Equal("3", NumericFieldEditor.ValueText("2.6", null, 0m, 100m, isInt: true));
        Assert.Equal("3", NumericFieldEditor.ValueText("3.0", null, 0m, 100m, isInt: true));
    }

    [Fact]
    public void ValueText_writes_invariant_reals_with_trailing_zeros_dropped()
    {
        Assert.Equal("0.85", NumericFieldEditor.ValueText("0.850", null, 0m, 10m, isInt: false));
        Assert.Equal("2", NumericFieldEditor.ValueText("2.0", null, 0m, 10m, isInt: false));
    }

    [Fact]
    public void ValueText_keeps_empty_empty_so_canbeempty_fields_can_be_cleared()
    {
        Assert.Equal("", NumericFieldEditor.ValueText("", null, 0m, 100m, isInt: true));
        Assert.Equal("", NumericFieldEditor.ValueText("   ", null, 0m, 100m, isInt: true));
    }

    [Fact]
    public void ValueText_falls_back_to_the_committed_value_on_garbage()
    {
        Assert.Equal("7", NumericFieldEditor.ValueText("abc", 7m, 0m, 100m, isInt: true));
        Assert.Equal("", NumericFieldEditor.ValueText("abc", null, 0m, 100m, isInt: true));
    }

    [Fact]
    public void CanEdit_refuses_shapes_a_spin_box_would_destroy()
    {
        Assert.True(NumericFieldEditor.CanEdit(""));
        Assert.True(NumericFieldEditor.CanEdit(null));
        Assert.True(NumericFieldEditor.CanEdit("3.5"));
        Assert.True(NumericFieldEditor.CanEdit("-12"));
        Assert.False(NumericFieldEditor.CanEdit("1,2,3"));
        Assert.False(NumericFieldEditor.CanEdit("Abil,Data,A"));
    }
}

/// <summary>
/// The asset picker's candidate sources. Family membership must come from
/// <see cref="AssetPathCandidates"/> (one authority for path rules), the map side must
/// list exactly the family's imports, and the widget dispatch must stay the wiki's
/// mapping, icon and model and nothing guessed.
/// </summary>
public class AssetCatalogTests
{
    [Fact]
    public void Only_icon_and_model_fields_get_a_picker()
    {
        Assert.Equal(AssetFamily.Icon, AssetCatalog.FamilyForFieldType("icon"));
        Assert.Equal(AssetFamily.Model, AssetCatalog.FamilyForFieldType("model"));
        Assert.Null(AssetCatalog.FamilyForFieldType("int"));
        Assert.Null(AssetCatalog.FamilyForFieldType("string"));
        Assert.Null(AssetCatalog.FamilyForFieldType(""));
    }

    [Fact]
    public void Family_extensions_come_from_AssetPathCandidates()
    {
        var model = AssetCatalog.FamilyExtensions(AssetFamily.Model);
        Assert.Contains(".mdx", model, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(".mdl", model, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(".blp", model, StringComparer.OrdinalIgnoreCase);

        var icon = AssetCatalog.FamilyExtensions(AssetFamily.Icon);
        Assert.Contains(".blp", icon, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(".dds", icon, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(".tga", icon, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(".mdx", icon, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapPaths_lists_only_the_familys_imports()
    {
        var doc = BlankMap.Create();
        doc.AddOrReplaceRawFile(@"war3mapImported\BTNCustom.blp", new byte[] { 1, 2, 3 });
        doc.AddOrReplaceRawFile(@"war3mapImported\Custom.mdx", new byte[] { 4, 5, 6 });

        var icons = AssetCatalog.MapPaths(doc, AssetFamily.Icon);
        Assert.Contains(@"war3mapImported\BTNCustom.blp", icons);
        Assert.DoesNotContain(@"war3mapImported\Custom.mdx", icons);

        var models = AssetCatalog.MapPaths(doc, AssetFamily.Model);
        Assert.Contains(@"war3mapImported\Custom.mdx", models);
        Assert.DoesNotContain(@"war3mapImported\BTNCustom.blp", models);
    }

    [Fact]
    public async Task Game_paths_are_empty_when_no_install_answers()
    {
        // An explicit override that does not exist means "not found", never a fallback
        // to a detected install, so this is hermetic on any machine.
        var paths = await AssetCatalog.GamePathsAsync(@"X:\no_such_wc3_install", AssetFamily.Icon);
        Assert.Empty(paths);
    }
}

/// <summary>The base game side of the picker against the real install. Gated because the
/// CASC listfile pass needs Warcraft III on disk.</summary>
public class AssetCatalogGameDataTests
{
    [Fact]
    [Trait("Category", "GameData")]
    public async Task Base_game_icon_and_model_paths_enumerate()
    {
        if (Wc3.GameData.GameInstall.Locate(null) is null) return;

        var icons = await AssetCatalog.GamePathsAsync(null, AssetFamily.Icon);
        Assert.True(icons.Count > 100, $"expected hundreds of base icons, got {icons.Count}");
        Assert.Contains(icons, p =>
            p.Contains(@"ReplaceableTextures\CommandButtons", StringComparison.OrdinalIgnoreCase));
        // Logical paths only, the w3mod storage prefixes must be stripped for the value
        // an object field stores.
        Assert.All(icons, p => Assert.DoesNotContain(":", p));

        var models = await AssetCatalog.GamePathsAsync(null, AssetFamily.Model);
        Assert.True(models.Count > 1000, $"expected thousands of base models, got {models.Count}");
        Assert.All(models, p => Assert.EndsWith(".mdx", p, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The full ConfigureEditor dispatch against real metadata, gated because the
/// field type token only exists once game data answers. Hermetic coverage of the same
/// editors lives in <see cref="TypedFieldEditorViewTests"/>, which calls the Show methods
/// directly.</summary>
public class TypedFieldEditorDispatchTests
{
    [AvaloniaFact]
    [Trait("Category", "GameData")]
    public void Metadata_types_pick_the_numeric_editor_and_the_asset_picker()
    {
        if (Wc3.GameData.GameInstall.Locate(null) is null) return;

        var view = new ObjectEditorView();
        var window = new Window { Width = 900, Height = 650, Content = view };
        window.Show();
        typeof(ObjectEditorView)
            .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(view, new MapSession { Current = BlankMap.Create() });

        // 'uhpm' (hit points) is an int field, the default kind is Units.
        InvokePrivate(view, "ConfigureEditor",
            new ObjectEditorView.FieldRow(new MergedField("uhpm", "HP", "550", "base")));
        Assert.True(((NumericUpDown)Get(view, "EditorNum")).IsVisible,
            "an int field should get the numeric editor");

        // 'uico' (interface icon) is an icon field, text box plus path picker.
        InvokePrivate(view, "ConfigureEditor",
            new ObjectEditorView.FieldRow(new MergedField("uico", "Icon", "", "base")));
        Assert.True(((TextBox)Get(view, "EditorBox")).IsVisible);
        Assert.True(((SearchableComboBox)Get(view, "AssetPickerCombo")).IsVisible,
            "an icon field should offer the path picker");
    }

    private static void InvokePrivate(ObjectEditorView view, string method, params object[] args) =>
        view.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, args);

    private static object Get(object obj, string name) => obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;
}

/// <summary>
/// Drives the new editors through the real panel, headlessly and hermetically (a blank
/// in-memory map, a game dir that resolves to nothing). Dispatch by metadata type needs a
/// WC3 install, so these call the private Show/Configure methods directly, the same calls
/// ConfigureEditor makes once the metadata answers.
/// </summary>
public class TypedFieldEditorViewTests
{
    [AvaloniaFact]
    public void Numeric_editor_cannot_produce_an_out_of_range_value()
    {
        var view = NewView(out _);
        var row = Row("uhpm", "550", Field("int", "0", "100000"));
        Invoke(view, "ShowNumericEditor", row, "int");

        Assert.True(Control<NumericUpDown>(view, "EditorNum").IsVisible);
        Assert.False(Control<TextBox>(view, "EditorBox").IsVisible);

        var num = Control<NumericUpDown>(view, "EditorNum");
        num.Text = "999999999";
        Assert.Equal("100000", CurrentEditorValue(view));
        num.Text = "-50";
        Assert.Equal("0", CurrentEditorValue(view));
        num.Text = "";
        Assert.Equal("", CurrentEditorValue(view));
    }

    [AvaloniaFact]
    public void Real_fields_write_invariant_decimals()
    {
        var view = NewView(out _);
        var row = Row("uacq", "0.5", Field("real", "0", "10"));
        Invoke(view, "ShowNumericEditor", row, "real");

        Control<NumericUpDown>(view, "EditorNum").Text = "0.850";
        Assert.Equal("0.85", CurrentEditorValue(view));
    }

    [AvaloniaFact]
    public void Asset_picker_offers_map_imports_and_fills_the_text_box()
    {
        var view = NewView(out var session);
        session.Current!.AddOrReplaceRawFile(@"war3mapImported\BTNCustom.blp", new byte[] { 1 });

        var row = Row("uico", @"ReplaceableTextures\CommandButtons\BTNFootman.blp",
            Field("icon", null, null));
        Invoke(view, "ShowTextEditor", row);
        Invoke(view, "ConfigureAssetPicker", row, "icon");

        var picker = Control<SearchableComboBox>(view, "AssetPickerCombo");
        Assert.True(picker.IsVisible);
        Assert.Contains(picker.Items, i => i.Id == @"war3mapImported\BTNCustom.blp");

        // Picking fills the text editor, the single value carrier the Apply path reads,
        // so hand-typed paths keep working alongside the picker.
        Assert.True(picker.Select(@"war3mapImported\BTNCustom.blp", raiseEvent: true));
        Assert.Equal(@"war3mapImported\BTNCustom.blp", Control<TextBox>(view, "EditorBox").Text);
        Assert.Equal(@"war3mapImported\BTNCustom.blp", CurrentEditorValue(view));
    }

    [AvaloniaFact]
    public void Asset_picker_stays_hidden_for_non_asset_fields()
    {
        var view = NewView(out _);
        var row = Row("unam", "Footman", Field("string", null, null));
        Invoke(view, "ShowTextEditor", row);
        Invoke(view, "ConfigureAssetPicker", row, "string");
        Assert.False(Control<SearchableComboBox>(view, "AssetPickerCombo").IsVisible);
    }

    // --- plumbing ---

    /// <summary>A shown panel over a blank in-memory map. The game dir points at a path
    /// that cannot exist, so no test here depends on a local WC3 install.</summary>
    private static ObjectEditorView NewView(out MapSession session)
    {
        var view = new ObjectEditorView();
        var window = new Window { Width = 900, Height = 650, Content = view };
        window.Show();
        session = new MapSession
        {
            Current = BlankMap.Create(),
            GameDir = @"X:\no_such_wc3_install",
        };
        typeof(ObjectEditorView)
            .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(view, session);
        return view;
    }

    private static FormField Field(string type, string? min, string? max) =>
        new("utst", "Test Field", "", "", "base", type, min, max,
            ForceNonNegative: false, CanBeEmpty: true, MultiLine: false,
            ObjectLayer.Map, LayerIsAuthoritative: true);

    private static ObjectEditorView.FieldRow Row(string code, string value, FormField form) =>
        new(new MergedField(code, form.Name, value, "map"), null, "", form with { Code = code, Value = value });

    private static void Invoke(ObjectEditorView view, string method, params object[] args) =>
        view.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, args);

    private static string CurrentEditorValue(ObjectEditorView view) => (string)view.GetType()
        .GetMethod("CurrentEditorValue", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(view, null)!;

    private static T Control<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
        .GetValue(obj)!;
}
