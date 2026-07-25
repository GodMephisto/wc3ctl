using System.Globalization;
using Avalonia.Controls;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>World-Editor style "Map Description / Map Options" editor, a thin shell
/// over <see cref="MapInfoCommand"/>. Refresh populates every editor from the parsed
/// w3i, one Apply writes only the fields that actually changed (each through
/// <see cref="MapInfoCommand.Set"/>, so the write path stays byte-faithful). All
/// parsing and validation of stored values lives in Wc3.Commands.</summary>
public partial class MapInfoView : UserControl, IMapPanel
{
    private MapSession? _session;
    /// <summary>The values currently shown, as loaded. Apply diffs against these so
    /// untouched fields never rewrite the file.</summary>
    private MapInfoFields? _loaded;

    public MapInfoView()
    {
        InitializeComponent();
        TilesetPicker.SetItems(Choices(MapInfoCommand.TilesetChoices), selectFirstWhenNoMatch: false);
        LightPicker.SetItems(Choices(MapInfoCommand.TilesetChoices), selectFirstWhenNoMatch: false);
        WeatherPicker.SetItems(Choices(MapInfoCommand.WeatherChoices), selectFirstWhenNoMatch: false);
        FogStylePicker.SetItems(Choices(MapInfoCommand.FogStyleChoices), selectFirstWhenNoMatch: false);
        ApplyButton.Click += (_, _) => OnApply();
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        Refresh();
    }

    private static List<SearchableComboBoxItem> Choices(IReadOnlyList<FieldChoice> choices) =>
        choices.Select(ch => new SearchableComboBoxItem(ch.Name, ch.Id)).ToList();

    /// <summary>(Re)populates every editor from the command layer (file truth).</summary>
    private void Refresh()
    {
        _loaded = null;
        var doc = _session?.Current;
        if (doc is null)
        {
            ShowHint("No map open.");
            return;
        }

        MapInfoFields f;
        try
        {
            f = MapInfoCommand.Read(doc);
        }
        catch (InvalidOperationException ex)
        {
            ShowHint(ex.Message);
            return;
        }

        _loaded = f;
        var inv = CultureInfo.InvariantCulture;
        // MapInfoCommand.Read resolves TRIGSTR_ references against war3map.wts, so the boxes
        // show real text (the map title, not "TRIGSTR_4084"). Apply diffs against these loaded
        // values, so an untouched field is never rewritten and its TRIGSTR reference survives;
        // editing a field replaces it with the typed literal.
        var strings = MapStrings.From(doc);

        SizeText.Text = $"Playable area {f.PlayableWidth} x {f.PlayableHeight}, {f.Players} player slot(s)";
        CameraText.Text = f.CameraBounds.Length == 0 ? "" : $"Camera bounds: {f.CameraBounds}";

        SetText(NameBox, NameField, f.MapName, strings);
        SetText(AuthorBox, AuthorField, f.Author, strings);
        SetText(DescriptionBox, DescriptionField, f.Description, strings);
        SetText(SuggestedBox, SuggestedField, f.RecommendedPlayers, strings);

        SelectChoice(TilesetPicker, f.Tileset);
        SelectChoice(LightPicker, f.LightEnvironment);
        SelectChoice(WeatherPicker, f.GlobalWeather);
        SoundBox.Text = f.SoundEnvironment;
        TintWaterCheck.IsChecked = f.HasWaterTint;
        WaterTintBox.Text = f.WaterTintColor;

        UseFogCheck.IsChecked = f.HasTerrainFog;
        SelectChoice(FogStylePicker, f.FogStyle);
        FogStartBox.Text = f.FogStartZ.ToString("0.###", inv);
        FogEndBox.Text = f.FogEndZ.ToString("0.###", inv);
        FogDensityBox.Text = f.FogDensity.ToString("0.###", inv);
        FogColorBox.Text = f.FogColor;

        LoadingIndexBox.Text = f.LoadingScreenIndex.ToString(inv);
        LoadingPathBox.Text = f.LoadingScreenPath;
        SetText(LoadingTitleBox, LoadingTitleField, f.LoadingScreenTitle, strings);
        SetText(LoadingSubtitleBox, LoadingSubtitleField, f.LoadingScreenSubtitle, strings);
        SetText(LoadingTextBox, LoadingTextField, f.LoadingScreenText, strings);

        SetText(PrologueTitleBox, PrologueTitleField, f.PrologueTitle, strings);
        SetText(PrologueSubtitleBox, PrologueSubtitleField, f.PrologueSubtitle, strings);
        SetText(PrologueTextBox, PrologueTextField, f.PrologueText, strings);

        MeleeCheck.IsChecked = f.MeleeMap;
        HideMinimapCheck.IsChecked = f.HideMinimapInPreview;
        AllyPrioritiesCheck.IsChecked = f.ModifyAllyPriorities;
        MaskedPartialCheck.IsChecked = f.MaskedAreasPartiallyVisible;
        FixedSettingsCheck.IsChecked = f.FixedPlayerSettings;
        CustomForcesCheck.IsChecked = f.UseCustomForces;
        CustomTechCheck.IsChecked = f.UseCustomTechtree;
        CustomAbilitiesCheck.IsChecked = f.UseCustomAbilities;
        CustomUpgradesCheck.IsChecked = f.UseCustomUpgrades;
        CliffWavesCheck.IsChecked = f.WaterWavesOnCliffShores;
        RollingWavesCheck.IsChecked = f.WaterWavesOnRollingShores;
        ItemClassCheck.IsChecked = f.ItemClassification;
        AccurateProbCheck.IsChecked = f.AccurateProbabilities;

        StatusText.Text = "";
        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
    }

