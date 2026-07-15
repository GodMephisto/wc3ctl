using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Wc3.Commands;

namespace Wc3.Studio.Panels;

/// <summary>
/// World-Editor-style object editor: the map's units on the left, the selected
/// unit's merged fields (base game data ⊕ map deltas) on the right. The Value
/// column edits inline via per-row TextBoxes on a ListBox — the DataGrid control
/// lives in the separate Avalonia.Controls.DataGrid package (plus an App.axaml
/// theme include), neither of which this project references.
/// </summary>
public partial class ObjectEditorView : UserControl, IMapPanel
{
    private MapSession? _session;

    public ObjectEditorView()
    {
        InitializeComponent();
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        UnitList.ItemsSource = null;
        FieldList.ItemsSource = null;
        SelectedHeader.Text = "";
        StatusText.Text = "";

        if (session.Current is not { } doc)
        {
            ShowPlaceholder("Object editor panel — no map open");
            return;
        }

        List<UnitRow> units;
        try
        {
            units = ObjectListCommand.Execute(doc, session.GameDir).Items
                .Select(i => new UnitRow(i.Rawcode, i.Name ?? i.Rawcode))
                .ToList();
        }
        catch (Exception ex)
        {
            ShowPlaceholder($"Object editor panel — failed to list units: {ex.Message}");
            return;
        }

        if (units.Count == 0)
        {
            ShowPlaceholder("Object editor panel — map has no unit object data (war3map.w3u)");
            return;
        }

        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;
        UnitList.ItemsSource = units;
        UnitList.SelectedIndex = 0;
    }

    private void ShowPlaceholder(string message)
    {
        PlaceholderText.Text = message;
        PlaceholderText.IsVisible = true;
        ContentRoot.IsVisible = false;
    }

    private void OnUnitSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        FieldList.ItemsSource = null;
        SelectedHeader.Text = "";
        if (_session?.Current is not { } doc || UnitList.SelectedItem is not UnitRow unit)
            return;

        try
        {
            var result = ObjectGetCommand.Execute(doc, unit.Rawcode, _session.GameDir);
            var baseInfo = result.BaseRawcode is null ? "no base" : $"base {result.BaseRawcode}";
            SelectedHeader.Text =
                $"{result.Name ?? unit.Rawcode} ({unit.Rawcode}) — {baseInfo} — {result.Fields.Count} field(s)";
            FieldList.ItemsSource = result.Fields.Select(f => new FieldRow(f)).ToList();
            StatusText.Text = result.Diagnostics.Count > 0 ? string.Join("; ", result.Diagnostics) : "";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load {unit.Rawcode}: {ex.Message}";
        }
    }

    private void OnSaveEditsClick(object? sender, RoutedEventArgs e)
    {
        if (_session?.Current is not { } doc)
        {
            StatusText.Text = "No map open.";
            return;
        }
        if (_session.MapPath is not { } mapPath)
        {
            StatusText.Text = "The map has no file path to save next to.";
            return;
        }
        if (UnitList.SelectedItem is not UnitRow unit
            || FieldList.ItemsSource is not IReadOnlyList<FieldRow> rows)
        {
            StatusText.Text = "Select a unit first.";
            return;
        }

        var edited = rows.Where(r => r.IsEdited).ToList();
        if (edited.Count == 0)
        {
            StatusText.Text = "No changes to save.";
            return;
        }

        int applied = 0;
        var problems = new List<string>();
        foreach (var row in edited)
        {
            try
            {
                var result = ObjectSetCommand.Execute(doc, unit.Rawcode, row.Code, row.Value);
                if (result.Ok)
                {
                    applied++;
                    row.MarkSaved();
                    if (result.Warning is not null)
                        problems.Add($"{row.Code}: warning — {result.Warning}");
                }
                else
                {
                    problems.Add($"{row.Code}: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                problems.Add($"{row.Code}: {ex.Message}");
            }
        }

        if (applied == 0)
        {
            StatusText.Text = $"No fields applied — {string.Join("; ", problems)}";
            return;
        }

        // Never overwrite the original map: save next to it as <name>.edited<ext>.
        var editedPath = EditedPath(mapPath);
        try
        {
            doc.Save(editedPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Applied {applied} field(s) but saving failed: {ex.Message}";
            return;
        }

        var summary = $"Saved {editedPath} — {applied} field(s) applied on {unit.Rawcode}";
        if (problems.Count > 0)
            summary += $" — {string.Join("; ", problems)}";
        StatusText.Text = summary;
    }

    private static string EditedPath(string mapPath)
    {
        var dir = Path.GetDirectoryName(mapPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(mapPath);
        var ext = Path.GetExtension(mapPath);
        return Path.Combine(dir, $"{stem}.edited{ext}");
    }

    /// <summary>Unit list row: rawcode plus display name (name delta/base name, else rawcode).</summary>
    public sealed record UnitRow(string Rawcode, string Display);

    /// <summary>
    /// Field grid row. Value binds TwoWay to the inline TextBox; IsEdited compares
    /// against the value loaded from the merge (or last saved). Map-sourced fields
    /// render gold + semibold, like the World Editor's modified-field highlight.
    /// </summary>
    public sealed class FieldRow : INotifyPropertyChanged
    {
        private static readonly IBrush BaseBrush = new SolidColorBrush(Color.Parse("#C8CDD3"));
        private static readonly IBrush MapBrush = new SolidColorBrush(Color.Parse("#E8C56A"));

        private string _value;
        private string _originalValue;
        private string _source;

        public FieldRow(MergedField field)
        {
            Code = field.Code;
            Name = field.Name;
            _value = field.Value;
            _originalValue = field.Value;
            _source = field.Source;
        }

        public string Code { get; }
        public string Name { get; }

        public string Value
        {
            get => _value;
            set
            {
                if (_value == value)
                    return;
                _value = value;
                OnPropertyChanged(nameof(Value));
                OnPropertyChanged(nameof(SourceDisplay));
            }
        }

        public bool IsEdited => _value != _originalValue;

        /// <summary>"base"/"map", starred while the row holds an unsaved edit.</summary>
        public string SourceDisplay => IsEdited ? $"{_source} *" : _source;

        public IBrush RowBrush => _source == "map" ? MapBrush : BaseBrush;
        public FontWeight RowWeight => _source == "map" ? FontWeight.SemiBold : FontWeight.Normal;

        /// <summary>A saved edit becomes the new baseline and is now a map delta.</summary>
        public void MarkSaved()
        {
            _originalValue = _value;
            _source = "map";
            OnPropertyChanged(nameof(SourceDisplay));
            OnPropertyChanged(nameof(RowBrush));
            OnPropertyChanged(nameof(RowWeight));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
