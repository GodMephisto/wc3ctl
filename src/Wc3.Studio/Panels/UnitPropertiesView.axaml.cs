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
/// Thin shell over <see cref="UnitInstanceCommand"/>: read-only identity rows plus
/// editable fields (LabeledField rows) and one Apply that writes only the fields that
/// actually changed. The owner dropdowns are <see cref="OwnerTeamPicker"/>s, which
/// group the map's players under their force (team), neutrals last.
/// </summary>
public partial class UnitPropertiesView : UserControl, IMapPanel
{
    private MapSession? _session;
    private int? _creationNumber;
    /// <summary>Type rawcode → display name, resolved once per map through the base game
    /// data (units placed from the palette carry no map name delta, so the hermetic
    /// UnitInstanceCommand read returns null — we resolve "Footman" etc. here). Cleared on
    /// map change since map-local name deltas differ per map.</summary>
    private readonly Dictionary<string, string?> _typeNameCache = new();
    /// <summary>The values currently shown, as loaded - Apply diffs against these so
    /// untouched fields never rewrite the file.</summary>
    private UnitInstanceInfo? _loaded;
    /// <summary>The multi-select view's creation numbers (2+ when MultiRoot is
    /// visible, empty otherwise). Bulk Apply/Remove act on this snapshot.</summary>
    private IReadOnlyList<int> _multiSelection = Array.Empty<int>();

    /// <summary>Raised after Apply wrote at least one edit to the in-memory map; the
    /// argument is the unit's creation number. The workspace reacts by re-rendering
    /// the viewport, restoring the highlight, and enabling Save.</summary>
    public event Action<int>? UnitEdited;

    /// <summary>Raised after a bulk edit (multi-select owner change) mutated the
    /// in-memory map; the argument is the affected creation numbers. The workspace
    /// re-renders the viewport, re-echoes the selection, and enables Save.</summary>
    public event Action<IReadOnlyList<int>>? UnitsEdited;

    /// <summary>Raised after "Remove selected" deleted the units from the in-memory
    /// map; the argument is the removed creation numbers. The workspace re-renders
    /// the viewport, clears the selection, and enables Save.</summary>
    public event Action<IReadOnlyList<int>>? UnitsRemoved;

    public UnitPropertiesView()
    {
        InitializeComponent();
    }

    /// <summary>IMapPanel entry: a (re)opened map invalidates creation numbers, so
    /// reset to the "pick a unit" hint until the viewport routes a selection here.</summary>
    public void ShowMap(MapSession session)
    {
        _session = session;
        _creationNumber = null;
        _loaded = null;
        _multiSelection = Array.Empty<int>();
        _typeNameCache.Clear(); // names (incl. map-local deltas) belong to the previous map
        ShowHint(session.Current is null
            ? "No map open."
            : "Click a unit in the 3D view (Terrain tab) to edit its properties.");
    }

    /// <summary>The unit type's display label "Name (rawcode)", resolving the proper name
    /// even for base-game units (which carry no map name delta) via the shared
    /// <see cref="ObjectGetCommand"/> merge, cached per type. Falls back to the bare
    /// rawcode only when no name resolves (e.g. no game data available).</summary>
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

    /// <summary>Loads and shows the placed unit - a superset of <see cref="ShowMap"/>.
    /// No-ops when that unit is already shown, so tab flips keep in-progress edits.</summary>
    public void ShowUnit(MapSession session, int creationNumber)
    {
        bool sameDoc = ReferenceEquals(_session?.Current, session.Current);
        _session = session;
        _multiSelection = Array.Empty<int>();
        MultiRoot.IsVisible = false;
        if (sameDoc && _creationNumber == creationNumber && _loaded is not null)
        {
            // Already showing this unit (keep in-progress edits) - just make sure the
            // single editor is frontmost after a multi view.
            ContentRoot.IsVisible = true;
            PlaceholderText.IsVisible = false;
            return;
        }
        _creationNumber = creationNumber;
        Load();
    }

    /// <summary>Routes a viewport selection here by size: 0 = the empty hint, 1 = the
    /// full single-unit editor (<see cref="ShowUnit"/>), 2+ = the compact multi-select
    /// view (bulk owner change + remove).</summary>
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
        _multiSelection = creationNumbers.Count > 1
            ? creationNumbers.ToArray()
            : Array.Empty<int>();

        if (session.Current is not { } doc || creationNumbers.Count == 0)
        {
            MultiRoot.IsVisible = false;
            ShowHint(session.Current is null
                ? "No map open."
                : "Click a unit in the 3D view (Terrain tab) to edit its properties.");
            return;
        }

