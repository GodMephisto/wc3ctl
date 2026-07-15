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
