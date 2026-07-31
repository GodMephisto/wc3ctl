// tests/Wc3.Tests/ScriptPortTests.cs
using System.Text;
using War3Net.Build.Extensions;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Tests;

public class ScriptPortTests
{
    // Source: hero H000 with hero-ability A000, and a war3map.j where a spell handler
    // and its InitTrig reference the ability rawcode via a global alias. An unrelated
    // function + global must NOT be carried.
    private const string SourceScript = @"globals
    integer udg_RaidenQ_ID= 'A000'
    integer udg_Unrelated= 5
endglobals
function Trig_RaidenQ_Actions takes nothing returns nothing
    if GetSpellAbilityId() == udg_RaidenQ_ID then
        call KillUnit(GetTriggerUnit())
    endif
endfunction
function InitTrig_RaidenQ takes nothing returns nothing
    local integer i= udg_RaidenQ_ID
    call TriggerAddAction(CreateTrigger(), function Trig_RaidenQ_Actions)
endfunction
function UnrelatedFunc takes nothing returns nothing
    set udg_Unrelated= 99
endfunction
";

    // Target: already has a DIFFERENT Trig_RaidenQ_Actions (name collision → rename) and
    // an InitCustomTriggers (the init-hook target), plus its own globals block. It also
    // already defines H000 and A000 (rawcode collisions → remap + rawcode rewrite).
    private const string TargetScript = @"globals
    integer udg_Foo= 1
endglobals
function Trig_RaidenQ_Actions takes nothing returns nothing
    call BJDebugMsg(""target's own"")
endfunction
function InitCustomTriggers takes nothing returns nothing
    call BJDebugMsg(""init"")
endfunction
function main takes nothing returns nothing
    call InitCustomTriggers()
endfunction
";

    [Fact]
    public void An_init_that_a_carried_aggregator_already_calls_is_not_hooked_twice()
    {
        // These scripts often have one InitTrig_* that calls many others. Carrying it AND hooking its
        // callees would call each callee twice, building two triggers on the same event, so every
        // affected spell fires twice (doubled damage and effects). The aggregator is hooked, its
        // callees are not.
        const string aggregated = @"globals
    integer udg_RaidenQ_ID= 'A000'
endglobals
function Trig_RaidenQ_Actions takes nothing returns nothing
    if GetSpellAbilityId() == udg_RaidenQ_ID then
        call KillUnit(GetTriggerUnit())
    endif
endfunction
function InitTrig_RaidenQ takes nothing returns nothing
    call TriggerAddAction(CreateTrigger(), function Trig_RaidenQ_Actions)
endfunction
function InitTrig_AllSystems takes nothing returns nothing
    call InitTrig_RaidenQ()
endfunction
";
        var source = SourceMap(aggregated);
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);

        PortCommand.PortUnit(source, bundle, target);

        var reloaded = MapDocument.Load(target.SaveToBytes());
        var j = Encoding.UTF8.GetString(reloaded.GetFile("war3map.j")!.RawBytes);

        // The aggregator runs the inner init exactly once, so InitCustomTriggers must not call it again.
        int inner = Count(j, "call InitTrig_RaidenQ()");
        Assert.Equal(1, inner);
        Assert.Contains("call InitTrig_AllSystems()", j);
    }

    private static int Count(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    [Fact]
    public void A_ported_script_always_compiles()
    {
        // The whole point of the compile gate: whatever the port splices, the target's script must
        // still compile. One undeclared variable fails the entire war3map.j, so config() never runs
        // and the hosted map shows no player slots.
        var source = SourceMap();
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);

        var result = PortCommand.PortUnit(source, bundle, target);

        Assert.NotNull(result.Script);
        Assert.True(result.Script!.Written, "the ported script was refused: "
            + string.Join(" | ", result.Script.Notes));

        var reloaded = MapDocument.Load(target.SaveToBytes());
        var j = Encoding.UTF8.GetString(reloaded.GetFile("war3map.j")!.RawBytes);
        var issues = JassScriptCheck.Check(j);
        Assert.True(JassScriptCheck.IsCompilable(issues),
            "ported script would not compile: " + string.Join(" | ", issues.Select(i => i.Message)));
    }

    [Fact]
    public void A_dropped_declaration_in_the_carried_source_is_repaired_not_shipped_broken()
    {
        // Re-porting out of a map that was itself produced by an older port: its script already
        // carries a fully commented-out local declaration. Carrying that verbatim would ship an
        // undeclared variable, so the gate must repair it and still write the script.
        const string alreadyTrimmed = @"globals
    integer udg_RaidenQ_ID= 'A000'
endglobals
function Trig_RaidenQ_Actions takes nothing returns nothing
//[wc3ctl trimmed]     local real angle=AbA(1.0)
    if GetSpellAbilityId() == udg_RaidenQ_ID then
        call BJDebugMsg(R2S(angle))
    endif
endfunction
function InitTrig_RaidenQ takes nothing returns nothing
    call TriggerAddAction(CreateTrigger(), function Trig_RaidenQ_Actions)
endfunction
";
        var source = SourceMap(alreadyTrimmed);
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);

        var result = PortCommand.PortUnit(source, bundle, target);

        Assert.NotNull(result.Script);
        Assert.True(result.Script!.Written, "the script should be repaired and written, not refused");
        Assert.Contains(result.Script.Notes, n => n.Contains("repaired", StringComparison.OrdinalIgnoreCase));

