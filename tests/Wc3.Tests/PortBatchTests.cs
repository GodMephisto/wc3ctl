// tests/Wc3.Tests/PortBatchTests.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using War3Net.Build.Extensions;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Tests;

/// <summary>
/// Batch porting: several units into the SAME target in one operation. The used-rawcode
/// set accumulates across bundles (later units can't collide with earlier ports), shared
/// custom dependencies are ported once and reused, and the merged script closure is
/// spliced a single time.
/// </summary>
public class PortBatchTests
{
    // Two heroes H000/H001 with their own custom abilities A000/A001, BOTH referencing
    // the shared custom ability A002 and the same imported icon.
    private static MapDocument SourceMap(string? script = null)
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var h0 = new SimpleObjectModification { OldId = "Hpal".FromRawcode(), NewId = "H000".FromRawcode() };
        h0.Modifications.Add(Str("uhab", "A000,A002"));
        h0.Modifications.Add(Str("uico", "war3mapImported\\sharedicon.blp"));
        w3u.NewUnits.Add(h0);
        var h1 = new SimpleObjectModification { OldId = "Hamg".FromRawcode(), NewId = "H001".FromRawcode() };
        h1.Modifications.Add(Str("uhab", "A001,A002"));
        h1.Modifications.Add(Str("uico", "war3mapImported\\sharedicon.blp"));
        w3u.NewUnits.Add(h1);

        var w3a = new AbilityObjectData(ObjectDataFormatVersion.v2);
        foreach (var code in new[] { "A000", "A001", "A002" })
            w3a.NewAbilities.Add(new LevelObjectModification
            { OldId = "ANcl".FromRawcode(), NewId = code.FromRawcode() });

