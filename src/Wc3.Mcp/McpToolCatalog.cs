// src/Wc3.Mcp/McpToolCatalog.cs
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Wc3.Mcp;

/// <summary>
/// One tool exactly as the server serves it, for the read-only catalog dump and the audit.
/// A null hint means the tool left that annotation at its default, which is what the SDK does
/// when the attribute does not set the property. <see cref="Annotated"/> is true when at least
/// one hint is present, since the SDK omits the whole annotations object when every hint is a
/// default.
/// </summary>
public sealed record McpToolInfo(
    string Name,
    string? Description,
    bool? ReadOnly,
    bool? Destructive,
    bool? Idempotent,
    bool? OpenWorld,
    string? Family = null)
{
    /// <summary>Other names a caller may use for this tool. Empty until a tool declares one.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    /// <summary>The JSON Schema of the tool arguments, copied from the served tool.</summary>
    public JsonElement? InputSchema { get; init; }

    public bool Annotated =>
        ReadOnly is not null || Destructive is not null || Idempotent is not null || OpenWorld is not null;
}

/// <summary>One annotation problem, naming the tool and what is wrong with it.</summary>
public sealed record McpAnnotationProblem(string Tool, string Problem);

/// <summary>
/// One prompt exactly as the server serves it, for the catalog resource. Name and title come from
/// the prompt attribute, the description from its Description attribute, and Arguments holds the
/// argument names a caller must or may pass.
/// </summary>
public sealed record McpPromptInfo(
    string Name,
    string? Title,
    string? Description,
    IReadOnlyList<string> Arguments);

/// <summary>
/// The whole catalog resource. The tools keep the array they already had, now under tools, and the
/// prompts sit beside them so a client that reads only wc3://tools can see what a prompt takes.
/// </summary>
public sealed record McpCatalog(
    IReadOnlyList<McpToolInfo> Tools,
    IReadOnlyList<McpPromptInfo> Prompts);

/// <summary>The audit outcome. An empty problem list means the surface is clean.</summary>
public sealed record McpAnnotationAuditResult(IReadOnlyList<McpAnnotationProblem> Problems)
{
    public int Count => Problems.Count;
}

/// <summary>
/// The read-only view of the tools the server builds. Both the catalog dump and the audit read
/// through <see cref="Wc3McpServer.CreateTools"/>, the same reflection the stdio server uses, so
/// neither can drift from what a client is actually offered.
/// </summary>
public static class McpToolCatalog
{
    /// <summary>Every tool, ordinal-sorted, with its description and four annotation hints.</summary>
    public static IReadOnlyList<McpToolInfo> Describe() =>
        Wc3McpServer.CreateTools()
            .Select(t => ToInfo(t.ProtocolTool))
            .OrderBy(i => i.Name, StringComparer.Ordinal)
            .ToList();

    internal static McpToolInfo ToInfo(Tool tool)
    {
        var a = tool.Annotations;
        return new McpToolInfo(
            tool.Name, tool.Description,
            a?.ReadOnlyHint, a?.DestructiveHint, a?.IdempotentHint, a?.OpenWorldHint,
            McpToolFamilies.Of(tool.Name))
        {
            Aliases = Array.Empty<string>(),
            InputSchema = tool.InputSchema,
        };
    }

    /// <summary>Every prompt, ordinal-sorted, with its title, description and argument names.</summary>
    public static IReadOnlyList<McpPromptInfo> DescribePrompts() =>
        Wc3McpServer.CreatePrompts()
            .Select(p => ToPromptInfo(p.ProtocolPrompt))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

    internal static McpPromptInfo ToPromptInfo(Prompt prompt) =>
        new(prompt.Name, prompt.Title, prompt.Description,
            (prompt.Arguments ?? Array.Empty<PromptArgument>()).Select(a => a.Name).ToList());

    /// <summary>
    /// The catalog as one read-only MCP resource, so a client can pull the whole surface in a
    /// single read instead of listing tools or prompts. Same reflection as the tools and the
    /// prompts, so it cannot drift. The JSON is serialized once at start, and the resource carries
    /// its byte size and digest, so resources/list names what a read returns.
    /// </summary>
    public static McpServerResource CreateCatalogResource()
    {
        var catalog = new McpCatalog(Describe(), DescribePrompts());
        string json = JsonSerializer.Serialize(catalog, Wc3McpServer.JsonOptions);
        return new ServedTextResource(
            "wc3://tools",
            "tool_catalog",
            null,
            "The MCP surface as JSON. Every tool with its description, its family, the four annotations, its aliases and its input schema, and every prompt with its title, description and argument names.",
            "application/json",
            json);
    }
}

