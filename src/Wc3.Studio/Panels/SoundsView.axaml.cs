using Avalonia.Controls;
using Avalonia.Layout;
using Wc3.Commands;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>
/// Sound-catalog editor (war3map.w3s) over <see cref="SoundCommand"/>: an add form (name +
/// file) plus one <see cref="CatalogCard"/> per sound with its fields, an inline field editor
/// and a Remove button. Rebuilds from the command layer after every edit, so the list always
/// shows file truth. Composes <see cref="CatalogEditorView"/> for all its chrome.
/// </summary>
public partial class SoundsView : UserControl, IMapPanel
{
    private MapSession? _session;
    private TextBox? _addName;
    private TextBox? _addFile;

    /// <summary>Raised after an edit mutated the in-memory map; the workspace enables Save.</summary>
    public event EventHandler? MapEdited;

    public SoundsView()
    {
        InitializeComponent();
        Catalog.AddContent = BuildAddForm();
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        Refresh();
    }

    /// <summary>Name + file inputs and an Add button. Reads the live session at click time.</summary>
    private Control BuildAddForm()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto") };
        _addName = new TextBox { Watermark = "New sound name" };
        Grid.SetColumn(_addName, 0);
        grid.Children.Add(_addName);

        _addFile = new TextBox { Watermark = "Sound file path (optional)", Margin = new Avalonia.Thickness(6, 0, 6, 0) };
        Grid.SetColumn(_addFile, 1);
        grid.Children.Add(_addFile);

        var add = new Button { Content = "Add", Padding = new Avalonia.Thickness(10, 2), VerticalAlignment = VerticalAlignment.Center };
        add.Click += (_, _) => AddSound();
        Grid.SetColumn(add, 2);
        grid.Children.Add(add);
        return grid;
    }

    private void Refresh()
    {
        if (_session?.Current is not { } doc)
        {
            Catalog.ShowHint("Sounds panel - no map open.");
            return;
        }

        IReadOnlyList<SoundFields> sounds;
        try { sounds = SoundCommand.List(doc); }
        catch (Exception ex) { Catalog.ShowHint($"Sounds cannot be read: {ex.Message}"); return; }

        if (sounds.Count == 0)
        {
            Catalog.SetCards(Array.Empty<Control>());
            Catalog.SetStatus("This map has no sound definitions yet. Add one above.");
            return;
        }

        var cards = sounds.Select(s => (Control)CatalogCard.Build(
            title: s.Name,
            fields: new (string, string)[]
            {
                ("file", string.IsNullOrEmpty(s.FilePath) ? "(none)" : s.FilePath),
                ("channel", s.Channel),
                ("volume", s.Volume.ToString()),
                ("priority", s.Priority.ToString()),
                ("distance", $"{s.MinDistance:0}..{s.MaxDistance:0}"),
            },
            editableFields: SoundCommand.EditableFields,
            onSetField: (field, value) => Mutate(() => SoundCommand.Set(doc, s.Name, field, value)),
            onRemove: () => Mutate(() => SoundCommand.Remove(doc, s.Name))));
        Catalog.SetCards(cards);
        Catalog.SetStatus($"{sounds.Count} sound(s).");
    }

    private void AddSound()
    {
        if (_session?.Current is not { } doc) return;
        var name = _addName?.Text?.Trim() ?? "";
        if (name.Length == 0) { Catalog.SetStatus("Enter a sound name to add."); return; }
        Mutate(() => SoundCommand.Add(doc, name, string.IsNullOrWhiteSpace(_addFile?.Text) ? null : _addFile!.Text));
        if (_addName is not null) _addName.Text = "";
        if (_addFile is not null) _addFile.Text = "";
    }

    /// <summary>Runs a mutating command, reports it, refreshes the list, and flags the map
    /// dirty on success. Shared by add/set/remove.</summary>
    private void Mutate(Func<SoundOpResult> op)
    {
        var r = op();
        Refresh();
        Catalog.SetStatus(r.Message);
        if (r.Ok)
            MapEdited?.Invoke(this, EventArgs.Empty);
    }
}