        var reloaded = MapDocument.Load(target.SaveToBytes());
        var j = Encoding.UTF8.GetString(reloaded.GetFile("war3map.j")!.RawBytes);
        Assert.True(JassScriptCheck.IsCompilable(JassScriptCheck.Check(j)));
        // The declaration is back, so the later read of 'angle' compiles.
        Assert.Contains("local real angle", j);
    }

    private static MapDocument SourceMap(string script = SourceScript)
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var hero = new SimpleObjectModification { OldId = "Hpal".FromRawcode(), NewId = "H000".FromRawcode() };
        hero.Modifications.Add(new SimpleObjectDataModification
        { Id = "uhab".FromRawcode(), Type = ObjectDataType.String, Value = "A000" });
        w3u.NewUnits.Add(hero);

        var w3a = new AbilityObjectData(ObjectDataFormatVersion.v2);
        w3a.NewAbilities.Add(new LevelObjectModification { OldId = "ANcl".FromRawcode(), NewId = "A000".FromRawcode() });

        return MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3map.w3a"] = Ser(w => w.Write(w3a)),
            ["war3map.j"] = Encoding.UTF8.GetBytes(script),
        }));
    }

    private static MapDocument TargetMap()
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        w3u.NewUnits.Add(new SimpleObjectModification { OldId = "Hamg".FromRawcode(), NewId = "H000".FromRawcode() });
        var w3a = new AbilityObjectData(ObjectDataFormatVersion.v2);
        w3a.NewAbilities.Add(new LevelObjectModification { OldId = "AHbz".FromRawcode(), NewId = "A000".FromRawcode() });

        return MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3map.w3a"] = Ser(w => w.Write(w3a)),
            ["war3map.j"] = Encoding.UTF8.GetBytes(TargetScript),
        }));
    }

    [Fact]
    public void Ports_the_script_closure_with_rename_rawcode_rewrite_and_init_hook()
    {
        var source = SourceMap();
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);

        // Wave-3 closure found the two spell functions (and not the unrelated one).
        Assert.Contains(bundle.Functions, f => f.Name == "Trig_RaidenQ_Actions");
        Assert.Contains(bundle.Functions, f => f.Name == "InitTrig_RaidenQ");
        Assert.DoesNotContain(bundle.Functions, f => f.Name == "UnrelatedFunc");

        var result = PortCommand.PortUnit(source, bundle, target);

        Assert.NotNull(result.Script);
        Assert.Equal(2, result.Script!.Functions);
        Assert.Equal(1, result.Script.Globals);          // udg_RaidenQ_ID carried, udg_Unrelated not
        Assert.True(result.Script.Renamed >= 1);          // Trig_RaidenQ_Actions collided → renamed
        Assert.True(result.Script.InitHooked);

        // A ported script only lives in war3map.j (not the wtg/wct trigger tree), so the
        // World Editor would wipe it on save — the result must carry the loud warning.
        Assert.Contains(PortCommand.ScriptDurabilityWarning, result.Warnings);

        var reloaded = MapDocument.Load(target.SaveToBytes());
        var j = Encoding.UTF8.GetString(reloaded.GetFile("war3map.j")!.RawBytes);

        // The ability rawcode was remapped (A000 collided) and rewritten in the ported global.
        string abilTo = result.Remaps.Single(r => r.From == "A000" && r.Kind == ObjectKind.Ability).To;
        Assert.Contains($"udg_RaidenQ_ID= '{abilTo}'", j);
        Assert.DoesNotContain("udg_RaidenQ_ID= 'A000'", j);

        // The unrelated global/function were not carried.
        Assert.DoesNotContain("UnrelatedFunc", j);

        // The colliding function name was renamed in the ported block, and the original
        // target function of that name survives.
        Assert.Contains("Trig_RaidenQ_Actions_p1", j);
        Assert.Contains("call BJDebugMsg(\"target's own\")", j); // original untouched

        // InitTrig was hooked into InitCustomTriggers.
        var initFn = JassFunctionIndex.Parse(j).Single(f => f.Name == "InitCustomTriggers");
        var initBody = string.Join('\n', j.Replace("\r\n", "\n").Split('\n')[(initFn.StartLine - 1)..initFn.EndLine]);
        Assert.Contains("call InitTrig_RaidenQ()", initBody);
    }

    // A hero whose real spell fires on a combo ability it only GRANTS at runtime: the learn
    // handler adds 'A001', and a second trigger fires when 'A001' is cast. The data-driven closure
    // reaches the learn handler (it tests the hero's own ability 'A000') but cannot reach the combo
    // handler or its InitTrig, because nothing in the hero's data names 'A001'. The port must still
    // carry the combo handler AND both InitTrig_* and wire them, or the spell is defined but dead —
    // the exact break seen porting Nanaya Shiki (QShikiOne fires on 'A1BP', granted by the learn).
    private const string ComboSource = @"globals
    integer udg_Learn_ID= 'A000'
    trigger gg_trg_Learn= null
    trigger gg_trg_Combo= null
endglobals
function LearnCast takes nothing returns nothing
    if GetSpellAbilityId() == udg_Learn_ID then
        call UnitAddAbility(GetTriggerUnit(), 'A001')
    endif
endfunction
function ComboCast takes nothing returns nothing
    if GetSpellAbilityId() == 'A001' then
        call KillUnit(GetTriggerUnit())
    endif
endfunction
function InitTrig_Learn takes nothing returns nothing
    set gg_trg_Learn= CreateTrigger()
    call TriggerAddAction(gg_trg_Learn, function LearnCast)
endfunction
function InitTrig_Combo takes nothing returns nothing
    set gg_trg_Combo= CreateTrigger()
    call TriggerAddAction(gg_trg_Combo, function ComboCast)
