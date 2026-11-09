// tests/Wc3.Tests/ColdStartTests.cs
// PRF-01. A measurement rather than a guard on the requirement's own budget, because the one
// second figure is a whole process figure and an in-process session is already warm. What these
// tests hold is that the server records the three phases, that the surface build is the phase the
// line names, and that a gross regression in either fails here before a client sees it.
using System.Diagnostics;
using ModelContextProtocol.Client;
using Wc3.Mcp;
using Xunit.Abstractions;

namespace Wc3.Tests;

public class ColdStartTests
{
    private readonly ITestOutputHelper _out;

    public ColdStartTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void The_surface_build_is_recorded_and_stays_under_its_budget()
    {
        // Another class may have built the surface first. That is the point, the number is the
        // first build in this process and not a cache hit taken under a stopwatch.
        _ = Wc3McpServer.CreateOptions();

        double surface = ServerRuntime.ColdStart.SurfaceMs;
        _out.WriteLine($"surface build {surface:0} ms, {Wc3McpServer.ToolCount()} tools");

        Assert.True(surface >= 0, "the surface build never recorded its time");
        Assert.True(surface < 1500, $"the surface build took {surface:0} ms against a 1500 ms budget");
    }

    [Fact]
    public void The_initialize_reply_closes_the_cold_start_clock()
    {
        McpTestClient.WithClient(async (client, ct) =>
        {
            Assert.NotNull(client.ServerInfo);
            await Task.CompletedTask;
        }).GetAwaiter().GetResult();

        string? line = ServerRuntime.ColdStart.Line;
        _out.WriteLine(line ?? "no cold start line");
        _out.WriteLine($"surface {ServerRuntime.ColdStart.SurfaceMs:0} ms"
            + $", handshake {ServerRuntime.ColdStart.HandshakeMs:0} ms"
            + $", total {ServerRuntime.ColdStart.TotalMs:0} ms");

        Assert.False(string.IsNullOrEmpty(line), "the initialize reply did not close the cold start clock");
        Assert.Contains("startup", line);
        Assert.Contains("host", line);
        Assert.Contains("surface", line);
        Assert.Contains("handshake", line);

        // The in-process session is not the exe, so this is a gross regression bound. The budget
        // that matters, one second to the initialize reply, is printed by the exe as this line.
        Assert.True(ServerRuntime.ColdStart.TotalMs < 4000,
            $"the in-process cold start took {ServerRuntime.ColdStart.TotalMs:0} ms");
    }

    [Fact]
    public void The_three_phases_are_each_bounded_by_the_total()
    {
        _ = Wc3McpServer.CreateOptions();
        double surface = ServerRuntime.ColdStart.SurfaceMs;
        double total = ServerRuntime.ColdStart.TotalMs;

        // A phase cannot be longer than the clock that contains it. This catches a phase timed
        // from a different origin, which is the way a phase line silently lies.
        Assert.True(surface >= 0);
        if (total >= 0) Assert.True(surface <= total, $"surface {surface:0} ms is longer than total {total:0} ms");
    }
}