        // Header: count + the distinct type names in the selection (proper names, not ids).
        string typeSummary = "";
        try
        {
            var sel = new HashSet<int>(creationNumbers);
            var names = UnitInstanceCommand.List(doc)
                .Where(u => sel.Contains(u.CreationNumber))
                .Select(u => u.Name ?? ResolveTypeName(doc, u.TypeRawcode) ?? u.TypeRawcode)
                .Distinct().OrderBy(n => n).ToList();
            if (names.Count > 0)
                typeSummary = " · " + string.Join(", ",
                    names.Take(4)) + (names.Count > 4 ? $", +{names.Count - 4} more" : "");
        }
        catch { /* unreadable placements: just show the count */ }
        MultiHeaderText.Text = $"{creationNumbers.Count} units selected{typeSummary}";
        MultiStatusText.Text = "";

        // Owner dropdown: same grouped/colored builder as the single editor. When the
        // whole selection already shares one owner, preselect it; otherwise leave the
        // picker blank so a mixed selection is not misreported.
        int? common = null;
        try
        {
            var set = new HashSet<int>(creationNumbers);
            var owners = UnitInstanceCommand.List(doc)
                .Where(u => set.Contains(u.CreationNumber))
                .Select(u => u.OwnerId)
                .Distinct()
                .Take(2)
                .ToList();
            if (owners.Count == 1)
                common = owners[0];
        }
        catch
        {
            // unreadable placements: the picker simply starts blank
        }
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
        HeroLevelBox.Text = info.HeroLevel.ToString(inv);
        HpBox.Text = info.HpPercent.ToString(inv);
        ManaBox.Text = info.ManaPercent.ToString(inv);
        GoldBox.Text = info.GoldAmount.ToString(inv);
        ScaleXBox.Text = info.Scale.Sx.ToString("0.###", inv);
        ScaleYBox.Text = info.Scale.Sy.ToString("0.###", inv);
        ScaleZBox.Text = info.Scale.Sz.ToString("0.###", inv);
        FacingBox.Text = (info.Rotation * 180.0 / Math.PI).ToString("0.##", inv);

        // The unit type's abilities, resolved from rawcodes to names so a character's
        // spells are actually readable (game-data is already warm from TypeLabel above).
        var abilities = UnitAbilitiesCommand.ForUnitType(doc, info.TypeRawcode, _session?.GameDir);
        var abilityLabels = abilities
            .Select(a => (a.Name ?? a.Rawcode) + (a.IsHeroAbility ? "  (hero)" : ""))
            .ToList();
        AbilitiesList.ItemsSource = abilityLabels;
        AbilitiesHeader.Text = abilityLabels.Count == 0 ? "Abilities: none" : $"Abilities ({abilityLabels.Count})";

        StatusText.Text = "";
        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
    }

    /// <summary>"Team N: name" (or a bare "Team N" for a nameless force). The builder
    /// moved into <see cref="OwnerTeamPicker"/> with the grouped owner dropdown; this
    /// forwarder stays so existing callers keep working.</summary>
    internal static string ForceLabel(ForceInfo force) => OwnerTeamPicker.ForceLabel(force);

    /// <summary>Validates every field first (nothing is written when any is bad), then
    /// applies only the changed ones and reloads so the panel shows file truth.</summary>
    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc || _loaded is not { } before
            || _creationNumber is not { } cn)
        {
            StatusText.Text = "No unit loaded.";
            return;
        }

        if (!TryInt(HeroLevelBox.Text, out int heroLevel))
        { StatusText.Text = "Hero Level must be a whole number."; return; }
        if (!TryInt(HpBox.Text, out int hp))
        { StatusText.Text = "HP % must be a whole number: 0..100, or -1 for default."; return; }
        if (!TryInt(ManaBox.Text, out int mana))
        { StatusText.Text = "Mana % must be a whole number: 0..100, or -1 for default."; return; }
        if (!TryInt(GoldBox.Text, out int gold))
        { StatusText.Text = "Gold must be a whole number."; return; }
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
        if (heroLevel != before.HeroLevel)
            Run(UnitInstanceCommand.SetHeroLevel(doc, cn, heroLevel));
        if (hp != before.HpPercent)
            Run(UnitInstanceCommand.SetHpPercent(doc, cn, hp));
        if (mana != before.ManaPercent)
            Run(UnitInstanceCommand.SetManaPercent(doc, cn, mana));
        if (gold != before.GoldAmount)
            Run(UnitInstanceCommand.SetGold(doc, cn, gold));
        // Epsilons absorb the display rounding ("0.###" / "0.##"): an untouched field
        // parses back within them, so it never registers as a change.
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

        // Reload so the fields show what the file now carries (failed edits revert),
        // THEN report - Load clears the status line.
        Load();
        StatusText.Text = (allOk ? "" : "Some edits failed. ") + string.Join("; ", messages);
        if (anyOk)
            UnitEdited?.Invoke(cn);
    }

    /// <summary>Multi-select "Apply": sets the picked owner on every selected unit in
    /// one command-layer write, reports the result, and notifies the workspace.</summary>
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

    /// <summary>Multi-select "Remove selected": deletes every selected unit from the
    /// in-memory map, reports the result, and notifies the workspace (which clears the
    /// viewport selection and refreshes placements).</summary>
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
        MultiRoot.IsVisible = false;
    }
}