    /// <summary>Fills a raw-value editor and shows the wts-resolved text as the row
    /// hint when the value is a TRIGSTR_ reference.</summary>
    private static void SetText(TextBox box, LabeledField field, string raw, MapStrings strings)
    {
        box.Text = raw;
        var resolved = raw.StartsWith("TRIGSTR_", StringComparison.OrdinalIgnoreCase)
            ? strings.Resolve(raw)
            : null;
        field.Hint = resolved is null || resolved == raw ? null : resolved;
    }

    /// <summary>Selects the picker row matching a stored value by Set-name or short id,
    /// leaving the selection empty when nothing matches (e.g. an unknown weather code).</summary>
    private static void SelectChoice(SearchableComboBox box, string value)
    {
        var item = box.Items.FirstOrDefault(i =>
            string.Equals(i.Name, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(i.Id, value, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
            box.Select(item.Id);
        else
            box.SetItems(box.Items, selectId: null, selectFirstWhenNoMatch: false);
    }

    /// <summary>Validates the numeric editors first (nothing is written when one is
    /// bad), then applies only the changed fields and reloads so the panel shows file
    /// truth. Colors and picker values are validated by the command itself.</summary>
    private void OnApply()
    {
        if (_session?.Current is not { } doc || _loaded is not { } before)
        {
            StatusText.Text = "No map info loaded.";
            return;
        }

        var inv = CultureInfo.InvariantCulture;
        if (!TryFloat(FogStartBox.Text, out var fogStart))
        { StatusText.Text = "Fog Start Z must be a number (dot decimal)."; return; }
        if (!TryFloat(FogEndBox.Text, out var fogEnd))
        { StatusText.Text = "Fog End Z must be a number (dot decimal)."; return; }
        if (!TryFloat(FogDensityBox.Text, out var fogDensity))
        { StatusText.Text = "Fog density must be a number (dot decimal)."; return; }
        if (!TryInt(LoadingIndexBox.Text, out var loadingIndex))
        { StatusText.Text = "Loading screen preset must be a whole number (-1 = none)."; return; }

        var edits = new List<(string Field, string Value)>();
        void Text(string field, TextBox box, string was)
        {
            var now = box.Text ?? "";
            if (now != was)
                edits.Add((field, now));
        }
        void Pick(string field, SearchableComboBox box, string was)
        {
            if (box.SelectedItem is { } item
                && !string.Equals(item.Name, was, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.Id, was, StringComparison.OrdinalIgnoreCase))
                edits.Add((field, item.Name));
        }
        void Flag(string field, CheckBox box, bool was)
        {
            var now = box.IsChecked == true;
            if (now != was)
                edits.Add((field, now ? "true" : "false"));
        }

        Text("MapName", NameBox, before.MapName);
        Text("Author", AuthorBox, before.Author);
        Text("Description", DescriptionBox, before.Description);
        Text("RecommendedPlayers", SuggestedBox, before.RecommendedPlayers);

        Pick("Tileset", TilesetPicker, before.Tileset);
        Pick("LightEnvironment", LightPicker, before.LightEnvironment);
        Pick("GlobalWeather", WeatherPicker, before.GlobalWeather);
        Text("SoundEnvironment", SoundBox, before.SoundEnvironment);
        Flag("HasWaterTint", TintWaterCheck, before.HasWaterTint);
        Text("WaterTintColor", WaterTintBox, before.WaterTintColor);

        Flag("HasTerrainFog", UseFogCheck, before.HasTerrainFog);
        Pick("FogStyle", FogStylePicker, before.FogStyle);
        // Epsilons absorb the "0.###" display rounding: an untouched box parses back
        // within them, so it never registers as a change.
        if (Differs(fogStart, before.FogStartZ))
            edits.Add(("FogStartZ", fogStart.ToString(inv)));
        if (Differs(fogEnd, before.FogEndZ))
            edits.Add(("FogEndZ", fogEnd.ToString(inv)));
        if (Differs(fogDensity, before.FogDensity))
            edits.Add(("FogDensity", fogDensity.ToString(inv)));
        Text("FogColor", FogColorBox, before.FogColor);

        if (loadingIndex != before.LoadingScreenIndex)
            edits.Add(("LoadingScreenIndex", loadingIndex.ToString(inv)));
        Text("LoadingScreenPath", LoadingPathBox, before.LoadingScreenPath);
        Text("LoadingScreenTitle", LoadingTitleBox, before.LoadingScreenTitle);
        Text("LoadingScreenSubtitle", LoadingSubtitleBox, before.LoadingScreenSubtitle);
        Text("LoadingScreenText", LoadingTextBox, before.LoadingScreenText);

        Text("PrologueTitle", PrologueTitleBox, before.PrologueTitle);
        Text("PrologueSubtitle", PrologueSubtitleBox, before.PrologueSubtitle);
        Text("PrologueText", PrologueTextBox, before.PrologueText);

        Flag("MeleeMap", MeleeCheck, before.MeleeMap);
        Flag("HideMinimapInPreview", HideMinimapCheck, before.HideMinimapInPreview);
        Flag("ModifyAllyPriorities", AllyPrioritiesCheck, before.ModifyAllyPriorities);
        Flag("MaskedAreasPartiallyVisible", MaskedPartialCheck, before.MaskedAreasPartiallyVisible);
        Flag("FixedPlayerSettings", FixedSettingsCheck, before.FixedPlayerSettings);
        Flag("UseCustomForces", CustomForcesCheck, before.UseCustomForces);
        Flag("UseCustomTechtree", CustomTechCheck, before.UseCustomTechtree);
        Flag("UseCustomAbilities", CustomAbilitiesCheck, before.UseCustomAbilities);
        Flag("UseCustomUpgrades", CustomUpgradesCheck, before.UseCustomUpgrades);
        Flag("WaterWavesOnCliffShores", CliffWavesCheck, before.WaterWavesOnCliffShores);
        Flag("WaterWavesOnRollingShores", RollingWavesCheck, before.WaterWavesOnRollingShores);
        Flag("ItemClassification", ItemClassCheck, before.ItemClassification);
        Flag("AccurateProbabilities", AccurateProbCheck, before.AccurateProbabilities);

        if (edits.Count == 0)
        {
            StatusText.Text = "No changes to apply.";
            return;
        }

        var applied = 0;
        var errors = new List<string>();
        foreach (var (field, value) in edits)
        {
            try
            {
                MapInfoCommand.Set(doc, field, value);
                applied++;
            }
            catch (ArgumentException ex)
            {
                errors.Add($"{field}: {FirstLine(ex.Message)}");
            }
        }

        // Reload so the editors show what the file now carries (failed edits revert),
        // THEN report. Refresh clears the status line.
        Refresh();
        StatusText.Text = errors.Count == 0
            ? $"{applied} field(s) updated (in memory). Save the map to persist."
            : $"{applied} updated, {errors.Count} failed. {string.Join(" ", errors)}";
    }

    /// <summary>ArgumentException appends "(Parameter '...')" on its own line; only the
    /// first line is a user-facing message.</summary>
    private static string FirstLine(string message)
    {
        var i = message.IndexOf('\n');
        return i < 0 ? message : message[..i].TrimEnd('\r', ' ');
    }

    private static bool Differs(float a, float b) => Math.Abs(a - b) > 0.001f;

    private static bool TryInt(string? text, out int value) =>
        int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out value);

    private static bool TryFloat(string? text, out float value) =>
        float.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
            out value);

    private void ShowHint(string text)
    {
        PlaceholderText.Text = text;
        PlaceholderText.IsVisible = true;
        ContentRoot.IsVisible = false;
    }
}
