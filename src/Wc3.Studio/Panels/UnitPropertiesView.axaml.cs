using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Wc3.Commands;
using Wc3.Model;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>
/// World-Editor-style properties editor for ONE placed unit (war3mapUnits.doo),
/// opened by clicking a unit in the 3D viewport (the workspace routes the pick here).
/// Thin shell over <see cref="UnitInstanceCommand"/>: read-only identity rows, then the
/// fields the Reforged editor shows, Player, a hero block (Level/Str/Agi/Int) that appears
/// only for heroes, Health/Mana/Target as a Default checkbox plus a stepper, Gold, and an
/// Advanced fold for scale/facing. Apply writes only the fields that actually changed. The
/// owner dropdowns are <see cref="OwnerTeamPicker"/>s, grouping the map's players by force.
/// </summary>
public partial class UnitPropertiesView : UserControl, IMapPanel
{
    private MapSession? _session;
    private int? _creationNumber;
    /// <summary>Type rawcode → display name, resolved once per map through the base game data.</summary>
    private readonly Dictionary<string, string?> _typeNameCache = new();
    /// <summary>The values currently shown, as loaded. Apply diffs against these so untouched
    /// fields never rewrite the file.</summary>
    private UnitInstanceInfo? _loaded;
    /// <summary>Whether the loaded unit's type is a hero (has hero abilities). Gates the hero block
    /// and whether Apply writes hero level/attributes.</summary>
    private bool _isHero;
    /// <summary>The multi-select view's creation numbers (2+ when MultiRoot is visible).</summary>
    private IReadOnlyList<int> _multiSelection = Array.Empty<int>();

    /// <summary>Raised after Apply wrote at least one edit to the in-memory map; the argument is the
    /// unit's creation number. The workspace re-renders the viewport and enables Save.</summary>
    public event Action<int>? UnitEdited;

    /// <summary>Raised after a bulk edit (multi-select owner change) mutated the in-memory map.</summary>
    public event Action<IReadOnlyList<int>>? UnitsEdited;

    /// <summary>Raised after "Remove selected" deleted the units from the in-memory map.</summary>
    public event Action<IReadOnlyList<int>>? UnitsRemoved;

    public UnitPropertiesView()
    {
        InitializeComponent();
    }

    /// <summary>IMapPanel entry: a (re)opened map invalidates creation numbers, so reset to the
    /// "pick a unit" hint until the viewport routes a selection here.</summary>
    public void ShowMap(MapSession session)
    {
        _session = session;
        _creationNumber = null;
        _loaded = null;
        _multiSelection = Array.Empty<int>();
        _typeNameCache.Clear();
        ShowHint(session.Current is null
            ? "No map open."
            : "Click a unit in the 3D view (Terrain tab) to edit its properties.");
    }

    /// <summary>The unit type's display label "Name (rawcode)", resolving the proper name even for
    /// base-game units via the shared <see cref="ObjectGetCommand"/> merge, cached per type.</summary>
    private string TypeLabel(MapDocument doc, string rawcode, string? mapDeltaName)
    {
        var name = mapDeltaName ?? ResolveTypeName(doc, rawcode);
        return string.IsNullOrWhiteSpace(name) ? rawcode : $"{name} ({rawcode})";
    }

    private string? ResolveTypeName(MapDocument doc, string rawcode)
    {
        if (_typeNameCache.TryGetValue(rawcode, out var cached))
            return cached;
        string? name = null;
        try
        {
            var merged = ObjectGetCommand.Execute(doc, ObjectKind.Unit, rawcode, _session?.GameDir);
            if (merged.Found && !string.IsNullOrWhiteSpace(merged.Name))
                name = merged.Name;
        }
        catch { /* no game data / unreadable → fall back to the rawcode */ }
        _typeNameCache[rawcode] = name;
        return name;
    }

