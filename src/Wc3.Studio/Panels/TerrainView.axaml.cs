using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace Wc3.Studio.Panels;

public partial class TerrainView : UserControl, IMapPanel
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 10.0;

    private Bitmap? _bitmap;
    private double _zoom = 1.0;

    public TerrainView()
    {
        InitializeComponent();
        // Tunnel so zooming wins over the ScrollViewer's own wheel scrolling.
        ScrollHost.AddHandler(PointerWheelChangedEvent, OnPointerWheel, RoutingStrategies.Tunnel);
    }

    public void ShowMap(MapSession session)
    {
        ClearImage();

        if (session.Current is null)
        {
            ShowMessage("No map loaded.");
            return;
        }

        try
        {
            var png = Wc3.Commands.RenderCommand.Execute(session.Current);
            _bitmap = new Bitmap(new MemoryStream(png));
        }
        catch (Exception ex)
        {
            ShowMessage($"Terrain cannot be rendered: {ex.Message}");
            return;
        }

        TerrainImage.Source = _bitmap;
        _zoom = 1.0;
        ApplyZoom();

        MessageText.IsVisible = false;
        ScrollHost.IsVisible = true;
        FitButton.IsEnabled = true;
        ResetButton.IsEnabled = true;
    }

    private void ClearImage()
    {
        TerrainImage.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageText.IsVisible = true;
        ScrollHost.IsVisible = false;
        FitButton.IsEnabled = false;
        ResetButton.IsEnabled = false;
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
        if (_bitmap is null)
            return;
        _zoom = 1.0;
        ApplyZoom();
    }
}
