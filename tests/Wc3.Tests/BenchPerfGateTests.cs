// tests/Wc3.Tests/BenchPerfGateTests.cs
// Hermetic tests for BenchPerfGateCommand. The comparison is pure data, so the cases run with
// BenchRun objects built in memory. The Execute path reads two temporary JSON files. One CLI test
// pins the non-zero exit code, in the Serial collection because it redirects the console.
using Wc3.Commands;

namespace Wc3.Tests;

public sealed class BenchPerfGateTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "wc3ctl-bench-gate", Guid.NewGuid().ToString("N"));

    public BenchPerfGateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static BenchRun Run(double ttft, double throughput, params string[] higher) =>
        new("run", null,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["ttft_ms"] = ttft, ["throughput_tps"] = throughput,
            },
            higher);

    [Fact]
    public void A_metric_above_the_threshold_fails_the_gate()
    {
        var r = BenchPerfGateCommand.Compare(Run(100, 1000, "throughput_tps"), Run(120, 1000, "throughput_tps"), 10);
        Assert.False(r.Pass);
        Assert.Equal(1, r.Regressed);
        var ttft = r.Metrics.Single(m => m.Metric == "ttft_ms");
        Assert.True(ttft.Regressed);
        Assert.Equal(20, ttft.ChangePct!.Value, 3);
    }

    [Fact]
    public void A_metric_at_or_below_the_threshold_passes()
    {
        var r = BenchPerfGateCommand.Compare(Run(100, 1000, "throughput_tps"), Run(105, 1000, "throughput_tps"), 10);
        Assert.True(r.Pass);
        Assert.Equal(0, r.Regressed);
    }

    [Fact]
    public void An_improvement_in_every_metric_passes()
    {
        var r = BenchPerfGateCommand.Compare(Run(100, 1000, "throughput_tps"), Run(80, 1100, "throughput_tps"), 10);
        Assert.True(r.Pass);
        Assert.All(r.Metrics, m => Assert.False(m.Regressed));
    }

    [Fact]
    public void A_higher_is_better_metric_regresses_when_it_falls()
    {
        var r = BenchPerfGateCommand.Compare(Run(100, 1000, "throughput_tps"), Run(100, 500, "throughput_tps"), 10);
        var tps = r.Metrics.Single(m => m.Metric == "throughput_tps");
        Assert.True(tps.Regressed);
        Assert.False(r.Pass);
    }

    [Fact]
    public void A_zero_baseline_that_becomes_positive_is_a_regression_for_a_cost_metric()
    {
        var baseline = new BenchRun("b", null,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["errors"] = 0 }, Array.Empty<string>());
        var run = new BenchRun("r", null,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["errors"] = 5 }, Array.Empty<string>());

        var r = BenchPerfGateCommand.Compare(baseline, run, 10);
        var errors = Assert.Single(r.Metrics);
        Assert.Null(errors.ChangePct);
        Assert.True(errors.Regressed);
        Assert.False(r.Pass);
    }

    [Fact]
    public void Metrics_in_only_one_run_are_reported_not_counted()
    {
        var baseline = new BenchRun("b", null,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 }, Array.Empty<string>());
        var run = new BenchRun("r", null,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["a"] = 1, ["c"] = 3 }, Array.Empty<string>());

        var r = BenchPerfGateCommand.Compare(baseline, run, 10);
        Assert.True(r.Pass);
        Assert.Equal(1, r.MetricsCompared);
        Assert.Equal(new[] { "b" }, r.MissingInRun);
        Assert.Equal(new[] { "c" }, r.AddedInRun);
    }

    [Fact]
    public void Execute_reads_two_json_files()
    {
        File.WriteAllText(Path.Combine(_dir, "baseline.json"),
            "{ \"name\": \"base\", \"metrics\": { \"ttft_ms\": 100.0 }, \"higherIsBetter\": [] }");
        File.WriteAllText(Path.Combine(_dir, "run.json"),
            "{ \"name\": \"run\", \"metrics\": { \"ttft_ms\": 140.0 }, \"higherIsBetter\": [] }");

        var r = BenchPerfGateCommand.Execute(
            Path.Combine(_dir, "baseline.json"), Path.Combine(_dir, "run.json"), 10);

        Assert.False(r.Pass);
        Assert.Equal(40, Assert.Single(r.Metrics).ChangePct!.Value, 3);
    }

    [Fact]
    public void Load_rejects_a_file_with_no_metrics()
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, "{ \"name\": \"bad\" }");
        Assert.Throws<InvalidDataException>(() => BenchPerfGateCommand.Load(path));
    }
}

[Collection("Serial")]
public sealed class BenchPerfGateCliTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "wc3ctl-bench-gate-cli", Guid.NewGuid().ToString("N"));

    public BenchPerfGateCliTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Cli_exits_non_zero_when_a_metric_regressed()
    {
        var baseline = Path.Combine(_dir, "baseline.json");
        var run = Path.Combine(_dir, "run.json");
        File.WriteAllText(baseline, "{ \"metrics\": { \"ttft_ms\": 100.0 } }");
        File.WriteAllText(run, "{ \"metrics\": { \"ttft_ms\": 200.0 } }");

        var console = System.Console.Out;
        System.Console.SetOut(new StringWriter());
        int code = await Wc3Ctl.Program.Main(new[] { "bench", "perf-gate", baseline, run, "--max-regression-pct", "10" });
        System.Console.SetOut(console);

        Assert.NotEqual(0, code);
    }

    [Fact]
    public async Task Cli_exits_zero_when_within_the_threshold()
    {
        var baseline = Path.Combine(_dir, "baseline2.json");
        var run = Path.Combine(_dir, "run2.json");
        File.WriteAllText(baseline, "{ \"metrics\": { \"ttft_ms\": 100.0 } }");
        File.WriteAllText(run, "{ \"metrics\": { \"ttft_ms\": 105.0 } }");

        var console = System.Console.Out;
        System.Console.SetOut(new StringWriter());
        int code = await Wc3Ctl.Program.Main(new[] { "bench", "perf-gate", baseline, run, "--max-regression-pct", "10" });
        System.Console.SetOut(console);

        Assert.Equal(0, code);
    }
}
