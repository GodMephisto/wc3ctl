using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Wc3.Commands;

namespace Wc3.Studio.Panels;

/// <summary>Tile model for the palette grid (reflection-bound from XAML). Notifies for
/// <see cref="Icon"/> (lands after an off-thread decode) and <see cref="IsSelected"/>
/// (drives the selection highlight).</summary>
public sealed class PaletteRow : INotifyPropertyChanged
{
    public ObjectKind Kind { get; init; }
    public string Rawcode { get; init; } = "";
    public string? Name { get; init; }
    public string Source { get; init; } = "";
    /// <summary>Icon art path from the palette entry (null = no icon for this row).</summary>
    public string? IconPath { get; init; }
    /// <summary>Loader shared by every row of one catalog load; null when icons are off (tests).</summary>
    internal PaletteIconLoader? IconLoader { get; init; }

    private Bitmap? _icon;
    private bool _iconRequested;

    /// <summary>
    /// The row's decoded icon, or null (no/undecodable icon — the tile simply stays an
    /// empty box). Lazy: the first read — which happens when the grid realizes the tile
    /// and binds it — kicks off an off-thread decode; the binding refreshes via
    /// PropertyChanged when it lands. Never blocks the UI thread, never throws.
    /// </summary>
    public Bitmap? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                if (IconPath is not null && IconLoader is not null)
                {
                    if (IconLoader.TryGetCached(IconPath, out var cached))
                        _icon = cached;
                    else
                        IconLoader.Load(IconPath, bmp =>
                        {
                            if (bmp is null) return; // graceful fallback: empty tile
                            _icon = bmp;
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
                        });
                }
            }
            return _icon;
        }
    }

    private bool _isSelected;
    /// <summary>Whether this tile is the armed placement — drives the selection highlight
    /// via a <c>Classes.selected</c> binding. Set by <see cref="PaletteView"/>.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>How many times this type is actually placed on the map (0 if unplaced).</summary>
    public int PlacedCount { get; init; }
    /// <summary>Whether the map's script (war3map.j) references this rawcode - the "in use"
    /// signal for maps that spawn characters at runtime instead of pre-placing them.</summary>
    public bool InScript { get; init; }

    /// <summary>Used = placed on the map or referenced by the script. Among look-alike rawcodes,
    /// the used one is the real character; unused ones are stale/duplicate leftovers.</summary>
    public bool IsUsed => PlacedCount > 0 || InScript;
    /// <summary>Corner badge: the placement count when placed, else a tick for script-referenced.</summary>
    public string UsageBadge => PlacedCount > 0 ? $"×{PlacedCount}" : (InScript ? "✓" : "");

    public string Display => $"{Name ?? "(unnamed)"} ({Rawcode})";
    public string Detail => $"{Kind.ToString().ToLowerInvariant()} · {Source}";
    /// <summary>Hover label: identity plus why this rawcode is (or is not) in use.</summary>
    public string Tooltip
    {
        get
        {
            var use = PlacedCount > 0 ? $"placed {PlacedCount}x on the map"
                : InScript ? "referenced by the map script"
                : "not placed and not referenced by the script (likely unused)";
            return $"{Display} · {Detail} · {use}";
        }
    }

    /// <summary>Whether this type has icon art at all. Most doodads (and hero/custom unit
    /// variants) have none — WC3 lists those by name, not icon — so those tiles show
    /// <see cref="TileText"/> instead of an empty box.</summary>
    public bool HasIconArt => !string.IsNullOrWhiteSpace(IconPath);
    /// <summary>Fallback tile caption when there's no icon: the name, or the rawcode.</summary>
    public string TileText => Name is { Length: > 0 } ? Name : Rawcode;
}

/// <summary>
/// Decodes palette icon art to Avalonia bitmaps off the UI thread, cached per path.
/// Resolution and decoding are the command layer's (<see cref="PaletteCommand.IconPng"/>:
/// map imports first, then base-game CASC, BLP/DDS/TGA sniffed) — this class only adds
/// caching, request coalescing, and thread marshalling. Both dictionaries are touched on
/// the UI thread only (requests originate from bindings; completions are posted back),
/// so no locking is needed. One instance per catalog load: replacing it wholesale when
/// the map changes means a stale decode can never leak into a newer palette.
/// </summary>
internal sealed class PaletteIconLoader
{
    private readonly Wc3.Model.MapDocument _doc;
    private readonly string? _gameDir;
    private readonly Dictionary<string, Bitmap?> _done = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<Bitmap?>>> _pending = new(StringComparer.OrdinalIgnoreCase);