    /// <summary>Loads and shows the placed unit. No-ops when that unit is already shown, so tab
    /// flips keep in-progress edits.</summary>
    public void ShowUnit(MapSession session, int creationNumber)
    {
        bool sameDoc = ReferenceEquals(_session?.Current, session.Current);
        _session = session;
        _multiSelection = Array.Empty<int>();
        MultiRoot.IsVisible = false;
        if (sameDoc && _creationNumber == creationNumber && _loaded is not null)
        {
            ContentRoot.IsVisible = true;
            PlaceholderText.IsVisible = false;
            return;
        }
        _creationNumber = creationNumber;
        Load();
    }

    /// <summary>Routes a viewport selection here by size: 0 = the empty hint, 1 = the full
    /// single-unit editor, 2+ = the compact multi-select view.</summary>
    public void ShowUnits(MapSession session, IReadOnlyList<int> creationNumbers)
    {
        if (creationNumbers.Count == 1)
        {
            ShowUnit(session, creationNumbers[0]);
            return;
        }

        _session = session;
        _creationNumber = null;
        _loaded = null;
        _multiSelection = creationNumbers.Count > 1 ? creationNumbers.ToArray() : Array.Empty<int>();

        if (session.Current is not { } doc || creationNumbers.Count == 0)
        {
            MultiRoot.IsVisible = false;
            ShowHint(session.Current is null
                ? "No map open."
                : "Click a unit in the 3D view (Terrain tab) to edit its properties.");
            return;
        }

        string typeSummary = "";
        try
        {
            var sel = new HashSet<int>(creationNumbers);
            var names = UnitInstanceCommand.List(doc)
                .Where(u => sel.Contains(u.CreationNumber))
                .Select(u => u.Name ?? ResolveTypeName(doc, u.TypeRawcode) ?? u.TypeRawcode)
                .Distinct().OrderBy(n => n).ToList();
            if (names.Count > 0)
                typeSummary = " · " + string.Join(", ", names.Take(4)) + (names.Count > 4 ? $", +{names.Count - 4} more" : "");
        }
        catch { /* unreadable placements: just show the count */ }
        MultiHeaderText.Text = $"{creationNumbers.Count} units selected{typeSummary}";
        MultiStatusText.Text = "";

        int? common = null;
        try
        {
            var set = new HashSet<int>(creationNumbers);
            var owners = UnitInstanceCommand.List(doc)
                .Where(u => set.Contains(u.CreationNumber))
                .Select(u => u.OwnerId).Distinct().Take(2).ToList();
            if (owners.Count == 1)
                common = owners[0];
        }
        catch { /* unreadable placements: the picker simply starts blank */ }
        MultiOwnerPicker.Load(doc, common);

        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = false;
        MultiRoot.IsVisible = true;
    }

    /// <summary>(Re)populates every field from the command layer (file truth).</summary>
    private void Load()
    {
        _loaded = null;
        if (_session?.Current is not { } doc || _creationNumber is not { } cn)
        {
            ShowHint("Click a unit in the 3D view (Terrain tab) to edit its properties.");
            return;
        }

        UnitInstanceInfo? info;
        try
        {
            info = UnitInstanceCommand.Get(doc, cn);
        }
        catch (Exception ex)
        {
            ShowHint($"Placed units cannot be read: {ex.Message}");
            return;
        }
        if (info is null)
        {
            ShowHint($"Placed unit #{cn} is not in this map (it may have been removed).");
            return;
        }

        _loaded = info;
        var inv = CultureInfo.InvariantCulture;
        HeaderText.Text = $"Placed unit #{info.CreationNumber}";
        TypeText.Text = TypeLabel(doc, info.TypeRawcode, info.Name);
        PositionText.Text = string.Format(inv, "Position: ({0:0.##}, {1:0.##})", info.X, info.Y);
        OwnerPicker.Load(doc, info.OwnerId);

        // Abilities from every source, and hero detection (only heroes carry hero abilities).
        var abilities = UnitAbilitiesCommand.Execute(doc, info.TypeRawcode, _session?.GameDir, _creationNumber).Abilities;
        _isHero = abilities.Any(a => a.Source == AbilitySource.ObjectHero);
        HeroSection.IsVisible = _isHero;
        if (_isHero)
        {
            HeroLevelBox.Value = info.HeroLevel;
            StrBox.Value = info.HeroStrength;
            AgiBox.Value = info.HeroAgility;
            IntBox.Value = info.HeroIntelligence;
        }

        LoadDefaultable(HpDefault, HpBox, info.HpPercent);
        LoadDefaultable(ManaDefault, ManaBox, info.ManaPercent);
        LoadDefaultable(TargetDefault, TargetBox,
            info.TargetAcquisition < 0f ? -1 : (int)Math.Round(info.TargetAcquisition));
        GoldBox.Value = info.GoldAmount;

        ScaleXBox.Text = info.Scale.Sx.ToString("0.###", inv);
        ScaleYBox.Text = info.Scale.Sy.ToString("0.###", inv);
        ScaleZBox.Text = info.Scale.Sz.ToString("0.###", inv);
        FacingBox.Text = (info.Rotation * 180.0 / Math.PI).ToString("0.##", inv);

        // Every ability this placed unit has, from every source (unit data, spellbooks, its own
        // placed abilities, morph forms, the script), grouped under a line naming the source.
        var abilityLabels = new List<string>();
        foreach (var g in abilities.GroupBy(a => a.Source).OrderBy(g => g.Key))
        {
            abilityLabels.Add($"{UnitAbilitiesCommand.SourceLabel(g.Key)} ({g.Count()})");
            abilityLabels.AddRange(g.Select(a => "    " + UnitAbilitiesCommand.Describe(a)));
        }
        AbilitiesList.ItemsSource = abilityLabels;
        AbilitiesHeader.Text = abilities.Count == 0 ? "Abilities: none" : $"Abilities ({abilities.Count})";

        StatusText.Text = "";
        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
    }

