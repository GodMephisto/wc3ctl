// src/wc3ctl/DocsCommand.cs
using System.CommandLine;
using System.Reflection;
using Wc3.Commands;

namespace Wc3Ctl;

/// <summary>
/// 'wc3ctl docs', the generator for the two reference documents. Both walks live here because this
/// is the only project that sees both the System.CommandLine tree and the MCP tool surface. The
/// formatting is in <see cref="DocsReference"/>, so the CI guard can call the render in memory and
/// compare it against the committed files without writing anything.
///
/// The same two builders back the command and the test, so a new command or tool changes the render
/// and the guard fails until the committed document is regenerated.
/// </summary>
public static class DocsCommand
{
    /// <summary>Relative paths the command writes when no output directory is given.</summary>
    public const string CliRelativePath = "docs/wc3ctl-command-reference.md";
    public const string McpRelativePath = "docs/mcp-reference.md";

    public static Command Build()
    {
        var target = new Argument<string>("target", "Which reference to write, cli, mcp or all.");
        var force = new Option<bool>("--force",
            "Overwrite an existing file with a different render. Without it a changed file is reported and left alone.");
        var outDir = new Option<string?>("--out-dir",
            "Directory to write into. Default, the repository docs folder.");
        var cmd = new Command("docs",
            "Generate the command reference and the MCP tool reference from the live command tree and tool surface.")
        { target, force, outDir };
        cmd.SetHandler((string t, bool f, string? dir) =>
        {
            string token = (t ?? string.Empty).Trim().ToLowerInvariant();
            if (token is not ("cli" or "mcp" or "all"))
            {
                Console.Error.WriteLine($"error, target must be cli, mcp or all, not '{t}'");
                Environment.ExitCode = 2;
                return;
            }
            string directory = dir is { Length: > 0 } ? Path.GetFullPath(dir) : RepoDocsDir();
            Directory.CreateDirectory(directory);
            if (token is "cli" or "all")
                WriteOne(Path.Combine(directory, "wc3ctl-command-reference.md"), BuildCliMarkdown(), f);
            if (token is "mcp" or "all")
                WriteOne(Path.Combine(directory, "mcp-reference.md"), BuildMcpMarkdown(), f);
        }, target, force, outDir);
        return cmd;
    }

    /// <summary>The command reference text, walked from the real tree.</summary>
    public static string BuildCliMarkdown()
    {
        var root = Program.BuildRoot();
        var globals = root.Options.Where(o => !o.IsHidden).ToList();
        var globalSet = globals.ToHashSet();
        var topLevel = root.Subcommands
            .Where(c => !c.IsHidden)
            .Select(c => ToDocCommand(c, globalSet, c.Name))
            .ToList();
        return DocsReference.RenderCli(topLevel, globals.Select(ToDocOption).ToList());
    }

    /// <summary>The MCP reference text, read from the reflected tool surface and its annotations.</summary>
    public static string BuildMcpMarkdown()
    {
        var tools = Wc3.Mcp.Wc3McpServer.CreateTools()
            .Select(t => t.ProtocolTool)
            .Select(t =>
            {
                var a = t.Annotations;
                return new DocTool(t.Name, t.Description,
                    a?.ReadOnlyHint, a?.DestructiveHint, a?.IdempotentHint, a?.OpenWorldHint);
            })
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        return DocsReference.RenderMcp(tools);
    }

    private static void WriteOne(string path, string text, bool force)
    {
        string relative = path.Replace('\\', '/');
        if (!File.Exists(path))
        {
            File.WriteAllText(path, text);
            Console.WriteLine($"wrote {relative}, {CountLines(text)} line(s)");
            return;
        }
        var diff = DocsReference.Diff(File.ReadAllText(path), text);
        if (diff.Identical)
        {
            Console.WriteLine($"unchanged {relative}");
            return;
        }
        if (!force)
        {
            Console.WriteLine($"changed {relative}, {diff.Added} line(s) added, {diff.Removed} removed, pass --force to overwrite");
            return;
        }
        File.WriteAllText(path, text);
        Console.WriteLine($"wrote {relative}, {CountLines(text)} line(s)");
    }

    private static int CountLines(string text) =>
        text.Length == 0 ? 0 : text.Split('\n').Length - (text.EndsWith('\n') ? 1 : 0);

    /// <summary>The repository docs folder, found by walking up for the solution file. Falls back to
    /// a docs folder beside the current directory so a published binary still does something sane.</summary>
    internal static string RepoDocsDir()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = start;
            while (dir is { Length: > 0 })
            {
                if (File.Exists(Path.Combine(dir, "Wc3.sln"))) return Path.Combine(dir, "docs");
                var parent = Directory.GetParent(dir);
                if (parent is null) break;
                dir = parent.FullName;
            }
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "docs");
    }

    // ---- tree to records ----

    private static DocCommand ToDocCommand(Command c, HashSet<Option> globals, string path)
    {
        var options = c.Options
            .Where(o => !o.IsHidden && !globals.Contains(o))
            .Select(ToDocOption)
            .ToList();
        var args = c.Arguments
            .Where(a => !a.IsHidden)
            .Select(ToDocArgument)
            .ToList();
        var subs = c.Subcommands
            .Where(s => !s.IsHidden)
            .Select(s => ToDocCommand(s, globals, path + " " + s.Name))
            .ToList();
        return new DocCommand(path, c.Name, c.Description, c.Handler is not null, args, options, subs);
    }

    private static DocArgument ToDocArgument(Argument a)
    {
        bool required = a.Arity.MinimumNumberOfValues >= 1 && !a.HasDefaultValue;
        bool variadic = a.Arity.MaximumNumberOfValues > 1;
        return new DocArgument(a.Name, a.Description, required, variadic);
    }

    private static DocOption ToDocOption(Option o)
    {
        var aliases = o.Aliases.ToList();
        bool takesValue = o.ValueType != typeof(bool);
        string names = aliases.Count > 0 ? string.Join(", ", aliases) : "--" + o.Name;
        if (takesValue) names += " <" + o.Name + ">";
        return new DocOption(names, o.Description, DefaultOf(o), o.IsRequired);
    }

    /// <summary>The option's default value when it declares one. The base Option exposes neither the
    /// default nor the argument that carries it, so this reads the internal argument by reflection and
    /// returns null when the shape is not what this pinned SDK version ships.</summary>
    private static string? DefaultOf(Option o)
    {
        try
        {
            var arg = typeof(Option)
                .GetProperty("Argument", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(o);
            if (arg is null) return null;
            var type = arg.GetType();
            bool has = type.GetProperty("HasDefaultValue")?.GetValue(arg) is true;
            if (!has) return null;
            object? value = type.GetMethod("GetDefaultValue", Type.EmptyTypes)?.Invoke(arg, null);
            return value switch
            {
                null => null,
                bool => null,
                string s => s.Length == 0 ? "\"\"" : s,
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
        }
        catch
        {
            return null;
        }
    }
}
