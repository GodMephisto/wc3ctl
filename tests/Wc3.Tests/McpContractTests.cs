// tests/Wc3.Tests/McpContractTests.cs
// Contract facts that are not protocol plumbing: the log level the server runs at (ENV-05).
// The initialize capabilities are measured from the published exe by tools/measure_surface.py,
// because the pinned SDK derives them from the registered collections and ignores the options.
using Microsoft.Extensions.Logging;
using Wc3.Mcp;

namespace Wc3.Tests;

public class McpContractTests
{
    [Theory]
    [InlineData("error", LogLevel.Error)]
    [InlineData("warn", LogLevel.Warning)]
    [InlineData(" WARN ", LogLevel.Warning)]
    [InlineData("warning", LogLevel.Warning)]
    [InlineData("info", LogLevel.Information)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("trace", LogLevel.Warning)]
    [InlineData("payload", LogLevel.Warning)]
    [InlineData("", LogLevel.Warning)]
    [InlineData(null, LogLevel.Warning)]
    public void Wc3_mcp_log_maps_to_a_level_and_an_unknown_value_falls_back_to_warn(string? value, LogLevel expected)
    {
        Assert.Equal(expected, McpLogLevel.Parse(value));
    }

    [Fact]
    public void The_start_line_carries_the_build_the_target_and_the_profile()
    {
        string line = Wc3McpServer.BuildStartLine();

        Assert.Contains(Wc3.Mcp.ServerIdentity.ProductId, line);
        Assert.Contains(Wc3.Mcp.ServerIdentity.Version, line);
        Assert.Contains("protocol ", line);
        Assert.Contains("tools " + Wc3McpServer.ToolCount(), line);
        Assert.Contains("target ", line);
        Assert.Contains("game ", line);
        Assert.Contains("profile " + Wc3McpServer.DefaultProfile, line);
        Assert.DoesNotContain("\n", line);
    }

    [Fact]
    public async Task The_stated_protocol_revision_is_the_one_the_handshake_answers()
    {
        await McpTestClient.WithClient((client, ct) =>
        {
            Assert.Equal(Wc3McpServer.ProtocolRevision, client.NegotiatedProtocolVersion);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void The_instructions_carry_the_product_the_rules_and_the_routing_hint()
    {
        string text = Wc3McpServer.CreateOptions().ServerInstructions ?? "";

        Assert.Contains(Wc3.Mcp.ServerIdentity.ProductId, text);
        Assert.Contains("target", text);
        Assert.Contains(".edited", text);
        Assert.Contains("quickstart", text);
        Assert.True(text.Length < 1000, "the instructions are " + text.Length + " characters");
    }

    [Fact]
    public void A_folder_is_not_a_map()
    {
        string folder = Path.Combine(Path.GetTempPath(), "wc3-map-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var error = Assert.Throws<ModelContextProtocol.McpException>(() => Wc3Tools.MapInfo(folder));
            Assert.Contains("folder", error.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_default_level_is_warn()
    {
        // No variable set on the build machine, so FromEnvironment reads nothing and lands on warn.
        Assert.Equal(LogLevel.Warning, McpLogLevel.Parse(null));
    }
}
