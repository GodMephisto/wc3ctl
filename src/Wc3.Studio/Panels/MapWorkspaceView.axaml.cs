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
    /// <summary>The port seam's CURRENT selection: the freshest pick across the
    /// Objects and Dependencies tabs, which the Port buttons act on. Display is
    /// the friendly "Name (rawcode)" label once the Dependencies tab resolves it,
    /// null (rawcode-only labels) until then.</summary>
    private (ObjectKind Kind, string Rawcode, string? Display)? _portSelection;
    /// <summary>The 3D viewport's picked placed units (creation numbers) - what the
    /// Unit tab shows when opened (live-routed on pick; 1 = the full editor, 2+ = the
    /// bulk view). Cleared on map change: creation numbers do not survive a reload.</summary>
    private IReadOnlyList<int> _currentUnits = Array.Empty<int>();
    /// <summary>The 3D viewport's picked placed doodad (war3map.doo creation number),
    /// what the Doodad tab shows when opened. Single-pick, exclusive with the unit
    /// selection. Cleared on map change like <see cref="_currentUnits"/>.</summary>
    private int? _currentDoodad;

    public MapWorkspaceView()
    {
        InitializeComponent();
        DependenciesPanel.SelectionChanged += OnPortSelectionChanged;
        ObjectsPanel.ObjectSelected += OnObjectSelected;
        ObjectsPanel.DependenciesRequested += OnDependenciesRequested;
        PalettePanel.PlacementChanged += OnPalettePlacementChanged;
        TerrainPanel.MapEdited += OnMapEdited;
        TerrainPanel.UnitsSelected += OnTerrainUnitsSelected;
        TerrainPanel.DoodadSelected += OnTerrainDoodadSelected;
        TerrainPanel.RemoveUnitsRequested += OnRemoveUnitsRequested;
        // Esc / right-click in the viewport cancels the brush → clear the Palette highlight too.
        TerrainPanel.PlacementBrushCleared += (_, _) => PalettePanel.ClearSelection();
        UnitPropsPanel.UnitEdited += OnUnitEdited;
        UnitPropsPanel.UnitsEdited += OnUnitsEdited;
        UnitPropsPanel.UnitsRemoved += OnUnitsRemoved;
        DoodadPropsPanel.DoodadEdited += OnDoodadEdited;
        DoodadPropsPanel.DoodadRemoved += OnDoodadRemoved;
        PlayersPanel.MapEdited += OnPlayersEdited;
        RegionsPanel.MapEdited += OnMapEdited;
        CamerasPanel.MapEdited += OnMapEdited;
        SoundsPanel.MapEdited += OnMapEdited;
        TriggersPanel.MapEdited += OnMapEdited;
    }

    /// <summary>Dev/QA: select a top-level tab by its header text (drives the --tab startup
    /// flag so a panel can be screenshotted without clicking). Triggers the same lazy load a
    /// user click would. No-op if no tab matches.</summary>
    public void SelectTab(string header)
    {
        foreach (var item in PanelTabs.Items)
            if (item is TabItem t && string.Equals(t.Header as string, header, StringComparison.OrdinalIgnoreCase))
            { PanelTabs.SelectedItem = t; return; }
    }

    /// <summary>Dev/QA: select a Terrain-tab dock sub-tab (Palette/Unit/Doodad/Players) by header.</summary>
    public void SelectDock(string header)
    {
        foreach (var item in DockTabs.Items)
            if (item is TabItem t && string.Equals(t.Header as string, header, StringComparison.OrdinalIgnoreCase))
            { DockTabs.SelectedItem = t; return; }
    }

    /// <summary>Palette selection → arm the Terrain tab's placement brush and jump
    /// there, with the Palette sub-tab front in the side dock so the armed row's
    /// highlight stays visible next to the viewport.</summary>
    private void OnPalettePlacementChanged(object? sender, PaletteRow? row)
    {
        TerrainPanel.SetPlacementBrush(row);
        if (row is not null)
        {
            PanelTabs.SelectedItem = TerrainTab;
            DockTabs.SelectedItem = PaletteDockTab;
        }
    }

    /// <summary>A click-to-place mutated the in-memory map → enable Save.</summary>
    private void OnMapEdited(object? sender, EventArgs e)
    {
        SaveButton.IsEnabled = true;
        StatusText.Text = "Placed object (unsaved) — click Save to write it to the map.";
    }

    /// <summary>Viewport selection change (click/marquee/Shift-toggle/clear) → route the
    /// units to the side dock's Unit sub-tab. A non-empty pick flips the dock over to it
    /// (1 = full editor, 2+ = bulk view); a cleared selection just resets the panel if it
    /// has been shown, without yanking the dock over to it.</summary>
    private void OnTerrainUnitsSelected(IReadOnlyList<int> creationNumbers)
    {
        _currentUnits = creationNumbers;
        if (creationNumbers.Count == 0)
        {
            if (_loadedPanels.Contains(UnitPropsPanel))
                UnitPropsPanel.ShowUnits(Session, creationNumbers);
            return;
        }
        _currentDoodad = null; // unit and doodad selection are exclusive in the viewport
        PanelTabs.SelectedItem = TerrainTab; // picks come from the viewport, so usually a no-op
        DockTabs.SelectedItem = UnitDockTab;
        // Route directly - a dock sub-tab flip is not a PanelTabs selection change,
        // so LoadSelectedPanel won't fire. ShowUnits no-ops when already showing.
        _loadedPanels.Add(UnitPropsPanel);
        UnitPropsPanel.ShowUnits(Session, creationNumbers);
    }

    /// <summary>Viewport doodad pick → route it to the side dock's Doodad sub-tab and
    /// flip the dock over to it. The viewport clears the unit selection first (an empty
    /// UnitsSelected snapshot arrives before this), so the two stay exclusive.</summary>
    private void OnTerrainDoodadSelected(int creationNumber)
    {
        _currentDoodad = creationNumber;
        PanelTabs.SelectedItem = TerrainTab; // picks come from the viewport, so usually a no-op
        DockTabs.SelectedItem = DoodadDockTab;
        // Route directly - a dock sub-tab flip is not a PanelTabs selection change,
        // so LoadSelectedPanel won't fire. ShowDoodad no-ops when already showing.
        _loadedPanels.Add(DoodadPropsPanel);
        DoodadPropsPanel.ShowDoodad(Session, creationNumber);
    }

    /// <summary>Delete/Backspace in the viewport → remove the selected units through the
    /// command layer, then refresh the viewport and clear the selection everywhere.</summary>
    private void OnRemoveUnitsRequested(IReadOnlyList<int> creationNumbers)
    {
        if (Session.Current is not { } doc || creationNumbers.Count == 0)
            return;
        var result = UnitInstanceCommand.DeleteMany(doc, creationNumbers);
        if (!result.Ok)
        {
            StatusText.Text = $"Remove failed: {result.Message}";
            return;
        }
        ClearUnitSelection();
        SaveButton.IsEnabled = true;
        StatusText.Text = $"{result.Message} (unsaved) - click Save to write it to the map.";
    }

    /// <summary>A Unit-tab Apply mutated the in-memory map → enable Save, re-render
    /// the viewport, and keep the edited unit highlighted there.</summary>
    private void OnUnitEdited(int creationNumber)
    {
        _currentUnits = new[] { creationNumber };
        SaveButton.IsEnabled = true;
        StatusText.Text = $"Edited unit #{creationNumber} (unsaved) - click Save to write it to the map.";
        TerrainPanel.RefreshPlacements();
        TerrainPanel.SelectUnit(creationNumber);
    }

    /// <summary>A Unit-tab bulk edit (multi-select owner change) mutated the in-memory
    /// map → enable Save, re-render the viewport (re-tint team colors), and re-echo the
    /// selection so the highlight survives the rebuild.</summary>
    private void OnUnitsEdited(IReadOnlyList<int> creationNumbers)
    {
        _currentUnits = creationNumbers;
        SaveButton.IsEnabled = true;
        StatusText.Text = $"Edited {creationNumbers.Count} unit(s) (unsaved) - click Save to write it to the map.";
        TerrainPanel.RefreshPlacements();
        TerrainPanel.SelectUnits(creationNumbers);
    }

    /// <summary>The Unit tab's "Remove selected" deleted units from the in-memory map →
    /// enable Save and clear the now-dangling selection everywhere.</summary>
    private void OnUnitsRemoved(IReadOnlyList<int> creationNumbers)
    {
        ClearUnitSelection();
        SaveButton.IsEnabled = true;
        StatusText.Text = $"Removed {creationNumbers.Count} unit(s) (unsaved) - click Save to write it to the map.";
    }

    /// <summary>A Doodad-tab Apply mutated the in-memory map → enable Save, re-render
    /// the viewport, and keep the edited doodad highlighted there.</summary>
    private void OnDoodadEdited(int creationNumber)
    {
        _currentDoodad = creationNumber;
        SaveButton.IsEnabled = true;
        StatusText.Text = $"Edited doodad #{creationNumber} (unsaved) - click Save to write it to the map.";
        TerrainPanel.RefreshPlacements();
        TerrainPanel.SelectDoodad(creationNumber);
    }

    /// <summary>The Doodad tab's Remove deleted the doodad from the in-memory map →
    /// enable Save and clear the now-dangling selection everywhere.</summary>
    private void OnDoodadRemoved(int creationNumber)
    {
        _currentDoodad = null;
        TerrainPanel.RefreshPlacements();
        TerrainPanel.SelectDoodad(null);
        SaveButton.IsEnabled = true;
        StatusText.Text = $"Removed doodad #{creationNumber} (unsaved) - click Save to write it to the map.";
    }

    /// <summary>After a removal: refresh the viewport's placement scene and drop the
    /// selection from the viewport, the Unit tab, and this workspace's tracking.</summary>
    private void ClearUnitSelection()
    {
        _currentUnits = Array.Empty<int>();
        TerrainPanel.RefreshPlacements();
        TerrainPanel.SelectUnits(Array.Empty<int>());
        UnitPropsPanel.ShowUnits(Session, Array.Empty<int>());
    }

    /// <summary>A Players-tab edit mutated the in-memory map → enable Save.</summary>
    private void OnPlayersEdited(object? sender, EventArgs e)
    {
        SaveButton.IsEnabled = true;
        StatusText.Text = "Edited players/forces (unsaved) - click Save to write it to the map.";
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

    /// <summary>This workspace's 3D terrain tab - exposed so shell code can re-render
    /// placements and drive the selection highlight after unit edits.</summary>
    public TerrainView Terrain => TerrainPanel;

    public bool HasMap => Session.Current is not null;

    /// <summary>
    /// Port seam: the selected unit whose closure the port copies into the Target
    /// workspace. Tracks the FRESHEST selection across the Objects and Dependencies
    /// tabs - no need to open Dependencies first. Null until a unit is chosen AND
    /// null when the current selection is a non-unit - porting is unit-rooted.
    /// </summary>
    public string? SelectedUnitForPort =>
        _portSelection is { } sel && sel.Kind == ObjectKind.Unit ? sel.Rawcode : null;

    /// <summary>Raised after a map is successfully opened into this workspace.</summary>
    public event EventHandler? MapChanged;

    /// <summary>A port ask, the rooted unit plus whichever rawcodes/file paths the user
    /// excluded on the Dependencies tab's graph (empty when nothing was excluded, the
    /// default, port everything). MainWindow narrows the bundle through
    /// <c>BundleFilter.Apply</c> before handing it to <c>PortCommand</c>.</summary>
    public readonly record struct PortRequest(string Rawcode, IReadOnlySet<string> ExcludedKeys);

    /// <summary>
    /// Raised (Source only) when the user clicks "Port → Target". MainWindow handles it
    /// because only it holds both the Source and Target sessions.
    /// </summary>
    public event EventHandler<PortRequest>? PortRequested;

    /// <summary>
    /// Raised (Source only) when the user asks for a dry-run preview of the port: the
    /// same report the real port would produce, with nothing written. Argument and
    /// handling mirror <see cref="PortRequested"/>.
    /// </summary>
    public event EventHandler<PortRequest>? PortPreviewRequested;

    /// <summary>The exclusion set to act on for the current port selection, the
    /// Dependencies tab's graph state when it is showing exactly the unit about to be
    /// ported, empty otherwise (the Objects tab alone never excludes anything).</summary>
    private IReadOnlySet<string> ExcludedForPort =>
        SelectedUnitForPort is { } rawcode
            && DependenciesPanel.SelectedObjectKind == ObjectKind.Unit
            && DependenciesPanel.SelectedRawcode == rawcode
            ? DependenciesPanel.ExcludedKeys
            : new HashSet<string>();

    /// <summary>Sets this workspace's status line (used to report port progress/results).</summary>
    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>
    /// The Dependencies tab's selection changed (manual pick, push-through of the
    /// Objects tab, or cleared on map change): it becomes the current port selection,
    /// upgrading the label to the panel's friendly "Name (rawcode)" display.
    /// </summary>
    private void OnPortSelectionChanged(object? sender, EventArgs e)
    {
        _portSelection = DependenciesPanel.SelectedRawcode is { } rawcode
            ? (DependenciesPanel.SelectedObjectKind, rawcode, DependenciesPanel.SelectedDisplay)
            : null;
        UpdatePortButtons();
    }

    /// <summary>
    /// Renders the Port buttons from <see cref="_portSelection"/>: enabled with a
    /// live label when a UNIT is chosen (before the Dependencies tab ever resolves
    /// it). Porting is unit-rooted, so a non-unit selection still graphs but
    /// disables the buttons with an explaining tip.
    /// </summary>
    private void UpdatePortButtons()
    {
        var sel = _portSelection;
        string? display = sel is { } unit && unit.Kind == ObjectKind.Unit
            ? StripKindTag(unit.Display) ?? unit.Rawcode : null;
        bool nonUnit = display is null && sel is not null;
        PortButton.IsEnabled = display is not null;
        PreviewPortButton.IsEnabled = display is not null;
        PortButton.Content = display is null
            ? "Port selected → Target ▶"
            : $"Port {display} → Target ▶";
        var kindWord = (sel is { } k ? k.Kind : ObjectKind.Unit).ToString().ToLowerInvariant();
        var selLabel = sel is { } s ? s.Display ?? s.Rawcode : null;
        var tip = display is not null
            ? $"Port {display} and everything it uses into the Target map."
            : nonUnit
                ? $"Porting is for units — {selLabel} is a {kindWord}."
                : "Pick a unit in the Objects or Dependencies tab, then port it into the Target map.";
        ToolTip.SetTip(PortButtonHost, tip);
        ToolTip.SetTip(PortButton, tip);
        ToolTip.SetTip(PreviewPortButton, display is not null
            ? $"Dry run: show exactly what porting {display} would change without writing anything."
            : nonUnit
                ? $"Porting is for units — {selLabel} is a {kindWord}."
                : "Dry run: show exactly what the port would change without writing anything.");
    }

    /// <summary>Drops the " · kind" tag the Dependencies picker adds to its display, so the
    /// Port button reads "Name (rawcode)" without the redundant kind. The rawcode (not this
    /// label) drives the actual port, so this is purely cosmetic.</summary>
    private static string? StripKindTag(string? display)
    {
        if (display is null) return null;
        int dot = display.IndexOf(" · ", StringComparison.Ordinal);
        int paren = display.LastIndexOf(" (", StringComparison.Ordinal);
        return dot >= 0 && paren > dot ? display.Remove(dot, paren - dot) : display;
    }

    /// <summary>
    /// Objects tab selection moved: remember it so the Dependencies tab resolves
    /// it when opened; push it through immediately when that tab is visible. The
    /// Port button follows right away - a unit selection arms it before the
    /// Dependencies tab has ever resolved anything.
    /// </summary>
    private void OnObjectSelected(object? sender, (ObjectKind Kind, string Rawcode) obj)
    {
        _currentObject = obj;
        TrackPortSelection(obj);
        if (PanelTabs.SelectedItem is TabItem { Content: DependencyGraphView })
            DependenciesPanel.ShowObject(Session, obj.Kind, obj.Rawcode);
    }

    /// <summary>Right-click "Show dependencies": jump to the Dependencies tab and resolve.</summary>
    private void OnDependenciesRequested(object? sender, (ObjectKind Kind, string Rawcode) obj)
    {
        _currentObject = obj;
        TrackPortSelection(obj);
        DependenciesTab.IsSelected = true; // tab-changed handler resolves via LoadSelectedPanel
        LoadSelectedPanel();               // covers "already on that tab" (no selection change)
    }

    /// <summary>Make <paramref name="obj"/> the current port selection, borrowing the
    /// Dependencies tab's friendly display when it already shows this very object.</summary>
    private void TrackPortSelection((ObjectKind Kind, string Rawcode) obj)
    {
        var display = DependenciesPanel.SelectedObjectKind == obj.Kind
            && DependenciesPanel.SelectedRawcode == obj.Rawcode
            ? DependenciesPanel.SelectedDisplay : null;
        _portSelection = (obj.Kind, obj.Rawcode, display);
        UpdatePortButtons();
    }

    private void OnPortClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedUnitForPort is { } rawcode)
            PortRequested?.Invoke(this, new PortRequest(rawcode, ExcludedForPort));
        // Re-evaluate against the current selection so the button never keeps
        // offering a unit the user has since navigated away from.
        UpdatePortButtons();
    }

    private void OnPreviewPortClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedUnitForPort is { } rawcode)
            PortPreviewRequested?.Invoke(this, new PortRequest(rawcode, ExcludedForPort));
        UpdatePortButtons();
    }

    /// <summary>Shows the OS map picker, then loads the chosen map into this workspace.</summary>
    public async Task PickAndOpenMapAsync()
    {
        var path = await Controls.FilePicker.PickOpenAsync(this, $"Open Warcraft III map - {_role}",
            new Controls.FilePicker.Filter("Warcraft III maps", new[] { "*.w3x", "*.w3m" }));
        if (path is not null)
            OpenMap(path);
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
            _currentObject = null;                  // rawcodes from the previous map are stale
            _currentUnits = Array.Empty<int>();     // creation numbers too
            _currentDoodad = null;
            _portSelection = null;
            UpdatePortButtons();
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
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (Session.Current is not { } doc)
        {
            StatusText.Text = "No map open to save.";
            return;
        }

        // A blank/new map has no path yet — prompt for one (Save As).
        var path = Session.MapPath;
        if (path is null)
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null)
                return;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save map as",
                SuggestedFileName = "NewMap.w3x",
                DefaultExtension = "w3x",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Warcraft III map") { Patterns = new[] { "*.w3x", "*.w3m" } },
                },
            });
            if (file?.TryGetLocalPath() is not { } chosen)
                return; // cancelled
            path = chosen;
            Session.MapPath = path;
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
    /// Creates a fresh blank map in memory (via <see cref="BlankMap"/>, which synthesizes
    /// a valid w3i + w3e archive) and loads it into this workspace. It has no file yet, so
    /// Save prompts for a location (see <see cref="OnSaveClick"/>).
    /// </summary>
    private void OnNewBlankMapClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Start-location markers so a new map opens a real host lobby (see BlankMap.IncludeStartLocations).
            var doc = BlankMap.Create(new BlankMapOptions { IncludeStartLocations = true });
            Session.Current = doc;
            Session.MapPath = null; // no file on disk yet — Save will prompt for one
            SaveButton.IsEnabled = true;
            TestButton.IsEnabled = true;
            _loadedPanels.Clear();
            _currentObject = null;
            _currentUnits = Array.Empty<int>();
            _currentDoodad = null;
            _portSelection = null;
            UpdatePortButtons();
            LoadSelectedPanel();
            MapChanged?.Invoke(this, EventArgs.Empty);
            StatusText.Text = "New blank map created (in memory) — edit it, then Save to write it to disk.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Blank-map creation failed: {ex.Message}";
        }
    }

    /// <summary>Re-shows the visible tab after the in-memory map was mutated externally
    /// (e.g. a port into an unsaved blank target): rebuilds panels from the changed doc,
    /// enables Save/Test, and updates the global status. Save then prompts for a location.</summary>
    public void RefreshAfterExternalEdit()
    {
        if (Session.Current is null)
            return;
        SaveButton.IsEnabled = true;
        TestButton.IsEnabled = true;
        _loadedPanels.Clear();   // force the visible tab to rebuild from the mutated doc
        _currentObject = null;   // ported rawcodes are new; drop the stale selection
        _currentUnits = Array.Empty<int>(); // creation numbers may have shifted with the edit
        _currentDoodad = null;
        LoadSelectedPanel();
        MapChanged?.Invoke(this, EventArgs.Empty);
    }

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
        // The Terrain tab hosts a Grid (viewport + side dock), not a bare
        // IMapPanel - load the viewport and all three dock panels together.
        if (ReferenceEquals(PanelTabs.SelectedItem, TerrainTab))
        {
            LoadTerrainDock();
            return;
        }

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

    /// <summary>Shows the Terrain tab's viewport plus its docked panels
    /// (Palette / Unit / Doodad / Players) on first open - the Palette loads its
    /// heavy art off-thread, so eager-loading the dock stays cheap. The Unit and
    /// Doodad panels follow the viewport's current selection when there is one
    /// (ShowUnits/ShowDoodad are supersets of ShowMap and no-op when already
    /// showing that selection, so tab flips keep in-progress edits).</summary>
    private void LoadTerrainDock()
    {
        if (_loadedPanels.Add(TerrainPanel))
            TerrainPanel.ShowMap(Session);
        if (_loadedPanels.Add(PalettePanel))
            PalettePanel.ShowMap(Session);
        if (_loadedPanels.Add(PlayersPanel))
            PlayersPanel.ShowMap(Session);
        if (_currentUnits.Count > 0)
        {
            _loadedPanels.Add(UnitPropsPanel);
            UnitPropsPanel.ShowUnits(Session, _currentUnits);
        }
        else if (_loadedPanels.Add(UnitPropsPanel))
        {
            UnitPropsPanel.ShowMap(Session);
        }
        if (_currentDoodad is { } doodad)
        {
            _loadedPanels.Add(DoodadPropsPanel);
            DoodadPropsPanel.ShowDoodad(Session, doodad);
        }
        else if (_loadedPanels.Add(DoodadPropsPanel))
        {
            DoodadPropsPanel.ShowMap(Session);
        }
    }
}
