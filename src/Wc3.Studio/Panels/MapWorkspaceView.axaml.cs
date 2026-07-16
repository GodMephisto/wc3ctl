using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Studio.Panels;

/// <summary>
/// A self-contained workspace for ONE map: a role header ("Source"/"Target") with
/// its own open button and status line, its own <see cref="MapSession"/>, and its
/// own panel-tab instances with lazy per-tab loading. Two instances live side by
/// side in MainWindow and must never share mutable map state; a later wave wires
/// porting across them through the public <see cref="Session"/> surface.
/// </summary>
public partial class MapWorkspaceView : UserControl
{
    private readonly HashSet<IMapPanel> _loadedPanels = new();
    private string _role = "Map";

    public MapWorkspaceView()
    {
        InitializeComponent();
        DependenciesPanel.SelectionChanged += OnPortSelectionChanged;
    }

    /// <summary>
    /// Header label, "Source" or "Target" (set from MainWindow.axaml). The role
    /// also picks the role-specific header affordances: only the Source side
    /// shows the (still disabled) port seam; only the Target side offers a
    /// blank map to port into.
    /// </summary>
    public string Role
    {
        get => _role;
        set
        {
            _role = value;
            RoleText.Text = value;
            PortButtonHost.IsVisible = value == "Source";
            NewBlankMapButton.IsVisible = value == "Target";
        }
    }

    /// <summary>This workspace's map state, passed to its panels — never shared.</summary>
    public MapSession Session { get; } = new();

    public bool HasMap => Session.Current is not null;

    /// <summary>
    /// Port seam: the unit picked in the Dependencies tab, whose closure the
    /// port wave will copy into the Target workspace. Null until one is chosen.
    /// </summary>
    public string? SelectedUnitForPort => DependenciesPanel.SelectedUnitRawcode;

    /// <summary>Raised after a map is successfully opened into this workspace.</summary>
    public event EventHandler? MapChanged;

    /// <summary>
    /// Raised (Source only) when the user clicks "Port → Target"; the argument is the
    /// selected unit's rawcode. MainWindow handles it because only it holds both the
    /// Source and Target sessions.
    /// </summary>
    public event EventHandler<string>? PortRequested;

    /// <summary>Sets this workspace's status line (used to report port progress/results).</summary>
    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>
    /// The port button tracks the Dependencies tab's selected unit: enabled with a
    /// live label when a unit is chosen, disabled otherwise.
    /// </summary>
    private void OnPortSelectionChanged(object? sender, EventArgs e)
    {
        var display = DependenciesPanel.SelectedUnitDisplay;
        PortButton.IsEnabled = display is not null;
        PortButton.Content = display is null
            ? "Port selected → Target ▶"
            : $"Port {display} → Target ▶";
        var tip = display is null
            ? "Pick a unit in the Dependencies tab, then port it into the Target map."
            : $"Port {display} and everything it uses into the Target map.";
        ToolTip.SetTip(PortButtonHost, tip);
        ToolTip.SetTip(PortButton, tip);
    }

    private void OnPortClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedUnitForPort is { } rawcode)
            PortRequested?.Invoke(this, rawcode);
    }

    /// <summary>Shows the OS map picker, then loads the chosen map into this workspace.</summary>
    public async Task PickAndOpenMapAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Open Warcraft III map — {_role}",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Warcraft III maps") { Patterns = new[] { "*.w3x", "*.w3m" } },
                FilePickerFileTypes.All,
            },
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
        {
            OpenMap(path);
        }
    }

    public void OpenMap(string path)
    {
        try
        {
            var doc = MapDocument.Load(path);
            Session.Current = doc;
            Session.MapPath = path;
            // GameDir stays null — panels auto-detect the game install.

            var info = InfoCommand.Execute(doc);
            var list = ListCommand.Execute(doc);
            var name = string.IsNullOrEmpty(info.Name) ? "(unnamed)" : info.Name;
            StatusText.Text = info.Diagnostics.Count > 0
                ? $"{path} — {name} — {list.Files.Count} file(s) — {info.Diagnostics.Count} diagnostic(s): {string.Join("; ", info.Diagnostics)}"
                : $"{path} — {name} — {list.Files.Count} file(s)";

            // Lazy loading: only the visible tab refreshes now; the other
            // panels load on first selection (see OnPanelTabsSelectionChanged).
            _loadedPanels.Clear();
            LoadSelectedPanel();

            MapChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error opening {path}: {ex.Message}";
        }
    }

    private async void OnOpenMapClick(object? sender, RoutedEventArgs e) =>
        await PickAndOpenMapAsync();

    /// <summary>
    /// Blank-map creation is not implemented yet: MapDocument can only Load()
    /// existing archive bytes, and Save() re-opens those bytes as the MPQ to
    /// rebuild from — a blank map means synthesizing a minimal valid archive
    /// (HM3W header + w3i/w3e/wpm/… + script) and a new MapDocument construction
    /// path. War3Net 6.x has no blank-map factory either (Map(MapInfo?,
    /// MapEnvironment?) wants a fully populated info + terrain grid). Follow-up
    /// slice; the affordance stays visible so the workflow is discoverable.
    /// </summary>
    private void OnNewBlankMapClick(object? sender, RoutedEventArgs e) =>
        StatusText.Text = "Blank-map creation is a follow-up — open an existing map as the target for now.";

    private void OnPanelTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles up from selectors inside tab content too.
        if (!ReferenceEquals(e.Source, PanelTabs) || Session.Current is null)
        {
            return;
        }

        try
        {
            LoadSelectedPanel();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading panel: {ex.Message}";
        }
    }

    private void LoadSelectedPanel()
    {
        if (PanelTabs.SelectedItem is TabItem { Content: IMapPanel panel } && _loadedPanels.Add(panel))
        {
            panel.ShowMap(Session);
        }
    }
}