    public PaletteIconLoader(Wc3.Model.MapDocument doc, string? gameDir)
    {
        _doc = doc;
        _gameDir = gameDir;
    }

    /// <summary>A completed decode for the path (the bitmap is null when it failed).</summary>
    public bool TryGetCached(string iconPath, out Bitmap? bitmap) =>
        _done.TryGetValue(iconPath, out bitmap);

    /// <summary>Requests an off-thread decode; <paramref name="onLoaded"/> runs later on
    /// the UI thread (null bitmap = unresolvable/undecodable). Concurrent requests for
    /// the same path share one decode.</summary>
    public void Load(string iconPath, Action<Bitmap?> onLoaded)
    {
        if (_done.TryGetValue(iconPath, out var hit)) { onLoaded(hit); return; }
        if (_pending.TryGetValue(iconPath, out var waiters)) { waiters.Add(onLoaded); return; }
        _pending[iconPath] = new List<Action<Bitmap?>> { onLoaded };
        Task.Run(() =>
        {
            Bitmap? bmp = null;
            try
            {
                if (PaletteCommand.IconPng(_doc, iconPath, _gameDir) is { } png)
                {
                    using var ms = new MemoryStream(png);
                    bmp = new Bitmap(ms);
                }
            }
            catch { /* no icon — the tile stays an empty box */ }
            Dispatcher.UIThread.Post(() =>
            {
                _done[iconPath] = bmp;
                if (_pending.Remove(iconPath, out var callbacks))
                    foreach (var cb in callbacks) cb(bmp);
            });
        });
    }
}

/// <summary>One palette group: a visible collapsible header (kind · source) plus its
/// icon tiles. Rendered in PaletteView.axaml as a full-width <c>SectionHeader</c> stacked
/// over a <c>WrapPanel</c> of tiles — the header is a normal vertical child, so it is
/// ALWAYS visible (unlike the old mixed-item WrapPanel hack where it vanished).</summary>
public sealed class PaletteGroup
{
    /// <summary>Stable identity for the group's collapsed state (kind·source).</summary>
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public bool Collapsed { get; init; }
    /// <summary>Tiles are hidden (not built away) while collapsed.</summary>
    public bool ShowTiles => !Collapsed;
    public IReadOnlyList<PaletteRow> Tiles { get; init; } = Array.Empty<PaletteRow>();
}

