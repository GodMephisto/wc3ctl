// src/Wc3.Render/TerrainRenderer.cs
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using War3Net.Build.Environment;

namespace Wc3.Render;

public static class TerrainRenderer
{
    // Keep output viewable without being huge: integer nearest-neighbor upscale
    // until the longer side would exceed this.
    private const int MaxSide = 1024;

    /// <summary>
    /// Renders a top-down terrain heightmap of the map and returns it as PNG bytes.
    /// Grayscale: darkest = lowest ground, lightest = highest. North is up
    /// (war3map.w3e stores tilepoint rows south-to-north, so rows are flipped).
    /// The caller (CLI) is responsible for writing the bytes to disk.
    /// </summary>
    public static byte[] RenderTerrainPng(Wc3.Model.MapDocument doc)
    {
        if (doc.GetFile("war3map.w3e")?.Model is not MapEnvironment env)
            throw new InvalidDataException("no terrain (war3map.w3e) in map");

        // Width/Height count tiles; the tilepoint grid has one more point per axis.
        int w = (int)env.Width + 1;
        int h = (int)env.Height + 1;
        var tiles = env.TerrainTiles;
        if (w <= 0 || h <= 0 || tiles.Count != w * h)
            throw new InvalidDataException($"terrain grid {w}x{h} does not match {tiles.Count} tilepoints");

        // TerrainTile.Height excludes cliff level; one cliff step equals 1.0 in
        // War3Net's normalized height units (512 raw = 128 world units).
        var elevation = new float[tiles.Count];
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < tiles.Count; i++)
        {
            float e = tiles[i].Height + tiles[i].CliffLevel;
            elevation[i] = e;
            if (e < min) min = e;
            if (e > max) max = e;
        }
        float range = max - min;

        int scale = Math.Max(1, MaxSide / Math.Max(w, h));
        using var image = new Image<Rgba32>(w * scale, h * scale);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Tilepoints are stored row-major from the south-west corner;
                // flip vertically so north ends up at the top of the image.
                float e = elevation[y * w + x];
                byte v = range > 0 ? (byte)Math.Clamp((e - min) / range * 255f, 0f, 255f) : (byte)128;
                var pixel = new Rgba32(v, v, v);

                int px = x * scale, py = (h - 1 - y) * scale;
                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        image[px + dx, py + dy] = pixel;
            }
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}
