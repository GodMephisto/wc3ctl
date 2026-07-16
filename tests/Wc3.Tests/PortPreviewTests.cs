// tests/Wc3.Tests/PortPreviewTests.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using War3Net.Build.Extensions;
using War3Net.Build.Object;
using War3Net.Common.Extensions;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Tests;

/// <summary>
/// The dry-run guarantee: PreviewPort reports EXACTLY what PortUnit would do (same
/// remap plan, objects, copy/skip decisions, string inlining and script summary —
/// one code path, so they cannot drift) while writing nothing into the target.
/// </summary>
public class PortPreviewTests
{
    // Source: custom hero H000 (custom ability A000, imported icon, TRIGSTR name) plus a
    // JASS spell handler + InitTrig referencing A000 via a global alias — the full Tier-1
    // + script surface, so the preview is compared against the real port on every axis.
    private const string SourceScript = @"globals
    integer udg_RaidenQ_ID= 'A000'
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
";

    // Target: defines its own H000 and A000 (forces remaps), has a colliding function
    // name and an InitCustomTriggers to hook — everything the script port can exercise.
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
        hero.Modifications.Add(Str("uhab", "A000"));
        hero.Modifications.Add(Str("uico", "war3mapImported\\raidenicon.blp"));
        hero.Modifications.Add(Str("unam", "TRIGSTR_100"));
        w3u.NewUnits.Add(hero);

        var w3a = new AbilityObjectData(ObjectDataFormatVersion.v2);
        var abil = new LevelObjectModification { OldId = "ANcl".FromRawcode(), NewId = "A000".FromRawcode() };
        abil.Modifications.Add(new LevelObjectDataModification
        { Level = 0, Pointer = 0, Id = "anam".FromRawcode(), Type = ObjectDataType.String, Value = "Naginata Combo" });
        w3a.NewAbilities.Add(abil);

        return MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Ser(w => w.Write(w3u)),
            ["war3map.w3a"] = Ser(w => w.Write(w3a)),
            ["war3map.wts"] = Encoding.UTF8.GetBytes("STRING 100\n{\nRaiden Ei\n}\n"),
            ["war3mapImported\\raidenicon.blp"] = new byte[] { 10, 20, 30, 40 },
            ["war3map.j"] = Encoding.UTF8.GetBytes(SourceScript),
        }));
    }

    private static MapDocument TargetMap()
    {
        var w3u = new UnitObjectData(ObjectDataFormatVersion.v2);
        var other = new SimpleObjectModification { OldId = "Hamg".FromRawcode(), NewId = "H000".FromRawcode() };
        other.Modifications.Add(Str("unam", "Someone Else"));
        w3u.NewUnits.Add(other);
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
    public void Preview_produces_the_identical_report_the_real_port_applies()
    {
        // Preview against one pair of documents...
        var previewSource = SourceMap();
        var previewTarget = TargetMap();
        var previewBundle = BundleCommand.ResolveUnit(previewSource, "H000", gameDirOverride: null);
        var preview = PortCommand.PreviewPort(previewSource, previewBundle, previewTarget);

        // ...must equal the real port against an identical fresh pair, field for field.
        var portSource = SourceMap();
        var portTarget = TargetMap();
        var portBundle = BundleCommand.ResolveUnit(portSource, "H000", gameDirOverride: null);
        var real = PortCommand.PortUnit(portSource, portBundle, portTarget);

        Assert.Equal(AsJson(real), AsJson(preview));

        // Sanity: the shared fixture actually exercised every axis of the report.
        Assert.NotEmpty(preview.Remaps);                    // H000 + A000 collided
        Assert.True(preview.Objects.Count >= 2);            // hero + ability
        Assert.NotEmpty(preview.CopiedFiles);               // the icon would copy
        Assert.True(preview.InlinedStrings >= 1);           // TRIGSTR_100 inlined
        Assert.NotNull(preview.Script);                     // closure summarized
        Assert.Equal(2, preview.Script!.Functions);
        Assert.True(preview.Script.InitHooked);
    }

    [Fact]
    public void Preview_writes_nothing_into_the_target()
    {
        var source = SourceMap();
        var target = TargetMap();
        var originalScript = target.GetFile("war3map.j")!.RawBytes.ToArray();
        var bundle = BundleCommand.ResolveUnit(source, "H000", gameDirOverride: null);

        var preview = PortCommand.PreviewPort(source, bundle, target);
        Assert.NotEmpty(preview.Objects); // the preview really planned a port

        var reloaded = MapDocument.Load(target.SaveToBytes());
        // No object injected: the target's own H000 is still the only unit...
        var w3u = (UnitObjectData)reloaded.GetFile("war3map.w3u")!.Model!;
        Assert.Single(w3u.NewUnits);
        Assert.Equal("H000".FromRawcode(), w3u.NewUnits[0].NewId);
        // ...and its w3a still holds only its own A000.
        var w3a = (AbilityObjectData)reloaded.GetFile("war3map.w3a")!.Model!;
        Assert.Single(w3a.NewAbilities);
        // No asset copied, no import registered, script byte-identical.
        Assert.Null(reloaded.GetFile("war3mapImported\\raidenicon.blp"));
        Assert.Null(reloaded.GetFile("war3map.imp"));
        Assert.Equal(originalScript, reloaded.GetFile("war3map.j")!.RawBytes);
    }

    [Fact]
    public async Task Cli_dry_run_prints_report_and_writes_no_ported_file()
    {
        var srcPath = TempMapPath("preview_src");
        var tgtPath = TempMapPath("preview_tgt");
        var portedPath = Path.Combine(
            Path.GetDirectoryName(tgtPath)!, Path.GetFileNameWithoutExtension(tgtPath) + ".ported.w3x");
        File.WriteAllBytes(srcPath, SourceMap().SaveToBytes());
        File.WriteAllBytes(tgtPath, TargetMap().SaveToBytes());
        try
        {
            var sw = new StringWriter();
            var console = Console.Out;
            Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(new[]
            { "port", "unit", srcPath, "H000", tgtPath, "--dry-run", "--game-dir", "Z:\\no_such" });
            Console.SetOut(console);

            Assert.Equal(0, code);
            var output = sw.ToString();
            Assert.Contains("DRY RUN - nothing written", output);
            Assert.DoesNotContain("Saved:", output);
            Assert.False(File.Exists(portedPath), "dry run must not write the ported map");
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

    private static string AsJson(PortResult r) => JsonSerializer.Serialize(r, Json);

    private static SimpleObjectDataModification Str(string code, string value) =>
        new() { Id = code.FromRawcode(), Type = ObjectDataType.String, Value = value };

    private static byte[] Ser(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) write(bw);
        return ms.ToArray();
    }
}