    /// <summary>"Team N: name" forwarder (the builder lives in <see cref="OwnerTeamPicker"/>).</summary>
    internal static string ForceLabel(ForceInfo force) => OwnerTeamPicker.ForceLabel(force);

    // Default checkbox toggles just enable/disable the paired stepper (the value is read at Apply).
    private void OnHpDefaultChanged(object? sender, RoutedEventArgs e) => HpBox.IsEnabled = HpDefault.IsChecked != true;
    private void OnManaDefaultChanged(object? sender, RoutedEventArgs e) => ManaBox.IsEnabled = ManaDefault.IsChecked != true;
    private void OnTargetDefaultChanged(object? sender, RoutedEventArgs e) => TargetBox.IsEnabled = TargetDefault.IsChecked != true;

    /// <summary>Loads a Default-checkbox + stepper pair from a percent-or-range value where -1
    /// (or any negative) means "use the object's default".</summary>
    private static void LoadDefaultable(CheckBox check, NumericUpDown num, int value)
    {
        bool isDefault = value < 0;
        check.IsChecked = isDefault;
        num.IsEnabled = !isDefault;
        num.Value = isDefault ? 0 : value;
    }

    /// <summary>Reads a Default-checkbox + stepper pair back to a value, -1 when Default is checked.</summary>
    private static int ReadDefaultable(CheckBox check, NumericUpDown num) =>
        check.IsChecked == true ? -1 : (int)(num.Value ?? 0m);

    private static int Int(NumericUpDown num) => (int)(num.Value ?? 0m);

