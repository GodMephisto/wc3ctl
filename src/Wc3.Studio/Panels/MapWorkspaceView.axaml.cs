using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Wc3.Commands;
using Wc3.GameData;
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
    /// <summary>The Objects tab's primary selection - what the Dependencies tab
    /// resolves when opened (live-pushed when that tab is already visible).</summary>
    private (ObjectKind Kind, string Rawcode)? _currentObject;

    public MapWorkspaceView()
    {
        InitializeComponent();
        DependenciesPanel.SelectionChanged += OnPortSelectionChanged;
        ObjectsPanel.ObjectSelected += OnObjectSelected;
        ObjectsPanel.DependenciesRequested += OnDependenciesRequested;
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

    /// <summary>This workspace's map state, passed to its panels - never shared.</summary>
    public MapSession Session { get; } = new();

    public bool HasMap => Session.Current is not null;

    /// <summary>
    /// Port seam: the unit shown in the Dependencies tab, whose closure the port
    /// copies into the Target workspace. Null until one is chosen AND null when
    /// the tab shows a non-unit object - porting is unit-rooted.
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

    /// <summary>
    /// Raised (Source only) when the user asks for a dry-run preview of the port: the
    /// same report the real port would produce, with nothing written. Argument and
    /// handling mirror <see cref="PortRequested"/>.
    /// </summary>
    public event EventHandler<string>? PortPreviewRequested;

    /// <summary>Sets this workspace's status line (used to report port progress/results).</summary>
    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>
    /// The port button tracks the Dependencies tab's selection: enabled with a
    /// live label when a UNIT is chosen. Porting is unit-rooted, so a non-unit
    /// selection still graphs but disables the buttons with an explaining tip.
    /// </summary>
    private void OnPortSelectionChanged(object? sender, EventArgs e)
    {
        var display = DependenciesPanel.SelectedUnitDisplay; // null for non-units
        bool nonUnit = display is null && DependenciesPanel.SelectedRawcode is not null;
        PortButton.IsEnabled = display is not null;
        PreviewPortButton.IsEnabled = display is not null;
        PortButton.Content = display is null
            ? "Port selected → Target ▶"
            : $"Port {display} → Target ▶";
        var kindWord = DependenciesPanel.SelectedObjectKind.ToString().ToLowerInvariant();
        var tip = display is not null
            ? $"Port {display} and everything it uses into the Target map."
            : nonUnit
                ? $"Porting is for units — {DependenciesPanel.SelectedDisplay} is a {kindWord}."
                : "Pick a unit in the Dependencies tab, then port it into the Target map.";
        ToolTip.SetTip(PortButtonHost, tip);
        ToolTip.SetTip(PortButton, tip);
        ToolTip.SetTip(PreviewPortButton, display is not null
            ? $"Dry run: show exactly what porting {display} would change without writing anything."
            : nonUnit
                ? $"Porting is for units — {DependenciesPanel.SelectedDisplay} is a {kindWord}."
                : "Dry run: show exactly what the port would change without writing anything.");
    }

    /// <summary>
    /// Objects tab selection moved: remember it so the Dependencies tab resolves
    /// it when opened; push it through immediately when that tab is visible.
    /// </summary>
    private void OnObjectSelected(object? sender, (ObjectKind Kind, string Rawcode) obj)
    {
        _currentObject = obj;
        if (PanelTabs.SelectedItem is TabItem { Content: DependencyGraphView })
            DependenciesPanel.ShowObject(Session, obj.Kind, obj.Rawcode);
    }

    /// <summary>Right-click "Show dependencies": jump to the Dependencies tab and resolve.</summary>
    private void OnDependenciesRequested(object? sender, (ObjectKind Kind, string Rawcode) obj)
    {
        _currentObject = obj;
        DependenciesTab.IsSelected = true; // tab-changed handler resolves via LoadSelectedPanel
        LoadSelectedPanel();               // covers "already on that tab" (no selection change)
    }

    private void OnPortClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedUnitForPort is { } rawcode)
            PortRequested?.Invoke(this, rawcode);
    }

    private void OnPreviewPortClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedUnitForPort is { } rawcode)
            PortPreviewRequested?.Invoke(this, rawcode);
    }

    /// <summary>Shows the OS map picker, then loads the chosen map into this workspace.</summary>
    public async Task PickAndOpenMapAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Open Warcraft III map - {_role}",
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
            // GameDir stays null - panels auto-detect the game install.
            SaveButton.IsEnabled = true;
            TestButton.IsEnabled = true;

            var info = InfoCommand.Execute(doc);
            var list = ListCommand.Execute(doc);
            var name = string.IsNullOrEmpty(info.Name) ? "(unnamed)" : info.Name;
            StatusText.Text = info.Diagnostics.Count > 0
                ? $"{path} - {name} - {list.Files.Count} file(s) - {info.Diagnostics.Count} diagnostic(s): {string.Join("; ", info.Diagnostics)}"
                : $"{path} - {name} - {list.Files.Count} file(s)";

            // Lazy loading: only the visible tab refreshes now; the other
            // panels load on first selection (see OnPanelTabsSelectionChanged).
            _loadedPanels.Clear();
            _currentObject = null; // rawcodes from the previous map are stale
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
    /// Writes the in-memory map (with any edits made this session) back to the .w3x
    /// file it was opened from. MapDocument.Save re-serializes through the byte-faithful
    /// writer, so an untouched map round-trips unchanged.
    /// </summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (Session.Current is not { } doc || Session.MapPath is not { } path)
        {
            StatusText.Text = "No map open to save.";
            return;
        }
        try
        {
            doc.Save(path);
            StatusText.Text = $"Saved {path}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Saves the map, then launches Warcraft III on it via -loadfile. The executable
    /// is found under the located install (Reforged layout first, then classic
    /// war3.exe); launching is best-effort and its outcome is reported on the status
    /// line - a missing install or a failed launch never throws.
    /// </summary>
    private void OnTestClick(object? sender, RoutedEventArgs e)
    {
        if (Session.Current is not { } doc || Session.MapPath is not { } path)
        {
            StatusText.Text = "No map open to test.";
            return;
        }
        try
        {
            doc.Save(path);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Test aborted - save failed: {ex.Message}";
            return;
        }
        if (GameInstall.LocateExecutable(Session.GameDir) is not { } exe)
        {
            StatusText.Text = "Saved, but couldn't find the Warcraft III executable to launch - open the map from the game manually.";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(exe, $"-loadfile \"{path}\"") { UseShellExecute = true });
            StatusText.Text = $"Saved and launched Warcraft III on {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Saved, but launch failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Blank-map creation is not implemented yet: MapDocument can only Load()
    /// existing archive bytes, and Save() re-opens those bytes as the MPQ to
    /// rebuild from - a blank map means synthesizing a minimal valid archive
    /// (HM3W header + w3i/w3e/wpm/… + script) and a new MapDocument construction
    /// path. War3Net 6.x has no blank-map factory either (Map(MapInfo?,
    /// MapEnvironment?) wants a fully populated info + terrain grid). Follow-up
    /// slice; the affordance stays visible so the workflow is discoverable.
    /// </summary>
    private void OnNewBlankMapClick(object? sender, RoutedEventArgs e) =>
        StatusText.Text = "Blank-map creation is a follow-up - open an existing map as the target for now.";

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
        if (PanelTabs.SelectedItem is not TabItem { Content: IMapPanel panel })
        {
            return;
        }

        bool firstShow = _loadedPanels.Add(panel);
        if (ReferenceEquals(panel, DependenciesPanel) && _currentObject is { } obj)
        {
            // Smart path: the Dependencies tab follows the Objects tab's selection.
            // ShowObject fully initializes the panel (a superset of ShowMap) and
            // no-ops when the object is already shown, so tab flips never re-resolve.
            DependenciesPanel.ShowObject(Session, obj.Kind, obj.Rawcode);
        }
        else if (firstShow)
        {
            // No object picked yet: the Dependencies tab keeps its manual picker
            // prompt (no forced resolve); every other panel loads as before.
            panel.ShowMap(Session);
        }
    }
}