endfunction
";

    [Fact]
    public void Carries_and_wires_the_initializer_of_a_runtime_granted_combo_spell()
    {
        var source = SourceMap(ComboSource);
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);

        // The data-driven closure cannot reach the combo handler (fires on 'A001', which the hero
        // only grants at runtime) nor its initializer.
        Assert.DoesNotContain(bundle.Functions, f => f.Name == "ComboCast");
        Assert.DoesNotContain(bundle.Functions, f => f.Name == "InitTrig_Combo");

        var result = PortCommand.PortUnit(source, bundle, target);
        var j = Encoding.UTF8.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);

        // The porter follows the runtime-granted rawcode: the combo handler and BOTH initializers
        // come across, and both are wired into InitCustomTriggers so the spell actually turns on.
        Assert.Contains("function ComboCast", j);
        Assert.Contains("function InitTrig_Learn", j);
        Assert.Contains("function InitTrig_Combo", j);
        var initFn = JassFunctionIndex.Parse(j).Single(f => f.Name == "InitCustomTriggers");
        var initBody = string.Join('\n', j.Replace("\r\n", "\n").Split('\n')[(initFn.StartLine - 1)..initFn.EndLine]);
        Assert.Contains("call InitTrig_Learn()", initBody);
        Assert.Contains("call InitTrig_Combo()", initBody);
    }

    [Fact]
    public void Script_port_is_skipped_cleanly_when_target_has_no_script()
    {
        var source = SourceMap();
        var target = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(new UnitObjectData(ObjectDataFormatVersion.v2))),
        }));
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        var result = PortCommand.PortUnit(source, bundle, target);
        // No throw; script info reports it was not ported.
        Assert.True(result.Script is null || result.Script.Functions == 0);
        // Nothing was ported into war3map.j, so the durability warning must NOT appear.
        Assert.DoesNotContain(PortCommand.ScriptDurabilityWarning, result.Warnings);
    }

    [Fact]
    public void Durability_warning_is_absent_when_script_port_is_opted_out()
    {
        var source = SourceMap();
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        var result = PortCommand.PortUnit(source, bundle, target, includeScript: false);
        Assert.Null(result.Script);
        Assert.DoesNotContain(PortCommand.ScriptDurabilityWarning, result.Warnings);
    }

    [Fact]
    public void Port_preserves_non_utf8_bytes_in_the_target_script()
    {
        var source = SourceMap();

        // Target war3map.j authored in a legacy codepage: a comment holds GBK bytes for a
        // Chinese string that are not valid UTF8. A UTF8 decode/re-encode would replace them
        // with U+FFFD, corrupting the target's own untouched script.
        byte[] gbk = { 0xC4, 0xE3, 0xBA, 0xC3 }; // "你好" in GBK, invalid as UTF8
        var head = Encoding.ASCII.GetBytes(
            "globals\ninteger udg_Foo= 1\nendglobals\n" +
            "function InitCustomTriggers takes nothing returns nothing\n// name: ");
        var tail = Encoding.ASCII.GetBytes(
            "\nendfunction\nfunction main takes nothing returns nothing\n" +
            "call InitCustomTriggers()\nendfunction\n");
        var targetJ = head.Concat(gbk).Concat(tail).ToArray();

        var target = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(new UnitObjectData(ObjectDataFormatVersion.v2))),
            ["war3map.j"] = targetJ,
        }));

        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        PortCommand.PortUnit(source, bundle, target);

        var outBytes = MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes;
        Assert.True(ContainsSubsequence(outBytes, gbk),
            "the target's original non-UTF8 bytes must survive the port unchanged");
    }

    [Fact]
    public void Re_porting_the_same_unit_does_not_double_splice_the_script()
    {
        var source = SourceMap();
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);

        // First port, then persist to disk. The real re-port path reloads the saved map, which
        // is where the prior port's marker becomes visible (AddOrReplaceRawFile keeps a pending
        // override, so RawBytes only reflects it after a save/reload).
        PortCommand.PortUnit(source, bundle, target);
        target = MapDocument.Load(target.SaveToBytes());
        var afterFirst = target.GetFile("war3map.j")!.RawBytes;
        Assert.Equal(1, CountOccurrences(Encoding.Latin1.GetString(afterFirst), "BEGIN wc3ctl ported"));

        // Re-port the same bundle into the already-ported target.
        var second = PortCommand.PortUnit(source, bundle, target);
        var afterSecond = MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes;

        Assert.Equal(0, second.Script!.Functions);                       // nothing re-spliced
        var j = Encoding.Latin1.GetString(afterSecond);
        Assert.Equal(1, CountOccurrences(j, "BEGIN wc3ctl ported"));      // still one block, not two
        Assert.Equal(1, CountOccurrences(j, "call InitTrig_RaidenQ()"));  // still one init hook
        Assert.DoesNotContain("Trig_RaidenQ_Actions_p1_p1", j);          // no runaway re-rename
    }

    [Fact]
    public void Port_into_an_unrelated_map_trims_other_heroes_dispatcher_branches()
    {
        // Source, two custom heroes sharing one cast dispatcher. H000 is ours, H001 is another hero.
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var raiden = new SimpleObjectModification { OldId = "Hpal".FromRawcode(), NewId = "H000".FromRawcode() };
        raiden.Modifications.Add(new SimpleObjectDataModification
        { Id = "uhab".FromRawcode(), Type = ObjectDataType.String, Value = "A000" });
        w3u.NewUnits.Add(raiden);
        w3u.NewUnits.Add(new SimpleObjectModification { OldId = "Hpal".FromRawcode(), NewId = "H001".FromRawcode() });
        var w3a = new AbilityObjectData(ObjectDataFormatVersion.v2);
        w3a.NewAbilities.Add(new LevelObjectModification { OldId = "ANcl".FromRawcode(), NewId = "A000".FromRawcode() });

        const string srcJass =
            "function RaidenFn takes nothing returns nothing\n" +
            "    call BJDebugMsg(\"raiden\")\n" +
            "endfunction\n" +
            "function NatsuFn takes nothing returns nothing\n" +
            "    call BJDebugMsg(\"natsu\")\n" +
            "endfunction\n" +
            "function CastHero takes nothing returns nothing\n" +
            "    if GetUnitTypeId(GetSpellAbilityUnit()) == 'H000' then\n" +
            "        call RaidenFn()\n" +
            "    endif\n" +
            "    if GetUnitTypeId(GetSpellAbilityUnit()) == 'H001' then\n" +
            "        call NatsuFn()\n" +
            "    endif\n" +
            "endfunction\n" +
            "function InitTrig_Cast takes nothing returns nothing\n" +
            "    local integer i= 'A000'\n" +
            "    call TriggerAddAction(CreateTrigger(), function CastHero)\n" +
            "endfunction\n";

        var source = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3map.w3a"] = Ser(w => w.Write(w3a)),
            ["war3map.j"] = Encoding.UTF8.GetBytes(srcJass),
        }));

        // Target, an unrelated map without this engine (no H000/H001/A000, no dispatcher).
        var target = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(new UnitObjectData(ObjectDataFormatVersion.v2))),
            ["war3map.j"] = Encoding.UTF8.GetBytes(
                "function InitCustomTriggers takes nothing returns nothing\nendfunction\n" +
                "function main takes nothing returns nothing\ncall InitCustomTriggers()\nendfunction\n"),
        }));

        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        Assert.Contains(bundle.Functions, f => f.Name == "RaidenFn");     // our branch's handler
        Assert.DoesNotContain(bundle.Functions, f => f.Name == "NatsuFn"); // foreign branch's handler

        PortCommand.PortUnit(source, bundle, target);
        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);

        // Our branch's function is carried and its call stays live.
        Assert.Contains("function RaidenFn", j);
        Assert.True(HasActiveCall(j, "RaidenFn"), "our hero's handler call must remain live");
        // The foreign handler is neither defined nor actively called (its call was commented out),
        // so the ported script has no undefined-function reference and can compile in the target.
        Assert.DoesNotContain("function NatsuFn", j);
        Assert.False(HasActiveCall(j, "NatsuFn"), "the other hero's call must be commented out, not live");
    }

    // Source whose spell handler reads only DPS. DPS's initializer reads TICK, so TICK must
    // be carried too or the ported war3map.j references an undeclared name and fails to
    // compile. udg_UnusedRate is referenced by nothing carried and must stay out.
    private const string ChainedGlobalsScript = @"globals
    integer udg_RaidenQ_ID= 'A000'
    real TICK= 0.03
    real DPS= 300. * TICK
    real udg_UnusedRate= 1.5
