// tests/Wc3.Tests/PreplacedUnitsScriptTests.cs
// A map with a custom war3map.j spawns its preplaced widgets from generated JASS
// (CreateAllUnits/CreateAllItems), not from war3mapUnits.doo directly. These guard that
// placing through wc3ctl wires that script in, keeps it idempotent, and never clobbers a
// map that already ships its own creation script.
using System.Text;
using Wc3.Commands;
using Wc3.Commands.Editing;
using Wc3.Model;
using Xunit;

namespace Wc3.Tests;

public class PreplacedUnitsScriptTests
{
    // war3map.j as it stands after a save/reload, byte-faithful (Latin1 round-trips exactly).
    private static string ScriptOf(MapDocument doc)
    {
        byte[] saved = doc.SaveToBytes();
        var reloaded = MapDocument.Load(saved);
        return Encoding.Latin1.GetString(reloaded.GetFile(PreplacedUnitsScript.ScriptFile)!.RawBytes);
    }

    private static int Count(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    [Fact]
    public void PlacingAUnit_WiresCreateAllUnitsIntoMain()
    {
        var doc = BlankMap.Create();
        PlacementCommand.PlaceUnit(doc, "hfoo", ownerId: 0, x: 128f, y: -256f);

        string j = ScriptOf(doc);
        Assert.Contains("function CreateAllUnits takes nothing returns nothing", j);
        Assert.Contains("CreateUnit(Player(0), 'hfoo', 128.0, -256.0", j);
        Assert.Contains("call CreateAllUnits(  )", j);
    }

    [Fact]
    public void PlacingTwoUnits_ProducesOneFunctionAndOneCall()
    {
        var doc = BlankMap.Create();
        PlacementCommand.PlaceUnit(doc, "hfoo", 0, 0f, 0f);
        PlacementCommand.PlaceUnit(doc, "hkni", 1, 64f, 0f);

        string j = ScriptOf(doc);
        Assert.Equal(1, Count(j, "function CreateAllUnits takes nothing"));
        Assert.Equal(1, Count(j, "call CreateAllUnits("));
        Assert.Contains("'hfoo'", j);
        Assert.Contains("Player(1), 'hkni'", j);
    }

    [Fact]
    public void PlacingAnItem_WiresCreateAllItemsNotUnits()
    {
        var doc = BlankMap.Create();
        PlacementCommand.PlaceItem(doc, "ratf", x: 100f, y: 200f);

        string j = ScriptOf(doc);
        Assert.Contains("function CreateAllItems takes nothing returns nothing", j);
        Assert.Contains("CreateItem('ratf', 100.0, 200.0)", j);
        Assert.Contains("call CreateAllItems(  )", j);
        // Only an item was placed, so no unit creation is emitted.
        Assert.DoesNotContain("function CreateAllUnits", j);
    }

    [Fact]
    public void RemovingTheLastUnit_ClearsTheGeneratedBlockAndCall()
    {
        var doc = BlankMap.Create();
        var r = PlacementCommand.PlaceUnit(doc, "hfoo", 0, 0f, 0f);
        Assert.Contains("call CreateAllUnits(  )", ScriptOf(doc));

        new RemoveUnitEdit(r.CreationNumber).Apply(doc);

        string j = ScriptOf(doc);
        Assert.DoesNotContain("call CreateAllUnits(", j);
        Assert.DoesNotContain("function CreateAllUnits", j);
    }

    [Fact]
    public void AMapThatAlreadyCreatesItsUnits_IsLeftUntouched()
    {
        var doc = BlankMap.Create();

        // Give the map its own (foreign) CreateAllUnits, as a real World-Editor map has.
        var entry = doc.GetFile(PreplacedUnitsScript.ScriptFile)!;
        string orig = Encoding.Latin1.GetString(entry.RawBytes);
        string foreign = orig.Replace(
            "function main takes nothing returns nothing",
            "function CreateAllUnits takes nothing returns nothing\n    local unit u\n    set u = null\nendfunction\n"
                + "function main takes nothing returns nothing");
        doc.AddOrReplaceRawFile(PreplacedUnitsScript.ScriptFile, Encoding.Latin1.GetBytes(foreign));

        var res = PreplacedUnitsScript.Sync(doc);
        Assert.False(res.Ok);
        Assert.Contains("untouched", res.Message);

        // Our generated block must never be spliced into a map that owns its creation.
        string j = ScriptOf(doc);
        Assert.DoesNotContain("wc3ctl preplaced widgets", j);
    }

    [Fact]
    public void SyncWithNoPlacements_LeavesTheScriptUnchanged()
    {
        // A map with no preplaced widgets has nothing to wire, so the script stays byte-identical.
        var doc = BlankMap.Create();
        string before = ScriptOf(doc);
        var res = PreplacedUnitsScript.Sync(doc);
        Assert.True(res.Ok);
        Assert.Equal(0, res.Units);
        Assert.Equal(0, res.Items);
        Assert.Equal(before, ScriptOf(doc));
    }
}