/// <summary>
/// Browse/search the things placeable on a map: the union of the base-game unit and
/// doodad catalogs with the map's own object-data, via <see cref="PaletteCommand"/>.
/// The catalog is built off the UI thread (the first game-data open per install hits
/// CASC/SLK and can take seconds) and presented as an icon grid grouped under visible
/// kind · source headers, map content ahead of the base bulk. The selected tile is the
/// placement source consulted by the Terrain tab's click-to-place.
/// </summary>
public partial class PaletteView : UserControl, IMapPanel
{
    private MapSession? _session;
    private List<PaletteRow> _all = new();
    private List<PaletteGroup> _groups = new();
    /// <summary>The armed placement tile (its IsSelected drives the highlight), or null.</summary>
    private PaletteRow? _selected;
    /// <summary>Stamp that invalidates an in-flight catalog load when the map changes.</summary>
    private int _generation;
    /// <summary>True from ShowMap until its load lands; filter events wait for real data.</summary>
    private bool _loading;
    /// <summary>Group keys (kind·source) the user has collapsed — their tiles are hidden until
    /// re-expanded. The base catalog is collapsed by default (it's the ~thousands-tile bulk).</summary>
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal)
        { "Unit·base", "Doodad·base", "Item·base", "Destructable·base" };

    public PaletteView() => InitializeComponent();

    public void ShowMap(MapSession session)
    {
        _session = session;
        int gen = ++_generation; // drop any load still in flight for the previous map
        Select(null);            // a new map invalidates the armed placement
        if (session.Current is not { } doc)
        {
            _loading = false;
            _all = new List<PaletteRow>();
            _groups = new List<PaletteGroup>();
            PaletteGroups.ItemsSource = null;
            StatusText.Text = "";
            ContentRoot.IsVisible = false;
            PlaceholderText.IsVisible = true;
            return;
        }

        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
        _loading = true;
        _all = new List<PaletteRow>();
        _groups = new List<PaletteGroup>();
        PaletteGroups.ItemsSource = null;
        StatusText.Text = "Loading palette…";

        // Build the catalog off the UI thread - the first game-data query per install
        // opens CASC, which can take seconds. The generation stamp drops results that
        // land after the map changed.
        string? gameDir = session.GameDir;
        var icons = new PaletteIconLoader(doc, gameDir);
        Task.Run(() =>
        {
            List<PaletteRow>? rows = null;
            string? error = null;
            try
            {
                var units = PaletteCommand.UnitPalette(doc, gameDir);
                var doodads = PaletteCommand.DoodadPalette(doc, gameDir);
                var items = PaletteCommand.ItemPalette(doc, gameDir);
                var destructables = PaletteCommand.DestructablePalette(doc, gameDir);
                // How many times each unit type is actually placed on the map: the signal for
                // "which of these look-alike rawcodes is the real one" - the placed one is in use.
                var placed = UnitInstanceCommand.List(doc)
                    .GroupBy(u => u.TypeRawcode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var scriptCodes = ScriptRawcodes(doc);
                rows = units.Entries.Select(e => Row(ObjectKind.Unit, e, icons,
                        placed.GetValueOrDefault(e.Rawcode), scriptCodes.Contains(e.Rawcode)))
                    .Concat(doodads.Entries.Select(e => Row(ObjectKind.Doodad, e, icons)))
                    .Concat(items.Entries.Select(e => Row(ObjectKind.Item, e, icons)))
                    .Concat(destructables.Entries.Select(e => Row(ObjectKind.Destructable, e, icons)))
                    .ToList();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (gen != _generation)
                    return; // superseded by a newer map
                _loading = false;
                if (rows is null)
                {
                    StatusText.Text = $"Failed to load palette: {error}";
                    return;
                }
                _all = rows;
                ApplyFilter();
            });
        });
    }

    private static PaletteRow Row(ObjectKind kind, PaletteEntry e, PaletteIconLoader icons,
        int placedCount = 0, bool inScript = false) =>
        new()
        {
            Kind = kind, Rawcode = e.Rawcode, Name = e.Name, Source = e.Source,
            IconPath = e.IconPath, IconLoader = icons, PlacedCount = placedCount, InScript = inScript,
        };

    /// <summary>The set of 4-character rawcodes the map script references, from its 'xxxx'
    /// FourCC literals. One scan yields an O(1) "is this type used by the script" lookup - the
    /// usage signal for maps that spawn units at runtime rather than pre-placing them.</summary>
    private static HashSet<string> ScriptRawcodes(Wc3.Model.MapDocument doc)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        if (doc.GetFile("war3map.j")?.CurrentBytes is not { Length: > 0 } bytes)
            return codes;
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        for (int i = 0; i + 5 < text.Length; i++)
            if (text[i] == '\'' && text[i + 5] == '\'')
            {
                var code = text.Substring(i + 1, 4);
                if (code.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')))
                    codes.Add(code);
            }
        return codes;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();
    private void OnKindChanged(object? sender, SelectionChangedEventArgs e) => ApplyFilter();

    /// <summary>Map content first - it's what a mapper places most - then the base bulk.</summary>
    private static int SourceRank(string source) => source switch
    {
        "map-custom" => 0,
        "map-modified" => 1,
        "base" => 2,
        _ => 3,
    };

    private static string KindPlural(ObjectKind kind) => kind switch
    {
        ObjectKind.Unit => "Units",
        ObjectKind.Doodad => "Doodads",
        ObjectKind.Item => "Items",
        ObjectKind.Destructable => "Destructables",
        _ => kind.ToString() + "s",
    };

    /// <summary>Display order of the kind groups (units first, base bulk last).</summary>
    private static int KindRank(ObjectKind kind) => kind switch
    {
        ObjectKind.Unit => 0,
        ObjectKind.Item => 1,
        ObjectKind.Doodad => 2,
        ObjectKind.Destructable => 3,
        _ => 4,
    };

    /// <summary>Re-apply the search text and kind filter to the full catalog, then regroup
    /// the survivors under kind · source headers (empty groups simply don't appear). A
    /// search expands everything (matches are never hidden inside a collapsed group); with
    /// no search, the user's collapse choices are honoured.</summary>
    private void ApplyFilter()
    {
        if (_loading || _session?.Current is null) return;

        var q = (SearchBox.Text ?? "").Trim();
        IEnumerable<PaletteRow> rows = _all;
        // 0 = All, 1 = Units, 2 = Doodads, 3 = Items, 4 = Destructables (see XAML order).
        rows = KindFilter.SelectedIndex switch
        {
            1 => rows.Where(r => r.Kind == ObjectKind.Unit),
            2 => rows.Where(r => r.Kind == ObjectKind.Doodad),
            3 => rows.Where(r => r.Kind == ObjectKind.Item),
            4 => rows.Where(r => r.Kind == ObjectKind.Destructable),
            _ => rows,
        };
        if (q.Length > 0)
            rows = rows.Where(r =>
                r.Rawcode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (r.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));

        bool searching = q.Length > 0;
        int shown = 0;
        var groups = new List<PaletteGroup>();
        foreach (var byKind in rows.GroupBy(r => r.Kind)
                     .OrderBy(g => KindRank(g.Key)))
        {
            foreach (var bySource in byKind.GroupBy(r => r.Source)
                         .OrderBy(g => SourceRank(g.Key)))
            {
                var members = bySource.ToList();
                var key = $"{byKind.Key}·{bySource.Key}";
                bool collapsed = !searching && _collapsed.Contains(key);
                groups.Add(new PaletteGroup
                {
                    Key = key,
                    Title = $"{KindPlural(byKind.Key)} · {bySource.Key} ({members.Count})",
                    Collapsed = collapsed,
                    Tiles = members,
                });
                if (!collapsed)
                    shown += members.Count;
            }
        }
        _groups = groups;
        PaletteGroups.ItemsSource = _groups;

        int Count(ObjectKind k) => _all.Count(r => r.Kind == k);
        StatusText.Text =
            $"{shown} shown · {Count(ObjectKind.Unit)} unit(s), {Count(ObjectKind.Doodad)} doodad(s), "
            + $"{Count(ObjectKind.Item)} item(s), {Count(ObjectKind.Destructable)} destructable(s)";
    }

    /// <summary>Toggle a group's collapsed state and rebuild. The SectionHeader already
    /// marked the pointer press handled, so the click never falls through to a tile.</summary>
    private void OnGroupHeaderToggled(object? sender, EventArgs e)
    {
        if ((sender as Control)?.DataContext is not PaletteGroup g)
            return;
        if (!_collapsed.Add(g.Key)) // Add returns false when already collapsed → expand it
            _collapsed.Remove(g.Key);
        ApplyFilter();
    }

    /// <summary>A tile click arms it as the placement (and highlights it).</summary>
    private void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not PaletteRow row)
            return;
        e.Handled = true;
        Select(row);
    }

    /// <summary>Sets (or clears, with null) the armed placement, updates the highlight and
    /// status line, and raises <see cref="PlacementChanged"/>. Idempotent.</summary>
    private void Select(PaletteRow? row)
    {
        if (ReferenceEquals(_selected, row))
            return;
        if (_selected is not null)
            _selected.IsSelected = false;
        _selected = row;
        if (row is not null)
        {
            row.IsSelected = true;
            StatusText.Text = $"Selected: {row.Display} — {row.Detail}  ·  click the terrain to place";
        }
        PlacementChanged?.Invoke(this, row);
    }

    /// <summary>The currently selected placeable, or null. Consulted by the Terrain
    /// tab's click-to-place via <see cref="PlacementChanged"/>.</summary>
    public PaletteRow? SelectedPlacement => _selected;

    /// <summary>Raised when the selected placeable changes (tile click or clear).</summary>
    public event EventHandler<PaletteRow?>? PlacementChanged;

    /// <summary>Clears the palette selection so no placeable is armed (pointer mode).
    /// Called when the Terrain tab cancels the brush (Esc / right-click) so the palette
    /// highlight and the terrain brush stay in sync.</summary>
    public void ClearSelection() => Select(null);
}
