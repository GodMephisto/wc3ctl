// tests/Wc3.Tests/BlankMapTests.cs
using System.Text;
using War3Net.Build.Environment;
using War3Net.Build.Extensions;
using War3Net.Build.Info;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Commands;
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

    [Fact]
    public void Create_PopulatesGroundTiles_SoTheMapRendersRealGround()
    {
        // Without a ground-texture palette the terrain renders as a flat gray placeholder
        // ("no ground"). A blank map must ship the tileset's ground tiles, grass first, and
        // survive a round-trip so the on-disk map opens the same way.
        var doc = BlankMap.Create();
        var w3e = Assert.IsType<MapEnvironment>(doc.GetFile("war3map.w3e")!.Model);

        Assert.NotEmpty(w3e.TerrainTypes);
        Assert.Equal("Lgrs", ((int)w3e.TerrainTypes[0]).ToRawcode()); // grass at index 0
        // Every tile's texture index must point at a real palette entry.
        Assert.All(w3e.TerrainTiles, t => Assert.InRange((int)t.Texture, 0, w3e.TerrainTypes.Count - 1));

        var reloaded = MapDocument.Load(BlankMap.CreateArchiveBytes());
        var w3e2 = Assert.IsType<MapEnvironment>(reloaded.GetFile("war3map.w3e")!.Model);
        Assert.Equal("Lgrs", ((int)w3e2.TerrainTypes[0]).ToRawcode());
    }

    [Fact]
    public void Create_EmitsAStandardJassScriptSkeleton()
    {
        var doc = BlankMap.Create();

        var j = doc.GetFile("war3map.j");
        Assert.NotNull(j);
        Assert.NotEmpty(j!.RawBytes);
        // No UTF-8 BOM — WC3's JASS parser never sees one from real WE-saved maps.
        Assert.NotEqual(0xEF, j.RawBytes[0]);

        var text = Encoding.UTF8.GetString(j.RawBytes);
        Assert.Contains("globals", text);
        Assert.Contains("endglobals", text);
        Assert.Contains("function InitCustomTriggers takes nothing returns nothing", text);
        Assert.Contains("function main takes nothing returns nothing", text);
        Assert.Contains("function config takes nothing returns nothing", text);
        Assert.Contains("call InitBlizzard(", text);
        Assert.Contains("call SetMapName( \"Blank Map\" )", text);
        Assert.Contains("call SetPlayers( 1 )", text);
        Assert.Contains("call DefineStartLocation( 0, 0.0, 0.0 )", text);
    }

    [Fact]
    public void Create_ScriptSurvivesRoundTrip_WithNameIntact()
    {
        var doc = BlankMap.Create(new BlankMapOptions { MapName = "Scripted Arena" });

        var reloaded = MapDocument.Load(doc.SaveToBytes());

        Assert.Equal("Scripted Arena", ((MapInfo)reloaded.GetFile("war3map.w3i")!.Model!).MapName);
        var text = Encoding.UTF8.GetString(reloaded.GetFile("war3map.j")!.RawBytes);
        Assert.Contains("call SetMapName( \"Scripted Arena\" )", text);
        Assert.Contains("function InitCustomTriggers takes nothing returns nothing", text);
    }

    /// <summary>
    /// The reason the blank map carries a war3map.j at all: porting a unit whose source
    /// closure includes trigger functions must splice them in and hook InitTrig_* into
    /// the skeleton's InitCustomTriggers — not bail with "target has no war3map.j".
    /// </summary>
    [Fact]
    public void Port_IntoBlankMap_SplicesAndHooksTheScript()
    {
        // Source: custom hero H000 + a trigger pair referencing it (dispatcher pattern
        // from PortBatchTests).
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        w3u.NewUnits.Add(new SimpleObjectModification
        { OldId = "Hpal".FromRawcode(), NewId = "H000".FromRawcode() });
        const string sourceScript = @"globals
    integer udg_D= 0
endglobals
function Trig_Blank_Actions takes nothing returns nothing
    if GetUnitTypeId(GetTriggerUnit()) == 'H000' then
        call KillUnit(GetTriggerUnit())
    endif
endfunction
function InitTrig_Blank takes nothing returns nothing
    local integer i= 'H000'
    call TriggerAddAction(CreateTrigger(), function Trig_Blank_Actions)
endfunction
";
        var source = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3map.j"] = Encoding.UTF8.GetBytes(sourceScript),
        }));

        var target = BlankMap.Create();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        var result = PortCommand.PortUnit(source, bundle, target);

        Assert.NotNull(result.Script);
        Assert.DoesNotContain(result.Script!.Notes, n => n.Contains("target has no war3map.j"));
        Assert.Equal(2, result.Script.Functions); // Trig_Blank_Actions + InitTrig_Blank
        Assert.True(result.Script.InitHooked);

        var reloaded = MapDocument.Load(target.SaveToBytes());
        var j = Encoding.UTF8.GetString(reloaded.GetFile("war3map.j")!.RawBytes);
        Assert.Contains("function Trig_Blank_Actions takes", j);
        // The init hook landed inside the skeleton's InitCustomTriggers.
        Assert.True(j.IndexOf("call InitTrig_Blank()", StringComparison.Ordinal)
                    > j.IndexOf("function InitCustomTriggers takes", StringComparison.Ordinal));
    }

    private static byte[] Ser(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) write(bw);
        return ms.ToArray();
    }
}