endglobals
function Trig_RaidenQ_Actions takes nothing returns nothing
    if GetSpellAbilityId() == udg_RaidenQ_ID then
        call BJDebugMsg(R2S(DPS))
    endif
endfunction
function InitTrig_RaidenQ takes nothing returns nothing
    local integer i= udg_RaidenQ_ID
    call TriggerAddAction(CreateTrigger(), function Trig_RaidenQ_Actions)
endfunction
";

    [Fact]
    public void Carried_global_initializers_pull_their_referenced_globals_transitively()
    {
        var source = SourceMap(ChainedGlobalsScript);
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        var result = PortCommand.PortUnit(source, bundle, target);

        Assert.NotNull(result.Script);
        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);

        // The carried function reads only DPS, whose initializer reads TICK. Both must land,
        // and in source order (TICK declared before the DPS line that uses it).
        Assert.Contains("real DPS= 300. * TICK", j);
        Assert.Contains("real TICK= 0.03", j);
        Assert.True(j.IndexOf("real TICK= 0.03", StringComparison.Ordinal)
                  < j.IndexOf("real DPS= 300. * TICK", StringComparison.Ordinal),
            "TICK must be declared before the DPS initializer that reads it");
    }

    [Fact]
    public void Unreferenced_globals_stay_out_after_the_initializer_fixpoint()
    {
        var source = SourceMap(ChainedGlobalsScript);
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        var result = PortCommand.PortUnit(source, bundle, target);

        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);
        Assert.DoesNotContain("udg_UnusedRate", j);
        Assert.Equal(3, result.Script!.Globals); // udg_RaidenQ_ID, TICK, DPS and nothing else
    }

    // A GUI map whose InitGlobals allocates a per-player TIMER the spell handler starts. The bare
    // declaration ("timer udg_RaidenQ_Timer= null") carries no non-default value, so without carrying
    // InitGlobals's own assignment the handler's TimerStart runs on a still-null timer and silently
    // does nothing, exactly the break measured on GGGA's Tohno Shiki (its own per-player timers and
    // groups are allocated in InitGlobals-equivalent code, not simple literals). A second, unrelated
    // global is also assigned there and must NOT be carried.
    private const string InitGlobalsScript = @"globals
    integer udg_RaidenQ_ID= 'A000'
    timer udg_RaidenQ_Timer= null
    timer udg_Unrelated_Timer= null
endglobals
function InitGlobals takes nothing returns nothing
    set udg_RaidenQ_Timer= CreateTimer()
    set udg_Unrelated_Timer= CreateTimer()
endfunction
function Trig_RaidenQ_Actions takes nothing returns nothing
    if GetSpellAbilityId() == udg_RaidenQ_ID then
        call TimerStart(udg_RaidenQ_Timer, 1.0, false, function Trig_RaidenQ_Actions)
    endif
endfunction
function InitTrig_RaidenQ takes nothing returns nothing
    call TriggerAddAction(CreateTrigger(), function Trig_RaidenQ_Actions)
