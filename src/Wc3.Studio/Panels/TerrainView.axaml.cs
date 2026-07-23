using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Wc3.Commands;

namespace Wc3.Studio.Panels;

public partial class TerrainView : UserControl, IMapPanel
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 10.0;

    private Bitmap? _bitmap;
    private double _zoom = 1.0;

    // True while the GL viewport (the 3D view) is shown. The 2D minimap paths
    // (scroll/zoom/click-to-place on ScrollHost) only run while it is hidden.
    private bool _is3D;

    private MapSession? _session;
    private PaletteRow? _brush;

    // Sculpt mode: the terrain brush that reshapes war3map.w3e through the
    // TerrainCommand area ops. Mutually exclusive with the placement brush and with
    // pointer selection, all three own the left click. NOTE: sculpt strokes bypass
    // EditHistory (the placement undo journal), so they cannot be undone in-session
    // yet. Journaled terrain undo is a follow-up.
    private bool _sculpt;
    private bool _sculptDrag2D;                 // a left-drag stroke is live on the 2D minimap
    private (int Col, int Row)? _sculptLastCorner; // drag throttle: re-apply only on a new corner
    private bool _glTerrainStale;               // sculpted while 2D showed, GL mesh needs a rebuild

    private readonly Random _rng = new();
    private Wc3.Render.TerrainRenderer.TerrainTransform _xform;
    private readonly List<(double X, double Y)> _markers = new();
    private readonly Wc3.Commands.Editing.EditHistory _history = new();

    /// <summary>Raised after a click-to-place (or an undo/redo) mutates the in-memory map.</summary>
    public event EventHandler? MapEdited;

    /// <summary>Raised whenever a GL viewport gesture changes the unit selection — a
    /// plain click (a 1-element list), a Shift-click toggle, a marquee drag, or a clear
    /// (an empty list). The list is a snapshot of the FULL current selection.
    /// Programmatic selection via <see cref="SelectUnits"/> does NOT re-raise this, so
    /// hosts can echo panel selections into the viewport without feedback loops.</summary>
    public event Action<IReadOnlyList<int>>? UnitsSelected;

    /// <summary>Raised when the user asks to remove the selected units (Delete/Backspace
    /// in pointer mode). The host owns the actual delete (command layer + refresh); the
    /// list is a snapshot of the selection at the time of the request.</summary>
    public event Action<IReadOnlyList<int>>? RemoveUnitsRequested;

    /// <summary>Raised when a pointer-mode click in the GL viewport picks a doodad;
    /// the argument is its war3map.doo creation number. Doodad selection is single-pick
    /// (the marquee stays a unit multi-select), and picking a doodad clears the unit
    /// selection first (<see cref="UnitsSelected"/> fires with an empty snapshot before
    /// this event). Programmatic selection via <see cref="SelectDoodad"/> does NOT
    /// re-raise this, mirroring the unit flow.</summary>
    public event Action<int>? DoodadSelected;

    /// <summary>Raised when the placement brush is cleared (Esc or right-click in the GL
    /// view) so the host can clear the Palette's selection and stay in sync — returning the
    /// user to "pointer mode" where a left click selects a unit instead of placing.</summary>
    public event EventHandler? PlacementBrushCleared;

    /// <summary>Highlights placed units in the GL viewport by creation number (an empty
    /// list clears the highlight). For the workspace host to mirror selection made
    /// elsewhere; does not raise <see cref="UnitsSelected"/>.</summary>
    public void SelectUnits(IReadOnlyList<int> creationNumbers)
    {
        _selection.Clear();
        _selection.AddRange(creationNumbers);
        GlView.SetSelectedUnits(_selection);
        if (creationNumbers.Count > 0)
            GlView.SetSelectedDoodad(null); // unit and doodad selection are exclusive
    }

    /// <summary>Single-unit convenience over <see cref="SelectUnits"/> (null clears).</summary>
    public void SelectUnit(int? creationNumber) =>
        SelectUnits(creationNumber is int cn ? new[] { cn } : Array.Empty<int>());

    /// <summary>Highlights a placed doodad in the GL viewport by creation number (null
    /// clears the highlight). For the workspace host to mirror selection made elsewhere;
    /// does not raise <see cref="DoodadSelected"/>. Selecting a doodad drops any unit
    /// selection, the two are exclusive like in the World Editor.</summary>
    public void SelectDoodad(int? creationNumber)
    {
        if (creationNumber is not null && _selection.Count > 0)
        {
            _selection.Clear();
            GlView.SetSelectedUnits(_selection);
        }
        GlView.SetSelectedDoodad(creationNumber);
    }

    /// <summary>Re-reads the map's placements and rebuilds the GL scene. For the workspace
    /// host to call after a unit edit made through a side panel (owner/team/etc.), so the
    /// viewport reflects the change (e.g. team-color tint) immediately.</summary>
    public void RefreshPlacements() => GlView.RefreshPlacements();

    public TerrainView()
    {
        InitializeComponent();
        // Tunnel so zooming wins over the ScrollViewer's own wheel scrolling.
        ScrollHost.AddHandler(PointerWheelChangedEvent, OnPointerWheel, RoutingStrategies.Tunnel);
        _history.Changed += OnHistoryChanged;
        // Keep the scale-variance readout in sync with the slider.
        ScaleVarSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                ScaleVarText.Text = $"±{ScaleVarSlider.Value * 100:0}%";
        };
        // Same pattern for the sculpt-brush radius readout.
        SculptRadiusSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                SculptRadiusText.Text = $"r{(int)SculptRadiusSlider.Value}";
        };
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        ClearImage();
        _history.Clear();                  // the previous map's undo stack no longer applies
        SelectUnits(Array.Empty<int>());   // creation numbers don't carry across maps
        GlView.SetSelectedDoodad(null);    // doodad creation numbers don't either
        if (_sculpt)
            ExitSculpt();                  // the new map's grid invalidates any live stroke
        _glTerrainStale = false;           // SetMap below rebuilds the GL terrain anyway

        if (session.Current is null)
        {
            GlView.SetMap(null);
            ShowMessage("No map loaded.");
            return;
        }

        try
        {
            _xform = Wc3.Render.TerrainRenderer.GetTransform(session.Current);
            GlView.SetMap(session.Current, BuildModelResolver(session));
            GlView.DriveReset(); // fresh map -> default framing
            // Placement owner: keep the user's pick when one is set, else Player 1.
            PlaceOwnerPicker.Load(session.Current, PlaceOwnerPicker.SelectedOwnerId ?? 0);
        }
        catch (Exception ex)
        {
            ShowMessage($"Terrain cannot be rendered: {ex.Message}");
            return;
        }

        MessageText.IsVisible = false;
        ScrollHost.IsVisible = true;
        FitButton.IsEnabled = true;
        ResetButton.IsEnabled = true;
        Mode3DButton.IsEnabled = true;
        GlButton.IsEnabled = true;
        SculptButton.IsEnabled = true;
        LoadSculptTiles(session.Current);
        RenderCurrent();
        if (_is3D)
            ShowGlViewport(true); // restore the 3D viewport if it was active
    }

    /// <summary>Renders the flat 2D minimap into the scrollable Image. The 3D view is the
    /// GL viewport (<see cref="TerrainGlView"/>) and does not go through this path.</summary>
    private void RenderCurrent()
    {
        if (!RenderMinimap())
            return;
        _zoom = 1.0;
        ApplyZoom();
    }

    /// <summary>Re-renders the minimap at the current zoom, keeping the scroll position.
    /// Used after a sculpt stroke so painting does not yank the view back to 1:1.</summary>
    private void RerenderPreservingZoom()
    {
        if (RenderMinimap())
            ApplyZoom();
    }

    /// <summary>Runs RenderCommand over the current doc and swaps the new bitmap into the
    /// Image. Returns false (with the failure surfaced) when there is nothing to render.</summary>
    private bool RenderMinimap()
    {
        if (_session?.Current is not { } doc)
            return false;
        try
        {
            byte[] png = Wc3.Commands.RenderCommand.Execute(doc);
            var old = _bitmap;
            _bitmap = new Bitmap(new MemoryStream(png));
            TerrainImage.Source = _bitmap;
            old?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            ShowMessage($"Terrain cannot be rendered: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Builds the GL viewport's per-type model resolver: model path from the merged
    /// object data (map deltas win; Reforged skin-profile fallback), geometry + decoded
    /// textures via <see cref="RenderModelCommand.Prepare(Wc3.Model.MapDocument, string, Wc3.GameData.GameDataContext?)"/>
    /// (map imports first, then base game via CASC), posed at "Stand" — the viewport
    /// UV-maps each geoset with its real texture. Results — including failures, as null —
    /// are cached per type, so each distinct rawcode is resolved at most once per map.
    /// </summary>
    private static Func<string, bool, Wc3.Render.PlacementModel?> BuildModelResolver(MapSession session)
    {
        var doc = session.Current!;
        Wc3.GameData.GameDataContext? ctx = null;
        bool ctxTried = false;
        var cache = new Dictionary<(string Rawcode, bool IsUnit), Wc3.Render.PlacementModel?>();
        return (rawcode, isUnit) =>
        {
            var key = (rawcode, isUnit);
            if (cache.TryGetValue(key, out var cached))
                return cached;
            if (!ctxTried)
            {
                ctxTried = true; // null ctx degrades gracefully to map-imported models only
                Wc3.GameData.GameData.TryOpen(session.GameDir, out ctx, out _);
            }
            Wc3.Render.PlacementModel? resolved = null;
            try
            {
                var path = PlacementModelResolver.ResolveModelPath(doc, rawcode, isUnit, session.GameDir);
                if (path is not null)
                {
                    var prepared = RenderModelCommand.Prepare(doc, path, ctx);
                    resolved = new Wc3.Render.PlacementModel(
                        prepared.Model.PosedAt("Stand"), prepared.Textures);
                }
            }
            catch
            {
                resolved = null; // any parse/IO surprise -> box fallback for this type
            }
            cache[key] = resolved;
            return resolved;
        };
    }

    /// <summary>Arms (or clears) the placeable that a map click will place. Called by
    /// the workspace host when the Palette selection changes.</summary>
    public void SetPlacementBrush(PaletteRow? row)
    {
        // Arming a placeable while sculpting leaves sculpt mode first, the two brushes
        // are mutually exclusive owners of the left click.
        if (row is not null && _sculpt)
            ExitSculpt();
        _brush = row;
        StopPlacingButton.IsEnabled = row is not null; // lights up only while a tool is armed
        BrushText.Text = row is null
            ? "▶ Pointer mode — click or drag a box to select units (Shift adds, Delete removes)"
            : $"● Placing: {row.Display} — click to place · ✋ Stop placing (or Esc / right-click) to cancel";
    }

    /// <summary>Toolbar "Stop placing": disarm the brush → pointer mode. Works in both the
    /// 2D and GL views (unlike Esc/right-click which are GL-only), so it's the reliable exit.</summary>
    private void OnStopPlacingClick(object? sender, RoutedEventArgs e) =>
        ClearPlacementBrush("Pointer mode — click a unit to select.");

    /// <summary>Disarms the placement brush (back to pointer mode) and tells the host so the
    /// Palette selection clears too. Used by right-click / Esc in the GL view.</summary>
    private void ClearPlacementBrush(string? caption = null)
    {
        if (_brush is null)
            return;
        SetPlacementBrush(null);
        if (caption is not null)
            CaptionText.Text = caption;
        PlacementBrushCleared?.Invoke(this, EventArgs.Empty);
    }

    // --- terrain sculpting brush ----------------------------------------------
    // The Sculpt toggle turns the left click into a TerrainCommand area brush over
    // war3map.w3e, in both the 2D minimap and the GL viewport. Strokes mutate the doc
    // directly (no EditHistory journal yet, so no Ctrl+Z, that is a follow-up) and
    // refresh whichever terrain view is showing.

    /// <summary>Sculpt tools, in the exact order of SculptToolBox's items in the XAML.</summary>
    private enum SculptTool { Raise, Lower, Flatten, Paint, CliffUp, CliffDown, Water }

    private SculptTool CurrentSculptTool => (SculptTool)Math.Max(0, SculptToolBox.SelectedIndex);

    /// <summary>Tools whose strength comes from the Amount box (height step for
    /// raise/lower, absolute level for water). The others are fixed-strength.</summary>
    private static bool NeedsAmount(SculptTool tool) =>
        tool is SculptTool.Raise or SculptTool.Lower or SculptTool.Water;

    private void OnToggleSculpt(object? sender, RoutedEventArgs e)
    {
        if (SculptButton.IsChecked == true)
            EnterSculpt();
        else
            ExitSculpt();
    }

    private void OnSculptToolChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SculptToolPanel is null)
            return; // fires while the XAML is still loading
        UpdateSculptControls();
        UpdateSculptHint();
    }

    private void EnterSculpt()
    {
        // Sculpt and placement fight over the left click, so entering sculpt disarms
        // the palette brush first (the host hears PlacementBrushCleared and clears
        // the Palette selection, keeping the two in sync).
        ClearPlacementBrush();
        _sculpt = true;
        _sculptLastCorner = null;
        SculptButton.IsChecked = true;
        UpdateSculptControls();
        UpdateSculptHint();
        CaptionText.Text = "Sculpt mode: left click or drag the terrain to apply the brush.";
    }

    /// <summary>Leaves sculpt mode and restores the pointer-mode status line. Safe to
    /// call redundantly (map switches, Esc, arming a placement brush).</summary>
    private void ExitSculpt(string? caption = null)
    {
        _sculpt = false;
        _sculptDrag2D = false;
        SculptButton.IsChecked = false;
        UpdateSculptControls();
        SetPlacementBrush(null); // sculpt cleared the brush on entry, this resets BrushText
        if (caption is not null)
            CaptionText.Text = caption;
    }

    /// <summary>Shows the sculpt option groups that apply to the active tool (Amount for
    /// raise/lower/water, the tile picker for paint), hiding everything when sculpt is off.</summary>
    private void UpdateSculptControls()
    {
        bool on = _sculpt;
        var tool = CurrentSculptTool;
        SculptToolPanel.IsVisible = on;
        SculptSizePanel.IsVisible = on;
        SculptAmountPanel.IsVisible = on && NeedsAmount(tool);
        SculptTilePanel.IsVisible = on && tool == SculptTool.Paint;
    }

    /// <summary>Status-line summary of the active sculpt tool. It reuses BrushText, the
    /// same line the placement brush writes, because the two modes are exclusive.</summary>
    private void UpdateSculptHint()
    {
        if (!_sculpt)
            return;
        BrushText.Text = "⛰ " + (CurrentSculptTool switch
        {
            SculptTool.Raise => "Sculpt raise: each stroke lifts the ground by Amount (1 = one cliff step)",
            SculptTool.Lower => "Sculpt lower: each stroke drops the ground by Amount (1 = one cliff step)",
            SculptTool.Flatten => "Sculpt flatten: levels the brushed area to its own mean height",
            SculptTool.Paint => "Sculpt paint: repaints the brushed corners with the chosen ground tile",
            SculptTool.CliffUp => "Sculpt cliff up: raises the cliff plateau layer by 1",
            SculptTool.CliffDown => "Sculpt cliff down: lowers the cliff plateau layer by 1",
            SculptTool.Water => "Sculpt water: sets water on the brushed corners at level Amount",
            _ => "Sculpt",
        });
    }

    /// <summary>Fills the Paint tile picker from the map's ground tile-type list. Item
    /// order matches that list, so SelectedIndex is exactly the texture index
    /// <see cref="TerrainCommand.Paint"/> takes.</summary>
    private void LoadSculptTiles(Wc3.Model.MapDocument doc)
    {
        var tiles = TerrainEditCommand.GetInfo(doc)?.GroundTiles;
        SculptTileBox.ItemsSource = tiles is null
            ? new List<string>()
            : tiles.Select((id, i) => $"{i}: {id}").ToList();
        SculptTileBox.SelectedIndex = tiles is { Count: > 0 } ? 0 : -1;
    }

    /// <summary>Nearest terrain corner (col, row) to world (wx, wy), in the grid
    /// convention the TerrainCommand brushes take (row-major from the south-west
    /// corner, 128 world units per tile).</summary>
    private (int Col, int Row) WorldToCorner(float wx, float wy)
    {
        const float tileWorld = Wc3.Render.TerrainRenderer.TerrainTransform.TileWorld;
        return ((int)Math.Round((wx - _xform.OriginX) / tileWorld),
                (int)Math.Round((wy - _xform.OriginY) / tileWorld));
    }

    /// <summary>True when world (wx, wy) rounds to the corner the last stroke already
    /// hit, the drag throttle shared by the 2D and GL sculpt paths.</summary>
    private bool SameSculptCorner(float wx, float wy) => _sculptLastCorner == WorldToCorner(wx, wy);

    private static (bool Ok, string Msg, int Changed) AsTuple(TerrainCommand.DeformResult r) =>
        (r.Ok, r.Message, r.TilesChanged);

    private static (bool Ok, string Msg, int Changed) AsTuple(TerrainCommand.PaintResult r) =>
        (r.Ok, r.Message, r.TilesChanged);

    private static string SculptToolLabel(SculptTool tool) => tool switch
    {
        SculptTool.Raise => "raised",
        SculptTool.Lower => "lowered",
        SculptTool.Flatten => "flattened",
        SculptTool.Paint => "painted",
        SculptTool.CliffUp => "cliff-raised",
        SculptTool.CliffDown => "cliff-lowered",
        SculptTool.Water => "watered",
        _ => "sculpted",
    };

    /// <summary>
    /// Applies the active sculpt tool at world (wx, wy): converts to the nearest
    /// terrain corner and runs the matching TerrainCommand area brush with the chosen
    /// radius/amount/tile, then refreshes whichever terrain view is showing and flags
    /// the map dirty (MapEdited enables Save in the host). Strokes bypass EditHistory,
    /// so terrain-sculpt undo is a follow-up.
    /// </summary>
    private void ApplySculptAt(Wc3.Model.MapDocument doc, float wx, float wy)
    {
        var (col, row) = WorldToCorner(wx, wy);
        _sculptLastCorner = (col, row);

        int radius = (int)SculptRadiusSlider.Value;
        var tool = CurrentSculptTool;

        float amount = 0f;
        if (NeedsAmount(tool) && !float.TryParse(
                SculptAmountBox.Text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out amount))
        {
            CaptionText.Text = $"Sculpt: amount '{SculptAmountBox.Text}' is not a number.";
            return;
        }
        if (tool == SculptTool.Paint && SculptTileBox.SelectedIndex < 0)
        {
            CaptionText.Text = "Sculpt: this map has no ground tiles to paint with.";
            return;
        }

        var (ok, msg, changed) = tool switch
        {
            SculptTool.Raise => AsTuple(TerrainCommand.Deform(
                doc, col, row, radius, TerrainCommand.HeightOp.Raise, amount)),
            SculptTool.Lower => AsTuple(TerrainCommand.Deform(
                doc, col, row, radius, TerrainCommand.HeightOp.Lower, amount)),
            SculptTool.Flatten => AsTuple(TerrainCommand.Deform(
                doc, col, row, radius, TerrainCommand.HeightOp.Flatten)),
            SculptTool.Paint => AsTuple(TerrainCommand.Paint(
                doc, col, row, radius, SculptTileBox.SelectedIndex)),
            SculptTool.CliffUp => AsTuple(TerrainCommand.Cliff(
                doc, col, row, radius, TerrainCommand.CliffOp.Raise, 1)),
            SculptTool.CliffDown => AsTuple(TerrainCommand.Cliff(
                doc, col, row, radius, TerrainCommand.CliffOp.Lower, 1)),
            SculptTool.Water => AsTuple(TerrainCommand.Water(
                doc, col, row, radius, TerrainCommand.WaterOp.Set, amount)),
            _ => (false, "unknown sculpt tool", 0),
        };

        if (!ok)
        {
            CaptionText.Text = $"Sculpt failed: {msg}";
            return;
        }

        if (changed > 0)
        {
            if (_is3D)
            {
                GlView.RefreshTerrain();
            }
            else
            {
                RerenderPreservingZoom();  // ApplyZoom rewrote the caption, ours lands below
                _glTerrainStale = true;    // GL catches up when the 3D toggle next shows it
            }
            MapEdited?.Invoke(this, EventArgs.Empty);
        }

        CaptionText.Text =
            $"Sculpt: {SculptToolLabel(tool)} terrain radius {radius} at ({wx:0}, {wy:0}), {changed} corners changed";
    }

    private void ClearImage()
    {
        TerrainImage.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _glDrag = GlDrag.None;
        ClearMarquee();
        _markers.Clear();
        MarkerCanvas?.Children.Clear();
    }

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageText.IsVisible = true;
        ScrollHost.IsVisible = false;
        GlView.IsVisible = false;
        GlInput.IsVisible = false;
        GlOverlay.IsVisible = false;
        FitButton.IsEnabled = false;
        ResetButton.IsEnabled = false;
        Mode3DButton.IsEnabled = false;
        GlButton.IsEnabled = false;
        if (_sculpt)
            ExitSculpt();
        SculptButton.IsEnabled = false;
        CaptionText.Text = "";
    }

    /// <summary>Sizes the Image to the bitmap's pixel size scaled by the current zoom.</summary>
    private void ApplyZoom()
    {
        if (_bitmap is null)
            return;
        TerrainImage.Width = _bitmap.PixelSize.Width * _zoom;
        TerrainImage.Height = _bitmap.PixelSize.Height * _zoom;
        CaptionText.Text = $"{_bitmap.PixelSize.Width}×{_bitmap.PixelSize.Height} px - {_zoom:P0}";
        RedrawMarkers();
    }

    /// <summary>Click on the 2D minimap: with sculpt on, start a terrain brush stroke.
    /// Otherwise map the click to world (x,y) and place the armed brush.</summary>
    private void OnTerrainPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_bitmap is null || _session?.Current is not { } doc)
            return;

        var p = e.GetPosition(ImageHost);
        double srcX = p.X / _zoom, srcY = p.Y / _zoom;
        var (wx, wy) = _xform.PixelToWorld(srcX, srcY);

        // Sculpt mode: a left press starts a brush stroke instead of placing or
        // selecting. Dragging keeps sculpting (OnTerrainMoved) until release.
        if (_sculpt)
        {
            if (!e.GetCurrentPoint(ImageHost).Properties.IsLeftButtonPressed)
                return;
            _sculptDrag2D = true;
            _sculptLastCorner = null;
            e.Pointer.Capture(ImageHost);
            ApplySculptAt(doc, wx, wy);
            e.Handled = true;
            return;
        }

        if (_brush is null)
        {
            CaptionText.Text = "Select a placeable in the Palette tab first.";
            return;
        }

        var (ok, msg) = PlaceViaHistory(doc, wx, wy);

        if (ok)
        {
            _markers.Add((srcX, srcY));
            RedrawMarkers();
            CaptionText.Text = $"Placed {_brush!.Name ?? _brush.Rawcode} at ({wx:0}, {wy:0})";
            MapEdited?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            CaptionText.Text = $"Place failed: {msg}";
        }
        e.Handled = true;
    }

    /// <summary>Continuous 2D sculpting: while a stroke is live, re-apply the brush each
    /// time the pointer crosses into a new terrain corner (a cheap throttle that also
    /// keeps a stationary pointer from stacking repeat hits on one corner).</summary>
    private void OnTerrainMoved(object? sender, PointerEventArgs e)
    {
        if (!_sculptDrag2D || _bitmap is null || _session?.Current is not { } doc)
            return;
        var pt = e.GetCurrentPoint(ImageHost);
        if (!pt.Properties.IsLeftButtonPressed)
        {
            _sculptDrag2D = false; // the release never reached us (capture lost), end the stroke
            return;
        }
        var (wx, wy) = _xform.PixelToWorld(pt.Position.X / _zoom, pt.Position.Y / _zoom);
        if (SameSculptCorner(wx, wy))
            return;
        ApplySculptAt(doc, wx, wy);
        e.Handled = true;
    }

    /// <summary>Ends a live 2D sculpt stroke (no-op for ordinary clicks).</summary>
    private void OnTerrainReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_sculptDrag2D)
            return;
        _sculptDrag2D = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>Applies the armed brush at world (wx,wy) through the undo journal so the
    /// placement can be reverted, honoring the toolbar brush options (random rotation,
    /// scale variance). Returns (ok, message): on failure the message is the placement
    /// diagnostic; on success it is the edit's label. Shared by the 2D and GL click paths
    /// so both get undo (and live GL markers) for free.</summary>
    private (bool ok, string msg) PlaceViaHistory(Wc3.Model.MapDocument doc, float wx, float wy)
    {
        if (_brush is null)
            return (false, "Select a placeable in the Palette tab first.");

        // Brush options: random facing (radians, as the .doo format stores) and a
        // uniform scale jitter of ±variance around 1.0.
        float rotation = RandomRotButton.IsChecked == true
            ? (float)(_rng.NextDouble() * 2.0 * Math.PI)
            : 0f;
        double variance = ScaleVarSlider.Value;
        float scale = variance > 0
            ? (float)(1.0 + (_rng.NextDouble() * 2.0 - 1.0) * variance)
            : 1f;

        try
        {
            // Units belong to the toolbar's "Place as" player (doodads have no owner).
            Wc3.Commands.Editing.IMapEdit edit = _brush.Kind == ObjectKind.Unit
                ? new Wc3.Commands.Editing.PlaceUnitEdit(_brush.Rawcode,
                    ownerId: PlaceOwnerPicker.SelectedOwnerId ?? 0, x: wx, y: wy,
                    rotation: rotation, scale: scale)
                : new Wc3.Commands.Editing.PlaceDoodadEdit(_brush.Rawcode, x: wx, y: wy,
                    rotation: rotation, scale: scale);
            _history.Do(doc, edit);
            GlView.RefreshPlacements(); // GL markers track the live widget set
            return (true, edit.Describe);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e) => Undo();
    private void OnRedoClick(object? sender, RoutedEventArgs e) => Redo();

    /// <summary>Reverts the most recent placement and repaints the live view. No-op if
    /// nothing to undo.</summary>
    public void Undo()
    {
        if (_session?.Current is not { } doc || !_history.Undo(doc))
            return;
        AfterHistoryEdit();
    }

    /// <summary>Re-applies the most recently undone placement and repaints. No-op if nothing to redo.</summary>
    public void Redo()
    {
        if (_session?.Current is not { } doc || !_history.Redo(doc))
            return;
        AfterHistoryEdit();
    }

    /// <summary>Repaints after an undo/redo: the click-breadcrumb dots no longer map to the
    /// changed object set, so clear them, refresh the GL markers, re-render the 2D view when
    /// it is showing, and flag the map dirty.</summary>
    private void AfterHistoryEdit()
    {
        _markers.Clear();
        MarkerCanvas?.Children.Clear();
        GlView.RefreshPlacements();
        if (!_is3D)
            RenderCurrent();
        MapEdited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps the Undo/Redo buttons' enablement in sync with the journal depth.</summary>
    private void OnHistoryChanged()
    {
        if (UndoButton is not null) UndoButton.IsEnabled = _history.CanUndo;
        if (RedoButton is not null) RedoButton.IsEnabled = _history.CanRedo;
    }

    /// <summary>The "3D" toggle shows the GL viewport (the GPU renderer replaced the old
    /// CPU perspective render for interactive navigation). Kept in sync with "GL".</summary>
    private void OnToggle3D(object? sender, RoutedEventArgs e)
    {
        bool show = Mode3DButton.IsChecked == true;
        GlButton.IsChecked = show; // the two toggles alias the same viewport
        ShowGlViewport(show);
    }

    /// <summary>"GL" is an alias for the 3D toggle (kept for muscle memory).</summary>
    private void OnToggleGl(object? sender, RoutedEventArgs e)
    {
        bool show = GlButton.IsChecked == true;
        Mode3DButton.IsChecked = show;
        ShowGlViewport(show);
    }

    /// <summary>Swaps the GL viewport in front of (or back behind) the 2D minimap. The GL
    /// camera keeps its state across toggles; leaving 3D re-renders the minimap.</summary>
    private void ShowGlViewport(bool show)
    {
        _is3D = show;
        GlView.IsVisible = show;
        GlInput.IsVisible = show;
        GlOverlay.IsVisible = show;
        ScrollHost.IsVisible = !show;
        _glDrag = GlDrag.None;
        ClearMarquee();
        if (show)
        {
            if (_glTerrainStale)
            {
                _glTerrainStale = false;
                GlView.RefreshTerrain(); // sculpted while hidden: rebuilds mesh, heights, and placements
            }
            else
            {
                GlView.RefreshPlacements(); // pick up placements made while the viewport was hidden
            }
            GlInput.Focus();            // so arrow keys reach OnGlInputKeyDown
            CaptionText.Text = "3D · right-drag orbit, middle-drag pan, wheel zoom, arrows pan, Home reset · left-click selects (Shift toggles, drag box-selects, Delete removes) or places the armed brush";
        }
        else
        {
            RenderCurrent();
        }
    }

    // --- GL viewport input (via the transparent GlInput catcher) --------------
    // OpenGlControlBase can't receive pointer input, so GlInput catches it here.
    // Camera (all modes): RIGHT-drag orbits, MIDDLE-drag pans, wheel zooms, arrow
    // keys pan, PageUp/Down zoom, Home resets. LEFT belongs to the tools: with a
    // brush armed a left click places; in pointer mode a left click selects (Shift
    // toggles) and a left DRAG sweeps a marquee box (Shift adds to the selection).
    // With sculpt mode on, a left press or drag runs the terrain brush instead.
    // A press that never moves more than a few pixels counts as a click.
    private enum GlDrag { None, Orbit, Pan, Marquee, PlaceClick, Sculpt, Move, Rotate }
    private GlDrag _glDrag;
    private bool _glDragMoved;
    private bool _glShift; // Shift state at press: additive marquee / click-toggle
    private bool _glAlt;   // Alt state at press: a widget drag rotates instead of moving
    private Avalonia.Point _glLast;
    private Avalonia.Point _glPressPos;
    private readonly List<int> _selection = new();
    private Rectangle? _marqueeRect;
    // The widget grabbed by a Move/Rotate drag, and the terrain point the drag started
    // at (for moving a multi-unit selection by the same delta).
    private PlacementPick? _dragTarget;
    private (float X, float Y)? _dragStartWorld;

    private void OnGlInputPressed(object? sender, PointerPressedEventArgs e)
    {
        GlInput.Focus();
        var p = e.GetCurrentPoint(GlInput);
        _glLast = p.Position;
        _glPressPos = p.Position;
        _glDragMoved = false;
        _glShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _glAlt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        _dragTarget = null;
        _dragStartWorld = null;
        if (p.Properties.IsRightButtonPressed)
            _glDrag = GlDrag.Orbit;      // a right CLICK (no drag) still cancels the brush
        else if (p.Properties.IsMiddleButtonPressed)
            _glDrag = GlDrag.Pan;
        else if (_sculpt && p.Properties.IsLeftButtonPressed)
        {
            // Sculpt stroke: apply at the press point now, then keep applying as the
            // drag crosses new corners (OnGlInputMoved). Camera stays on right/middle.
            _glDrag = GlDrag.Sculpt;
            _sculptLastCorner = null;
            var (hit, wx, wy) = GlView.PickGround(p.Position.X, p.Position.Y);
            if (hit && _session?.Current is { } doc)
                ApplySculptAt(doc, wx, wy);
        }
        else if (_brush is null)
        {
            // Pointer mode. If the press lands on a placed widget, grab it: a drag then
            // MOVES it (Alt = ROTATE), and a plain click (no drag) selects it. On empty
            // ground a drag sweeps a marquee box. Click vs drag is decided on release.
            var picked = GlView.PickPlacement(p.Position.X, p.Position.Y);
            if (picked is { } hit)
            {
                _dragTarget = hit;
                _glDrag = _glAlt ? GlDrag.Rotate : GlDrag.Move;
                var (g, sx, sy) = GlView.PickGround(p.Position.X, p.Position.Y);
                _dragStartWorld = g ? (sx, sy) : null;
            }
            else
                _glDrag = GlDrag.Marquee;
        }
        else
            _glDrag = GlDrag.PlaceClick; // brush armed: click places, a drag does nothing
        e.Pointer.Capture(GlInput);
        e.Handled = true;
    }

    private void OnGlInputMoved(object? sender, PointerEventArgs e)
    {
        if (_glDrag == GlDrag.None)
            return;
        var pos = e.GetPosition(GlInput);
        double dx = pos.X - _glLast.X, dy = pos.Y - _glLast.Y;
        _glLast = pos;
        // A few pixels of jitter still counts as a click, not a drag.
        if (!_glDragMoved &&
            Math.Abs(pos.X - _glPressPos.X) + Math.Abs(pos.Y - _glPressPos.Y) > 3)
            _glDragMoved = true;
        switch (_glDrag)
        {
            case GlDrag.Orbit:
                GlView.DriveOrbit(dx, dy);
                break;
            case GlDrag.Pan:
                GlView.DrivePan(dx, dy);
                break;
            case GlDrag.Marquee:
                if (_glDragMoved)
                    UpdateMarquee(_glPressPos, pos);
                break;
            case GlDrag.Sculpt:
            {
                // Continuous sculpt: re-apply when the drag crosses into a new corner.
                var (hit, wx, wy) = GlView.PickGround(pos.X, pos.Y);
                if (hit && !SameSculptCorner(wx, wy) && _session?.Current is { } doc)
                    ApplySculptAt(doc, wx, wy);
                break;
            }
        }
        e.Handled = true;
    }

    private void OnGlInputReleased(object? sender, PointerReleasedEventArgs e)
    {
        var kind = _glDrag;
        bool moved = _glDragMoved;
        _glDrag = GlDrag.None;
        ClearMarquee();
        e.Pointer.Capture(null);
        e.Handled = true;

        if (kind == GlDrag.None || _session?.Current is not { } doc)
            return;

        // Sculpt strokes apply on press and on drag, the release itself is a no-op.
        if (kind == GlDrag.Sculpt)
            return;

        var p = e.GetPosition(GlInput);

        // Marquee drag finished: select every unit inside the box (Shift ADDS to the
        // current selection instead of replacing it).
        if (kind == GlDrag.Marquee && moved)
        {
            var hits = GlView.PickUnitsInRect(_glPressPos.X, _glPressPos.Y, p.X, p.Y);
            if (!_glShift)
                _selection.Clear();
            foreach (var cn in hits)
                if (!_selection.Contains(cn))
                    _selection.Add(cn);
            PushSelection(_selection.Count == 0
                ? "No units in the box · selection cleared."
                : $"{_selection.Count} unit(s) selected.");
            return;
        }

        // Move/Rotate drag finished: apply to the grabbed widget (a Move also drags the
        // rest of a multi-unit selection by the same delta). Applied on release with one
        // scene rebuild, a live per-frame preview is a follow-up.
        if ((kind == GlDrag.Move || kind == GlDrag.Rotate) && moved && _dragTarget is { } tgt)
        {
            ApplyDragEdit(doc, kind, tgt, p);
            return;
        }

        if (moved)
            return; // camera drag: no click action

        // Right CLICK cancels the armed tool (WC3-style); middle click is camera only.
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            if (_brush is not null)
                ClearPlacementBrush("Placement cancelled · pointer mode, click a unit to select.");
            else if (_sculpt)
                ExitSculpt("Sculpt off · pointer mode, click a unit to select.");
            return;
        }
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        // Brush armed, plain left click: ray-pick the ground and place.
        if (_brush is not null)
        {
            var (ok, wx, wy) = GlView.PickGround(p.X, p.Y);
            if (!ok)
            {
                CaptionText.Text = "That click missed the ground · aim at the terrain to place.";
                return;
            }
            var (placed, msg) = PlaceViaHistory(doc, wx, wy);
            CaptionText.Text = placed
                ? $"Placed {_brush!.Name ?? _brush.Rawcode} at ({wx:0}, {wy:0})"
                : $"Place failed: {msg}";
            if (placed)
                MapEdited?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Pointer-mode left click (no drag): select the widget under the cursor.
        SelectAtClick(GlView.PickPlacement(p.X, p.Y));
    }

    /// <summary>The pointer-mode click selection: a unit joins (Shift toggles) the unit
    /// selection, a doodad becomes THE selected doodad, empty space clears.</summary>
    private void SelectAtClick(PlacementPick? picked)
    {
        if (_glShift)
        {
            if (picked is { IsUnit: true } hit)
            {
                if (!_selection.Remove(hit.CreationNumber))
                    _selection.Add(hit.CreationNumber);
                PushSelection($"{_selection.Count} unit(s) selected.");
            }
            return; // Shift-click on a doodad or empty space keeps the selection.
        }
        if (picked is { IsUnit: true } unitHit)
        {
            _selection.Clear();
            _selection.Add(unitHit.CreationNumber);
            PushSelection($"Selected unit #{unitHit.CreationNumber}");
        }
        else if (picked is { IsUnit: false } doodadHit)
        {
            _selection.Clear();
            GlView.SetSelectedUnits(_selection);
            UnitsSelected?.Invoke(Array.Empty<int>());
            GlView.SetSelectedDoodad(doodadHit.CreationNumber);
            CaptionText.Text = $"Selected doodad #{doodadHit.CreationNumber}";
            DoodadSelected?.Invoke(doodadHit.CreationNumber);
        }
        else
        {
            _selection.Clear();
            PushSelection("Nothing under the cursor · selection cleared.");
        }
    }

    /// <summary>Applies a finished Move or Rotate drag to the grabbed widget. Move sends
    /// it (and the rest of a multi-unit selection) to the release terrain point by delta,
    /// Rotate faces it toward the release point. Rebuilds the scene once, re-highlights.</summary>
    private void ApplyDragEdit(Wc3.Model.MapDocument doc, GlDrag kind, PlacementPick tgt, Avalonia.Point p)
    {
        var (g, wx, wy) = GlView.PickGround(p.X, p.Y);
        if (!g)
        {
            CaptionText.Text = "Drag ended off the terrain, nothing moved.";
            return;
        }

        if (kind == GlDrag.Move)
        {
            if (tgt.IsUnit)
            {
                if (_dragStartWorld is { } s && _selection.Count > 0 && _selection.Contains(tgt.CreationNumber))
                {
                    float ddx = wx - s.X, ddy = wy - s.Y;
                    foreach (var cn in _selection)
                        if (UnitInstanceCommand.Get(doc, cn) is { } info)
                            UnitInstanceCommand.SetPosition(doc, cn, info.X + ddx, info.Y + ddy);
                }
                else
                {
                    UnitInstanceCommand.SetPosition(doc, tgt.CreationNumber, wx, wy);
                }
                CaptionText.Text = $"Moved unit #{tgt.CreationNumber} to ({wx:0}, {wy:0})";
            }
            else
            {
                float z = DoodadInstanceCommand.Get(doc, tgt.CreationNumber)?.Z ?? 0f;
                DoodadInstanceCommand.SetPosition(doc, tgt.CreationNumber, wx, wy, z);
                CaptionText.Text = $"Moved doodad #{tgt.CreationNumber} to ({wx:0}, {wy:0})";
            }
        }
        else // Rotate: face the widget toward the release point.
        {
            float ox, oy;
            if (tgt.IsUnit)
            {
                if (UnitInstanceCommand.Get(doc, tgt.CreationNumber) is not { } info) return;
                ox = info.X; oy = info.Y;
            }
            else
            {
                if (DoodadInstanceCommand.Get(doc, tgt.CreationNumber) is not { } info) return;
                ox = info.X; oy = info.Y;
            }
            float angle = MathF.Atan2(wy - oy, wx - ox);
            if (tgt.IsUnit)
                UnitInstanceCommand.SetFacing(doc, tgt.CreationNumber, angle);
            else
                DoodadInstanceCommand.SetRotation(doc, tgt.CreationNumber, angle);
            CaptionText.Text =
                $"Rotated {(tgt.IsUnit ? "unit" : "doodad")} #{tgt.CreationNumber} to {angle * 180.0 / Math.PI:0} deg";
        }

        RefreshPlacements();
        if (tgt.IsUnit)
            GlView.SetSelectedUnits(_selection.Count > 0 ? _selection : new List<int> { tgt.CreationNumber });
        else
            GlView.SetSelectedDoodad(tgt.CreationNumber);
        MapEdited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Capture can vanish mid-drag (alt-tab, window deactivation); drop the drag
    /// so the next pointer move doesn't keep orbiting without a button held.</summary>
    private void OnGlInputCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _glDrag = GlDrag.None;
        ClearMarquee();
    }

    /// <summary>Pushes the internal selection into the GL highlight, reports it on the
    /// caption line, and raises <see cref="UnitsSelected"/> with a snapshot. Any unit
    /// selection change (including a clear) also drops the doodad highlight, so the
    /// viewport never shows both kinds selected at once.</summary>
    private void PushSelection(string caption)
    {
        GlView.SetSelectedUnits(_selection);
        GlView.SetSelectedDoodad(null);
        CaptionText.Text = caption;
        UnitsSelected?.Invoke(_selection.ToArray());
    }

    /// <summary>Positions (creating on first use) the translucent marquee rectangle in
    /// the overlay canvas, spanning the two drag corners.</summary>
    private void UpdateMarquee(Avalonia.Point a, Avalonia.Point b)
    {
        if (_marqueeRect is null)
        {
            _marqueeRect = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(48, 90, 170, 255)),
                Stroke = new SolidColorBrush(Color.FromArgb(220, 120, 190, 255)),
                StrokeThickness = 1,
            };
            GlOverlay.Children.Add(_marqueeRect);
        }
        _marqueeRect.Width = Math.Abs(a.X - b.X);
        _marqueeRect.Height = Math.Abs(a.Y - b.Y);
        Canvas.SetLeft(_marqueeRect, Math.Min(a.X, b.X));
        Canvas.SetTop(_marqueeRect, Math.Min(a.Y, b.Y));
    }

    /// <summary>Removes the marquee rectangle from the overlay (drag ended/aborted).</summary>
    private void ClearMarquee()
    {
        if (_marqueeRect is null)
            return;
        GlOverlay.Children.Remove(_marqueeRect);
        _marqueeRect = null;
    }

    private void OnGlInputWheel(object? sender, PointerWheelEventArgs e)
    {
        GlView.DriveZoom(e.Delta.Y);
        e.Handled = true;
    }

    private void OnGlInputKeyDown(object? sender, KeyEventArgs e)
    {
        float amt = GlView.PanStep;
        bool handled = true;
        switch (e.Key)
        {
            case Key.Left:     GlView.DrivePanWorld(-amt, 0f); break;
            case Key.Right:    GlView.DrivePanWorld(amt, 0f); break;
            case Key.Up:       GlView.DrivePanWorld(0f, amt); break;
            case Key.Down:     GlView.DrivePanWorld(0f, -amt); break;
            case Key.PageUp:   GlView.DriveZoom(1); break;
            case Key.PageDown: GlView.DriveZoom(-1); break;
            case Key.Home:     GlView.DriveReset(); break;
            case Key.Delete:
            case Key.Back:
                // Pointer mode with a selection: ask the host to remove those units
                // (it owns the command call + refresh + selection clear).
                if (_brush is null && _selection.Count > 0)
                    RemoveUnitsRequested?.Invoke(_selection.ToArray());
                break;
            case Key.Escape:
                // First Esc cancels the placement tool (or sculpt mode). With neither
                // active it clears the unit selection.
                if (_brush is not null)
                    ClearPlacementBrush("Placement cancelled · pointer mode — click a unit to select.");
                else if (_sculpt)
                    ExitSculpt("Sculpt off · pointer mode, click a unit to select.");
                else
                {
                    _selection.Clear();
                    PushSelection("Selection cleared.");
                }
                break;
        }
        e.Handled = handled;
    }

    /// <summary>Redraws placement markers in the overlay at the current zoom.</summary>
    private void RedrawMarkers()
    {
        if (MarkerCanvas is null)
            return;
        MarkerCanvas.Children.Clear();
        const double r = 4;
        var fill = new SolidColorBrush(Color.FromArgb(220, 255, 80, 80));
        foreach (var (mx, my) in _markers)
        {
            var dot = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Fill = fill,
                Stroke = Brushes.White,
                StrokeThickness = 1,
            };
            Canvas.SetLeft(dot, mx * _zoom - r);
            Canvas.SetTop(dot, my * _zoom - r);
            MarkerCanvas.Children.Add(dot);
        }
    }

    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_bitmap is null)
            return;

        var factor = e.Delta.Y > 0 ? 1.1 : e.Delta.Y < 0 ? 0.9 : 1.0;
        if (factor == 1.0)
            return;
        var cursor = e.GetPosition(ScrollHost);
        var (offsetX, offsetY, zoom) = Wc3.Commands.ZoomMath.ZoomAtPoint(
            ScrollHost.Offset.X, ScrollHost.Offset.Y, _zoom,
            cursor.X, cursor.Y, factor, MinZoom, MaxZoom);
        _zoom = zoom;
        ApplyZoom();
        // The Image was just resized; run layout so the ScrollViewer's extent grows
        // before the new offset is coerced against it.
        ScrollHost.UpdateLayout();
        ScrollHost.Offset = new Vector(offsetX, offsetY);
        e.Handled = true;
    }

    private void OnFitClick(object? sender, RoutedEventArgs e)
    {
        if (_is3D)
        {
            GlView.DriveReset();
            return;
        }
        if (_bitmap is null)
            return;
        var vw = ScrollHost.Viewport.Width;
        var vh = ScrollHost.Viewport.Height;
        if (vw <= 0 || vh <= 0)
        {
            vw = ScrollHost.Bounds.Width;
            vh = ScrollHost.Bounds.Height;
        }
        if (vw <= 0 || vh <= 0)
            return;
        _zoom = Math.Clamp(
            Math.Min(vw / _bitmap.PixelSize.Width, vh / _bitmap.PixelSize.Height),
            MinZoom, MaxZoom);
        ApplyZoom();
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (_is3D)
        {
            GlView.DriveReset();
            return;
        }
        if (_bitmap is null)
            return;
        _zoom = 1.0;
        ApplyZoom();
    }
}
