// tests/Wc3.Tests/HeroWiringAuditTests.cs
using System.Text;
using War3Net.Build.Extensions;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Tests;

/// <summary>
/// Hermetic coverage for the two false-positive classes the audit used to report:
/// a dispatch found in an inline condition helper (attached only through a caller), and
/// an unreferenced passive or aura (pure object data, no script) wrongly called inert.
/// </summary>
public class HeroWiringAuditTests
{
    // ---- Bug 1: dispatch attached through a caller, not directly --------------

    [Fact]
    public void Dispatch_in_an_inline_condition_helper_is_wired_via_its_attached_caller()
    {
        // The id check lives in Trig_ChangeWay2_Func001C, which nothing attaches to a trigger. Its
        // caller Trig_ChangeWay2_Actions is the one on gg_trg_ChangeWay2, so the spell is fully wired.
        // The old audit stopped at "the helper is not attached" and reported NotAttachedToTrigger.
        const string jass = @"globals
    trigger gg_trg_ChangeWay2= null
endglobals
function Trig_ChangeWay2_Func001C takes nothing returns boolean
    return ( GetSpellAbilityId() == 'A000' )
endfunction
function Trig_ChangeWay2_Actions takes nothing returns nothing
    if ( not Trig_ChangeWay2_Func001C() ) then
        return
    endif
    call KillUnit(GetTriggerUnit())
endfunction
function InitTrig_ChangeWay2 takes nothing returns nothing
    set gg_trg_ChangeWay2= CreateTrigger()
    call TriggerRegisterAnyUnitEventBJ( gg_trg_ChangeWay2, EVENT_PLAYER_UNIT_SPELL_EFFECT )
    call TriggerAddAction( gg_trg_ChangeWay2, function Trig_ChangeWay2_Actions )
endfunction
function main takes nothing returns nothing
    call InitTrig_ChangeWay2()
endfunction
";
        var doc = BuildMap("A000", jass, new AbilitySpec("A000", "ANcl"));

        var result = HeroWiringAudit.Audit(doc, "H000", ownerId: 0);
        var a000 = result.Abilities.Single(a => a.Ability == "A000");

        Assert.Equal(WiringStatus.Ok, a000.Status);
        // The detail names the function that actually carries the trigger, not just the helper.
        Assert.Contains("via 'Trig_ChangeWay2_Actions'", a000.Detail);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Dispatch_no_caller_of_which_is_attached_still_reports_NotAttachedToTrigger()
    {
        // Same inline-helper shape, but nothing in the caller chain is ever attached to a trigger,
        // so the spell genuinely never fires. The caller walk must not paper over that.
        const string jass = @"function Trig_Solo_Func001C takes nothing returns boolean
    return ( GetSpellAbilityId() == 'A000' )
endfunction
function Trig_Solo_Actions takes nothing returns nothing
    if ( not Trig_Solo_Func001C() ) then
        return
    endif
    call KillUnit(GetTriggerUnit())
endfunction
";
        var doc = BuildMap("A000", jass, new AbilitySpec("A000", "ANcl"));

        var result = HeroWiringAudit.Audit(doc, "H000", ownerId: 0);
        var a000 = result.Abilities.Single(a => a.Ability == "A000");

        Assert.Equal(WiringStatus.NotAttachedToTrigger, a000.Status);
        Assert.Contains(result.Problems, p => p.Ability == "A000");
    }

    // ---- Bug 2: unreferenced passives are informational, not inert ------------

    [Fact]
    public void Unreferenced_self_target_aura_is_informational_not_inert()
    {
        // A custom aura (Targets Allowed = self, no cast cost) that the script never names. It works
        // purely from object data, so it must read as passive, not as a NoDispatch fault. This is the
        // A1R5 ("Eye Of Death Perception", based on an Endurance Aura) case from the real map.
        const string jass = "function main takes nothing returns nothing\nendfunction\n";
        var aura = new AbilitySpec("A1R5", "AOae",
            new FieldMod("atar", 1, "self"),
            new FieldMod("acdn", 1, "0"));
        var doc = BuildMap("A1R5", jass, aura);

        var result = HeroWiringAudit.Audit(doc, "H000", ownerId: 0);
        var a = result.Abilities.Single(x => x.Ability == "A1R5");

        Assert.Equal(WiringStatus.NotCastDispatched, a.Status);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Unreferenced_base_game_passive_the_map_does_not_define_is_informational()
    {
        // Ault (Ultravision) is a base-game code the map never customizes and the script never names.
        // The engine drives it from the unit's ability list alone, so it is passive, not inert.
        const string jass = "function main takes nothing returns nothing\nendfunction\n";
        var doc = BuildMap("Ault", jass); // no custom ability objects at all

        var result = HeroWiringAudit.Audit(doc, "H000", ownerId: 0);
        var a = result.Abilities.Single(x => x.Ability == "Ault");

        Assert.Equal(WiringStatus.NotCastDispatched, a.Status);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Unreferenced_active_spell_with_a_real_target_is_reported_inert()
    {
        // A custom active (Targets Allowed names enemies) that nothing in the script dispatches is a
        // spell the map defines but forgot to wire. That is the genuine fault NoDispatch is for.
        const string jass = "function main takes nothing returns nothing\nendfunction\n";
        var spell = new AbilitySpec("A200", "ANcl",
            new FieldMod("atar", 1, "enemies,ground"),
            new FieldMod("acdn", 1, "8"));
        var doc = BuildMap("A200", jass, spell);

        var result = HeroWiringAudit.Audit(doc, "H000", ownerId: 0);
        var a = result.Abilities.Single(x => x.Ability == "A200");

        Assert.Equal(WiringStatus.NoDispatch, a.Status);
        Assert.Contains(result.Problems, p => p.Ability == "A200");
    }

    // ---- synthetic map construction ------------------------------------------

    private sealed record FieldMod(string Field, int Level, string Value);
    private sealed record AbilitySpec(string Rawcode, string Base, params FieldMod[] Fields);

    /// <summary>Hero H000 whose hero-ability list is <paramref name="uhab"/>, the given war3map.j,
    /// and one custom ability object per <paramref name="abilities"/> spec (with its field deltas).</summary>
    private static MapDocument BuildMap(string uhab, string jass, params AbilitySpec[] abilities)
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var hero = new SimpleObjectModification { OldId = "Hpal".FromRawcode(), NewId = "H000".FromRawcode() };
        hero.Modifications.Add(new SimpleObjectDataModification
        { Id = "uhab".FromRawcode(), Type = ObjectDataType.String, Value = uhab });
        w3u.NewUnits.Add(hero);

        var w3a = new AbilityObjectData(ObjectDataFormatVersion.v2);
        foreach (var spec in abilities)
        {
            var mod = new LevelObjectModification
            { OldId = spec.Base.FromRawcode(), NewId = spec.Rawcode.FromRawcode() };
            foreach (var f in spec.Fields)
                mod.Modifications.Add(new LevelObjectDataModification
                { Id = f.Field.FromRawcode(), Type = ObjectDataType.String, Value = f.Value, Level = f.Level, Pointer = 0 });
            w3a.NewAbilities.Add(mod);
        }

        return MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3map.w3a"] = Ser(w => w.Write(w3a)),
            ["war3map.j"] = Encoding.UTF8.GetBytes(jass),
        }));
    }

    private static byte[] Ser(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) write(bw);
        return ms.ToArray();
    }
}
