using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Wc3.Commands;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>
/// Camera-catalog editor (war3map.w3c) over <see cref="CameraCommand"/>: an add form (name +
/// target x/y) plus one <see cref="CatalogCard"/> per camera with its key fields, an inline
/// field editor and a Remove button. Rebuilds from the command layer after every edit.
/// Composes <see cref="CatalogEditorView"/> for its chrome, exactly like the sounds panel.
/// </summary>
public partial class CamerasView : UserControl, IMapPanel
{
    private MapSession? _session;
    private TextBox? _addName;
    private TextBox? _addX;
    private TextBox? _addY;

    public event EventHandler? MapEdited;

    public CamerasView()
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
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        _addName = new TextBox { Watermark = "New camera name" };
        Grid.SetColumn(_addName, 0);
        grid.Children.Add(_addName);

        _addX = new TextBox { Watermark = "target X", Width = 84, Margin = new Avalonia.Thickness(6, 0, 4, 0) };
        Grid.SetColumn(_addX, 1);
        grid.Children.Add(_addX);

        _addY = new TextBox { Watermark = "target Y", Width = 84, Margin = new Avalonia.Thickness(0, 0, 6, 0) };
        Grid.SetColumn(_addY, 2);
        grid.Children.Add(_addY);

        var add = new Button { Content = "Add", Padding = new Avalonia.Thickness(10, 2), VerticalAlignment = VerticalAlignment.Center };
        add.Click += (_, _) => AddCamera();
        Grid.SetColumn(add, 3);
        grid.Children.Add(add);
        return grid;
    }

    private void Refresh()
    {
        if (_session?.Current is not { } doc)
        {
            Catalog.ShowHint("Cameras panel - no map open.");
            return;
        }

        IReadOnlyList<CameraFields> cameras;
        try { cameras = CameraCommand.List(doc); }
        catch (Exception ex) { Catalog.ShowHint($"Cameras cannot be read: {ex.Message}"); return; }

        if (cameras.Count == 0)
        {
            Catalog.SetCards(Array.Empty<Control>());
            Catalog.SetStatus("This map has no cameras yet. Add one above.");
            return;
        }

        var cards = cameras.Select(c => (Control)CatalogCard.Build(
            title: c.Name,
            fields: new (string, string)[]
            {
                ("target", $"({c.TargetX:0}, {c.TargetY:0})"),
                ("rotation", $"{c.Rotation:0}"),
                ("angle of attack", $"{c.AngleOfAttack:0}"),
                ("distance", $"{c.TargetDistance:0}"),
                ("field of view", $"{c.FieldOfView:0}"),
            },
            editableFields: CameraCommand.EditableFields,
            onSetField: (field, value) => Mutate(() => CameraCommand.Set(doc, c.Name, field, value)),
            onRemove: () => Mutate(() => CameraCommand.Remove(doc, c.Name))));
        Catalog.SetCards(cards);
        Catalog.SetStatus($"{cameras.Count} camera(s).");
    }

    private void AddCamera()
    {
        if (_session?.Current is not { } doc) return;
        var name = _addName?.Text?.Trim() ?? "";
        if (name.Length == 0) { Catalog.SetStatus("Enter a camera name to add."); return; }
        if (!TryParse(_addX?.Text, out var x) || !TryParse(_addY?.Text, out var y))
        {
            Catalog.SetStatus("Target X and Y must be numbers.");
            return;
        }
        Mutate(() => CameraCommand.Add(doc, name, x, y));
        if (_addName is not null) _addName.Text = "";
        if (_addX is not null) _addX.Text = "";
        if (_addY is not null) _addY.Text = "";
    }

    private static bool TryParse(string? s, out float value) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void Mutate(Func<CameraOpResult> op)
    {
        var r = op();
        Refresh();
        Catalog.SetStatus(r.Message);
        if (r.Ok)
            MapEdited?.Invoke(this, EventArgs.Empty);
    }
}
