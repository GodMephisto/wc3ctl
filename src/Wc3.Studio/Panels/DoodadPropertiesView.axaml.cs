using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Studio.Panels;

/// <summary>
/// World-Editor-style properties editor for ONE placed doodad/destructable
/// (war3map.doo), opened by clicking a doodad in the 3D viewport (the workspace
/// routes the pick here). Thin shell over <see cref="DoodadInstanceCommand"/>,
/// the doodad twin of <see cref="UnitPropertiesView"/>: read-only identity rows
/// plus editable fields (LabeledField rows) and one Apply that writes only the
/// fields that actually changed, plus a Remove that deletes the placement.
/// </summary>
public partial class DoodadPropertiesView : UserControl, IMapPanel
{
    private MapSession? _session;
    private int? _creationNumber;
    /// <summary>Type rawcode → display name, resolved once per map through the base game
    /// data (war3map.doo mixes doodads and destructables, so both kinds are consulted).
    /// Cleared on map change since map-local name deltas differ per map.</summary>
    private readonly Dictionary<string, string?> _typeNameCache = new();
    /// <summary>The values currently shown, as loaded. Apply diffs against these so
    /// untouched fields never rewrite the file.</summary>
    private DoodadInstanceInfo? _loaded;

    /// <summary>Raised after Apply wrote at least one edit to the in-memory map, the
    /// argument is the doodad's creation number. The workspace reacts by re-rendering
    /// the viewport, restoring the highlight, and enabling Save.</summary>
    public event Action<int>? DoodadEdited;

    /// <summary>Raised after Remove deleted the doodad from the in-memory map, the
    /// argument is the removed creation number. The workspace re-renders the viewport,
    /// clears the selection, and enables Save.</summary>
    public event Action<int>? DoodadRemoved;

    public DoodadPropertiesView()
    {
        InitializeComponent();
    }

    /// <summary>IMapPanel entry: a (re)opened map invalidates creation numbers, so
    /// reset to the "pick a doodad" hint until the viewport routes a selection here.</summary>
    public void ShowMap(MapSession session)
    {
        _session = session;
        _creationNumber = null;
        _loaded = null;
        _typeNameCache.Clear(); // names (incl. map-local deltas) belong to the previous map
        ShowHint(session.Current is null
            ? "No map open."
            : "Click a doodad in the 3D view (Terrain tab) to edit its properties.");
    }

    /// <summary>Loads and shows the placed doodad, a superset of <see cref="ShowMap"/>.
    /// No-ops when that doodad is already shown, so tab flips keep in-progress edits.</summary>
    public void ShowDoodad(MapSession session, int creationNumber)
    {
        bool sameDoc = ReferenceEquals(_session?.Current, session.Current);
        _session = session;
        if (sameDoc && _creationNumber == creationNumber && _loaded is not null)
        {
            // Already showing this doodad (keep in-progress edits), just surface it.
            ContentRoot.IsVisible = true;
            PlaceholderText.IsVisible = false;
            return;
        }
        _creationNumber = creationNumber;
        Load();
    }