        var files = new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3map.w3a"] = Ser(w => w.Write(w3a)),
            ["war3mapImported\\sharedicon.blp"] = new byte[] { 1, 2, 3, 4 },
        };
        if (script is not null)
            files["war3map.j"] = Encoding.UTF8.GetBytes(script);
        return MapDocument.Load(SyntheticMap.Build(files));
    }

    private static MapDocument TargetMap(string[] unitCodes, string[] abilityCodes, string? script = null)
    {
        var files = new Dictionary<string, byte[]>();
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        foreach (var code in unitCodes)
            w3u.NewUnits.Add(new SimpleObjectModification
            { OldId = "Hblm".FromRawcode(), NewId = code.FromRawcode() });
        files["war3map.w3u"] = Ser(w => w.Write(w3u));
        if (abilityCodes.Length > 0)
        {
            var w3a = new AbilityObjectData(ObjectDataFormatVersion.v2);
            foreach (var code in abilityCodes)
                w3a.NewAbilities.Add(new LevelObjectModification
                { OldId = "AHbz".FromRawcode(), NewId = code.FromRawcode() });
            files["war3map.w3a"] = Ser(w => w.Write(w3a));
        }
        if (script is not null)
            files["war3map.j"] = Encoding.UTF8.GetBytes(script);
        return MapDocument.Load(SyntheticMap.Build(files));
    }

    [Fact]
    public void Batch_accumulates_remaps_and_reuses_shared_dependencies()
    {
        var source = SourceMap();
        // Target already defines H000 and the shared ability A002 → both remap; the
        // second hero must avoid the first hero's freshly allocated codes too.
        var target = TargetMap(new[] { "H000" }, new[] { "A002" });
        var bundles = new[] { "H000", "H001" }
            .Select(rc => BundleCommand.ResolveUnit(source, rc, gameDirOverride: null)).ToList();

        var r = PortCommand.PortUnits(source, bundles, target);
        Assert.Equal(2, r.Units.Count);
        var (u0, u1) = (r.Units[0], r.Units[1]);

        // Distinct, non-colliding root codes: H000 collided with the target; whatever it
        // was remapped to, the second hero must land somewhere else.
        Assert.NotEqual("H000", u0.RootPortedTo);
        Assert.NotEqual(u0.RootPortedTo, u1.RootPortedTo);

        // The shared ability was remapped ONCE: both units agree on its new code, and the
        // second unit reused (not re-injected) it.
        string shared0 = u0.Remaps.Single(m => m.From == "A002" && m.Kind == ObjectKind.Ability).To;
        string shared1 = u1.Remaps.Single(m => m.From == "A002" && m.Kind == ObjectKind.Ability).To;
        Assert.Equal(shared0, shared1);
        Assert.DoesNotContain(u1.Objects, o => o.Rawcode == shared1); // not injected twice
        Assert.Contains(u1.Diagnostics, d => d.Contains("already ported by an earlier unit"));

        var reloaded = MapDocument.Load(target.SaveToBytes());

        // Units: the target's own H000 plus the two ported heroes.
        var w3u = (UnitObjectData)reloaded.GetFile("war3map.w3u")!.Model!;
        Assert.Equal(3, w3u.NewUnits.Count);
        Assert.Contains(w3u.NewUnits, u => u.NewId == "H000".FromRawcode());
        Assert.Contains(w3u.NewUnits, u => u.NewId == u0.RootPortedTo!.FromRawcode());
        Assert.Contains(w3u.NewUnits, u => u.NewId == u1.RootPortedTo!.FromRawcode());

        // Abilities: target's own A002 + A's two + B's one (shared NOT duplicated).
        var w3a = (AbilityObjectData)reloaded.GetFile("war3map.w3a")!.Model!;
        Assert.Equal(4, w3a.NewAbilities.Count);
        Assert.Single(w3a.NewAbilities, a => a.NewId == shared0.FromRawcode());

        // The second hero's ability list follows both remaps (its own + the shared one).
        string a1To = u1.Remaps.Single(m => m.From == "A001").To;
        var ported1 = w3u.NewUnits.Single(u => u.NewId == u1.RootPortedTo!.FromRawcode());
        var uhab = (string)ported1.Modifications.Single(m => m.Id == "uhab".FromRawcode()).Value!;
        Assert.Equal($"{a1To},{shared0}", uhab);

        // The shared icon copied once: unit A copied it, unit B saw it already in target.
        Assert.Contains(u0.CopiedFiles, f => f.EndsWith("sharedicon.blp", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(u1.CopiedFiles);
        Assert.Contains(u1.SkippedFiles, f => f.Contains("already in target"));
    }

    private const string DispatcherScript = @"globals
    integer udg_D= 0
endglobals
function Trig_Dispatch_Actions takes nothing returns nothing
    if GetUnitTypeId(GetTriggerUnit()) == 'H000' then
        call KillUnit(GetTriggerUnit())
    endif
    if GetUnitTypeId(GetTriggerUnit()) == 'H001' then
        call KillUnit(GetTriggerUnit())
    endif
endfunction
function InitTrig_Dispatch takes nothing returns nothing
    local integer i= 'H000'
    call TriggerAddAction(CreateTrigger(), function Trig_Dispatch_Actions)
endfunction
";

    private const string TargetScript = @"globals
    integer udg_Foo= 1
endglobals
function InitCustomTriggers takes nothing returns nothing
    call BJDebugMsg(""init"")
endfunction
function main takes nothing returns nothing
    call InitCustomTriggers()
endfunction
";

    [Fact]
    public void Batch_carries_a_shared_script_dispatcher_once_with_all_remaps_applied()
    {
        var source = SourceMap(DispatcherScript);
        // Both hero codes collide → both remapped; the one carried dispatcher copy must
        // reference BOTH new codes.
        var target = TargetMap(new[] { "H000", "H001" }, Array.Empty<string>(), TargetScript);
        var bundles = new[] { "H000", "H001" }
            .Select(rc => BundleCommand.ResolveUnit(source, rc, gameDirOverride: null)).ToList();

        var r = PortCommand.PortUnits(source, bundles, target);

        Assert.NotNull(r.Script);
        Assert.Equal(2, r.Script!.Functions);   // dispatcher + InitTrig, merged across bundles
        Assert.True(r.Script.InitHooked);
        Assert.All(r.Units, u => Assert.Null(u.Script)); // per-unit script superseded by the merge

        // The merged script lives in war3map.j only (not the wtg/wct trigger tree), so the
        // batch result must carry the World-Editor-wipes-it warning.
        Assert.Contains(PortCommand.ScriptDurabilityWarning, r.Warnings);

        var reloaded = MapDocument.Load(target.SaveToBytes());
        var j = Encoding.UTF8.GetString(reloaded.GetFile("war3map.j")!.RawBytes);

        // Carried exactly once — no renamed _p1 duplicate from a second splice.
        Assert.Single(Regex.Matches(j, @"function Trig_Dispatch_Actions takes"));
        Assert.Single(Regex.Matches(j, @"call InitTrig_Dispatch\(\)"));

        // Every ported unit's rawcode literal follows its remap; no original survives.
        string h0To = r.Units[0].Remaps.Single(m => m.From == "H000").To;
        string h1To = r.Units[1].Remaps.Single(m => m.From == "H001").To;
        Assert.Contains($"'{h0To}'", j);
        Assert.Contains($"'{h1To}'", j);
        Assert.DoesNotContain("'H000'", j);
        Assert.DoesNotContain("'H001'", j);
    }

    [Fact]
    public void Batch_preview_matches_batch_port_and_writes_nothing()
    {
        BatchPortResult Run(Func<MapDocument, IReadOnlyList<UnitBundle>, MapDocument, BatchPortResult> op,
            out MapDocument target)
        {
            var source = SourceMap(DispatcherScript);
            target = TargetMap(new[] { "H000" }, new[] { "A002" }, TargetScript);
            var tgt = target;
            var bundles = new[] { "H000", "H001" }
                .Select(rc => BundleCommand.ResolveUnit(source, rc, gameDirOverride: null)).ToList();
            return op(source, bundles, tgt);
        }

        var preview = Run((s, b, t) => PortCommand.PreviewPorts(s, b, t), out var previewTarget);
        var real = Run((s, b, t) => PortCommand.PortUnits(s, b, t), out _);

        Assert.Equal(AsJson(real), AsJson(preview));
        Assert.NotNull(preview.Script); // the merged script closure was summarized too

        // The previewed target is untouched: its own unit only, no icon, script identical.
        var reloaded = MapDocument.Load(previewTarget.SaveToBytes());
        Assert.Single(((UnitObjectData)reloaded.GetFile("war3map.w3u")!.Model!).NewUnits);
        Assert.Null(reloaded.GetFile("war3mapImported\\sharedicon.blp"));
        Assert.Equal(TargetScript, Encoding.UTF8.GetString(reloaded.GetFile("war3map.j")!.RawBytes));
    }

    [Fact]
    public async Task Cli_ports_multiple_rawcodes_into_one_target()
    {
        var srcPath = TempMapPath("batch_src");
        var tgtPath = TempMapPath("batch_tgt");
        var portedPath = Path.Combine(
            Path.GetDirectoryName(tgtPath)!, Path.GetFileNameWithoutExtension(tgtPath) + ".ported.w3x");
        File.WriteAllBytes(srcPath, SourceMap().SaveToBytes());
        File.WriteAllBytes(tgtPath, TargetMap(new[] { "H000" }, new[] { "A002" }).SaveToBytes());
        try
        {
            // Dry run first: full batch report, nothing written.
            var sw = new StringWriter();
            var console = Console.Out;
            Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(new[]
            { "port", "unit", srcPath, "H000,H001", tgtPath, "--dry-run", "--game-dir", "Z:\\no_such" });
            Console.SetOut(console);
            Assert.Equal(0, code);
            Assert.Contains("Ported 2 unit(s) into one target", sw.ToString());
            Assert.Contains("DRY RUN - nothing written", sw.ToString());
            Assert.False(File.Exists(portedPath), "dry run must not write the ported map");

            // Real batch: one <target>.ported.w3x containing both heroes.
            sw = new StringWriter();
            Console.SetOut(sw);
            code = await Wc3Ctl.Program.Main(new[]
            { "port", "unit", srcPath, "H000,H001", tgtPath, "--game-dir", "Z:\\no_such" });
            Console.SetOut(console);
            Assert.Equal(0, code);
            Assert.Contains("Ported 2 unit(s) into one target", sw.ToString());
            Assert.Contains($"Saved: {portedPath}", sw.ToString());
            Assert.True(File.Exists(portedPath));

            var reloaded = MapDocument.Load(portedPath);
            var w3u = (UnitObjectData)reloaded.GetFile("war3map.w3u")!.Model!;
            Assert.Equal(3, w3u.NewUnits.Count); // target's own + the two ported heroes
        }
        finally
        {
            File.Delete(srcPath);
            File.Delete(tgtPath);
            if (File.Exists(portedPath)) File.Delete(portedPath);
        }
    }

    private static string TempMapPath(string tag) =>
        Path.Combine(Path.GetTempPath(), $"wc3ctl_{tag}_{Guid.NewGuid():N}.w3x");

    private static readonly JsonSerializerOptions Json = new()
    { Converters = { new JsonStringEnumConverter() } };

    private static string AsJson(BatchPortResult r) => JsonSerializer.Serialize(r, Json);

    private static SimpleObjectDataModification Str(string code, string value) =>
        new() { Id = code.FromRawcode(), Type = ObjectDataType.String, Value = value };

    private static byte[] Ser(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) write(bw);
        return ms.ToArray();
    }
}
