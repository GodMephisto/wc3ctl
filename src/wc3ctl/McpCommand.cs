// src/wc3ctl/McpCommand.cs
using System.CommandLine;
using Wc3.Mcp.Setup;

namespace Wc3Ctl;

/// <summary>
/// 'wc3ctl mcp', the MCP server and its client setup inside the CLI exe. 'mcp serve' runs the
/// server on stdio, the other verbs are the same setup commands Wc3.Mcp.exe takes, so an AI app
/// is registered as 'wc3ctl mcp serve' and one exe carries the CLI and the server.
/// </summary>
public static class McpCommand
{
    /// <summary>The setup verbs, each with the line 'wc3ctl --help' shows for it.</summary>
    private static readonly (string Verb, string Description)[] Verbs =
    {
        ("install", "Register the MCP server with AI apps (install --all, or name them, e.g. install cursor vscode)."),
        ("uninstall", "Remove the MCP server from AI apps and the exe folder from the user PATH."),
        ("config", "Print the settings to paste into an AI app by hand."),
        ("clients", "List the supported AI apps and whether each is set up."),
        ("doctor", "Check the install, the game folder and the AI apps."),
        ("version", "Print the version."),
        ("help", "Show the setup help, with every option and app."),
    };

    /// <summary>
    /// Runs 'mcp ...' directly, before the command tree parses it, because the tree's global
    /// --game-dir would otherwise take the option meant for setup. Returns null for anything else,
    /// for a help request, and for the introspection verbs the tree handles, which the tree answers.
    /// </summary>
    public static Task<int>? TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "mcp") return null;
        var rest = args[1..];
        if (rest.Any(a => a is "-h" or "--help" or "-?" or "/?")) return null;
        // The two read-only introspection verbs live in the command tree so they parse --json and
        // show in help like any other command. SetupCli does not know them.
        if (rest.Length > 0 && (rest[0] == "tools" || rest[0] == "audit-annotations")) return null;
        if (rest.Length > 0 && rest[0] == "serve")
        {
            if (rest.Length > 1)
            {
                Console.Error.WriteLine("mcp serve takes no arguments. Set WC3_GAME_DIR for the game folder.");
                return Task.FromResult(2);
            }
            return Serve();
        }
        return Task.FromResult(SetupCli.Run(rest, SetupEnvironment.Current, Console.Out, SetupProduct.Wc3ctl));
    }

    private static async Task<int> Serve()
    {
        await Wc3.Mcp.Program.RunServerAsync();
        return 0;
    }

    /// <summary>The 'mcp' branch of the command tree, for help output and the parity tests.</summary>
    public static Command Build(Option<bool> jsonOption)
    {
        var mcp = new Command("mcp", "The MCP server for AI apps, and registering it with them.");
        var serve = new Command("serve", "Run the MCP server on stdio. AI apps start this, you normally do not.");
        serve.SetHandler(Serve);
        mcp.AddCommand(serve);

        var tools = new Command("tools",
            "Dump the MCP tool surface the server builds. Name, description and the four annotations, read-only.");
        tools.SetHandler((bool json) =>
        {
            var catalog = Wc3.Mcp.McpToolCatalog.Describe();
            Console.WriteLine(json ? Render.AsJson(catalog) : Render.McpTools(catalog));
        }, jsonOption);
        mcp.AddCommand(tools);

        var audit = new Command("audit-annotations",
            "Report MCP tools whose annotations disagree with their name or that declare no annotations.");
        audit.SetHandler((bool json) =>
        {
            var result = Wc3.Mcp.McpToolAudit.InspectServer();
            Console.WriteLine(json ? Render.AsJson(result) : Render.McpAudit(result));
        }, jsonOption);
        mcp.AddCommand(audit);

        foreach (var (verb, description) in Verbs)
        {
            var command = new Command(verb, description);
            // Reached only through help, since TryRun handles every real 'mcp' invocation first.
            command.SetHandler(() => SetupCli.Run(new[] { "help" }, SetupEnvironment.Current, Console.Out, SetupProduct.Wc3ctl));
            mcp.AddCommand(command);
        }
        return mcp;
    }
}
