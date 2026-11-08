// src/Wc3.Mcp/Permissions.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wc3.Mcp;

/// <summary>
/// The permission file, which belongs to the user and never to the agent (adopted from the
/// Roblox Studio MCP model). It sits beside the executable as permissions.json, or wherever
/// <c>WC3_MCP_PERMISSIONS</c> points, and holds one boolean switch per action the user is
/// willing to grant. Every switch defaults to false, a missing file grants nothing, and a file
/// that cannot be understood is a refusal rather than a silent allow.
///
/// Nothing in this server writes the file, and there is no tool that grants a switch. The user
/// edits it. A confirmation typed into a conversation is intent, not permission.
///
/// The file is read on every check rather than cached, so a user who edits it does not have to
/// restart the server, and a test can point <c>WC3_MCP_PERMISSIONS</c> at a temporary file
/// without touching the machine's own copy. The cost is one small file read per gated action.
/// </summary>
internal sealed class Permissions
{
    /// <summary>Names another permission file, which is how a test or a second install pins its own.</summary>
    internal const string EnvironmentVariable = "WC3_MCP_PERMISSIONS";

    /// <summary>The file's name beside the executable.</summary>
    internal const string FileName = "permissions.json";

    /// <summary>Every switch the file may carry, in the order the diagnostics print them.</summary>
    internal static readonly IReadOnlyList<string> Actions =
        new[] { "overwrite", "publish", "mirror_export", "npm", "delete" };

    private readonly Dictionary<string, bool> _switches;

    private Permissions(
        string pathName, bool present, string? fault,
        Dictionary<string, bool> switches, IReadOnlyList<string> ignoredKeys)
    {
        PathName = pathName;
        Present = present;
        Fault = fault;
        _switches = switches;
        IgnoredKeys = ignoredKeys;
    }

    /// <summary>The file this instance read, resolved from the environment when it names one.</summary>
    internal string PathName { get; }

    /// <summary>Whether a file was found. False means every action is denied.</summary>
    internal bool Present { get; }

    /// <summary>Why the file cannot be trusted, or null when it parsed. Non-null denies everything.</summary>
    internal string? Fault { get; }

    /// <summary>Keys the file carries that no action uses. Reported, never fatal.</summary>
    internal IReadOnlyList<string> IgnoredKeys { get; }

    /// <summary>The file as it is right now, which is what the gate reads.</summary>
    internal static Permissions Current => Load();

    /// <summary>Read a permission file, or the one the environment or the install names.</summary>
    internal static Permissions Load(string? path = null)
    {
        string full = Resolve(path);
        if (!File.Exists(full))
            return new Permissions(full, present: false, fault: null,
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase), Array.Empty<string>());

        string text;
        try
        {
            text = File.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException)
        {
            return Invalid(full, "the file could not be read. " + ex.Message);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or InvalidOperationException)
        {
            return Invalid(full, "the file is not valid JSON, so no switch is granted. " + ex.Message);
        }

        if (root is not JsonObject obj)
            return Invalid(full, "the file must hold a JSON object of switches, one per action.");

        var switches = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var ignored = new List<string>();
        foreach (var pair in obj)
        {
            string name = pair.Key;
            if (!Actions.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                ignored.Add(name);
                continue;
            }
            if (pair.Value is null || pair.Value.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                return Invalid(full, "the \"" + name + "\" switch is not true or false.");
            switches[name] = pair.Value.GetValue<bool>();
        }

        return new Permissions(full, present: true, fault: null, switches, ignored);
    }

    /// <summary>
    /// Whether one action is granted. An action the file does not name is denied, and so is every
    /// action when no file exists. A file that cannot be trusted throws rather than answering,
    /// because false would read as a plain denial and lose the reason the user needs.
    /// </summary>
    internal bool IsAllowed(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        if (Fault is not null) throw Faulted();
        return _switches.TryGetValue(action.Trim(), out bool granted) && granted;
    }

    /// <summary>
    /// Refuse unless one action is granted, naming the switch, the permission file and the file the
    /// caller wanted to touch. This is the shape every gated path reports.
    /// </summary>
    internal void Require(string action, string file, string effect)
    {
        if (IsAllowed(action)) return;
        throw new ToolError(
            "PERMISSION_DENIED",
            "\"" + action + "\" is not granted in " + PathName + ", so " + file + " was not " + effect + ".",
            "ask the user to set \"" + action + "\": true in " + PathName + ", then repeat the call. "
                + "The server cannot grant it and there is no tool that does.");
    }

    /// <summary>One diagnostic block for a client or a bug report. Plain text, no side effects.</summary>
    internal string Describe()
    {
        var text = new StringBuilder();
        text.Append("permissions file ").Append(PathName)
            .Append(Fault is not null ? ", invalid" : Present ? ", loaded" : ", absent")
            .AppendLine(".");
        if (Fault is not null) text.AppendLine(Fault);
        foreach (string action in Actions)
        {
            string verdict = _switches.TryGetValue(action, out bool granted)
                ? granted ? "granted" : "denied, the switch is false"
                : "denied, the switch is absent";
            text.Append(action).Append(' ').Append(verdict).AppendLine(".");
        }
        if (IgnoredKeys.Count > 0)
            text.Append("ignored unknown switches ").Append(string.Join(", ", IgnoredKeys)).AppendLine(".");
        if (!Present && Fault is null)
            text.AppendLine("Nothing is granted until the user creates this file. Ask, never assume.");
        return text.ToString().TrimEnd();
    }

    /// <summary>The named path, else the environment's, else the file beside the executable.</summary>
    private static string Resolve(string? path)
    {
        string? named = string.IsNullOrWhiteSpace(path)
            ? Environment.GetEnvironmentVariable(EnvironmentVariable)
            : path;
        return string.IsNullOrWhiteSpace(named)
            ? Path.Combine(AppContext.BaseDirectory, FileName)
            : named.Trim();
    }

    private static Permissions Invalid(string path, string detail) =>
        new(path, present: true, fault: path + " is not usable. " + detail,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase), Array.Empty<string>());

    private ToolError Faulted() =>
        new("PERMISSION_DENIED", Fault!,
            "fix the file or remove it so the defaults apply, then repeat the call. "
                + "Nothing is granted from a file that cannot be read.");
}
