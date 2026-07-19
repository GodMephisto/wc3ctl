using Avalonia.Controls;
using Wc3.Commands;

namespace Wc3.Studio.Panels;

/// <summary>Row model for the palette list (reflection-bound from XAML).</summary>
public sealed class PaletteRow
{
    public ObjectKind Kind { get; init; }
    public string Rawcode { get; init; } = "";
    public string? Name { get; init; }
    public string Source { get; init; } = "";

    public string Display => $"{Name ?? "(unnamed)"} ({Rawcode})";
    public string Detail => $"{Kind.ToString().ToLowerInvariant()} · {Source}";
}

/// <summary>
/// Browse/search the things placeable on a map: the union of the base-game unit and
/// doodad catalogs with the map's own object-data, via <see cref="PaletteCommand"/>.
/// The selected row is the placement source consulted by the Terrain tab's click-to-place.
/// </summary>
public partial class PaletteView : UserControl, IMapPanel
{
    private MapSession? _session;
    private List<PaletteRow> _all = new();

    public PaletteView() => InitializeComponent();

    public void ShowMap(MapSession session)
    {
        _session = session;
        if (session.Current is not { } doc)
        {
            _all = new List<PaletteRow>();
            PaletteList.ItemsSource = null;
            StatusText.Text = "";
            ContentRoot.IsVisible = false;
            PlaceholderText.IsVisible = true;
            return;
        }

        // Public overloads open GameData internally (map-only if no install is found).
        var units = PaletteCommand.UnitPalette(doc, session.GameDir);
        var doodads = PaletteCommand.DoodadPalette(doc, session.GameDir);
        _all = units.Entries.Select(e => Row(ObjectKind.Unit, e))
            .Concat(doodads.Entries.Select(e => Row(ObjectKind.Doodad, e)))
            .ToList();

        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
        ApplyFilter();
    }

    private static PaletteRow Row(ObjectKind kind, PaletteEntry e) =>
        new() { Kind = kind, Rawcode = e.Rawcode, Name = e.Name, Source = e.Source };

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();
    private void OnKindChanged(object? sender, SelectionChangedEventArgs e) => ApplyFilter();

    /// <summary>Re-apply the search text and kind filter to the full catalog.</summary>
    private void ApplyFilter()
    {
        if (_session?.Current is null) return;

        var q = (SearchBox.Text ?? "").Trim();
        IEnumerable<PaletteRow> rows = _all;
        // 0 = All, 1 = Units, 2 = Doodads (see XAML ComboBox order).
        rows = KindFilter.SelectedIndex switch
        {
            1 => rows.Where(r => r.Kind == ObjectKind.Unit),
            2 => rows.Where(r => r.Kind == ObjectKind.Doodad),
            _ => rows,
        };
        if (q.Length > 0)
            rows = rows.Where(r =>
                r.Rawcode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (r.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));

        var list = rows.ToList();
        PaletteList.ItemsSource = list;

        var units = _all.Count(r => r.Kind == ObjectKind.Unit);
        var doodads = _all.Count(r => r.Kind == ObjectKind.Doodad);
        StatusText.Text = $"{list.Count} shown · {units} unit(s), {doodads} doodad(s)";
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PaletteList.SelectedItem is PaletteRow row)
            StatusText.Text = $"Selected: {row.Display} — {row.Detail}";
    }
}
