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
        ["audit_trend"] = "audit trend",
        ["deprotect_map"] = "deprotect",
        ["file_list"] = "file list",
        ["file_get_text"] = "file get-text",
        ["file_set"] = "file set",
        ["map_info"] = "info",
        ["map_overview"] = "overview",
        ["mcp_audit_annotations"] = "mcp audit-annotations",
        ["list_files"] = "ls",
        ["object_form"] = "object form",
        ["unit_abilities"] = "unit abilities",
        ["object_field_options"] = "object options",
        ["asset_list"] = "asset list",
        ["asset_unused"] = "asset unused",
        ["asset_duplicates"] = "asset duplicates",
        ["asset_audit"] = "asset audit",
        ["asset_size_report"] = "asset size-report",
        ["asset_replace"] = "asset replace",
        ["lint"] = "lint",
        ["lint_baseline"] = "lint baseline",
        ["validate"] = "validate",
        ["validate_strict"] = "validate",
        ["roundtrip"] = "roundtrip",
        ["search"] = "search",
        ["diff"] = "diff",
        ["script_functions"] = "script functions",
        ["script_check"] = "script check",
        ["script_unused_globals"] = "script unused-globals",
        ["script_unused_functions"] = "script unused-functions",
        ["script_null_locals"] = "script null-locals",
        ["script_complexity"] = "script complexity",
        ["script_callgraph"] = "script callgraph",
        ["script_format"] = "script format",
        ["script_to_lua"] = "script to-lua",
        ["script_decompile"] = "script decompile",
        ["repair_generated"] = "repair-generated",
        ["hero_lint"] = "hero lint",
        ["hero_install"] = "hero install",
        ["hero_roster"] = "hero roster",
        ["terrain_fill"] = "terrain fill",
        ["trigger_recover_from_script"] = "trigger recover-from-script",
        ["trigger_add_category"] = "trigger add-category",
        ["trigger_add_eca"] = "trigger add-eca",
        ["trigger_remove_eca"] = "trigger remove-eca",
        ["trigger_set_eca_enabled"] = "trigger set-eca-enabled",
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
        ["strings_missing"] = "strings missing",
        ["strings_unused"] = "strings unused",
        ["strings_diff"] = "strings diff",
        ["strings_find"] = "strings find",
        ["strings_set"] = "strings set",
        ["imports_list"] = "imports list",
        ["triggers_read"] = "trigger read",
        ["placed_unit_set"] = "unit set",
        ["placed_unit_remove"] = "unit remove",
        ["placed_doodads_list"] = "doodad list",
        ["placed_doodad_set"] = "doodad set",
        ["placed_doodad_remove"] = "doodad remove",
        ["placed_stats"] = "placed stats",
        ["placed_missing_refs"] = "placed missing-refs",
        ["placed_fix_refs"] = "placed fix-refs",
        ["object_get"] = "object get",
        ["object_list"] = "object list",
        ["object_set"] = "object set",
        ["object_new"] = "object new",
        ["object_search"] = "object search",
        ["object_noop_fields"] = "object noop-fields",
        ["object_deprecated"] = "object deprecated",
        ["object_rawcode_suggest"] = "object rawcode-suggest",
        ["object_clone"] = "object clone",
        ["object_migrate"] = "object migrate",
        ["object_bulk_set"] = "object bulk-set",
        ["object_export"] = "object export",
        ["object_import"] = "object import",
        ["object_rename"] = "object rename",
        ["audit_hero"] = "audit hero",
        ["audit_ability"] = "audit ability",
        ["audit_fidelity"] = "audit fidelity",
        ["audit_readiness"] = "audit readiness",
        ["bundle_unit"] = "bundle unit",
        ["render_model"] = "render-model",
        ["replay_summary"] = "replay",
        ["replay_diff"] = "replay diff",
        ["replay_players"] = "replay players",
        ["replay_timeline"] = "replay timeline",
        ["replay_find_map"] = "replay find-map",
        ["replay_stats"] = "replay stats",
        ["uabi_profile"] = "uabi-profile",
        ["doctor_check"] = "doctor check",
        ["doctor_inspect"] = "doctor inspect",
        ["doctor_compare"] = "doctor compare",
        ["doctor_fix"] = "doctor fix",
        ["script_leaks"] = "script leaks",
        ["leak_list"] = "leak list",
        ["leak_harness"] = "leak harness",
        ["leak_join"] = "leak join",
        ["leak_ab"] = "leak ab",
        ["leak_benchmark"] = "leak benchmark",
        ["port_unit"] = "port unit",
        ["port_deps"] = "port deps",
        ["port_preview"] = "port preview",
        ["port_into_version"] = "port into-version",
        ["bundle_diff"] = "bundle diff",
        ["bundle_merge"] = "bundle merge",
        ["palette_doodad"] = "palette",
        ["place_doodad"] = "place doodad",
        ["place_grid"] = "place grid",
        ["place_scatter"] = "place scatter",
        ["place_region"] = "place region",
        ["place_unit"] = "place unit",
        ["place_start_location"] = "place start-location",
        ["place_item"] = "place item",
        ["camera_list"] = "camera list",
        ["camera_add"] = "camera add",
        ["camera_set"] = "camera set",
        ["camera_remove"] = "camera remove",
        ["camera_tour"] = "camera tour",
        ["pathing_paint"] = "pathing paint",
        ["pathing_export_png"] = "pathing export-png",
        ["pathing_import_png"] = "pathing import-png",
        ["pathing_validate"] = "pathing validate",
        ["map_info_get"] = "map-info get",
        ["map_info_set"] = "map-info set",
        ["player_list"] = "player list",
        ["player_set_force"] = "player set-force",
        ["force_list"] = "force list",
        ["force_set_flags"] = "force set-flags",
        ["gamedata_schema_diff"] = "gamedata schema-diff",
        ["gamedata_sources"] = "gamedata sources",
        ["gamedata_export_schemas"] = "gamedata export-schemas",
        ["gamedata_history"] = "gamedata history",
        ["gamedata_changelog"] = "gamedata changelog",
        ["gamedata_field_lookup"] = "gamedata field-lookup",
        ["gamedata_bounds"] = "gamedata bounds",
        ["gamedata_coverage"] = "gamedata coverage",
        ["bench_schema_coverage"] = "bench schema-coverage",
        ["bench_compat"] = "bench compat",
        ["bench_leaks"] = "bench leaks",
        ["bench_open"] = "bench open",
        ["bench_perf_gate"] = "bench perf-gate",
        ["map_compat"] = "compat",
        ["compat_matrix"] = "compat matrix",
        ["compat_explain"] = "compat explain",
        ["region_list"] = "region list",
        ["region_remove"] = "region remove",
        ["region_create"] = "region create",
        ["region_to_rect"] = "region to-rect",
        ["region_validate"] = "region validate",
        ["new_map"] = "new",
        ["terrain_stats"] = "terrain stats",
        ["terrain_deform"] = "terrain deform",
        ["terrain_export_heightmap"] = "terrain export-heightmap",
        ["terrain_import_heightmap"] = "terrain import-heightmap",
        ["terrain_smooth"] = "terrain smooth",
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
        ["trigger_to_script"] = "trigger to-script",
        ["trigger_diff"] = "trigger diff",
        ["trigger_lint"] = "trigger lint",
        ["trigger_export"] = "trigger export",
        ["trigger_dead"] = "trigger dead",
        ["target_get"] = "target",
        ["target_set"] = "target set",
        ["target_list"] = "target list",
        ["mpq_verify"] = "mpq verify",
        ["mpq_listfile"] = "mpq listfile",
        ["mpq_name_recover"] = "mpq name-recover",
        ["mpq_strip_bookkeeping"] = "mpq strip-bookkeeping",
        ["mpq_bytes_diff"] = "mpq bytes-diff",
    };

    // CLI commands that intentionally have no MCP tool (local dev / query utilities).
    private static readonly HashSet<string> CliOnly = new(StringComparer.Ordinal)
    {
        "gamedata schema",
        "gamedata schema-index",
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
        // The server itself and the commands that register it with AI apps. They are how the MCP
        // surface is reached, not map operations, so there is nothing for a tool to mirror.
        "mcp serve", "mcp install", "mcp uninstall", "mcp config", "mcp clients", "mcp doctor",
        "mcp version", "mcp help",
        // Dumps the tool catalog the server itself builds, so it is about the surface rather
        // than about a map and there is no map operation for a tool to mirror.
        "mcp tools",
        "target clear",
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
        // Writes a definition folder to disk; local file output like 'extract'. That reason is
        // true of export and was wrong about the other two, which are now wired. 'hero install'
        // produces an edited MAP, which is exactly what the other write tools produce, and
        // 'hero lint' returns findings rather than writing anything. If the hero definition
        // format is the centre of this tool, an agent has to be able to check one and install it.
        "hero export",
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
        // A command with its own handler is callable even when it also has subcommands, as
        // 'audit <map>' is beside 'audit hero'.
        if (prefix.Length > 0 && (subs.Count == 0 || cmd.Handler is not null)) yield return prefix;
        if (subs.Count == 0) yield break;
        foreach (var child in subs)
        {
            var next = prefix.Length == 0 ? child.Name : $"{prefix} {child.Name}";
            foreach (var p in LeafPaths(child, next))
                yield return p;
        }
    }

    private static HashSet<string> RealCliLeaves() =>
        LeafPaths(Wc3Ctl.Program.BuildRoot(), "").ToHashSet(StringComparer.Ordinal);

    // Two lines of this tool were merged, and each had added an 'audit' command, a 'file set'
    // command and an MCP tool named unit_abilities in its own shape. The parser keeps one of two
    // same-named siblings without an error, and the sets above fold duplicates away, so nothing
    // else here would notice the second one vanishing.
    [Fact]
    public void No_two_cli_commands_or_mcp_tools_share_a_name()
    {
        var clashes = new List<string>();
        void Walk(Command cmd, string path)
        {
            foreach (var g in cmd.Subcommands.GroupBy(c => c.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
                clashes.Add($"'{(path.Length == 0 ? "" : path + " ")}{g.Key}' is defined {g.Count()} times");
            foreach (var c in cmd.Subcommands) Walk(c, path.Length == 0 ? c.Name : $"{path} {c.Name}");
        }
        Walk(Wc3Ctl.Program.BuildRoot(), "");

        clashes.AddRange(typeof(Wc3Tools)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => n is not null)
            .GroupBy(n => n!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"MCP tool '{g.Key}' is defined {g.Count()} times"));

        Assert.True(clashes.Count == 0, string.Join("\n", clashes));
    }

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
