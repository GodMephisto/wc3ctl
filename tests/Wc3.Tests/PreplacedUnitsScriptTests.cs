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
    public void SpellDispatchRegistration_IsDeferredUntilAfterTriggersExist()
    {
        // An arena registers its spell-dispatch trigger per player. A placed hero needs its player
        // registered too, but CreateAllUnits runs BEFORE InitCustomTriggers in main(), so the trigger
        // does not exist yet at unit-creation time. The registration must therefore be deferred to a
        // 0-second timer, never emitted inline in CreateAllUnits (that would register on a null trigger).
        var doc = BlankMap.Create();
        var entry = doc.GetFile(PreplacedUnitsScript.ScriptFile)!;
        string orig = Encoding.Latin1.GetString(entry.RawBytes);
        string withDispatcher = orig.Replace(
            "function main takes nothing returns nothing",
            "function ArenaCast takes nothing returns nothing\n"
            + "    call TriggerRegisterPlayerUnitEvent(gg_trg_GearCastCheck, GetOwningPlayer(GetTriggerUnit()), EVENT_PLAYER_UNIT_SPELL_EFFECT, null)\n"
            + "endfunction\n"
            + "function main takes nothing returns nothing");
        doc.AddOrReplaceRawFile(PreplacedUnitsScript.ScriptFile, Encoding.Latin1.GetBytes(withDispatcher));

        PlacementCommand.PlaceUnit(doc, "hfoo", ownerId: 0, x: 0f, y: 0f);

        string j = ScriptOf(doc);

        // Registered in a deferred function, for the placed unit's player.
        Assert.Contains("function wc3ctl_WirePlacedHeroSpells takes nothing returns nothing", j);
        Assert.Contains(
            "TriggerRegisterPlayerUnitEvent(gg_trg_GearCastCheck, Player(0), EVENT_PLAYER_UNIT_SPELL_EFFECT",
            j);
        // Fired by a 0-second timer from CreateAllUnits.
        Assert.Contains("call TimerStart(CreateTimer(), 0., false, function wc3ctl_WirePlacedHeroSpells)", j);

        // The ordering guard: CreateAllUnits' own body must not register any spell event inline.
        int cau = j.IndexOf("function CreateAllUnits takes nothing", StringComparison.Ordinal);
        int cauEnd = j.IndexOf("endfunction", cau, StringComparison.Ordinal);
        string cauBody = j.Substring(cau, cauEnd - cau);
        Assert.DoesNotContain("TriggerRegisterPlayerUnitEvent", cauBody);
    }

    /// <summary>Only the generated block, so assertions cannot be satisfied (or broken) by the
    /// fixture's own script text that happens to mention the same array.</summary>
    private static string GeneratedBlock(string jass)
    {
        int start = jass.IndexOf("//=== wc3ctl preplaced widgets", StringComparison.Ordinal);
        int end = jass.IndexOf("//=== end wc3ctl preplaced widgets", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "no generated block in the script");
        return jass[start..end];
    }

    /// <summary>Replaces the blank map's script, then places a hero-capable unit so the generated
    /// block has something to register.</summary>
    private static MapDocument MapWithScript(string extraJass)
    {
        var doc = BlankMap.Create();
        var entry = doc.GetFile(PreplacedUnitsScript.ScriptFile)!;
        string orig = Encoding.Latin1.GetString(entry.RawBytes);
        doc.AddOrReplaceRawFile(PreplacedUnitsScript.ScriptFile, Encoding.Latin1.GetBytes(
            orig.Replace("function main takes nothing returns nothing",
                extraJass + "function main takes nothing returns nothing")));
        PlacementCommand.PlaceUnit(doc, "Hpal", ownerId: 0, x: 0f, y: 0f);
        return doc;
    }

    [Fact]
    public void Every_real_per_player_hero_array_is_registered_including_one_based_ones()
    {
        // A map that received ports from several arenas has several hero arrays, with different index
        // conventions. Registering only one (or only 0-based ones) leaves the other source's heroes
        // unable to cast, which is exactly what happened to a merged map in practice.
        string j = ScriptOf(MapWithScript(
            "globals\n" +
            "    unit array Hero\n" +
            "    unit array udg_Player\n" +
            "endglobals\n" +
            "function CastA takes nothing returns boolean\n" +
            "    return GetSpellAbilityUnit() == Hero[GetPlayerId(GetTriggerPlayer())]\n" +
            "endfunction\n" +
            "function CastB takes nothing returns boolean\n" +
            "    return GetTriggerUnit() == udg_Player[( 1 + GetPlayerId(GetTriggerPlayer()) )]\n" +
            "endfunction\n"));

        Assert.Contains("set Hero[GetPlayerId(GetOwningPlayer(u))] = u", j);
        Assert.Contains("set udg_Player[1 + GetPlayerId(GetOwningPlayer(u))] = u", j);
    }

    [Fact]
    public void A_per_player_helper_array_is_never_written_to()
    {
        // udg_Dummy and udg_RevengeUnit are per-player but hold spawned helpers, not the player's
        // hero. Writing a real hero into one makes that system think it owns our hero, so it may
        // kill or recycle it. Only identity-carrying arrays may be touched.
        string j = GeneratedBlock(ScriptOf(MapWithScript(
            "globals\n" +
            "    unit array Hero\n" +
            "    unit array udg_Dummy\n" +
            "    unit array udg_RevengeUnit\n" +
            "endglobals\n" +
            "function Spawn takes nothing returns nothing\n" +
            "    set udg_Dummy[GetPlayerId(GetTriggerPlayer())]=bj_lastCreatedUnit\n" +
            "    set udg_RevengeUnit[( 1 + GetPlayerId(GetTriggerPlayer()) )]=bj_lastCreatedUnit\n" +
            "    call BJDebugMsg(I2S(GetPlayerId(GetTriggerPlayer())))\n" +
            "endfunction\n" +
            "function Cast takes nothing returns boolean\n" +
            "    return GetSpellAbilityUnit() == Hero[GetPlayerId(GetTriggerPlayer())]\n" +
            "endfunction\n")));

        Assert.Contains("set Hero[GetPlayerId(GetOwningPlayer(u))] = u", j);
        Assert.DoesNotContain("set udg_Dummy[", j);
        Assert.DoesNotContain("set udg_RevengeUnit[", j);
    }

    [Fact]
    public void The_generated_block_carries_its_generator_version()
    {
        var doc = BlankMap.Create();
        PlacementCommand.PlaceUnit(doc, "hfoo", 0, 0f, 0f);

        Assert.Contains($"[gen v{PreplacedUnitsScript.GeneratorVersion}]", ScriptOf(doc));
    }

    [Fact]
    public void A_block_from_a_newer_generator_is_left_alone_instead_of_downgraded()
    {
        // The regression that cost real debugging time: an older build regenerated a newer block and
        // silently stripped its spell wiring, leaving a map that compiles and hosts but whose heroes
        // are mute. A newer block must survive an older generator untouched.
        var doc = BlankMap.Create();
        PlacementCommand.PlaceUnit(doc, "hfoo", 0, 0f, 0f);
        var entry = doc.GetFile(PreplacedUnitsScript.ScriptFile)!;
        string bumped = Encoding.Latin1.GetString(entry.OverrideBytes ?? entry.RawBytes)
            .Replace($"[gen v{PreplacedUnitsScript.GeneratorVersion}]",
                     $"[gen v{PreplacedUnitsScript.GeneratorVersion + 1}]");
        doc.AddOrReplaceRawFile(PreplacedUnitsScript.ScriptFile, Encoding.Latin1.GetBytes(bumped));

        var res = PreplacedUnitsScript.Sync(doc);

        Assert.False(res.Ok);
        Assert.Contains("newer", res.Message);
        // Untouched, so the newer behaviour it carried is still there.
        Assert.Contains($"[gen v{PreplacedUnitsScript.GeneratorVersion + 1}]", ScriptOf(doc));
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
