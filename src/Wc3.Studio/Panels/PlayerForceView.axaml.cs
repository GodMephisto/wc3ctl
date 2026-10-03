using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wc3.Commands;
using Wc3.Studio.Controls;

namespace Wc3.Studio.Panels;

/// <summary>
/// Scenario → Players / Forces editor (war3map.w3i) over <see cref="PlayerForceCommand"/>:
/// one <see cref="Card"/> per force (team) showing its alliance/sharing flag checkboxes
/// (applied on toggle via SetForceFlags) and its player rows (a <see cref="PlayerChip"/>
/// swatch+name plus a team dropdown that moves the player via SetPlayerForce). Cards
/// rebuild from the command layer after every edit, so the panel always shows file
/// truth. All controls are built in code with no virtualization - the
/// editable-control-in-recycled-item pitfall cannot occur.
/// </summary>
public partial class PlayerForceView : UserControl, IMapPanel
{
    private static readonly IBrush DimBrush = StudioPalette.Muted;

    private MapSession? _session;
    /// <summary>True while (re)building the cards - checkbox/dropdown handlers no-op so
    /// programmatic initialization never writes to the map.</summary>
    private bool _building;

    /// <summary>Raised after an edit mutated the in-memory map; the workspace reacts by
    /// enabling Save.</summary>
    public event EventHandler? MapEdited;

    public PlayerForceView()
    {
        InitializeComponent();
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        StatusText.Text = "";
        Refresh();
    }

    /// <summary>Rebuilds every card from the command layer (file truth).</summary>
    private void Refresh()
    {
        _building = true;
        try
        {
            BuildCards();
        }
        finally
        {
            _building = false;
        }
    }

    private void BuildCards()
    {
        ForceList.Children.Clear();
        if (_session?.Current is not { } doc)
        {
            ShowHint("No map open.");
            return;
        }

        IReadOnlyList<PlayerInfo> players;
        IReadOnlyList<ForceInfo> forces;
        try
        {
            players = PlayerForceCommand.GetPlayers(doc);
            forces = PlayerForceCommand.GetForces(doc);
        }
        catch (Exception ex)
        {
            ShowHint($"Players/forces cannot be read: {ex.Message}");
            return;
        }
        if (players.Count == 0 && forces.Count == 0)
        {
            ShowHint("This map has no player/force data (war3map.w3i missing or unparseable).");
            return;
        }

        PlaceholderText.IsVisible = false;
        ContentRoot.IsVisible = true;

        var byId = players.ToDictionary(p => p.Id);
        var teamNames = forces.Select(OwnerTeamPicker.ForceLabel).ToList();
        var placed = new HashSet<int>();
        foreach (var force in forces)
            ForceList.Children.Add(BuildForceCard(force, byId, teamNames, placed));

        // Players whose bit is set in no force still exist in the map; list them so
        // their team dropdown can move them into one.
        var loose = players.Where(p => !placed.Contains(p.Id)).ToList();
        if (loose.Count > 0)
            ForceList.Children.Add(BuildLooseCard(loose, teamNames));
    }

