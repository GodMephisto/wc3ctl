// tests/Wc3.Tests/BlankMapTests.cs
using War3Net.Build.Environment;
using War3Net.Build.Info;
using Wc3.Model;
using Xunit;

namespace Wc3.Tests;

public class BlankMapTests
{
    [Fact]
    public void Create_ProducesLoadableDocument_WithParsedInfoAndEnvironment()
    {
        var doc = BlankMap.Create();

        var w3i = doc.GetFile("war3map.w3i");
        var w3e = doc.GetFile("war3map.w3e");

        Assert.NotNull(w3i);
        Assert.NotNull(w3e);
        Assert.IsType<MapInfo>(w3i!.Model);
        Assert.IsType<MapEnvironment>(w3e!.Model);
        Assert.Equal("Blank Map", ((MapInfo)w3i.Model!).MapName);

        // A freshly synthesized map must parse cleanly (no preserved-as-raw warnings).
        Assert.DoesNotContain(doc.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Create_RoundTripsThroughSave()
    {
        var doc = BlankMap.Create();

        byte[] saved = doc.SaveToBytes();
        var reloaded = MapDocument.Load(saved);

        var w3i = reloaded.GetFile("war3map.w3i")?.Model as MapInfo;
        var w3e = reloaded.GetFile("war3map.w3e")?.Model as MapEnvironment;

        Assert.NotNull(w3i);
        Assert.NotNull(w3e);
        Assert.Equal("Blank Map", w3i!.MapName);
        Assert.Equal((32 + 1) * (32 + 1), w3e!.TerrainTiles.Count);
    }

    [Fact]
    public void Create_RespectsCustomOptions()
    {
        var doc = BlankMap.Create(new BlankMapOptions { MapName = "My Arena", TileEdge = 8 });

        var w3i = doc.GetFile("war3map.w3i")?.Model as MapInfo;
        var w3e = doc.GetFile("war3map.w3e")?.Model as MapEnvironment;

        Assert.Equal("My Arena", w3i!.MapName);
        Assert.Equal((8 + 1) * (8 + 1), w3e!.TerrainTiles.Count);
    }

    [Fact]
    public void CreateArchiveBytes_RejectsDegenerateSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlankMap.CreateArchiveBytes(new BlankMapOptions { TileEdge = 0 }));
    }
}
