// tests/Wc3.Tests/ToolCancellationTests.cs
// PRF-04. The host side of progress and cancellation. A client that cancels has its token carried
// into the tool delegate, the call is recorded and named in one line, and a long family call
// reports progress while it runs. The tool bodies still do not take the token themselves, which is
// what the count at the end of this file measures.
using System.Reflection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Wc3.Mcp;
using Xunit.Abstractions;

namespace Wc3.Tests;

public class ToolCancellationTests
{
    private readonly ITestOutputHelper _out;

    public ToolCancellationTests(ITestOutputHelper output) => _out = output;

    private sealed class Captured : IProgress<ProgressNotificationValue>
    {
        private readonly List<ProgressNotificationValue> _values = new();
        public int Count { get { lock (_values) return _values.Count; } }
        public IReadOnlyList<ProgressNotificationValue> Values { get { lock (_values) return _values.ToList(); } }
        public void Report(ProgressNotificationValue value) { lock (_values) _values.Add(value); }
    }

    /// <summary>A server object the request context accepts. Nothing runs on it, so its transport
    /// is a pair of empty streams and no message is ever read.</summary>
    private static McpServer BareServer() =>
        McpServer.Create(
            new StreamServerTransport(new MemoryStream(), new MemoryStream()),
            Wc3McpServer.CreateOptions(),
            loggerFactory: null,
            serviceProvider: null);

    private static RequestContext<CallToolRequestParams> Context(McpServer server, string tool, long id) =>
        new(server, new JsonRpcRequest { Id = new RequestId(id), Method = "tools/call" })
        {
            Params = new CallToolRequestParams { Name = tool },
        };

    [Fact]
    public async Task A_client_cancel_stops_the_wait_and_is_recorded()
    {
        await using McpServer server = BareServer();
        int before = ServerRuntime.CancelledCalls;
        using var cancel = new CancellationTokenSource();

        // The body ignores the token on purpose. That is the shape every tool on this surface has
        // today, and the filter still has to stop waiting for it when the client cancels.
        McpRequestHandler<CallToolRequestParams, CallToolResult> chain = ServerRuntime.ObserveToolCalls(
            async (req, ct) =>
            {
                await Task.Delay(Timeout.Infinite, CancellationToken.None);
                return new CallToolResult();
            });

        Task<CallToolResult> call = chain(Context(server, "library_scan", 4242), cancel.Token).AsTask();
        cancel.CancelAfter(50);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        clock.Stop();

        Assert.True(clock.ElapsedMilliseconds < 2000,
            $"the cancel took {clock.ElapsedMilliseconds} ms to surface, so the wait was not raced");
        Assert.Equal(before + 1, ServerRuntime.CancelledCalls);
        Assert.Equal("library_scan", ServerRuntime.LastCancelledTool);
    }

    [Fact]
    public async Task A_call_that_is_not_cancelled_is_not_counted_as_one()
    {
        await using McpServer server = BareServer();
        int before = ServerRuntime.CancelledCalls;

        McpRequestHandler<CallToolRequestParams, CallToolResult> chain = ServerRuntime.ObserveToolCalls(
            (req, ct) => ValueTask.FromResult(new CallToolResult()));

        _ = await chain(Context(server, "map_info", 4243), CancellationToken.None);
        Assert.Equal(before, ServerRuntime.CancelledCalls);
    }

    [Fact]
    public void A_long_family_call_reports_progress_when_the_client_asks_for_it()
    {
        string folder = Path.Combine(Path.GetTempPath(), "wc3ctl-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var captured = new Captured();
            bool called = false;
            McpTestClient.WithClient(async (client, ct) =>
            {
                called = true;
                await client.CallToolAsync(
                    "library_scan",
                    new Dictionary<string, object?> { ["folder"] = folder },
                    progress: captured,
                    cancellationToken: ct);
            }).GetAwaiter().GetResult();

            Assert.True(called);

            // The first beat is sent as the call starts. It can be delivered a moment after the
            // reply, so the check waits for it rather than racing it.
            for (int i = 0; i < 100 && captured.Count == 0; i++) Thread.Sleep(20);

            _out.WriteLine($"{captured.Count} progress notification(s): "
                + string.Join(" | ", captured.Values.Take(3).Select(v => v.Message)));
            Assert.NotEmpty(captured.Values);
            Assert.Contains("library_scan", captured.Values[0].Message);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Every_long_family_name_is_a_tool_on_the_surface()
    {
        var names = Wc3McpServer.CreateTools()
            .Select(t => t.ProtocolTool.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string tool in ServerRuntime.LongCallTools)
            Assert.Contains(tool, names);
    }

    [Fact]
    public void Count_the_long_family_tools_that_declare_a_cancellation_token()
    {
        var methods = typeof(Wc3Tools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToDictionary(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name!, StringComparer.Ordinal);

        var honours = ServerRuntime.LongCallTools
            .Where(name => methods.TryGetValue(name, out MethodInfo? method)
                && method.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)))
            .ToList();

        // A measurement, printed, because the filter already hands the token over and the day a
        // tool declares the parameter it honours the cancel with no change here. Nothing asserts
        // zero, which would fail the moment the surface improves.
        _out.WriteLine($"{honours.Count} of {ServerRuntime.LongCallTools.Count} long family tools declare a"
            + $" CancellationToken. Honouring it, {(honours.Count == 0 ? "none yet" : string.Join(", ", honours))}");
    }
}
