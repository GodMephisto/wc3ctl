using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Wc3.Commands;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>
/// Sound editor (war3map.w3s) over <see cref="SoundCommand"/>, in two halves: the sound
/// DEFINITIONS at the top (named, playable sounds - add / edit-a-field / remove via
/// <see cref="CatalogEditorView"/>), and the map's IMPORTED AUDIO files at the bottom (the
/// archive's raw mp3/wav assets), each with an "Add as sound" button that creates a definition
/// referencing it. Bridges the World Editor's Import Manager and Sound Editor so a map full of
/// audio but no definitions is not a blank panel.
/// </summary>
public partial class SoundsView : UserControl, IMapPanel
{
    private const int MaxAudioRows = 500;

    private MapSession? _session;
    private TextBox? _addName;
    private TextBox? _addFile;
    private IReadOnlyList<string> _allAudio = Array.Empty<string>();

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
        _allAudio = _session?.Current is { } doc ? SoundCommand.ImportedAudioFiles(doc) : Array.Empty<string>();
        ApplyAudioFilter();
        Refresh();
    }

    // ---- sound definitions (top) -----------------------------------------

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
            Catalog.SetStatus("No sound definitions yet. Add one, or turn imported audio below into a sound.");
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
        Catalog.SetStatus($"{sounds.Count} sound definition(s).");
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

    // ---- imported audio browser (bottom) ---------------------------------

    private void OnAudioSearchChanged(object? sender, TextChangedEventArgs e) => ApplyAudioFilter();

    /// <summary>Filters the imported-audio list by the search box and caps the rows shown
    /// (the archive can hold thousands), reporting how many matched.</summary>
    private void ApplyAudioFilter()
    {
        var q = (AudioSearch?.Text ?? "").Trim();
        IEnumerable<string> matches = _allAudio;
        if (q.Length > 0)
            matches = matches.Where(p => p.Contains(q, StringComparison.OrdinalIgnoreCase));

        var shown = matches.Take(MaxAudioRows).ToList();
        AudioList.ItemsSource = shown;

        int total = _allAudio.Count;
        int matched = q.Length > 0 ? _allAudio.Count(p => p.Contains(q, StringComparison.OrdinalIgnoreCase)) : total;
        AudioTitle.Text = $"Imported audio ({total})";
        AudioStatus.Text = total == 0
            ? "This map has no imported audio files."
            : shown.Count < matched
                ? $"Showing {shown.Count} of {matched} match(es) - refine the search to narrow it."
                : $"{matched} file(s){(q.Length > 0 ? " match" : "")}.";
    }

    /// <summary>Turns an imported audio file into a sound definition (a unique gg_snd_ name
    /// pointing at the file), then refreshes the definitions list.</summary>
    private void OnAddFromFile(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc) return;
        if ((sender as Control)?.DataContext is not string path) return;

        var name = UniqueSoundName(doc, SoundCommand.SoundNameForFile(path));
        var r = SoundCommand.Add(doc, name, path);
        Refresh();
        Catalog.SetStatus(r.Ok ? $"Created sound '{name}' -> {path}" : r.Message);
        if (r.Ok)
            MapEdited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ensures a sound name is not already taken, appending _2, _3, ... if needed.</summary>
    private static string UniqueSoundName(Wc3.Model.MapDocument doc, string baseName)
    {
        var existing = SoundCommand.List(doc).Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(baseName)) return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }
}
