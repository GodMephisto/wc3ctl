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
        ["object_form"] = "object form",
        ["unit_abilities"] = "unit abilities",
        ["object_field_options"] = "object options",
        ["asset_list"] = "asset list",
        ["lint"] = "lint",
        ["validate"] = "validate",
        ["roundtrip"] = "roundtrip",
        ["search"] = "search",
        ["diff"] = "diff",
        ["script_functions"] = "script functions",
        ["trigger_add_category"] = "trigger add-category",
        ["trigger_add"] = "trigger add",
        ["trigger_remove"] = "trigger remove",
        ["trigger_rename"] = "trigger rename",
        ["trigger_set_enabled"] = "trigger set-enabled",
        ["trigger_set_initially_on"] = "trigger set-initially-on",
        ["trigger_set_run_on_map_init"] = "trigger set-run-on-map-init",
        ["script_references"] = "script refs",
        ["terrain_info"] = "terrain info",
        ["terrain_corner_get"] = "terrain corner get",
        ["terrain_corner_set"] = "terrain corner set",
        ["placed_units_list"] = "unit list",
        ["placed_unit_get"] = "unit get",
        ["placed_doodad_get"] = "doodad get",
        ["strings_list"] = "strings list",
        ["imports_list"] = "imports list",
        ["triggers_read"] = "trigger read",
        ["placed_unit_set"] = "unit set",
        ["placed_unit_remove"] = "unit remove",
        ["placed_doodads_list"] = "doodad list",
        ["placed_doodad_set"] = "doodad set",
        ["placed_doodad_remove"] = "doodad remove",
        ["object_get"] = "object get",
        ["object_list"] = "object list",
        ["object_set"] = "object set",
        ["object_new"] = "object new",
        ["audit_hero"] = "audit hero",
        ["audit_ability"] = "audit ability",
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
        ["editor_catalog_list"] = "editor catalog list",
        ["editor_catalog_get"] = "editor catalog get",
        ["trigger_catalog_describe"] = "trigger catalog describe",
    };

    // CLI commands that intentionally have no MCP tool (local dev / query utilities).
    private static readonly HashSet<string> CliOnly = new(StringComparer.Ordinal)
    {
        // Writes an image to a path on this machine, so the output is a local file rather
        // than an answer. Same reason as extract.
        "render",
        // The generalisation of bundle_unit to any object kind. The unit form is what a
        // caller actually reaches for, and both would return the same shape.
        "bundle object",
        // Moves opaque bytes between the filesystem and the archive. A local file
        // operation rather than map semantics an agent reasons about.
        "extract",
        // Disk to disk asset conversion. Neither side is a map, so there is no map for an
        // agent to act on.
        "convert",
        // Repair utility: regenerates the preplaced-widget creation script. Placement
        // already runs it automatically (CommitUnits), so MCP needs no separate tool.
        "place sync",
        // Repair utility: restores declarations a port left commented out, so the script
        // compiles again. Porting now gates on this automatically, so it is only ever needed
        // for a map produced before the gate existed.
        "script repair",
        // Debug tool: instruments an already-ported map with BJDebugMsg calls so the running game,
        // not an agent, reports where its cast chain stops. A one-off local investigation aid over
        // a disposable copy of a map, not something an agent drives through MCP.
        "debug wiring",
        // Raw byte write from a disk path, the exact counterpart of 'extract' (also CLI-only).
        // Both sides of that pair move opaque bytes between the filesystem and an archive, a
        // local file operation rather than map semantics an agent would reason about.
        "file set",
        // Container-level archive inspection. These read MPQ internals (storage flags, hash slot
        // classification) to explain why a map that looks correct behaves wrongly. Diagnostics for
        // whoever is debugging the toolchain, not map semantics an agent acts on.
        "mpq-diff", "mpq-hash",
        // Samples a live Warcraft III process to tell a spin from a stall. Needs a running game on
        // this machine, so it is inherently local and cannot be driven remotely.
        "debug game-hang",
        "debug trace-load",
        // Read-only analysis of a target map's hero integration requirements.
        "contract",
        // Writes a definition folder to disk; local file output like 'extract'.
        "hero export",
        "hero install",
        "hero lint",
        // Launches the real game on this machine; inherently local.
        "test-load",
        // Heuristic script analysis for non-terminating loops. Reports candidates to read.
        "script loops",
        // Roots for a reachability pass; analysis output for whoever builds that pass.
        "script roots",
        // Reachability-based function removal; a repair utility like 'script repair'.
        "script strip",
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