    /// <summary>Validates the free-text Advanced fields (scale, facing), then applies only the
    /// changed fields and reloads so the panel shows file truth.</summary>
    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc || _loaded is not { } before || _creationNumber is not { } cn)
        {
            StatusText.Text = "No unit loaded.";
            return;
        }

        if (!TryFloat(ScaleXBox.Text, out float sx) || !TryFloat(ScaleYBox.Text, out float sy)
            || !TryFloat(ScaleZBox.Text, out float sz))
        { StatusText.Text = "Scale values must be numbers (dot decimal, e.g. 1.25)."; return; }
        if (!TryFloat(FacingBox.Text, out float facingDeg))
        { StatusText.Text = "Facing must be a number in degrees."; return; }

        var messages = new List<string>();
        bool anyOk = false, allOk = true;
        void Run(UnitEditResult result)
        {
            anyOk |= result.Ok;
            allOk &= result.Ok;
            messages.Add(result.Message);
        }

        if (OwnerPicker.SelectedOwnerId is int newOwner && newOwner != before.OwnerId)
            Run(UnitInstanceCommand.SetOwner(doc, cn, newOwner));

        // Hero fields only when the unit is actually a hero (the block is hidden otherwise).
        if (_isHero)
        {
            if (Int(HeroLevelBox) != before.HeroLevel)
                Run(UnitInstanceCommand.SetHeroLevel(doc, cn, Int(HeroLevelBox)));
            if (Int(StrBox) != before.HeroStrength)
                Run(UnitInstanceCommand.SetHeroStrength(doc, cn, Int(StrBox)));
            if (Int(AgiBox) != before.HeroAgility)
                Run(UnitInstanceCommand.SetHeroAgility(doc, cn, Int(AgiBox)));
            if (Int(IntBox) != before.HeroIntelligence)
                Run(UnitInstanceCommand.SetHeroIntelligence(doc, cn, Int(IntBox)));
        }

        int hp = ReadDefaultable(HpDefault, HpBox);
        if (hp != before.HpPercent)
            Run(UnitInstanceCommand.SetHpPercent(doc, cn, hp));
        int mana = ReadDefaultable(ManaDefault, ManaBox);
        if (mana != before.ManaPercent)
            Run(UnitInstanceCommand.SetManaPercent(doc, cn, mana));

        int targetVal = ReadDefaultable(TargetDefault, TargetBox);
        float target = targetVal < 0 ? -1f : targetVal;
        if (Differs(target, before.TargetAcquisition, 0.5f))
            Run(UnitInstanceCommand.SetTargetAcquisition(doc, cn, target));

        if (Int(GoldBox) != before.GoldAmount)
            Run(UnitInstanceCommand.SetGold(doc, cn, Int(GoldBox)));

        // Epsilons absorb the display rounding so an untouched field never registers as a change.
        if (Differs(sx, before.Scale.Sx, 0.001f) || Differs(sy, before.Scale.Sy, 0.001f)
            || Differs(sz, before.Scale.Sz, 0.001f))
            Run(UnitInstanceCommand.SetScale(doc, cn, sx, sy, sz));
        float facingRad = (float)(facingDeg * Math.PI / 180.0);
        if (Differs(facingRad, before.Rotation, 0.0002f))
            Run(UnitInstanceCommand.SetFacing(doc, cn, facingRad));

        if (messages.Count == 0)
        {
            StatusText.Text = "No changes to apply.";
            return;
        }

        Load();
        StatusText.Text = (allOk ? "" : "Some edits failed. ") + string.Join("; ", messages);
        if (anyOk)
            UnitEdited?.Invoke(cn);
    }

    /// <summary>Multi-select "Apply": sets the picked owner on every selected unit in one write.</summary>
    private void OnMultiApplyOwnerClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc || _multiSelection.Count == 0)
        {
            MultiStatusText.Text = "No units selected.";
            return;
        }
        if (MultiOwnerPicker.SelectedOwnerId is not int owner)
        {
            MultiStatusText.Text = "Pick an owner first.";
            return;
        }

        var result = UnitInstanceCommand.SetOwnerMany(doc, _multiSelection, owner);
        MultiStatusText.Text = result.Message;
        if (result.Ok)
            UnitsEdited?.Invoke(_multiSelection);
    }

    /// <summary>Multi-select "Remove selected": deletes every selected unit from the in-memory map.</summary>
    private void OnMultiRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc || _multiSelection.Count == 0)
        {
            MultiStatusText.Text = "No units selected.";
            return;
        }

        var removed = _multiSelection;
        var result = UnitInstanceCommand.DeleteMany(doc, removed);
        MultiStatusText.Text = result.Message;
        if (result.Ok)
        {
            _multiSelection = Array.Empty<int>();
            UnitsRemoved?.Invoke(removed);
        }
    }

    private static bool Differs(float a, float b, float epsilon) => Math.Abs(a - b) > epsilon;

    private static bool TryFloat(string? text, out float value) =>
        float.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void ShowHint(string text)
    {
        PlaceholderText.Text = text;
        PlaceholderText.IsVisible = true;
        ContentRoot.IsVisible = false;
        MultiRoot.IsVisible = false;
    }
}
