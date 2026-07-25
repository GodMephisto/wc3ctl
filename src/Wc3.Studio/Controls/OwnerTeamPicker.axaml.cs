using Avalonia.Controls;
using Avalonia.Media;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Studio.Controls;

/// <summary>Owner-dropdown row: a disabled team header, or a selectable player
/// (color swatch + "Player N (Color) · map name"). Public for reflection bindings
/// in the item template (same idiom as ObjectEditorView's rows).</summary>
public sealed record OwnerChoice(bool IsHeader, int PlayerId, string Label, IBrush? Swatch)
{
    public FontWeight Weight => IsHeader ? FontWeight.SemiBold : FontWeight.Normal;
}

/// <summary>
/// The grouped unit-owner dropdown: the map's players under their force (team)
/// headers, then players in no force, then the four neutral slots - each player row
/// with the slot's color swatch. Extracted from UnitPropertiesView, which used to
/// carry the same builder + item template twice (single and multi editors).
/// <see cref="Load"/> (re)builds the choices from the command layer;
/// <see cref="SelectedOwnerId"/> is the picked player id, or null while nothing is
/// selected. Headers are display-only: the ComboBoxItem style disables them so they
/// can never become the selection. Stretches to fill its container horizontally.
/// </summary>
public partial class OwnerTeamPicker : UserControl
{
    /// <summary>True while Load repopulates the combo - the selection handler no-ops
    /// so programmatic initialization never raises <see cref="SelectionChanged"/>.</summary>
    private bool _loading;

    public OwnerTeamPicker()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user picks a different owner (never during Load).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The picked player id, or null while nothing (valid) is selected.</summary>
    public int? SelectedOwnerId =>
        OwnerCombo.SelectedItem is OwnerChoice { IsHeader: false } choice
            ? choice.PlayerId
            : null;

    /// <summary>Combo hint shown while nothing is selected, e.g. "(pick an owner)".</summary>
    public string PlaceholderText
    {
        get => OwnerCombo.PlaceholderText ?? "";
        set => OwnerCombo.PlaceholderText = value;
    }

    /// <summary>(Re)builds the grouped choices from the map's players and forces.
    /// <paramref name="currentOwner"/> is preselected when known (it gets an "Other"
    /// row if it matches no slot); pass null to leave the picker blank - e.g. a mixed
    /// multi-selection, so it is never misreported as sharing one owner.</summary>
    public void Load(MapDocument doc, int? currentOwner)
    {
        _loading = true;
        try
        {
            var items = BuildOwnerItems(doc, currentOwner);
            OwnerCombo.ItemsSource = items;
            OwnerCombo.SelectedItem = currentOwner is int owner
                ? items.FirstOrDefault(i => !i.IsHeader && i.PlayerId == owner)
                : null;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnComboSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Builds the grouped owner-dropdown rows: the map's players grouped
    /// under their force (team) headers, then players in no force, then the four
    /// neutral slots. <paramref name="ownerId"/> is the current owner when known (it
    /// gets an "Other" row if it matches no slot, so a weird owner id still displays
    /// instead of blanking the picker); null for a mixed multi-selection.</summary>
    private static List<OwnerChoice> BuildOwnerItems(MapDocument doc, int? ownerId)
    {
        var players = PlayerForceCommand.GetPlayers(doc);
        var forces = PlayerForceCommand.GetForces(doc);
        var byId = players.ToDictionary(p => p.Id);
        var placed = new HashSet<int>();
        var items = new List<OwnerChoice>();

        void AddHeader(string label) => items.Add(new OwnerChoice(true, -1, label, null));
        void AddPlayer(int id)
        {
            var label = PlayerColors.DisplayName(id);
            if (byId.TryGetValue(id, out var p) && !string.IsNullOrWhiteSpace(p.Name)
                && !label.Contains(p.Name, StringComparison.OrdinalIgnoreCase))
                label = $"{label} · {p.Name}";
            var (r, g, b) = PlayerColors.Color(id);
            items.Add(new OwnerChoice(false, id, label,
                new SolidColorBrush(Color.FromRgb(r, g, b))));
            placed.Add(id);
        }

        foreach (var force in forces)
        {
            if (force.PlayerIds.Count == 0)
                continue;
            AddHeader(ForceLabel(force));
            foreach (var id in force.PlayerIds)
                AddPlayer(id);
        }

        var loose = players.Where(p => !placed.Contains(p.Id)).Select(p => p.Id).ToList();
        if (loose.Count > 0)
        {
            AddHeader("No Team");
            foreach (var id in loose)
                AddPlayer(id);
        }

        AddHeader("Neutral");
        for (int id = PlayerColors.NeutralHostileId; id <= PlayerColors.NeutralPassiveId; id++)
            AddPlayer(id);

        if (ownerId is int current && !placed.Contains(current))
        {
            AddHeader("Other");
            AddPlayer(current);
        }

        return items;
    }

    /// <summary>"Team N: name" (or a bare "Team N" for a nameless force). Shared by
    /// the picker's group headers and the Players tab so both label forces identically.</summary>
    internal static string ForceLabel(ForceInfo force) =>
        string.IsNullOrWhiteSpace(force.Name)
            ? $"Team {force.Index + 1}"
            : $"Team {force.Index + 1}: {force.Name}";
}