/// <summary>
/// Audits the MCP surface against six rules. Every tool must declare all four hints, because an
/// unset hint defaults to true and a client reads it as a claim the server never made. A tool
/// whose name begins with a mutating stem must not claim to be read-only. A tool that writes must
/// not say read-only in its description, in any scope, because a caller reads that word as the
/// whole answer. A tool cannot be both read-only and destructive, and a tool whose only effect is
/// to remove something must declare destructive. The prefix list is deliberately precise rather than broad. A blanket 'port_' or
/// 'terrain_' would flag port_deps and port_preview, which are genuinely read-only, so each
/// mutating stem is named instead.
/// </summary>
public static class McpToolAudit
{
    /// <summary>
    /// Tool names that discard content a caller cannot get back, so they must declare
    /// <c>destructiveHint</c> true. Kept deliberately precise: each name is a tool whose only
    /// effect is to remove something, and <c>mpq_strip_bookkeeping</c> is absent because the
    /// bookkeeping entries it drops are regenerated (SAF-03).
    /// </summary>
    private static readonly string[] DestructiveNames =
    {
        "camera_remove", "mpq_remove", "mpq_remove_many", "placed_doodad_remove",
        "placed_fix_refs", "placed_unit_remove", "region_remove", "sound_remove",
        "trigger_remove", "trigger_remove_eca", "trigger_var_remove",
    };

    /// <summary>Name stems that only ever prefix a tool that writes. Kept in step with the surface.</summary>
    private static readonly string[] MutatingPrefixes =
    {
        "asset_replace", "camera_add", "camera_remove", "camera_set", "camera_tour",
        "file_set", "force_set_flags", "gamedata_export_schemas", "hero_install",
        "leak_harness", "lint_baseline", "map_info_set", "new_map",
        "object_clone", "object_new", "object_set",
        "pathing_import_png", "pathing_paint",
        "place_",
        "placed_doodad_remove", "placed_doodad_set", "placed_fix_refs",
        "placed_unit_remove", "placed_unit_set",
        "player_set_force", "port_into_version", "port_unit",
        "region_create", "region_remove", "script_format",
        "sound_add", "sound_remove", "sound_set", "strings_set", "target_set",
        "terrain_blight", "terrain_cliff", "terrain_corner_set", "terrain_deform",
        "terrain_fill", "terrain_import_heightmap", "terrain_paint", "terrain_ramp",
        "terrain_smooth", "terrain_water",
        "trigger_add", "trigger_recover_from_script", "trigger_remove", "trigger_rename",
        "trigger_set_code", "trigger_set_eca_enabled", "trigger_set_enabled",
        "trigger_set_initially_on",
        "trigger_set_run_on_map_init",
        "trigger_var_add", "trigger_var_remove", "trigger_var_set",
    };

    /// <summary>Audit the live server surface.</summary>
    public static McpAnnotationAuditResult InspectServer() => Inspect(McpToolCatalog.Describe());

    /// <summary>Audit a tool list, so the rules can be tested without touching the real surface.</summary>
    public static McpAnnotationAuditResult Inspect(IReadOnlyList<McpToolInfo> tools)
    {
        var problems = new List<McpAnnotationProblem>();
        foreach (var tool in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            if (!tool.Annotated)
            {
                problems.Add(new McpAnnotationProblem(tool.Name, "declares no annotations"));
                continue;
            }
            var unset = new List<string>();
            if (tool.ReadOnly is null) unset.Add("readOnlyHint");
            if (tool.Destructive is null) unset.Add("destructiveHint");
            if (tool.Idempotent is null) unset.Add("idempotentHint");
            if (tool.OpenWorld is null) unset.Add("openWorldHint");
            if (unset.Count > 0)
                problems.Add(new McpAnnotationProblem(tool.Name, "leaves " + string.Join(", ", unset) + " unset"));
            if (tool.ReadOnly == true && MutatingPrefixes.Any(p => tool.Name.StartsWith(p, StringComparison.Ordinal)))
                problems.Add(new McpAnnotationProblem(tool.Name, "name looks mutating but it is marked read-only"));
            if (tool.ReadOnly != true && tool.Description is not null
                && tool.Description.Contains("read-only", StringComparison.OrdinalIgnoreCase))
                problems.Add(new McpAnnotationProblem(tool.Name, "description says read-only but the tool writes"));
            if (tool.ReadOnly == true && tool.Destructive == true)
                problems.Add(new McpAnnotationProblem(tool.Name, "declares both read-only and destructive"));
            if (tool.Destructive != true && DestructiveNames.Contains(tool.Name, StringComparer.Ordinal))
                problems.Add(new McpAnnotationProblem(tool.Name, "name looks destructive but it is marked non-destructive"));
        }
        return new McpAnnotationAuditResult(problems);
    }
}