endfunction
";

    [Fact]
    public void InitGlobals_initializer_for_a_carried_global_is_carried_live_and_the_non_carried_one_is_not()
    {
        var source = SourceMap(InitGlobalsScript);
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        var result = PortCommand.PortUnit(source, bundle, target);

        Assert.NotNull(result.Script);
        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);

        Assert.Contains("function InitGlobals", j);
        Assert.Contains("set udg_RaidenQ_Timer= CreateTimer()", j);

        // The unrelated global's initializer is trimmed (commented out), same discipline as a
        // dropped function reference, so the target never carries state no carried spell reads.
        // (The name still appears INSIDE the trim-marker comment, so check liveness, not absence.)
        Assert.False(LiveLineContains(j, "udg_Unrelated_Timer"),
            "the unrelated global's initializer must be commented out, not live");

        Assert.Contains(result.Script!.Notes, n => n.Contains("global-initializer", StringComparison.Ordinal));
        Assert.True(JassScriptCheck.IsCompilable(JassScriptCheck.Check(j)));
    }

    [Fact]
    public void InitGlobals_call_is_spliced_into_main_before_InitCustomTriggers()
    {
        var source = SourceMap(InitGlobalsScript);
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        PortCommand.PortUnit(source, bundle, target);

        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);
        var mainFn = JassFunctionIndex.Parse(j).Single(f => f.Name == "main");
        var mainBody = string.Join('\n', j.Replace("\r\n", "\n").Split('\n')[(mainFn.StartLine - 1)..mainFn.EndLine]);

        Assert.Contains("call InitGlobals()", mainBody);
        Assert.True(mainBody.IndexOf("call InitGlobals()", StringComparison.Ordinal)
                  < mainBody.IndexOf("call InitCustomTriggers()", StringComparison.Ordinal),
            "InitGlobals must run before InitCustomTriggers, the JASS convention the source itself follows");
    }

    // A map whose per-player state is NOT allocated in a function literally named InitGlobals, but in
    // a differently named helper InitCustomTriggers calls before it wires any trigger (GGGA's actual
    // shape, InitGlobals there is empty and WS_Init_WorkingSourceGeneratedMapState plays this role).
    // The helper must be carried, filtered the same way, and called at the FRONT of the target's
    // InitCustomTriggers, before the InitTrig_* wiring, matching the source's own order.
    private const string MapStateHelperScript = @"globals
    integer udg_RaidenQ_ID= 'A000'
    group udg_RaidenQ_Group= null
    group udg_Unrelated_Group= null
endglobals
function MapStateInit takes nothing returns nothing
    set udg_RaidenQ_Group= CreateGroup()
    set udg_Unrelated_Group= CreateGroup()
endfunction
function Trig_RaidenQ_Actions takes nothing returns nothing
    if GetSpellAbilityId() == udg_RaidenQ_ID then
        call GroupAddUnit(udg_RaidenQ_Group, GetTriggerUnit())
    endif
endfunction
function InitTrig_RaidenQ takes nothing returns nothing
    call TriggerAddAction(CreateTrigger(), function Trig_RaidenQ_Actions)
endfunction
function InitCustomTriggers takes nothing returns nothing
    call MapStateInit()
    call InitTrig_RaidenQ()
endfunction
";

    [Fact]
    public void A_differently_named_map_state_helper_is_carried_and_wired_before_the_trigger_init()
    {
        var source = SourceMap(MapStateHelperScript);
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);
        var result = PortCommand.PortUnit(source, bundle, target);

        Assert.NotNull(result.Script);
        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);

        Assert.Contains("function MapStateInit", j);
        Assert.Contains("set udg_RaidenQ_Group= CreateGroup()", j);
        Assert.False(LiveLineContains(j, "udg_Unrelated_Group"),
            "not read by anything carried, must be trimmed rather than live");

        var initFn = JassFunctionIndex.Parse(j).Single(f => f.Name == "InitCustomTriggers");
        var initBody = string.Join('\n', j.Replace("\r\n", "\n").Split('\n')[(initFn.StartLine - 1)..initFn.EndLine]);
        Assert.Contains("call MapStateInit()", initBody);
        Assert.Contains("call InitTrig_RaidenQ()", initBody);
        Assert.True(initBody.IndexOf("call MapStateInit()", StringComparison.Ordinal)
                  < initBody.IndexOf("call InitTrig_RaidenQ()", StringComparison.Ordinal),
            "the map-state helper must run before the spell's own trigger gets wired");
        Assert.True(JassScriptCheck.IsCompilable(JassScriptCheck.Check(j)));
    }

    // A spell that applies area damage does it inside a ForGroup callback, a GUI child of the same
    // trigger (Trig_RaidenQ_Func005A is a child of Trig_RaidenQ). The callback is named only as a
    // "function Foo" argument, so the data-driven closure never reaches it. Before fix (c) the whole
    // ForGroupBJ statement was trimmed as a dropped reference and the spell dealt no area damage.
    private const string CallbackAoESource = @"globals
    integer udg_RaidenQ_ID= 'A000'
endglobals
function Trig_RaidenQ_Func005A takes nothing returns nothing
    call UnitDamageTargetBJ(GetTriggerUnit(), GetEnumUnit(), 100., ATTACK_TYPE_NORMAL, DAMAGE_TYPE_NORMAL)
endfunction
function Trig_RaidenQ_Actions takes nothing returns nothing
    if GetSpellAbilityId() == udg_RaidenQ_ID then
        call ForGroupBJ(GetUnitsInRangeOfLocMatching(300., GetUnitLoc(GetTriggerUnit()), null), function Trig_RaidenQ_Func005A)
    endif