    /// <summary>One force card: header, flag checkboxes, then a row per member.</summary>
    private Border BuildForceCard(ForceInfo force, IReadOnlyDictionary<int, PlayerInfo> byId,
                                  IReadOnlyList<string> teamNames, HashSet<int> placed)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock
        {
            Text = OwnerTeamPicker.ForceLabel(force),
            FontWeight = FontWeight.SemiBold,
        });

        // Alliance/sharing flags: any toggle re-writes all five, read live from the
        // checkboxes (SetForceFlags takes the full set).
        var flagsPanel = new WrapPanel();
        var boxes = new List<CheckBox>();
        CheckBox Flag(string label, bool value, string tip)
        {
            var box = new CheckBox
            {
                Content = label,
                IsChecked = value,
                MinHeight = 0,
                Margin = new Thickness(0, 0, 12, 0),
            };
            ToolTip.SetTip(box, tip);
            boxes.Add(box);
            flagsPanel.Children.Add(box);
            return box;
        }
        var allied = Flag("Allied", force.Allied,
            "Players in this team are allied.");
        var victory = Flag("Allied Victory", force.AlliedVictory,
            "The team wins and loses together.");
        var vision = Flag("Shared Vision", force.SharedVision,
            "Team members see what each other sees.");
        var unitCtl = Flag("Shared Unit Control", force.SharedUnitControl,
            "Team members can control each other's units.");
        var advCtl = Flag("Shared Adv Unit Control", force.SharedAdvUnitControl,
            "Full control of allied units, including training and upgrades.");
        foreach (var box in boxes)
        {
            box.IsCheckedChanged += (_, _) =>
            {
                if (_building)
                    return;
                ApplyFlags(force.Index, allied.IsChecked == true, victory.IsChecked == true,
                    vision.IsChecked == true, unitCtl.IsChecked == true,
                    advCtl.IsChecked == true);
            };
        }
        body.Children.Add(flagsPanel);

        if (force.PlayerIds.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "No players in this team.",
                Foreground = DimBrush,
                FontSize = 11,
            });
        }
        foreach (var id in force.PlayerIds)
        {
            placed.Add(id);
            body.Children.Add(BuildPlayerRow(id, byId.TryGetValue(id, out var p) ? p : null,
                force.Index, teamNames));
        }

        return new Card { Child = body };
    }

    /// <summary>Card for players that belong to no force (their team dropdown starts
    /// empty; picking one moves them into it).</summary>
    private Border BuildLooseCard(IReadOnlyList<PlayerInfo> loose, IReadOnlyList<string> teamNames)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { Text = "No Team", FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock
        {
            Text = "These players are in no force; pick a team to move them.",
            Foreground = DimBrush,
            FontSize = 11,
        });
        foreach (var p in loose)
            body.Children.Add(BuildPlayerRow(p.Id, p, currentTeam: -1, teamNames));
        return new Card { Child = body };
    }

    /// <summary>One player row: a PlayerChip (swatch + "Player N (Color) · map name",
    /// truncating) over a dim controller/race line, with a team dropdown on the right
    /// that moves the player. A Grid (star column for the chip, fixed-width combo) -
    /// NOT a DockPanel - so a long name truncates instead of running under the combo
    /// when the panel sits in the narrow side dock.</summary>
    private Grid BuildPlayerRow(int id, PlayerInfo? player, int currentTeam,
                                IReadOnlyList<string> teamNames)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        var label = PlayerColors.DisplayName(id);
        if (player is not null && !string.IsNullOrWhiteSpace(player.Name)
            && !label.Contains(player.Name, StringComparison.OrdinalIgnoreCase))
            label = $"{label} · {player.Name}";

        var left = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        left.Children.Add(new PlayerChip { PlayerId = id, Text = label });
        if (player is not null)
        {
            left.Children.Add(new TextBlock
            {
                Text = $"{player.Controller}, {player.Race}",
                Foreground = DimBrush,
                FontSize = 11,
                // Aligns under the name: past the chip's 12px swatch + 6px gap.
                Margin = new Thickness(18, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        Grid.SetColumn(left, 0);
        row.Children.Add(left);

        var combo = new ComboBox
        {
            ItemsSource = teamNames,
            SelectedIndex = currentTeam,
            Width = 120,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(combo, "Move this player to another team");
        combo.SelectionChanged += (_, _) =>
        {
            if (_building || combo.SelectedIndex < 0 || combo.SelectedIndex == currentTeam)
                return;
            MovePlayer(id, combo.SelectedIndex);
        };
        Grid.SetColumn(combo, 1);
        row.Children.Add(combo);
        return row;
    }

    private void ApplyFlags(int forceIndex, bool allied, bool alliedVictory,
                            bool sharedVision, bool sharedUnitControl, bool sharedAdvUnitControl)
    {
        if (_session?.Current is not { } doc)
            return;
        var result = PlayerForceCommand.SetForceFlags(doc, forceIndex, allied, alliedVictory,
            sharedVision, sharedUnitControl, sharedAdvUnitControl);
        StatusText.Text = result.Message;
        if (result.Ok)
            MapEdited?.Invoke(this, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(Refresh); // revert the checkboxes to file truth
    }

    private void MovePlayer(int playerId, int forceIndex)
    {
        if (_session?.Current is not { } doc)
            return;
        var result = PlayerForceCommand.SetPlayerForce(doc, playerId, forceIndex);
        StatusText.Text = result.Message;
        if (result.Ok)
            MapEdited?.Invoke(this, EventArgs.Empty);
        // Rebuild either way: the row moves to its new card (or reverts on failure).
        // Deferred - never tear down the combo while its own event is on the stack.
        Dispatcher.UIThread.Post(Refresh);
    }

    private void ShowHint(string text)
    {
        PlaceholderText.Text = text;
        PlaceholderText.IsVisible = true;
        ContentRoot.IsVisible = false;
    }
}
