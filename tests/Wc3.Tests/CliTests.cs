// tests/Wc3.Tests/CliTests.cs
namespace Wc3.Tests;

public class CliTests
{
    [Fact]
    public async Task Ls_json_lists_files()
    {
        var map = SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1 } });
        var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_test_{System.Guid.NewGuid():N}.w3x");
        File.WriteAllBytes(path, map);
        try
        {
            var sw = new StringWriter();
            var console = System.Console.Out;
            System.Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(new[] { "ls", path, "--json" });
            System.Console.SetOut(console);
            Assert.Equal(0, code);
            Assert.Contains("war3map.j", sw.ToString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Extract_models_writes_mdx_to_out_dir_preserving_structure()
    {
        var mdxBytes = new byte[] { 0x4D, 0x44, 0x4C, 0x58, 1, 2, 3 };
        var map = SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            [@"war3mapImported\a.mdx"] = mdxBytes,
            ["b.blp"] = new byte[] { 9 },
        });
        var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_test_{System.Guid.NewGuid():N}.w3x");
        var outDir = Path.Combine(Path.GetTempPath(), $"wc3ctl_extract_{System.Guid.NewGuid():N}");
        File.WriteAllBytes(path, map);
        try
        {
            var sw = new StringWriter();
            var console = System.Console.Out;
            System.Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(new[] { "extract", path, "--models", "-o", outDir });
            System.Console.SetOut(console);
            Assert.Equal(0, code);
            var written = Path.Combine(outDir, "war3mapImported", "a.mdx");
            Assert.True(File.Exists(written), $"expected {written} to exist");
            Assert.Equal(mdxBytes, File.ReadAllBytes(written));
            Assert.False(File.Exists(Path.Combine(outDir, "b.blp")));   // texture not selected
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task Object_get_unknown_rawcode_is_clean_exit0()
    {
        // Bad --game-dir must degrade to deltas-only (hermetic: never falls back
        // to a real install), and an unknown rawcode is a clean "not found".
        var map = SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1 } });
        var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_og_{System.Guid.NewGuid():N}.w3x");
        File.WriteAllBytes(path, map);
        try
        {
            var sw = new StringWriter();
            var console = System.Console.Out;
            System.Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(new[] { "object", "get", path, "H999", "--game-dir", "Z:\\no_such" });
            System.Console.SetOut(console);
            Assert.Equal(0, code);
            Assert.Contains("not found", sw.ToString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Object_list_without_w3u_is_empty_exit0()
    {
        var map = SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1 } });
        var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_ol_{System.Guid.NewGuid():N}.w3x");
        File.WriteAllBytes(path, map);
        try
        {
            var sw = new StringWriter();
            var console = System.Console.Out;
            System.Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(
                new[] { "object", "list", path, "--json", "--game-dir", "Z:\\no_such" });
            System.Console.SetOut(console);
            Assert.Equal(0, code);
            Assert.Contains("\"Items\": []", sw.ToString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Bundle_unit_json_emits_closure_with_string_kinds()
    {
        // Hermetic: bad --game-dir degrades to map-deltas-only resolution.
        var w3u = new War3Net.Build.Object.UnitObjectData(War3Net.Build.Object.ObjectDataFormatVersion.v2);
        var unit = new War3Net.Build.Object.SimpleObjectModification
        {
            OldId = War3Net.Common.Extensions.StringExtensions.FromRawcode("Hpal"),
            NewId = War3Net.Common.Extensions.StringExtensions.FromRawcode("H000"),
        };
        unit.Modifications.Add(new War3Net.Build.Object.SimpleObjectDataModification
        {
            Id = War3Net.Common.Extensions.StringExtensions.FromRawcode("uabi"),
            Type = War3Net.Build.Object.ObjectDataType.String,
            Value = "A000",
        });
        w3u.NewUnits.Add(unit);
        var w3a = new War3Net.Build.Object.AbilityObjectData(War3Net.Build.Object.ObjectDataFormatVersion.v2);
        w3a.NewAbilities.Add(new War3Net.Build.Object.LevelObjectModification
        {
            OldId = War3Net.Common.Extensions.StringExtensions.FromRawcode("AHbz"),
            NewId = War3Net.Common.Extensions.StringExtensions.FromRawcode("A000"),
        });

        byte[] Serialize(Action<BinaryWriter> write)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) write(bw);
            return ms.ToArray();
        }
        var map = SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Serialize(w => War3Net.Build.Extensions.BinaryWriterExtensions.Write(w, w3u)),
            ["war3map.w3a"] = Serialize(w => War3Net.Build.Extensions.BinaryWriterExtensions.Write(w, w3a)),
        });
        var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_bu_{System.Guid.NewGuid():N}.w3x");
        File.WriteAllBytes(path, map);
        try
        {
            var sw = new StringWriter();
            var console = System.Console.Out;
            System.Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(
                new[] { "bundle", "unit", path, "H000", "--json", "--game-dir", "Z:\\no_such" });
            System.Console.SetOut(console);
            Assert.Equal(0, code);
            var output = sw.ToString();
            Assert.Contains("\"RootRawcode\": \"H000\"", output);
            Assert.Contains("\"Rawcode\": \"A000\"", output);
            Assert.Contains("\"Kind\": \"Ability\"", output);   // enum serialized by name
            Assert.Contains("\"Functions\": []", output);       // script-less map → empty closure
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Object_set_with_kind_edits_an_ability_level_and_saves_copy()
    {
        var w3a = new War3Net.Build.Object.AbilityObjectData(War3Net.Build.Object.ObjectDataFormatVersion.v2);
        var abil = new War3Net.Build.Object.LevelObjectModification
        {
            OldId = War3Net.Common.Extensions.StringExtensions.FromRawcode("AHbz"),
            NewId = War3Net.Common.Extensions.StringExtensions.FromRawcode("A000"),
        };
        abil.Modifications.Add(new War3Net.Build.Object.LevelObjectDataModification
        {
            Level = 1,
            Pointer = 0,
            Id = War3Net.Common.Extensions.StringExtensions.FromRawcode("adur"),
            Type = War3Net.Build.Object.ObjectDataType.Unreal,
            Value = 3.5f,
        });
        w3a.NewAbilities.Add(abil);
        byte[] Serialize(Action<BinaryWriter> write)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) write(bw);
            return ms.ToArray();
        }
        var map = SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3a"] = Serialize(w => War3Net.Build.Extensions.BinaryWriterExtensions.Write(w, w3a)),
        });
        var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_os_{System.Guid.NewGuid():N}.w3x");
        var outPath = Path.Combine(Path.GetTempPath(), $"wc3ctl_os_out_{System.Guid.NewGuid():N}.w3x");
        File.WriteAllBytes(path, map);
        try
        {
            var sw = new StringWriter();
            var console = System.Console.Out;
            System.Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(
                new[] { "object", "set", path, "A000", "adur:1", "9.5", "--kind", "ability", "-o", outPath });
            System.Console.SetOut(console);
            Assert.Equal(0, code);
            Assert.Contains("set adur:1=9.5 on A000", sw.ToString());

            var reloaded = Wc3.Model.MapDocument.Load(outPath);
            var model = (War3Net.Build.Object.AbilityObjectData)reloaded.GetFile("war3map.w3a")!.Model!;
            var mod = model.NewAbilities.Single().Modifications.Single();
            Assert.Equal(9.5f, mod.Value);
        }
        finally
        {
            File.Delete(path);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Fact]
    public async Task Object_new_prints_fresh_rawcode_and_saves_edited_copy()
    {
        var w3u = new War3Net.Build.Object.UnitObjectData(War3Net.Build.Object.ObjectDataFormatVersion.v2);
        w3u.NewUnits.Add(new War3Net.Build.Object.SimpleObjectModification
        {
            OldId = War3Net.Common.Extensions.StringExtensions.FromRawcode("Hpal"),
            NewId = War3Net.Common.Extensions.StringExtensions.FromRawcode("H000"),
        });
        byte[] Serialize(Action<BinaryWriter> write)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) write(bw);
            return ms.ToArray();
        }
        var map = SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3u"] = Serialize(w => War3Net.Build.Extensions.BinaryWriterExtensions.Write(w, w3u)),
        });
        var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_on_{System.Guid.NewGuid():N}.w3x");
        var editedPath = Path.Combine(
            Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".edited.w3x");
        File.WriteAllBytes(path, map);
        try
        {
            var sw = new StringWriter();
            var console = System.Console.Out;
            System.Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(new[] { "object", "new", path, "unit", "H000" });
            System.Console.SetOut(console);
            Assert.Equal(0, code);
            Assert.Contains("created unit H001 (base H000)", sw.ToString());
            Assert.True(File.Exists(editedPath), "default output <map>.edited.w3x must exist");

            var reloaded = Wc3.Model.MapDocument.Load(editedPath);
            var model = (War3Net.Build.Object.UnitObjectData)reloaded.GetFile("war3map.w3u")!.Model!;
            Assert.Contains(model.NewUnits, u =>
                u.NewId == War3Net.Common.Extensions.StringExtensions.FromRawcode("H001")
                && u.OldId == War3Net.Common.Extensions.StringExtensions.FromRawcode("H000"));
        }
        finally
        {
            File.Delete(path);
            if (File.Exists(editedPath)) File.Delete(editedPath);
        }
    }

    [Fact]
    public async Task Missing_file_exits_nonzero_with_clean_message_no_stack_trace()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"wc3ctl_missing_{System.Guid.NewGuid():N}.w3x");
        var outW = new StringWriter();
        var errW = new StringWriter();
        var origOut = System.Console.Out;
        var origErr = System.Console.Error;
        System.Console.SetOut(outW);
        System.Console.SetError(errW);
        try
        {
            int code = await Wc3Ctl.Program.Main(new[] { "info", missing });
            Assert.Equal(1, code);
            var err = errW.ToString();
            Assert.Contains("error:", err);
            Assert.DoesNotContain("Exception", err);      // no raw exception type
            Assert.DoesNotContain("   at ", err);          // no stack-trace frames
        }
        finally
        {
            System.Console.SetOut(origOut);
            System.Console.SetError(origErr);
        }
    }
}
