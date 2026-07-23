using Avalonia.Controls;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Studio.Panels;

/// <summary>Thin shell over MapInfoCommand: read-only field rows plus one dedicated
/// editor + Apply for the string fields. All logic lives in Wc3.Commands.</summary>
public partial class MapInfoView : UserControl, IMapPanel
{
    /// <summary>ListBox row. <paramref name="Value"/> is the raw stored value the editor
    /// edits (TRIGSTR_ intact); <paramref name="Display"/> is the wts-resolved text shown.</summary>
    private sealed record FieldRow(string Name, string Value, string Display, bool Editable)
    {
        public override string ToString() =>
            $"{Name}: {Display}{(Editable ? string.Empty : "   (read-only)")}";
    }

    private MapSession? _session;

    public MapInfoView()
    {
        InitializeComponent();
        FieldList.SelectionChanged += (_, _) => OnFieldSelected();
        ApplyButton.Click += (_, _) => OnApply();
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        Refresh(selectField: null);
    }

    private void Refresh(string? selectField)
    {
        FieldList.ItemsSource = null;
        EditorBox.Text = string.Empty;
        ApplyButton.IsEnabled = false;

        var doc = _session?.Current;
        if (doc is null)
        {
            HeaderText.Text = "No map open";
            return;
        }

        MapInfoFields fields;
        try
        {
            fields = MapInfoCommand.Read(doc);
        }
        catch (InvalidOperationException ex)
        {
            HeaderText.Text = ex.Message;
            return;
        }

        HeaderText.Text = "Map info (war3map.w3i)";
        // Resolve TRIGSTR_ for the displayed text; the editor still edits the raw value.
        var strings = MapStrings.From(doc);
        string Disp(string v) => strings.Resolve(v);
        var width = fields.PlayableWidth.ToString();
        var height = fields.PlayableHeight.ToString();
        var players = fields.Players.ToString();
        var rows = new List<FieldRow>
        {
            new("MapName", fields.MapName, Disp(fields.MapName), true),
            new("Author", fields.Author, Disp(fields.Author), true),
            new("Description", fields.Description, Disp(fields.Description), true),
            new("RecommendedPlayers", fields.RecommendedPlayers, Disp(fields.RecommendedPlayers), true),
            new("PlayableWidth", width, width, false),
            new("PlayableHeight", height, height, false),
            new("Players", players, players, false),
        };
        FieldList.ItemsSource = rows;
        if (selectField is not null)
            FieldList.SelectedItem = rows.FirstOrDefault(r => r.Name == selectField);
    }

    private void OnFieldSelected()
    {
        if (FieldList.SelectedItem is not FieldRow row)
        {
            ApplyButton.IsEnabled = false;
            return;
        }
        EditorBox.Text = row.Value;
        ApplyButton.IsEnabled = row.Editable;
        StatusText.Text = row.Editable ? string.Empty : $"{row.Name} is read-only.";
    }

    private void OnApply()
    {
        var doc = _session?.Current;
        if (doc is null || FieldList.SelectedItem is not FieldRow row || !row.Editable)
            return;

        try
        {
            MapInfoCommand.Set(doc, row.Name, EditorBox.Text ?? string.Empty);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        Refresh(selectField: row.Name);
        StatusText.Text = $"{row.Name} updated (in memory — save the map to persist).";
    }
}
