// tests/Wc3.Tests/TerrainRendererTests.cs
using Wc3.Model;
using Wc3.Render;

namespace Wc3.Tests;

public class TerrainRendererTests
{
    private static readonly byte[] PngSignature = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public void Throws_when_map_has_no_terrain()
    {
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.j"] = new byte[] { 1, 2 },
        }));
        var ex = Assert.Throws<InvalidDataException>(() => TerrainRenderer.RenderTerrainPng(doc));
        Assert.Contains("war3map.w3e", ex.Message);
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void Renders_real_map_terrain_to_png()
    {
        const string path = @"C:\Users\GodMephisto\Downloads\ggg_en_1.11r_slk.w3x";
        if (!File.Exists(path)) return;

        var png = TerrainRenderer.RenderTerrainPng(MapDocument.Load(path));

        Assert.True(png.Length > PngSignature.Length);
        Assert.Equal(PngSignature, png[..8]);

        // IHDR width/height (big-endian) sit at bytes 16..23 of any valid PNG.
        int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        Assert.True(width > 0 && height > 0, $"IHDR dims {width}x{height} should be positive");
        // ggg is a square 257x257-tilepoint map, so the render must be square too.
        Assert.Equal(width, height);
    }
}
