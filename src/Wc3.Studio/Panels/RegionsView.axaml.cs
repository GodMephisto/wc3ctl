using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Wc3.Commands;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>
/// Region editor (war3map.w3r) over <see cref="PlacementCommand"/>: an add form (name + the
/// four bounds) plus one <see cref="CatalogCard"/> per region showing its extent and a Remove
/// button. Rebuilds from the command layer after every edit. Bounds editing is add/remove for
/// now (drag-on-terrain is a later pass). Composes <see cref="CatalogEditorView"/> for chrome.
/// </summary>
public partial class RegionsView : UserControl, IMapPanel
{
    private MapSession? _session;
    private TextBox? _addName, _addLeft, _addBottom, _addRight, _addTop;

    public event EventHandler? MapEdited;

    public RegionsView()
    {
        InitializeComponent();
        Catalog.AddContent = BuildAddForm();
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        Refresh();
    }

    private Control BuildAddForm()
    {
        var stack = new StackPanel { Spacing = 4 };
        _addName = new TextBox { Watermark = "New region name" };
        stack.Children.Add(_addName);

        var bounds = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,Auto") };
        _addLeft = Coord("left (min X)", 0, bounds);
        _addBottom = Coord("bottom (min Y)", 1, bounds);
        _addRight = Coord("right (max X)", 2, bounds);
        _addTop = Coord("top (max Y)", 3, bounds);
        var add = new Button { Content = "Add", Padding = new Avalonia.Thickness(10, 2), VerticalAlignment = VerticalAlignment.Center };
        add.Click += (_, _) => AddRegion();
        Grid.SetColumn(add, 4);
        bounds.Children.Add(add);
        stack.Children.Add(bounds);
        return stack;
    }

    private static TextBox Coord(string watermark, int col, Grid host)
    {
        var box = new TextBox { Watermark = watermark, Margin = new Avalonia.Thickness(0, 0, 4, 0) };
        Grid.SetColumn(box, col);
        host.Children.Add(box);
        return box;
    }

    private void Refresh()
    {
        if (_session?.Current is not { } doc)
        {
            Catalog.ShowHint("Regions panel - no map open.");
            return;
        }

        IReadOnlyList<PlacementCommand.RegionInfo> regions;
        try { regions = PlacementCommand.ListRegions(doc); }
        catch (Exception ex) { Catalog.ShowHint($"Regions cannot be read: {ex.Message}"); return; }

        if (regions.Count == 0)
        {
            // See CamerasView. An absent war3map.w3r and an unreadable one both arrive here as
            // an empty list, so the panel asks which it is instead of announcing one.
            Catalog.SetEmpty("This map defines no regions.",
                MapFilePresence.Describe(doc, "war3map.w3r", "regions",
                    "Use the form above to add the first one."));
            return;
        }

        var cards = regions.Select(r => (Control)CatalogCard.Build(
            title: r.Name,
            fields: new (string, string)[]
            {
                ("bounds", $"[{r.Left:0}, {r.Bottom:0}] .. [{r.Right:0}, {r.Top:0}]"),
                ("size", $"{r.Right - r.Left:0} x {r.Top - r.Bottom:0}"),
            },
            onRemove: () => Mutate(() => PlacementCommand.RemoveRegion(doc, r.Name))));
        Catalog.SetCards(cards);
        Catalog.SetStatus($"{regions.Count} region(s).");
    }

    private void AddRegion()
    {
        if (_session?.Current is not { } doc) return;
        var name = _addName?.Text?.Trim() ?? "";
        if (name.Length == 0) { Catalog.SetStatus("Enter a region name to add."); return; }
        if (!TryParse(_addLeft?.Text, out var l) || !TryParse(_addBottom?.Text, out var b)
            || !TryParse(_addRight?.Text, out var rt) || !TryParse(_addTop?.Text, out var t))
        {
            Catalog.SetStatus("All four bounds must be numbers.");
            return;
        }
        Mutate(() => PlacementCommand.PlaceRegion(doc, name, l, b, rt, t));
        foreach (var box in new[] { _addName, _addLeft, _addBottom, _addRight, _addTop })
            if (box is not null) box.Text = "";
    }

    private static bool TryParse(string? s, out float value) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void Mutate(Func<PlacementCommand.PlaceRegionResult> op)
    {
        var r = op();
        Refresh();
        Catalog.SetStatus(r.Message);
        if (r.Ok)
            MapEdited?.Invoke(this, EventArgs.Empty);
    }
}
