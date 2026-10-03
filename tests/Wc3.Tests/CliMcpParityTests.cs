// tests/Wc3.Tests/CliMcpParityTests.cs
// Guards that wc3ctl's two surfaces stay in lockstep: everything an agent can do
// through the MCP server, a human can do through the CLI, and no CLI verb appears
// without a conscious parity decision. Both surfaces are thin wrappers over
// Wc3.Commands, so this catches "wired one side, forgot the other" drift — the exact
// failure mode that let terrain/place_unit/object_set land on MCP before the CLI.
using System.CommandLine;
using System.Reflection;
using ModelContextProtocol.Server;
using Wc3.Mcp;

namespace Wc3.Tests;

public class CliMcpParityTests
{
    // Every MCP tool -> the CLI command path that performs the same operation.
    private static readonly Dictionary<string, string> ToolToCliPath = new(StringComparer.Ordinal)
    {
        ["audit_map"] = "audit",
        ["deprotect_map"] = "deprotect",
        ["file_list"] = "file list",
        ["file_get_text"] = "file get-text",
        ["file_set"] = "file set",
        ["map_info"] = "info",
        ["list_files"] = "ls",
        ["object_get"] = "object get",
        ["object_list"] = "object list",
        ["object_set"] = "object set",
        ["object_new"] = "object new",
        ["unit_abilities"] = "object abilities",
        ["bundle_unit"] = "bundle unit",
        ["render_model"] = "render-model",
        ["replay_summary"] = "replay",
        ["uabi_profile"] = "uabi-profile",
        ["script_leaks"] = "script leaks",
        ["port_unit"] = "port unit",
        ["palette_doodad"] = "palette",
        ["place_doodad"] = "place doodad",
        ["place_region"] = "place region",
        ["place_unit"] = "place unit",
        ["place_start_location"] = "place start-location",
        ["place_item"] = "place item",
        ["camera_list"] = "camera list",
        ["camera_add"] = "camera add",
        ["camera_set"] = "camera set",
        ["camera_remove"] = "camera remove",
        ["pathing_paint"] = "pathing paint",
        ["map_info_get"] = "map-info get",
        ["map_info_set"] = "map-info set",
        ["player_list"] = "player list",
        ["player_set_force"] = "player set-force",
        ["force_list"] = "force list",
        ["force_set_flags"] = "force set-flags",
        ["new_map"] = "new",
        ["terrain_stats"] = "terrain stats",
        ["terrain_deform"] = "terrain deform",
        ["terrain_cliff"] = "terrain cliff",
        ["terrain_ramp"] = "terrain ramp",
        ["terrain_paint"] = "terrain paint",
        ["terrain_water"] = "terrain water",
        ["terrain_blight"] = "terrain blight",
        ["sound_list"] = "sound list",
        ["sound_add"] = "sound add",
        ["sound_set"] = "sound set",
        ["sound_remove"] = "sound remove",
        ["trigger_catalog_list"] = "trigger catalog list",
        ["trigger_catalog_describe"] = "trigger catalog describe",
    };

    // CLI commands that intentionally have no MCP tool (local dev / query utilities).
    private static readonly HashSet<string> CliOnly = new(StringComparer.Ordinal)
    {
        "roundtrip", "search", "diff", "render", "bundle object",
        "script functions", "extract", "convert", "validate",
        "repair generated-heroes", "repair reforged-3",
        // A structural edit to binary model assets, deliberately kept off the agent surface.
        // It rewrites imported .mdx files, a clean parse is not proof the game renders them,
        // and the result has to be play-tested before it is distributed.
        "repair portraits",
        // Rewrites the DataA to DataF selector on levelled object data across the whole map in
        // one pass. It exists to undo damage an earlier version of this tool did, so it is an
        // operator's repair with a blast radius rather than an edit an agent should reach for,
        // and its result belongs in a diff before it is shipped.
        "repair data-pointers",
        // Rewrites the map script and adds a bundled model, a whole-map repair whose result has to
        // be play-tested. Agents see the same findings read-only through audit_map's missing-model
        // check, so nothing is hidden from the agent surface, only the bulk write is.
        "repair model-paths",
        // Rewrites every unit type's ability list and injects code into main, a whole-map change
        // to how units get their abilities that has to be play-tested in multiplayer.
        "repair uabi-runtime",
        "repair audit-errors",
        "repair preload",
        // Writes roughly 1,200 files of base game data to a directory the operator names, to be
        // committed and diffed across patches. It is about the install rather than about a map,
        // and it is a bulk filesystem write, which is the same reason extract and convert sit
        // here rather than on the agent surface.
        "gamedata snapshot",
    };

    // The real MCP tool names, read straight from the [McpServerTool] attributes the
    // stdio exe serves (the ExpectedTools test proves these match the live server).
    private static HashSet<string> RealMcpTools() =>
        typeof(Wc3Tools)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Static | BindingFlags.Instance)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name!)
            .ToHashSet(StringComparer.Ordinal);

    // Every leaf command path in the real CLI tree, e.g. "terrain deform".
    private static IEnumerable<string> LeafPaths(Command cmd, string prefix)
    {
        var subs = cmd.Subcommands;
        if (subs.Count == 0)
        {
            if (prefix.Length > 0) yield return prefix;
            yield break;
        }
        foreach (var child in subs)
        {
            var next = prefix.Length == 0 ? child.Name : $"{prefix} {child.Name}";
            foreach (var p in LeafPaths(child, next))
                yield return p;
        }
    }

    private static HashSet<string> RealCliLeaves() =>
        LeafPaths(Wc3Ctl.Program.BuildRoot(), "").ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Mapping_covers_exactly_the_real_mcp_tool_set()
    {
        // Adding/removing an MCP tool forces a conscious update to this parity map.
        Assert.Equal(
            RealMcpTools().OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            ToolToCliPath.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Every_mcp_tool_has_a_real_cli_command()
    {
        var cli = RealCliLeaves();
        foreach (var (tool, path) in ToolToCliPath)
            Assert.True(cli.Contains(path),
                $"MCP tool '{tool}' maps to CLI path '{path}', which does not exist in the CLI tree.");
    }

    [Fact]
    public void Every_cli_command_is_mapped_or_allowlisted()
    {
        var mapped = ToolToCliPath.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var path in RealCliLeaves())
            Assert.True(mapped.Contains(path) || CliOnly.Contains(path),
                $"CLI command '{path}' has no MCP tool and is not in the CliOnly allowlist. " +
                "Either wire an MCP tool for it, or add it to CliOnly.");
    }
}
