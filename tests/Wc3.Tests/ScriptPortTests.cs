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

    private static MapDocument SourceMap()
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
            ["war3map.j"] = Encoding.UTF8.GetBytes(SourceScript),
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
