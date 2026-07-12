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