    /// <summary>The doodad type's display label "Name (rawcode)", resolving the proper
    /// name even for base-game doodads (which carry no map name delta) via the shared
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
        // war3map.doo holds both kinds, so try the doodad catalog first, then
        // destructables (same dual lookup DoodadInstanceCommand's name deltas use).
        foreach (var kind in new[] { ObjectKind.Doodad, ObjectKind.Destructable })
        {
            try
            {
                var merged = ObjectGetCommand.Execute(doc, kind, rawcode, _session?.GameDir);
                if (merged.Found && !string.IsNullOrWhiteSpace(merged.Name))
                {
                    name = merged.Name;
                    break;
                }
            }
            catch { /* no game data / unreadable → fall back to the rawcode */ }
        }
        _typeNameCache[rawcode] = name;
        return name;
    }

    /// <summary>(Re)populates every field from the command layer (file truth).</summary>
    private void Load()
    {
        _loaded = null;
        if (_session?.Current is not { } doc || _creationNumber is not { } cn)
        {
            ShowHint("Click a doodad in the 3D view (Terrain tab) to edit its properties.");
            return;
        }

        DoodadInstanceInfo? info;
        try
        {
            info = DoodadInstanceCommand.Get(doc, cn);
        }
        catch (Exception ex)
        {
            ShowHint($"Placed doodads cannot be read: {ex.Message}");
            return;
        }
        if (info is null)
        {
            ShowHint($"Placed doodad #{cn} is not in this map (it may have been removed).");
            return;
        }

        _loaded = info;
        var inv = CultureInfo.InvariantCulture;
        HeaderText.Text = $"Placed doodad #{info.CreationNumber}";
        TypeText.Text = TypeLabel(doc, info.TypeRawcode, info.Name);
        PositionText.Text = string.Format(inv,
            "Position: ({0:0.##}, {1:0.##}, {2:0.##})", info.X, info.Y, info.Z);
        RotationBox.Text = (info.Rotation * 180.0 / Math.PI).ToString("0.##", inv);
        ScaleXBox.Text = info.Scale.Sx.ToString("0.###", inv);
        ScaleYBox.Text = info.Scale.Sy.ToString("0.###", inv);
        ScaleZBox.Text = info.Scale.Sz.ToString("0.###", inv);
        VariationBox.Text = info.Variation.ToString(inv);
        LifeBox.Text = info.LifePercent.ToString(inv);
        StatusText.Text = "";
        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
    }

    /// <summary>Validates every field first (nothing is written when any is bad), then
    /// applies only the changed ones and reloads so the panel shows file truth.</summary>
    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc || _loaded is not { } before
            || _creationNumber is not { } cn)
        {
            StatusText.Text = "No doodad loaded.";
            return;
        }

        if (!TryFloat(RotationBox.Text, out float rotationDeg))
        { StatusText.Text = "Rotation must be a number in degrees."; return; }
        if (!TryFloat(ScaleXBox.Text, out float sx) || !TryFloat(ScaleYBox.Text, out float sy)
            || !TryFloat(ScaleZBox.Text, out float sz))
        { StatusText.Text = "Scale values must be numbers (dot decimal, e.g. 1.25)."; return; }
        if (!TryInt(VariationBox.Text, out int variation))
        { StatusText.Text = "Variation must be a whole number, 0 or more."; return; }
        if (!TryInt(LifeBox.Text, out int life))
        { StatusText.Text = "Life % must be a whole number in 0..100."; return; }

        var messages = new List<string>();
        bool anyOk = false, allOk = true;
        void Run(DoodadEditResult result)
        {
            anyOk |= result.Ok;
            allOk &= result.Ok;
            messages.Add(result.Message);
        }

        // Epsilons absorb the display rounding ("0.###" / "0.##"): an untouched field
        // parses back within them, so it never registers as a change.
        float rotationRad = (float)(rotationDeg * Math.PI / 180.0);
        if (Differs(rotationRad, before.Rotation, 0.0002f))
            Run(DoodadInstanceCommand.SetRotation(doc, cn, rotationRad));
        if (Differs(sx, before.Scale.Sx, 0.001f) || Differs(sy, before.Scale.Sy, 0.001f)
            || Differs(sz, before.Scale.Sz, 0.001f))
            Run(DoodadInstanceCommand.SetScale(doc, cn, sx, sy, sz));
        if (variation != before.Variation)
            Run(DoodadInstanceCommand.SetVariation(doc, cn, variation));
        if (life != before.LifePercent)
            Run(DoodadInstanceCommand.SetLifePercent(doc, cn, life));

        if (messages.Count == 0)
        {
            StatusText.Text = "No changes to apply.";
            return;
        }

        // Reload so the fields show what the file now carries (failed edits revert),
        // THEN report, because Load clears the status line.
        Load();
        StatusText.Text = (allOk ? "" : "Some edits failed. ") + string.Join("; ", messages);
        if (anyOk)
            DoodadEdited?.Invoke(cn);
    }

    /// <summary>Deletes this placed doodad from the in-memory map, resets the panel to
    /// its hint, and notifies the workspace (which clears the viewport selection and
    /// refreshes placements).</summary>
    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc || _creationNumber is not { } cn)
        {
            StatusText.Text = "No doodad loaded.";
            return;
        }

        var result = DoodadInstanceCommand.Delete(doc, cn);
        if (!result.Ok)
        {
            StatusText.Text = result.Message;
            return;
        }
        _creationNumber = null;
        _loaded = null;
        ShowHint($"Doodad #{cn} removed. Click another doodad in the 3D view to edit it.");
        DoodadRemoved?.Invoke(cn);
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
    }
}
