// tests/Wc3.Tests/CiCliTests.cs
// End-to-end tests for the wc3ctl ci command group, driven through Program.Main so the exit code
// a CI step would read is what is asserted. Hermetic, a synthetic map and a temporary schema
// folder only.
using System.Text;

namespace Wc3.Tests;

[Collection("Serial")]
public class CiCliTests : IDisposable
{
    private static readonly byte[] Blob = { 1, 2, 3, 4 };
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "wc3ctl-ci-cli", Guid.NewGuid().ToString("N"));

    public CiCliTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Map(params (string Name, byte[] Bytes)[] entries)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, bytes) in entries) files[name] = bytes;
        var path = Path.Combine(_dir, "map-" + Guid.NewGuid().ToString("N") + ".w3x");
        File.WriteAllBytes(path, SyntheticMap.Build(files));
        return path;
    }

    private string ValidMap() => Map(("war3map.w3i", Blob), ("war3map.w3e", Blob), ("war3map.j", Blob));

    private async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var outW = new StringWriter();
        var errW = new StringWriter();
        var origOut = Console.Out;
        var origErr = Console.Error;
        Console.SetOut(outW);
        Console.SetError(errW);
        try
        {
            int code = await Wc3Ctl.Program.Main(args);
            return (code, outW.ToString(), errW.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    [Fact]
    public async Task Ci_all_prints_a_summary_line_per_gate_and_exits_zero_on_pass()
    {
        var (code, output, _) = await Run("ci", "all", ValidMap());

        Assert.Equal(0, code);
        Assert.Contains("PASS  lint", output);
        Assert.Contains("PASS  validate", output);
        Assert.Contains("ci all PASS  exit 0", output);
    }

    [Fact]
    public async Task Ci_validate_exits_nonzero_on_an_invalid_map()
    {
        var (code, output, _) = await Run("ci", "validate", Map(("war3map.j", Blob)));

        Assert.NotEqual(0, code);
        Assert.Contains("ci validate FAIL", output);
    }

    [Fact]
    public async Task Ci_lint_baseline_gate_exits_nonzero_on_a_new_finding()
    {
        var map = Map(("war3map.j", Blob), ("junk.xyz", Blob));
        var baseline = Path.Combine(_dir, "baseline.json");
        var (writeCode, _, _) = await Run("lint", "baseline", map, "-o", baseline);
        Assert.Equal(0, writeCode);

        var (sameCode, sameOut, _) = await Run("ci", "lint", map, "--baseline", baseline);
        Assert.Equal(0, sameCode);
        Assert.Contains("ci lint PASS", sameOut);

        var changed = Map(("war3map.j", Blob), ("junk.xyz", Blob), ("other.abc", Blob));
        var (newCode, newOut, _) = await Run("ci", "lint", changed, "--baseline", baseline);
        Assert.NotEqual(0, newCode);
        Assert.Contains("ci lint FAIL", newOut);
    }

    [Fact]
    public async Task Ci_compat_requires_a_target()
    {
        var (missing, _, _) = await Run("ci", "compat", ValidMap());
        Assert.NotEqual(0, missing);
    }

    [Fact]
    public async Task Ci_compat_exits_nonzero_on_an_error_finding()
    {
        GamedataFixtures.Save(_dir, "TFT-v1.30.1.10211", natives: new[] { "NewTargetNative" });
        GamedataFixtures.Save(_dir, "TFT-v1.31.0.10420", natives: new[] { "NewTargetNative", "ExtraNative" });
        var map = Map(("war3map.w3i", Blob), ("war3map.w3e", Blob),
            ("war3map.j", Encoding.ASCII.GetBytes(
                "function f takes nothing returns nothing\n    call ExtraNative()\nendfunction\n")));

        var (code, output, _) = await Run(
            "ci", "compat", map, "--target", "TFT-v1.30.1.10211", "--schemas", _dir);

        Assert.NotEqual(0, code);
        Assert.Contains("ci compat FAIL", output);
    }

    [Fact]
    public async Task Ci_schema_exit_code_follows_the_real_object_data_floor()
    {
        GamedataFixtures.Save(_dir, "TFT-v1.30.1.10211", "units/unitmetadata.slk",
            new[] { GamedataFixtures.Field("Cool1", "int", "0", "100", true) });

        var (pass, passOut, _) = await Run("ci", "schema", "--schemas", _dir, "--min-real", "1");
        Assert.Equal(0, pass);
        Assert.Contains("ci schema PASS", passOut);

        var (fail, failOut, _) = await Run("ci", "schema", "--schemas", _dir, "--min-real", "2");
        Assert.NotEqual(0, fail);
        Assert.Contains("ci schema FAIL", failOut);
    }

    [Fact]
    public async Task Ci_all_json_carries_the_gates_and_exit_code()
    {
        var (code, output, _) = await Run("ci", "all", ValidMap(), "--json");

        Assert.Equal(0, code);
        Assert.Contains("\"Passed\": true", output);
        Assert.Contains("\"ExitCode\": 0", output);
        Assert.Contains("\"lint\"", output);
        Assert.Contains("\"validate\"", output);
    }
}
