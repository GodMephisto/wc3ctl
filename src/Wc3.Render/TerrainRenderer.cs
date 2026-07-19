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
    /// Renders a top-down colored terrain map and returns it as PNG bytes.
    /// Each tilepoint is colored by its ground tile type (grass/dirt/rock/…),
    /// shaded by elevation, outlined at cliff steps, and tinted for water and
    /// blight. North is up (war3map.w3e stores tilepoint rows south-to-north,
    /// so rows are flipped). The caller (CLI) writes the bytes to disk.
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

        // Precompute one base color per ground tile type so we only parse each
        // enum name once (a map has at most a handful of ground types). Blank
        // maps have an empty TerrainTypes list, so fall back to a neutral color.
        var typeColors = new Rgba32[env.TerrainTypes.Count];
        for (int i = 0; i < typeColors.Length; i++)
            typeColors[i] = ColorForTerrainType(env.TerrainTypes[i]);
        var fallback = new Rgba32(96, 108, 84); // muted grass-green

        // TerrainTile.Height excludes cliff level; one cliff step equals 1.0 in
        // War3Net's normalized height units. Combine them for elevation shading.
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
                int idx = y * w + x;
                var tile = tiles[idx];

                // Base color from the tile's ground type.
                Rgba32 c;
                if (typeColors.Length > 0 && tile.Texture >= 0 && tile.Texture < typeColors.Length)
                    c = typeColors[tile.Texture];
                else
                    c = fallback;

                // Elevation shading: darker in valleys, brighter on peaks.
                float n = range > 0 ? (elevation[idx] - min) / range : 0.5f;
                float shade = 0.65f + 0.55f * n;

                // Cliff outline: darken a tile whose east/south neighbor sits at a
                // different cliff level, so cliff faces read as crisp edges.
                int cl = tile.CliffLevel;
                bool edge =
                    (x + 1 < w && tiles[idx + 1].CliffLevel != cl) ||
                    (y + 1 < h && tiles[idx + w].CliffLevel != cl);
                if (edge) shade *= 0.72f;

                c = Scale(c, shade);

                // Water: blend toward blue; deeper ground under the water is darker.
                if (tile.IsWater)
                {
                    var deep = new Rgba32(28, 52, 104);
                    var shallow = new Rgba32(70, 116, 176);
                    var water = Lerp(deep, shallow, n);
                    c = Lerp(c, water, 0.68f);
                }

                // Blight: cracked reddish earth overlay.
                if (tile.IsBlighted)
                    c = Lerp(c, new Rgba32(104, 66, 54), 0.7f);

                // Tilepoints are stored row-major from the south-west corner;
                // flip vertically so north ends up at the top of the image.
                int px = x * scale, py = (h - 1 - y) * scale;
                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        image[px + dx, py + dy] = c;
            }
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Maps a ground <see cref="TerrainType"/> to a representative color by
    /// inspecting its enum name (e.g. "L_GrassCliff" → green). Names carry a
    /// tileset-letter prefix and a descriptive suffix; we match the suffix.
    /// </summary>
    private static Rgba32 ColorForTerrainType(TerrainType type)
    {
        string name = type.ToString();
        bool Has(string s) => name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;

        // Order matters: check more specific terms before generic ones.
        if (Has("Lava")) return new Rgba32(150, 52, 30);
        if (Has("Abyss")) return new Rgba32(26, 22, 32);
        if (Has("Snow") || Has("Ice")) return new Rgba32(228, 232, 242);
        if (Has("Water") || Has("Shore")) return new Rgba32(60, 96, 156);
        if (Has("Sand") || Has("Desert") || Has("Dune") || Has("Waste"))
            return new Rgba32(198, 176, 120);
        if (Has("Rock") || Has("Stone")) return new Rgba32(112, 110, 106);
        if (Has("Brick") || Has("Tiles") || Has("Path") || Has("Cobble") || Has("Flag"))
            return new Rgba32(150, 140, 122);
        if (Has("Mud") || Has("Bog") || Has("Marsh") || Has("Swamp"))
            return new Rgba32(92, 78, 52);
        if (Has("Dirt") || Has("Ground") || Has("Earth")) return new Rgba32(124, 92, 58);
        if (Has("Leaves") || Has("Vines") || Has("Foliage") || Has("Grassy"))
            return new Rgba32(58, 88, 44);
        if (Has("Grass")) return new Rgba32(86, 125, 57);
        return new Rgba32(96, 108, 84); // unknown → muted grass-green
    }

    private static Rgba32 Scale(Rgba32 c, float f) => new(
        (byte)Math.Clamp(c.R * f, 0f, 255f),
        (byte)Math.Clamp(c.G * f, 0f, 255f),
        (byte)Math.Clamp(c.B * f, 0f, 255f));

    private static Rgba32 Lerp(Rgba32 a, Rgba32 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Rgba32(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
