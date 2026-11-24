// tests/Wc3.Tests/MemoryBudgetTests.cs
// PRF-09. The budget is stated in ServerRuntime.MemoryBudget with the measurement it came from,
// and a call that would exceed it is refused rather than attempted. The end to end case uses a
// sparse file, so the archive is 300 MB by length without 300 MB of disk or of reading.
using ModelContextProtocol.Protocol;
using Wc3.Mcp;

namespace Wc3.Tests;

public sealed class MemoryBudgetTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "wc3ctl-memory-budget", Guid.NewGuid().ToString("N"));

    public MemoryBudgetTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void The_projection_admits_the_measured_map_and_refuses_the_largest_one_on_disk()
    {
        // Measured on the reference machine. A 38 MB archive peaked at 201 MB of working set.
        Assert.False(ServerRuntime.MemoryBudget.WouldExceed(38L * 1024 * 1024));

        // The largest map in the reference corpus is 495 MB and did not answer in seven minutes.
        Assert.True(ServerRuntime.MemoryBudget.WouldExceed(495L * 1024 * 1024));

        // The reference map itself is 767 KB and is never near the line.
        Assert.False(ServerRuntime.MemoryBudget.WouldExceed(767L * 1024));

        Assert.Equal(92_274_688, ServerRuntime.MemoryBudget.LargestSupportedInputBytes);
    }

    [Fact]
    public void An_input_past_the_budget_is_refused_before_it_is_read()
    {
        string map = Path.Combine(_dir, "oversized.w3x");
        using (var file = new FileStream(map, FileMode.CreateNew, FileAccess.Write))
            file.SetLength(300L * 1024 * 1024);

        CallToolResult? result = null;
        McpTestClient.WithClient(async (client, ct) =>
        {
            result = await client.CallToolAsync(
                "map_info",
                new Dictionary<string, object?> { ["map"] = map },
                cancellationToken: ct);
        }).GetAwaiter().GetResult();

        Assert.NotNull(result);
        Assert.True(result!.IsError == true, "a 300 MB input was attempted");
        string text = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
        Assert.Contains("PRF09_MEMORY_BUDGET", text);
        Assert.Contains(ServerRuntime.MemoryBudget.BudgetBytes.ToString(), text);
        Assert.Contains("Windows 10 x64", text);
    }

    [Fact]
    public void The_variable_raises_the_budget_so_a_big_map_is_admitted()
    {
        // The 331 MB map this was asked for projects to 3.02 GiB, past the 1 GiB default.
        long big = 346_903_449;
        Assert.True(ServerRuntime.MemoryBudget.WouldExceed(big));

        RunWith("4G", () =>
        {
            Assert.Equal(4L * 1024 * 1024 * 1024, ServerRuntime.MemoryBudget.BudgetBytes);
            Assert.False(ServerRuntime.MemoryBudget.WouldExceed(big));
            Assert.True(ServerRuntime.MemoryBudget.WouldExceed(600L * 1024 * 1024));
            Assert.Contains(ServerRuntime.MemoryBudget.BudgetEnvVar, ServerRuntime.MemoryBudget.Explain(big));
        });

        // The guard is back to the default once the variable is gone, so one raised process does
        // not leave the ceiling open for the next call.
        Assert.Equal(ServerRuntime.MemoryBudget.DefaultBudgetBytes, ServerRuntime.MemoryBudget.BudgetBytes);
        Assert.True(ServerRuntime.MemoryBudget.WouldExceed(big));
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("0")]
    [InlineData("-2048")]
    [InlineData("99999999999999999999")]
    public void A_value_that_does_not_parse_leaves_the_default_in_force(string value)
    {
        RunWith(value, () =>
        {
            Assert.Equal(ServerRuntime.MemoryBudget.DefaultBudgetBytes, ServerRuntime.MemoryBudget.BudgetBytes);
            Assert.Contains("Windows 10 x64", ServerRuntime.MemoryBudget.Explain(300L * 1024 * 1024));
        });
    }

    [Theory]
    [InlineData("4096M", 4096L * 1024 * 1024)]
    [InlineData("1G", 1024L * 1024 * 1024)]
    public void A_prefix_suffix_value_is_read_as_bytes(string value, long expected) =>
        RunWith(value, () => Assert.Equal(expected, ServerRuntime.MemoryBudget.BudgetBytes));

    /// <summary>Set the variable for one test body and restore what was there before.</summary>
    private static void RunWith(string? value, Action body)
    {
        string variable = ServerRuntime.MemoryBudget.BudgetEnvVar;
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, value);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }
}
