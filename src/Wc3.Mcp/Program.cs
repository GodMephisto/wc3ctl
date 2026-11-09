// src/Wc3.Mcp/Program.cs
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wc3.Mcp.Setup;

namespace Wc3.Mcp;

/// <summary>
/// With no arguments, the MCP server over stdio (newline-delimited JSON-RPC on stdin/stdout).
/// With a setup verb (install, uninstall, config, clients, doctor, help, version), the setup commands.
/// </summary>
public static class Program
{
    /// <summary>The one target this binary is published for. Anything else gets a refusal, not the
    /// operating system's message about a Windows executable.</summary>
    public const string SupportedTarget = "win-x64";

    public static async Task<int> Main(string[] args)
    {
        OSPlatform os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatform.Linux
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX
            : OSPlatform.Create("unknown");

        // Before any MCP output and before any setup verb, so stdout stays clean and the caller
        // sees one sentence instead of a loader error. Exit 3 is distinct from a command failure.
        if (PlatformRefusal(os, RuntimeInformation.ProcessArchitecture) is { } refusal)
        {
            Console.Error.WriteLine(refusal);
            return 3;
        }

        if (args.Length > 0)
            return SetupCli.Run(args, SetupEnvironment.Current, Console.Out, SetupProduct.Standalone);

        await RunServerAsync();
        return 0;
    }

    /// <summary>
    /// Why this build cannot run on the given platform, or null when it can. A function of the
    /// measured platform rather than of the running process, so the guard is testable for a
    /// platform this machine is not.
    /// </summary>
    internal static string? PlatformRefusal(OSPlatform os, Architecture arch)
    {
        if (os == OSPlatform.Windows && arch == Architecture.X64) return null;
        string osName = os == OSPlatform.Windows ? "Windows"
            : os == OSPlatform.Linux ? "Linux"
            : os == OSPlatform.OSX ? "macOS"
            : os.ToString();
        return "This build supports " + SupportedTarget + " only, so it cannot run on "
            + osName + " " + arch.ToString().ToLowerInvariant() + ".";
    }

    /// <summary>Runs the MCP server on stdio until the client closes the stream. Shared with 'wc3ctl mcp serve'.</summary>
    public static async Task RunServerAsync()
    {
        // One line on stderr before the protocol starts, so a bug report can quote the build it
        // came from (ENV-04). stdout stays clean for JSON-RPC.
        Console.Error.WriteLine(Wc3McpServer.BuildStartLine());

        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());

        // stdout carries the MCP protocol, so every log line must go to stderr.
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(McpLogLevel.FromEnvironment());

        builder.Services
            .AddMcpServer(Wc3McpServer.ApplyTo)
            .WithStdioServerTransport();

        await builder.Build().RunAsync();
    }
}
