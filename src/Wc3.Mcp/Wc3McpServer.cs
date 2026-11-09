// src/Wc3.Mcp/Wc3McpServer.cs
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Wc3.Mcp;

/// <summary>
/// Single source of truth for the server identity and tool set, shared by the
/// stdio entry point (Program) and the in-memory protocol tests, so what the
/// tests verify is exactly what a real MCP client sees.
/// </summary>
public static class Wc3McpServer
{
    /// <summary>Result POCOs serialize like the CLI's --json, enums as names ("Unit"), not numbers.</summary>
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        options.Converters.Add(new JsonStringEnumConverter());
        // A placed unit's or doodad's scale is a tuple, which printed as {} (see the converter).
        options.Converters.Add(new Wc3.Commands.ScaleJsonConverter());
        return options;
    }

    /// <summary>Server options with every wc3ctl tool registered.</summary>
    public static McpServerOptions CreateOptions()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var tool in CreateTools())
            tools.Add(tool);

        var resources = new McpServerResourceCollection();
        resources.Add(McpToolCatalog.CreateCatalogResource());
        foreach (var resource in Wc3Context.CreateResources())
            resources.Add(resource);
        foreach (var resource in Wc3Skills.CreateResources())
            resources.Add(resource);

        var prompts = new McpServerPrimitiveCollection<McpServerPrompt>();
        foreach (var prompt in CreatePrompts())
            prompts.Add(prompt);

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = ServerIdentity.ProductId, Version = ServerIdentity.Version },
            ToolCollection = tools,
            PromptCollection = prompts,
            ResourceCollection = resources,
            ServerInstructions = Instructions.Value,
        };

        // OBS-02, OBS-04, PRF-01, PRF-04 and TRN-03 ride the SDK 1.4.1 filter pipeline, which is
        // built from these lists when the server starts. The request filter wraps the tool
        // invocation, the incoming message filter catches the initialize request and the outgoing
        // one sees the response after it reaches the transport, so a client that closed stdin mid
        // call still gets the bytes before the read side reports end of stream.
        options.Filters.Request.CallToolFilters.Add(ServerRuntime.ObserveToolCalls);
        options.Filters.Message.IncomingFilters.Add(ServerRuntime.ObserveIncomingMessages);
        options.Filters.Message.OutgoingFilters.Add(ServerRuntime.ObserveOutgoingResponses);

        ServerRuntime.ColdStart.SurfaceBuilt(started);
        return options;
    }

    /// <summary>The only profile there is, until SUR-07 adds a second one.</summary>
    public const string DefaultProfile = "full";

    /// <summary>
    /// The newest MCP revision the pinned SDK speaks, and what an unknown offer falls back to.
    /// Measured on 2026-11-08 over a raw pipe, every revision the SDK knows is echoed back
    /// (2024-11-05, 2025-03-26, 2025-06-18 and 2025-11-25), and an unknown or malformed offer gets
    /// this value with no error. The SDK keeps its constants internal, so ProtocolRevisionTests
    /// pins the matrix and fails if a pin changes an answer.
    /// </summary>
    public const string ProtocolRevision = "2025-11-25";

    /// <summary>What the target and the game install resolved to, read once per process.</summary>
    /// <param name="Target">The build the compatibility tools check against.</param>
    /// <param name="Source">Where that build came from, such as settings or the environment.</param>
    /// <param name="GameBuild">The installed game build, or null when there is no install.</param>
    public sealed record StartupFacts(string Target, string Source, string? GameBuild);

    /// <summary>
    /// Resolved once per process. Reading it walks the schemas folder, so recomputing it for every
    /// session made each in-memory MCP test pay for a full scan and the suite took minutes instead
    /// of seconds. The instructions and the start line both read this one value.
    /// </summary>
    public static readonly Lazy<StartupFacts> Startup =
        new(ReadStartup, LazyThreadSafetyMode.ExecutionAndPublication);

    private static StartupFacts ReadStartup()
    {
        try
        {
            var settings = Wc3.Commands.TargetBuildSettings.Get();
            string source = settings.Source switch
            {
                Wc3.Commands.TargetSource.Option => "option",
                Wc3.Commands.TargetSource.Environment => "environment variable",
                Wc3.Commands.TargetSource.Settings => "settings",
                Wc3.Commands.TargetSource.Default => "default",
                _ => "unknown",
            };
            string? installDir = null;
            try { installDir = Wc3.GameData.GameInstall.Locate(); } catch { /* ignore */ }
            string? installBuild = null;
            if (installDir is not null)
            {
                try { installBuild = Wc3.GameData.GameInstall.ReadBuildVersion(installDir); } catch { /* ignore */ }
            }
            return new StartupFacts(settings.Build, source, installBuild);
        }
        catch
        {
            return new StartupFacts("unresolved", "unavailable", null);
        }
    }

    private static readonly Lazy<string> Instructions =
        new(BuildInstructions, LazyThreadSafetyMode.ExecutionAndPublication);

    private static string BuildInstructions()
    {
        StartupFacts facts = Startup.Value;
        string target = facts.Source == "unavailable"
            ? "target unresolved (no schemas directory configured), game data not found"
            : "target " + facts.Target + " (" + facts.Source + "), game data "
                + (facts.GameBuild is null ? "not found" : "available (" + facts.GameBuild + ")");
        // DSC-01. The three rules and the routing hint are the point. A caller that reads this far
        // and nothing else still cannot overwrite its input.
        return ServerIdentity.ProductId + " " + ServerIdentity.Version + ", " + target + ". "
            + "Three rules. Read a map before you change it. "
            + "A write never overwrites its input, it writes a sibling .edited copy or the out_path you name. "
            + "Chain edits by passing the previous output as the next call's map. "
            + "Call quickstart when the request names an outcome rather than a tool, "
            + "and read wc3://tools for the whole surface.";
    }

    /// <summary>
    /// The one start line ENV-04 requires, on stderr, so the first question in a client bug report
    /// has an answer. The tool count is read from the same reflection the surface uses.
    /// </summary>
    public static string BuildStartLine()
    {
        StartupFacts facts = Startup.Value;
        string line = ServerIdentity.ProductId + " " + ServerIdentity.Version
            + "  protocol " + ProtocolRevision
            + "  tools " + ToolCount()
            + "  target " + facts.Target + " (" + facts.Source + ")"
            + "  game " + (facts.GameBuild ?? "none")
            + "  profile " + DefaultProfile;

        // PRF-05. Only when this process is already over a gigabyte, and only then naming the work
        // that is running. At startup nothing is in flight, so the plain line is the normal case.
        if (ServerRuntime.IsOverMemoryLimit())
        {
            line += "  memory " + ServerRuntime.MemoryMegabytes() + " MB"
                + " in tool " + (ServerRuntime.CurrentTool ?? "none");
        }
        return line;
    }

    /// <summary>The methods that carry a tool, before any of them is turned into a handler. Scanned
    /// once per process, because the start line needs the count before the host builds and the
    /// surface then scans the same list. Two scans of 275 attributes cost part of the cold start
    /// budget for no gain.</summary>
    private static readonly Lazy<IReadOnlyList<MethodInfo>> ToolMethodList = new(
        () => new[] { typeof(Wc3Tools), typeof(Wc3Context) }
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The methods that carry a tool.</summary>
    private static IEnumerable<MethodInfo> ToolMethods() => ToolMethodList.Value;

    /// <summary>
    /// Every tool, bound once per process. Binding is a schema build per method, so a second
    /// session in the same process was paying for the whole surface again. The bound tools hold no
    /// state, so one list serves every session.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<McpServerTool>> ToolList = new(
        () => ToolMethodList.Value
            .Select(m => McpServerTool.Create(m, target: null,
                new McpServerToolCreateOptions { SerializerOptions = JsonOptions }))
            .ToList(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Every [McpServerTool] method on <see cref="Wc3Tools"/> and <see cref="Wc3Context"/>, bound with our JSON options.</summary>
    public static IReadOnlyList<McpServerTool> CreateTools() => ToolList.Value;

    /// <summary>
    /// How many tools the surface holds, counted without building a handler for each one. The start
    /// line needs the number before the host builds the surface, and the count comes from the one
    /// method scan rather than a second one.
    /// </summary>
    public static int ToolCount() => ToolMethodList.Value.Count;

    /// <summary>Every [McpServerPrompt] method on <see cref="Wc3Prompts"/>, bound with our JSON options.</summary>
    public static IReadOnlyList<McpServerPrompt> CreatePrompts() =>
        typeof(Wc3Prompts)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() is not null)
            .Select(m => McpServerPrompt.Create(m, target: null,
                new McpServerPromptCreateOptions { SerializerOptions = JsonOptions }))
            .ToList();

    /// <summary>
    /// Populates a host's options from <see cref="CreateOptions"/>. Both the stdio entry point and
    /// the in-memory protocol tests call this one method, so what the tests verify is exactly what a
    /// client receives, ServerInstructions included. Copying the fields by hand instead is what left
    /// the instructions off the initialize reply.
    /// </summary>
    public static void ApplyTo(McpServerOptions options) => ApplyTo(options, CreateOptions());

    /// <summary>
    /// The same copy from a surface that was already built. PRF-01 uses it so the tool collection is
    /// built on the thread pool while the host builds, rather than one phase after the other.
    /// </summary>
    public static void ApplyTo(McpServerOptions options, McpServerOptions configured)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configured);
        options.ServerInfo = configured.ServerInfo;
        options.Capabilities = configured.Capabilities;
        options.ServerInstructions = configured.ServerInstructions;
        options.ToolCollection = configured.ToolCollection;
        options.PromptCollection = configured.PromptCollection;
        options.ResourceCollection = configured.ResourceCollection;
        // The filters carry OBS-02, OBS-04, PRF-01, PRF-04 and TRN-03. Copying them by hand like the
        // rest is what keeps a host that calls ApplyTo from running without them.
        options.Filters = configured.Filters;
    }
}
