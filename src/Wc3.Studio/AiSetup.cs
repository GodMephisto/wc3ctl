using Wc3.Mcp;
using Wc3.Mcp.Setup;

namespace Wc3.Studio;

/// <summary>
/// The logic behind the Connect AI Apps dialog, kept apart from the Avalonia surface so it can be
/// tested without a window. It reports each supported client's detection and registration state,
/// resolves the server command to register, and runs the same setup layer the CLI uses.
/// </summary>
public static class AiSetup
{
    /// <summary>One client's state for the dialog: whether it is installed here and whether the
    /// server is already registered in it.</summary>
    public sealed record ClientStatus(string Id, string DisplayName, bool Detected, bool Registered, string State);

    /// <summary>
    /// The server command to register with the AI apps. Prefers the CLI exe (<c>wc3ctl mcp serve</c>)
    /// when it is on PATH, then the standalone server exe, then the auto-updating npm launcher, so
    /// the dialog works whether the user installed the CLI, the standalone server, or nothing yet.
    /// </summary>
    public static ServerEntry ResolveEntry(SetupEnvironment env)
    {
        var product = SetupProduct.Wc3ctl;
        if (env.FindOnPath("wc3ctl") is { } cli)
            return new ServerEntry(product.DefaultServerName, cli, product.ServeArgs, null);
        if (env.FindOnPath("wc3-mcp") is { } standalone)
            return new ServerEntry(ServerIdentity.ServerName, standalone, Array.Empty<string>(), null);
        return new ServerEntry("wc3-mcp", "npx", new[] { "-y", "wc3-mcp@latest" }, null);
    }

    /// <summary>Every supported client with its current state, in <see cref="ClientTargets.All"/> order.</summary>
    public static IReadOnlyList<ClientStatus> Statuses(SetupEnvironment env, string serverName)
    {
        var list = new List<ClientStatus>();
        foreach (var target in ClientTargets.All)
        {
            bool detected = target.IsDetected(env);
            bool registered = detected && IsRegistered(target, env, serverName);
            string state = !detected ? "not found"
                : target.Style == ConfigStyle.ClaudeCli ? "found"
                : registered ? "found, set up"
                : "found, not set up";
            list.Add(new ClientStatus(target.Id, target.DisplayName, detected, registered, state));
        }
        return list;
    }

    /// <summary>
    /// Registers the server in every detected client by running the shared setup path
    /// (<c>install --all</c>), returning the process exit code and the full log it produced.
    /// </summary>
    public static (int ExitCode, string Log) ConnectAll(SetupEnvironment env, string? gameDir)
    {
        var writer = new StringWriter();
        var entry = ResolveEntry(env) with { GameDir = gameDir };
        int code = SetupCli.Run(new[] { "install", "--all" }, env, writer, SetupProduct.Wc3ctl, entry);
        return (code, writer.ToString());
    }

    /// <summary>The starter prompt a person can paste into an AI app, naming the open map when there
    /// is one so the assistant has a concrete target.</summary>
    public static string StarterPrompt(string? mapPath)
    {
        var target = string.IsNullOrWhiteSpace(mapPath)
            ? "my Warcraft III map"
            : $"the Warcraft III map at {mapPath}";
        return $"Use the wc3ctl tools to inspect {target}. List its units, triggers and dependencies, "
             + "then summarise anything that looks broken, missing, or out of place.";
    }

    private static bool IsRegistered(ClientTarget target, SetupEnvironment env, string name) => target.Style switch
    {
        ConfigStyle.CodexToml => target.PresentFiles(env).Any(f => CodexToml.Contains(f, name)),
        ConfigStyle.Json => target.PresentFiles(env).Any(f => JsonConfig.Contains(f, target.ServersKey, name)),
        _ => false,
    };
}
