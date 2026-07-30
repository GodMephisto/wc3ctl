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
        ["map_info"] = "info",
        ["list_files"] = "ls",
        ["object_get"] = "object get",
        ["object_list"] = "object list",
        ["object_set"] = "object set",
        ["object_new"] = "object new",
        ["audit_hero"] = "audit hero",
        ["audit_fidelity"] = "audit fidelity",
        ["audit_readiness"] = "audit readiness",
        ["bundle_unit"] = "bundle unit",
        ["render_model"] = "render-model",
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
        ["region_list"] = "region list",
        ["region_remove"] = "region remove",
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
        // Repair utility: regenerates the preplaced-widget creation script. Placement
        // already runs it automatically (CommitUnits), so MCP needs no separate tool.
        "place sync",
        // Repair utility: restores declarations a port left commented out, so the script
        // compiles again. Porting now gates on this automatically, so it is only ever needed
        // for a map produced before the gate existed.
        "script repair",
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
