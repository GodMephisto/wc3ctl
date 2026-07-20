using System;
using System.Collections.Generic;
using System.IO;
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

    // 3D orbit-camera state (used when _is3D). Yaw spins around Z, pitch tilts down,
    // camZoom scales camera distance. Left-drag rotates, wheel zooms.
    private bool _is3D;
    private float _yaw = 45f, _pitch = 30f, _camZoom = 1f;
    private bool _dragging;
    private bool _dragMoved;
    private Avalonia.Point _dragOrigin;
    private float _dragYaw0, _dragPitch0;
    private const int View3DWidth = 1024, View3DHeight = 720;

    private MapSession? _session;
    private PaletteRow? _brush;
    private Wc3.Render.TerrainRenderer.TerrainTransform _xform;
    private readonly List<(double X, double Y)> _markers = new();
    private readonly Wc3.Commands.Editing.EditHistory _history = new();

    /// <summary>Raised after a click-to-place (or an undo/redo) mutates the in-memory map.</summary>
    public event EventHandler? MapEdited;

    public TerrainView()
    {
        InitializeComponent();
        // Tunnel so zooming wins over the ScrollViewer's own wheel scrolling.
        ScrollHost.AddHandler(PointerWheelChangedEvent, OnPointerWheel, RoutingStrategies.Tunnel);
        _history.Changed += OnHistoryChanged;
    }

    public void ShowMap(MapSession session)
    {
        _session = session;
        ClearImage();
        _history.Clear(); // the previous map's undo stack no longer applies

        if (session.Current is null)
        {
            ShowMessage("No map loaded.");
            return;
        }

        try
        {
            _xform = Wc3.Render.TerrainRenderer.GetTransform(session.Current);
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
        RenderCurrent();
    }

    /// <summary>Renders the terrain in the active mode: flat 2D minimap or 3D perspective
    /// ("in-game" look). <paramref name="draft"/> renders at half resolution for responsive
    /// camera drags; the Image control upscales it until a crisp full render lands.</summary>
    private void RenderCurrent(bool draft = false)
    {
        if (_session?.Current is not { } doc)
            return;
        try
        {
            byte[] png;
            if (_is3D)
            {
                int vw = draft ? View3DWidth / 2 : View3DWidth;
                int vh = draft ? View3DHeight / 2 : View3DHeight;
                png = Wc3.Render.TerrainRenderer.RenderPerspectivePng(doc, vw, vh, _yaw, _pitch, _camZoom);
            }
            else
            {
                png = Wc3.Commands.RenderCommand.Execute(doc);
            }
            var old = _bitmap;
            _bitmap = new Bitmap(new MemoryStream(png));
            TerrainImage.Source = _bitmap;
            old?.Dispose();
        }
        catch (Exception ex)
        {
            ShowMessage($"Terrain cannot be rendered: {ex.Message}");
            return;
        }

        if (_is3D)
        {
            // Fixed display size; the orbit camera (drag/wheel) replaces 2D pan/zoom.
            TerrainImage.Width = View3DWidth;
            TerrainImage.Height = View3DHeight;
            MarkerCanvas?.Children.Clear();
            CaptionText.Text = $"3D · yaw {_yaw:0}° pitch {_pitch:0}° zoom {_camZoom:0.0}× · drag to rotate, wheel to zoom";
        }
        else
        {
            _zoom = 1.0;
            ApplyZoom();
        }
    }

    /// <summary>Arms (or clears) the placeable that a map click will place. Called by
    /// the workspace host when the Palette selection changes.</summary>
    public void SetPlacementBrush(PaletteRow? row)
    {
        _brush = row;
        BrushText.Text = row is null
            ? ""
            : $"● Placing: {row.Display} — click the map to place";
    }

    private void ClearImage()
    {
        TerrainImage.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _dragging = false;
        _markers.Clear();
        MarkerCanvas?.Children.Clear();
    }

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageText.IsVisible = true;
        ScrollHost.IsVisible = false;
        FitButton.IsEnabled = false;
        ResetButton.IsEnabled = false;
        Mode3DButton.IsEnabled = false;
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

    /// <summary>Click-to-place: map the click to world (x,y) and add a unit/doodad.</summary>
    private void OnTerrainPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_bitmap is null || _session?.Current is not { } doc)
            return;

        // In 3D mode a press begins an orbit-camera drag instead of placing.
        if (_is3D)
        {
            _dragging = true;
            _dragMoved = false;
            _dragOrigin = e.GetPosition(ImageHost);
            _dragYaw0 = _yaw;
            _dragPitch0 = _pitch;
            e.Pointer.Capture(ImageHost);
            e.Handled = true;
            return;
        }

        if (_brush is null)
        {
            CaptionText.Text = "Select a placeable in the Palette tab first.";
            return;
        }

        var p = e.GetPosition(ImageHost);
        double srcX = p.X / _zoom, srcY = p.Y / _zoom;
        var (wx, wy) = _xform.PixelToWorld(srcX, srcY);

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

    /// <summary>Continues an orbit-camera drag: horizontal spins yaw, vertical tilts pitch.
    /// Renders at draft resolution while dragging so it stays responsive on big maps.</summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_is3D || !_dragging || _bitmap is null)
            return;
        var p = e.GetPosition(ImageHost);
        float dx = (float)(p.X - _dragOrigin.X);
        float dy = (float)(p.Y - _dragOrigin.Y);
        float yaw = _dragYaw0 - dx * 0.4f;
        float pitch = Math.Clamp(_dragPitch0 + dy * 0.4f, 5f, 89f);
        // Skip micro-moves so we don't thrash the software renderer.
        if (MathF.Abs(yaw - _yaw) < 0.8f && MathF.Abs(pitch - _pitch) < 0.8f)
            return;
        _yaw = yaw;
        _pitch = pitch;
        _dragMoved = true;
        RenderCurrent(draft: true);
        e.Handled = true;
    }

    /// <summary>Ends an orbit-camera drag and lands a crisp full-resolution render.</summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        // A press that didn't rotate is a click: ray-pick the terrain and place.
        if (_is3D && !_dragMoved)
        {
            if (_brush is null)
            {
                CaptionText.Text = "Select a placeable in the Palette tab first.";
                return;
            }
            if (_session?.Current is { } doc)
                Place3D(doc, e.GetPosition(ImageHost));
            return;
        }

        // Otherwise it was a camera drag — land a crisp full-resolution frame.
        RenderCurrent();
    }

    /// <summary>Ray-picks the terrain under the 3D click and places the armed brush there.</summary>
    private void Place3D(Wc3.Model.MapDocument doc, Avalonia.Point imagePos)
    {
        var (ok, wx, wy) = Wc3.Render.TerrainRenderer.PickTerrain(
            doc, View3DWidth, View3DHeight, _yaw, _pitch, _camZoom,
            (float)imagePos.X, (float)imagePos.Y);
        if (!ok)
        {
            CaptionText.Text = "That click missed the ground — aim at the terrain to place.";
            return;
        }

        var (placed, msg) = PlaceViaHistory(doc, wx, wy);

        if (placed)
        {
            CaptionText.Text = $"Placed {_brush!.Name ?? _brush.Rawcode} at ({wx:0}, {wy:0})";
            DrawMarkerAt(imagePos.X, imagePos.Y);
            MapEdited?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            CaptionText.Text = $"Place failed: {msg}";
        }
    }

    /// <summary>Applies the armed brush at world (wx,wy) through the undo journal so the
    /// placement can be reverted. Returns (ok, message): on failure the message is the
    /// placement diagnostic; on success it is the edit's label. Shared by the 2D and 3D
    /// click paths so both get undo for free.</summary>
    private (bool ok, string msg) PlaceViaHistory(Wc3.Model.MapDocument doc, float wx, float wy)
    {
        if (_brush is null)
            return (false, "Select a placeable in the Palette tab first.");
        try
        {
            Wc3.Commands.Editing.IMapEdit edit = _brush.Kind == ObjectKind.Unit
                ? new Wc3.Commands.Editing.PlaceUnitEdit(_brush.Rawcode, ownerId: 0, x: wx, y: wy)
                : new Wc3.Commands.Editing.PlaceDoodadEdit(_brush.Rawcode, x: wx, y: wy);
            _history.Do(doc, edit);
            return (true, edit.Describe);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e) => Undo();
    private void OnRedoClick(object? sender, RoutedEventArgs e) => Redo();

    /// <summary>Reverts the most recent placement and repaints the live view (3D models
    /// re-render WYSIWYG; the 2D minimap refreshes). No-op if nothing to undo.</summary>
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
    /// changed object set, so clear them, re-render the live view, and flag the map dirty.</summary>
    private void AfterHistoryEdit()
    {
        _markers.Clear();
        MarkerCanvas?.Children.Clear();
        RenderCurrent();
        MapEdited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps the Undo/Redo buttons' enablement in sync with the journal depth.</summary>
    private void OnHistoryChanged()
    {
        if (UndoButton is not null) UndoButton.IsEnabled = _history.CanUndo;
        if (RedoButton is not null) RedoButton.IsEnabled = _history.CanRedo;
    }

    /// <summary>Drops a transient placement dot at a pixel (cleared on the next render).</summary>
    private void DrawMarkerAt(double x, double y)
    {
        if (MarkerCanvas is null)
            return;
        const double r = 4;
        var dot = new Ellipse
        {
            Width = r * 2,
            Height = r * 2,
            Fill = new SolidColorBrush(Color.FromArgb(220, 255, 80, 80)),
            Stroke = Brushes.White,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(dot, x - r);
        Canvas.SetTop(dot, y - r);
        MarkerCanvas.Children.Add(dot);
    }

    /// <summary>Toggles between the flat minimap (2D, click-to-place) and the 3D in-game view.</summary>
    private void OnToggle3D(object? sender, RoutedEventArgs e)
    {
        _is3D = Mode3DButton.IsChecked == true;
        if (_is3D)
            ResetCamera();
        else
            RenderCurrent();
    }

    /// <summary>Resets the orbit camera to its default framing and re-renders.</summary>
    private void ResetCamera()
    {
        _yaw = 45f;
        _pitch = 30f;
        _camZoom = 1f;
        RenderCurrent();
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

        // In 3D mode the wheel dollies the orbit camera in/out.
        if (_is3D)
        {
            var f = e.Delta.Y > 0 ? 1.1f : e.Delta.Y < 0 ? 0.9f : 1f;
            if (f == 1f)
                return;
            _camZoom = Math.Clamp(_camZoom * f, 0.2f, 6f);
            RenderCurrent();
            e.Handled = true;
            return;
        }

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
        if (_bitmap is null)
            return;
        if (_is3D)
        {
            ResetCamera();
            return;
        }
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
        if (_bitmap is null)
            return;
        if (_is3D)
        {
            ResetCamera();
            return;
        }
        _zoom = 1.0;
        ApplyZoom();
    }
}