endfunction
function InitTrig_RaidenQ takes nothing returns nothing
    call TriggerAddAction(CreateTrigger(), function Trig_RaidenQ_Actions)
endfunction
";

    [Fact]
    public void Carries_a_same_trigger_ForGroup_callback_so_area_damage_survives_the_port()
    {
        var source = SourceMap(CallbackAoESource);
        var target = TargetMap();

        // Port the parent handler and its init but NOT the callback, the exact state ScriptPorter is in
        // when a handler was pulled in by CarryRegisteringInits or the structural fixpoint rather than by
        // the data-driven bundle crawl, so the bundle never followed its "function Foo" arguments. The
        // callback pre-pass must carry the same-trigger child so the ForGroup call stays live.
        var functions = CarriedExcept(CallbackAoESource, "Trig_RaidenQ_Func005A");
        Assert.DoesNotContain(functions, f => f.Name == "Trig_RaidenQ_Func005A");

        var info = ScriptPorter.PortScript(source, target, functions, "H000", new Dictionary<string, string>());
        Assert.NotNull(info);

        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);
        Assert.Contains("function Trig_RaidenQ_Func005A takes", j);
        Assert.True(CallbackLineIsLive(j, "Trig_RaidenQ_Func005A"),
            "the ForGroup callback line must be carried live, not commented out");
        Assert.True(JassScriptCheck.IsCompilable(JassScriptCheck.Check(j)));
    }

    // The same-trigger scope is the safeguard against snowballing. A carried handler that passes a
    // callback belonging to a DIFFERENT trigger (a shared event system's child, the way GGGA's damage
    // and death systems name every hero's callback) must NOT pull that callback, or the walk drags in
    // the whole roster (measured at +2446 functions on GGGA). The cross-trigger callback stays dropped
    // and its call is trimmed.
    private const string CrossTriggerCallbackSource = @"globals
    integer udg_RaidenQ_ID= 'A000'
endglobals
function Trig_SharedDmg_Func009A takes nothing returns nothing
    call BJDebugMsg(""another trigger's child"")
endfunction
function Trig_RaidenQ_Actions takes nothing returns nothing
    if GetSpellAbilityId() == udg_RaidenQ_ID then
        call ForGroupBJ(GetUnitsInRangeOfLocMatching(300., GetUnitLoc(GetTriggerUnit()), null), function Trig_SharedDmg_Func009A)
    endif
endfunction
function InitTrig_RaidenQ takes nothing returns nothing
    call TriggerAddAction(CreateTrigger(), function Trig_RaidenQ_Actions)
endfunction
";

    [Fact]
    public void The_callback_walk_does_not_carry_a_callback_of_a_different_trigger()
    {
        var source = SourceMap(CrossTriggerCallbackSource);
        var target = TargetMap();

        var functions = CarriedExcept(CrossTriggerCallbackSource, "Trig_SharedDmg_Func009A");
        Assert.DoesNotContain(functions, f => f.Name == "Trig_SharedDmg_Func009A");

        var info = ScriptPorter.PortScript(source, target, functions, "H000", new Dictionary<string, string>());
        Assert.NotNull(info);

        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);
        // Different trigger stem (Trig_SharedDmg vs Trig_RaidenQ), so it is NOT carried and its ForGroup
        // call is trimmed, leaving no dangling reference.
        Assert.DoesNotContain("function Trig_SharedDmg_Func009A takes", j);
        Assert.False(CallbackLineIsLive(j, "Trig_SharedDmg_Func009A"),
            "a callback belonging to a different trigger must stay trimmed");
        Assert.True(JassScriptCheck.IsCompilable(JassScriptCheck.Check(j)));
    }

    // The callback walk must not become a call-graph walk. A function reached only by a plain call from a
    // foreign-guarded branch (another hero's dispatcher branch) must stay dropped and its call commented,
    // exactly as before, so the closure does not snowball into the whole shared engine.
    private const string ForeignCallSource = @"globals
    integer udg_RaidenQ_ID= 'A000'
endglobals
function ForeignAoE takes nothing returns nothing
    call BJDebugMsg(""foreign"")
endfunction
function Trig_RaidenQ_Actions takes nothing returns nothing
    if GetSpellAbilityId() == udg_RaidenQ_ID then
        call BJDebugMsg(""ours"")
    endif
    if GetSpellAbilityId() == 'A999' then
        call ForeignAoE()
    endif
endfunction
function InitTrig_RaidenQ takes nothing returns nothing
    call TriggerAddAction(CreateTrigger(), function Trig_RaidenQ_Actions)
endfunction
";

    [Fact]
    public void The_callback_walk_does_not_carry_a_function_reached_only_by_a_plain_call()
    {
        var source = SourceMap(ForeignCallSource);
        var target = TargetMap();

        // Carry the parent handler but not ForeignAoE. ForeignAoE is reached only by "call ForeignAoE()"
        // (a foreign-guarded dispatch branch), never as a "function Foo" callback, so the callback walk
        // must NOT pull it, and the existing trim must comment the call out. This is what keeps the walk
        // from degenerating into a call-graph walk that snowballs the whole shared engine.
        var functions = CarriedExcept(ForeignCallSource, "ForeignAoE");
        Assert.DoesNotContain(functions, f => f.Name == "ForeignAoE");

        var info = ScriptPorter.PortScript(source, target, functions, "H000", new Dictionary<string, string>());
        Assert.NotNull(info);

        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);
        Assert.DoesNotContain("function ForeignAoE", j);
        Assert.False(HasActiveCall(j, "ForeignAoE"), "a plain-call-only foreign function must stay trimmed");
        Assert.True(JassScriptCheck.IsCompilable(JassScriptCheck.Check(j)));
    }

    // The shape a shared hero arena dispatcher actually takes: a gate the source map wired around
    // it (DisableMoveRoot), the hero's own "GetUnitTypeId(c) == Hero_ID" branch, and inside that an
    // ability id chain. HeroQ2 is a combo ability the hero grants at runtime (not on its own object
    // data), the same shape as Asta's Q2/W2 on the real corpus this was built against. The carried
    // set below deliberately excludes HeroQ2_Start, reproducing the state where the ordinary
    // closure never reached it, so --synth-dispatch's own seeding is what has to recover it.
    private const string SynthDispatchSource = @"globals
    integer udg_Hero_ID= 'H000'
    integer udg_HeroQ_ID= 'A000'
    integer udg_HeroQ2_ID= 'A001'
    trigger gg_trg_CastCheck= null
endglobals
function DisableMoveRoot takes unit u, integer id returns boolean
    return true
endfunction
function HeroQ_Start takes unit c, real x, real y returns nothing
    call UnitAddAbility(c, udg_HeroQ2_ID)
endfunction
function HeroQ2_Start takes unit c, real x, real y returns nothing
    call KillUnit(c)
endfunction
function CastHero_Conditions takes nothing returns boolean
    local unit c= GetSpellAbilityUnit()
    local integer id= GetSpellAbilityId()
    local real x= GetSpellTargetX()
    local real y= GetSpellTargetY()
    local boolean b= DisableMoveRoot(c, id)
    if b then
        if GetUnitTypeId(c) == udg_Hero_ID then
            if id == udg_HeroQ_ID then
                call HeroQ_Start(c, x, y)
            elseif id == udg_HeroQ2_ID then
                call HeroQ2_Start(c, x, y)
            endif
        endif
    endif
    return false
endfunction
function InitTrig_CastCheck takes nothing returns nothing
    set gg_trg_CastCheck = CreateTrigger()
    call TriggerAddCondition(gg_trg_CastCheck, Condition(function CastHero_Conditions))
endfunction
";

    [Fact]
    public void SynthDispatch_reads_the_hero_own_branch_and_seeds_the_combo_handler_the_ordinary_closure_missed()
    {
        var source = SourceMap(SynthDispatchSource);
        var target = TargetMap();

        // Simulate the ordinary closure's blind spot directly: it carried the dispatcher and Q's
        // own handler, but never reached HeroQ2_Start (granted at runtime, not on the hero's own
        // ability list).
        var functions = CarriedExcept(SynthDispatchSource, "HeroQ2_Start");
        Assert.DoesNotContain(functions, f => f.Name == "HeroQ2_Start");

        var info = ScriptPorter.PortScript(source, target, functions, "H000",
            new Dictionary<string, string>(), apply: true, synthDispatchHero: "H000");
        Assert.NotNull(info);
        Assert.True(info!.Written, "the ported script was refused: " + string.Join(" | ", info.Notes));

        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);
        Assert.True(JassScriptCheck.IsCompilable(JassScriptCheck.Check(j)),
            "synthesized dispatcher made the script uncompilable");

        // Both branches present, literal rawcodes (no hero-specific id global needed at all).
        Assert.Contains("if id == 'A000' then", j);
        Assert.Contains("elseif id == 'A001' then", j);
        // The combo handler's call is LIVE, and its definition was seeded in even though the
        // ordinary carried set excluded it — the whole point of the feature.
        Assert.True(HasActiveCall(j, "HeroQ2_Start"), "the combo ability's call must not be trimmed");
        Assert.Contains("function HeroQ2_Start takes", j);

        // Its own trigger (a fresh gg_trg_, not the source's gg_trg_CastCheck), wired end to end.
        var dispatchFn = JassFunctionIndex.Parse(j).Single(f => f.Name.StartsWith("wc3ctl_SynthCast_", StringComparison.Ordinal)
            && !f.Name.EndsWith("Init", StringComparison.Ordinal));
        Assert.Contains("trigger gg_trg_wc3ctl_SynthCast_", j);
        Assert.Contains($"call TriggerAddAction(gg_trg_wc3ctl_SynthCast_H000, function {dispatchFn.Name})", j);
        Assert.Contains("call TriggerRegisterAnyUnitEventBJ(gg_trg_wc3ctl_SynthCast_H000, EVENT_PLAYER_UNIT_SPELL_EFFECT)", j);
        var initFn = JassFunctionIndex.Parse(j).Single(f => f.Name == "InitCustomTriggers");
        var initBody = string.Join('\n', j.Replace("\r\n", "\n").Split('\n')[(initFn.StartLine - 1)..initFn.EndLine]);
        Assert.Contains("wc3ctl_SynthCast_H000Init()", initBody);

        // None of the shared dispatcher's gates or its hero-id check appear in the SYNTHESIZED
        // function itself (the source's own CastHero_Conditions still exists elsewhere, carried
        // whole like any other carried body — only the fresh function must be gate-free).
        var body = FunctionBody(j, dispatchFn.Name);
        Assert.DoesNotContain("DisableMoveRoot", body);
        Assert.DoesNotContain("GetUnitTypeId", body);
        Assert.DoesNotContain("udg_Hero_ID", body);
    }

    [Fact]
    public void SynthDispatch_is_a_no_op_when_the_flag_is_left_off()
    {
        var source = SourceMap(SynthDispatchSource);
        var target = TargetMap();
        var functions = CarriedExcept(SynthDispatchSource, "HeroQ2_Start");

        var info = ScriptPorter.PortScript(source, target, functions, "H000", new Dictionary<string, string>());
        Assert.NotNull(info);

        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);
        Assert.DoesNotContain("wc3ctl_SynthCast_", j);
        Assert.DoesNotContain("gg_trg_wc3ctl_SynthCast_", j);
        // Without the flag, the ordinary closure's blind spot is exactly that: the combo handler
        // stays uncarried and its call stays trimmed, the pre-existing (unchanged) behavior.
        Assert.DoesNotContain("function HeroQ2_Start", j);
    }

    [Fact]
    public void SynthDispatch_reports_and_does_not_guess_when_the_source_has_no_matching_dispatcher_shape()
    {
        // A hero whose script never compares GetUnitTypeId against anything: an unsupported shape,
        // the flag must degrade gracefully (still port normally) rather than synthesize nonsense.
        const string noDispatcherSource = @"globals
    integer udg_HeroQ_ID= 'A000'
endglobals
function Trig_HeroQ_Actions takes nothing returns nothing
    if GetSpellAbilityId() == udg_HeroQ_ID then
        call KillUnit(GetTriggerUnit())
    endif
endfunction
function InitTrig_HeroQ takes nothing returns nothing
    call TriggerAddAction(CreateTrigger(), function Trig_HeroQ_Actions)
endfunction
";
        var source = SourceMap(noDispatcherSource);
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);

        var result = PortCommand.PortUnit(source, bundle, target, includeScript: true, synthDispatch: true);
        Assert.NotNull(result.Script);
        Assert.True(result.Script!.Written);
        Assert.Contains(result.Script.Notes, n => n.Contains("no per-hero cast branch", StringComparison.Ordinal));

        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);
        Assert.DoesNotContain("wc3ctl_SynthCast_", j);
        // The ordinary port still happened.
        Assert.Contains("function Trig_HeroQ_Actions", j);
    }

    [Fact]
    public void PortCommand_threads_synth_dispatch_through_the_full_bundle_resolved_port()
    {
        // End to end through the real entry point (BundleCommand.ResolveUnit + PortCommand.PortUnit),
        // not a hand-built carried set, so the CLI-facing plumbing (bundle.RootRawcode -> ScriptPorter)
        // is covered too, not just ScriptPorter's own seeding logic.
        var source = SourceMap(SynthDispatchSource);
        var target = TargetMap();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);

        var result = PortCommand.PortUnit(source, bundle, target, includeScript: true, synthDispatch: true);
        Assert.NotNull(result.Script);
        Assert.True(result.Script!.Written, string.Join(" | ", result.Script.Notes));

        var j = Encoding.Latin1.GetString(MapDocument.Load(target.SaveToBytes()).GetFile("war3map.j")!.RawBytes);
        Assert.Contains("function wc3ctl_SynthCast_H000", j);
        Assert.True(JassScriptCheck.IsCompilable(JassScriptCheck.Check(j)));
    }

    /// <summary>The text of one function's body (signature line through endfunction), the same slice
    /// every "hook into InitCustomTriggers" assertion above already takes inline, pulled out once
    /// the synth-dispatch tests need it more than once.</summary>
    private static string FunctionBody(string jass, string functionName)
    {
        var f = JassFunctionIndex.Parse(jass).Single(x => x.Name == functionName);
        var lines = jass.Replace("\r\n", "\n").Split('\n');
        return string.Join('\n', lines[(f.StartLine - 1)..f.EndLine]);
    }

    /// <summary>Every function declared in <paramref name="script"/> as a carried BundleFunction, minus
    /// the named ones. Lets a test hand ScriptPorter a carried set that deliberately omits a callback,
    /// reproducing the state where the bundle crawl never reached it.</summary>
    private static IReadOnlyList<BundleFunction> CarriedExcept(string script, params string[] omit)
    {
        var drop = omit.ToHashSet(StringComparer.Ordinal);
        return JassFunctionIndex.Parse(script)
            .Where(f => !drop.Contains(f.Name))
            .Select(f => new BundleFunction(f.Name, f.StartLine, f.EndLine, "seed"))
            .ToList();
    }

    /// <summary>True when some uncommented line uses <paramref name="fn"/> as a "function Foo" callback
    /// argument (a live ForGroup/Condition pass), as opposed to merely declaring it. The declaration
    /// line begins with "function fn", so it is excluded, only a callback USE returns true.</summary>
    private static bool CallbackLineIsLive(string jass, string fn)
    {
        foreach (var line in jass.Replace("\r\n", "\n").Split('\n'))
        {
            int c = line.IndexOf("//", StringComparison.Ordinal);
            var code = c >= 0 ? line[..c] : line;
            if (code.Contains("function " + fn, StringComparison.Ordinal)
                && !code.TrimStart().StartsWith("function " + fn, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>True when some line has an uncommented call to <paramref name="fn"/>.</summary>
    private static bool HasActiveCall(string jass, string fn)
    {
        foreach (var line in jass.Replace("\r\n", "\n").Split('\n'))
        {
            int c = line.IndexOf("//", StringComparison.Ordinal);
            var code = c >= 0 ? line[..c] : line;
            if (code.Contains(fn + "(", StringComparison.Ordinal) || code.Contains(fn + " (", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>True when some uncommented line contains <paramref name="needle"/>. Trimmed lines
    /// keep the original text visible inside a trailing comment marker, so a plain
    /// <c>Assert.DoesNotContain</c> would false-fail on a correctly-trimmed name.</summary>
    private static bool LiveLineContains(string jass, string needle)
    {
        foreach (var line in jass.Replace("\r\n", "\n").Split('\n'))
        {
            int c = line.IndexOf("//", StringComparison.Ordinal);
            var code = c >= 0 ? line[..c] : line;
            if (code.Contains(needle, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            bool ok = true;
            for (int k = 0; k < needle.Length; k++)
                if (haystack[i + k] != needle[k]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }

    private static byte[] Ser(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) write(bw);
        return ms.ToArray();
    }
}
