// src/Wc3.Mcp/Wc3Tools.cs
using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Wc3.Commands;
using Wc3.Model;

namespace Wc3.Mcp;

/// <summary>
/// The MCP tool surface: one thin wrapper per Wc3.Commands handler. Each tool
/// loads the map(s), calls the shared command layer and returns its result POCO
/// (the SDK serializes it to JSON) — no map parsing or logic lives here.
/// </summary>
[McpServerToolType]
public static class Wc3Tools
{
    private const string KindValues = "unit|item|ability|destructable|doodad|buff|upgrade";

    [McpServerTool(Name = "map_info", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Show a Warcraft III map's metadata: name, author, player count, playable dimensions and load diagnostics.")]
    public static MapInfoResult MapInfo(
        [Description("Path to a .w3x/.w3m map file.")] string map)
        => Run(() => InfoCommand.Execute(LoadMap(map)));

    [McpServerTool(Name = "list_files", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the map archive's internal files: name (null = unnamed/protected entry), size in bytes, and whether wc3ctl knows/parses the format.")]
    public static FileListResult ListFiles(
        [Description("Path to a .w3x/.w3m map file.")] string map)
        => Run(() => ListCommand.Execute(LoadMap(map)));

    [McpServerTool(Name = "object_list", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the map's custom/modified objects of one Object Editor kind: rawcode, base rawcode (null = created from scratch) and resolved name.")]
    public static ObjectListResult ObjectList(
        [Description("Path to a .w3x/.w3m map file.")] string map,
        [Description("Object kind: " + KindValues + ".")] string kind = "unit",
        [Description("Warcraft III install directory (overrides auto-detection and the WC3_GAME_DIR env var). Used to resolve base-game names.")] string? game_dir = null)
        => Run(() => ObjectListCommand.Execute(LoadMap(map), ParseKind(kind), ResolveGameDir(game_dir)));

    [McpServerTool(Name = "object_get", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Get an object's merged fields (base game data overlaid by the map's deltas), each labeled 'base' or 'map', with field names resolved. Omit kind to auto-detect it from the rawcode.")]
    public static MergedObjectResult ObjectGet(
        [Description("Path to a .w3x/.w3m map file.")] string map,
        [Description("Four-character object rawcode, e.g. 'hfoo' or 'u000'.")] string rawcode,
        [Description("Object kind: " + KindValues + ". Default: probe every kind (map deltas first, then base game data).")] string? kind = null,
        [Description("Warcraft III install directory (overrides auto-detection and the WC3_GAME_DIR env var). Without it only the map's own deltas resolve.")] string? game_dir = null)
        => Run(() => kind is null
            ? ObjectGetCommand.Execute(LoadMap(map), rawcode, ResolveGameDir(game_dir))
            : ObjectGetCommand.Execute(LoadMap(map), ParseKind(kind), rawcode, ResolveGameDir(game_dir)));

    [McpServerTool(Name = "bundle_unit", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Resolve everything a unit depends on - the porting preview: referenced objects (with custom-to-map flags), asset files, trigger strings, dependency edges and the JASS trigger-function closure.")]
    public static UnitBundle BundleUnit(
        [Description("Path to a .w3x/.w3m map file.")] string map,
        [Description("Four-character unit rawcode, e.g. 'u000'.")] string rawcode,
        [Description("Warcraft III install directory (overrides auto-detection and the WC3_GAME_DIR env var). Used to tell base-game references from custom ones.")] string? game_dir = null)
        => Run(() => BundleCommand.ResolveUnit(LoadMap(map), rawcode, ResolveGameDir(game_dir)));

    /// <summary>PNGs up to this size also return inline as MCP image content.</summary>
    private const int InlineImageLimit = 1_000_000;

    [McpServerTool(Name = "render_model", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Render an object's model (map-imported .mdx/.mdl) to a PNG written at out_path. Returns the saved path and byte size; small images also come back inline as MCP image content. The map itself is never modified.")]
    public static CallToolResult RenderModel(
        [Description("Path to a .w3x/.w3m map file.")] string map,
        [Description("Four-character object rawcode (e.g. 'u000'), or an internal model path (contains '\\', '/' or '.').")] string rawcode,
        [Description("Output PNG file path. Created/overwritten; parent directories are created as needed.")] string out_path,
        [Description("Object kind: unit|item|destructable|doodad. Default: auto-detect from the rawcode.")] string? kind = null,
        [Description("Warcraft III install directory (overrides auto-detection and the WC3_GAME_DIR env var).")] string? game_dir = null)
        => Run(() =>
        {
            if (string.IsNullOrWhiteSpace(out_path))
                throw new McpException("out_path is required");
            var doc = LoadMap(map);

            // Anything that can't be a rawcode is treated as an internal model path (CLI parity).
            bool isPath = rawcode.Length != 4 || rawcode.IndexOfAny(new[] { '\\', '/', '.' }) >= 0;
            byte[] png = isPath
                ? RenderModelCommand.Execute(doc, rawcode)
                : kind is null
                    ? RenderModelCommand.Execute(doc, rawcode, ResolveGameDir(game_dir))
                    : RenderModelCommand.Execute(doc, ParseKind(kind), rawcode, ResolveGameDir(game_dir));

            string full = Path.GetFullPath(out_path);
            if (string.Equals(full, Path.GetFullPath(map), StringComparison.OrdinalIgnoreCase))
                throw new McpException("out_path must not be the map file itself");
            if (Path.GetDirectoryName(full) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            File.WriteAllBytes(full, png);

            bool inline = png.Length <= InlineImageLimit;
            var info = new RenderModelToolResult(full, png.Length, inline);
            var content = new List<ContentBlock>
            {
                new TextContentBlock { Text = JsonSerializer.Serialize(info, Wc3McpServer.JsonOptions) },
            };
            if (inline)
                content.Add(new ImageContentBlock { MimeType = "image/png", Data = png });
            return new CallToolResult { Content = content };
        });

    [McpServerTool(Name = "port_unit", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Port a unit (its custom-object closure + imported assets + strings, and best-effort its JASS trigger closure) from a source map into a target map, auto-remapping rawcode collisions. WRITES A NEW FILE '<target>.ported.<ext>' next to the target map (overwritten if it exists from a previous run) - the source and target maps themselves are NEVER modified. Returns the full port report: remaps, ported objects, copied files, inlined strings, script info and warnings.")]
    public static PortUnitToolResult PortUnit(
        [Description("Map to port FROM (.w3x/.w3m). Read-only.")] string source_map,
        [Description("Four-character rawcode of the unit to port, e.g. 'u000'.")] string rawcode,
        [Description("Map to port INTO (.w3x/.w3m). Read-only - the result is saved as a sibling '<target>.ported.<ext>' file.")] string target_map,
        [Description("Also carry the unit's JASS trigger-function closure into the target script (best effort). Default true.")] bool include_script = true,
        [Description("Warcraft III install directory (overrides auto-detection and the WC3_GAME_DIR env var).")] string? game_dir = null)
        => Run(() =>
        {
            var source = LoadMap(source_map);
            var target = LoadMap(target_map); // fresh load - ported output never feeds back into inputs
            var bundle = BundleCommand.ResolveUnit(source, rawcode, ResolveGameDir(game_dir));
            var report = PortCommand.PortUnit(source, bundle, target, includeScript: include_script);

            string targetFull = Path.GetFullPath(target_map);
            string outPath = Path.Combine(Path.GetDirectoryName(targetFull) ?? ".",
                Path.GetFileNameWithoutExtension(targetFull) + ".ported" + Path.GetExtension(targetFull));
            target.Save(outPath);
            return new PortUnitToolResult(outPath, report);
        });

    [McpServerTool(Name = "palette_doodad", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the doodad types placeable on a map - the palette to consult before place_doodad. Unions the base-game doodad catalog (installed GameData) with the map's own object-data: custom New* doodads and modified Base* doodads. Each entry carries its four-char rawcode, a resolved display name (null when unresolvable), its source (base|map-custom|map-modified) and the base it derives from. Without a WC3 install the palette is map-only. The map is never modified.")]
    public static DoodadPaletteResult PaletteDoodad(
        [Description("Path to a .w3x/.w3m map file.")] string map,
        [Description("Warcraft III install directory (overrides auto-detection and the WC3_GAME_DIR env var).")] string? game_dir = null)
        => Run(() => PaletteCommand.DoodadPalette(LoadMap(map), ResolveGameDir(game_dir)));

    [McpServerTool(Name = "place_doodad", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Place a doodad instance at (x, y) on the map's doodad layer (war3map.doo) and save the edited map to out_path. The input map is NEVER modified in place - out_path must differ from it. Returns the assigned creation number.")]
    public static PlacementToolResult PlaceDoodad(
        [Description("Path to the source .w3x/.w3m map file. Read-only; the edited copy is written to out_path.")] string map,
        [Description("Output map file path (must differ from the input map). Created/overwritten; parent directories are created as needed.")] string out_path,
        [Description("Four-character doodad type rawcode from palette_doodad, e.g. 'ATtr'.")] string type_rawcode,
        [Description("World X coordinate.")] float x,
        [Description("World Y coordinate.")] float y,
        [Description("World Z height offset. Default 0.")] float z = 0f,
        [Description("Facing angle in radians. Default 0.")] float rotation = 0f,
        [Description("Uniform scale. Default 1.")] float scale = 1f,
        [Description("Doodad variation index. Default 0.")] int variation = 0)
        => Run(() => SavePlacement(map, out_path, doc =>
        {
            var r = PlacementCommand.PlaceDoodad(doc, type_rawcode, x, y, z, rotation, scale, variation);
            return (r.Ok, r.Message, r.CreationNumber);
        }));

    [McpServerTool(Name = "place_region", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Define a rectangular region on the map's region layer (war3map.w3r) and save the edited map to out_path. The input map is NEVER modified in place - out_path must differ from it. Bounds need right>left and top>bottom. Returns the assigned creation number.")]
    public static PlacementToolResult PlaceRegion(
        [Description("Path to the source .w3x/.w3m map file. Read-only; the edited copy is written to out_path.")] string map,
        [Description("Output map file path (must differ from the input map). Created/overwritten; parent directories are created as needed.")] string out_path,
        [Description("Region name (non-empty).")] string name,
        [Description("West edge, min X (world coordinate).")] float left,
        [Description("South edge, min Y (world coordinate).")] float bottom,
        [Description("East edge, max X (world coordinate).")] float right,
        [Description("North edge, max Y (world coordinate).")] float top)
        => Run(() => SavePlacement(map, out_path, doc =>
        {
            var r = PlacementCommand.PlaceRegion(doc, name, left, bottom, right, top);
            return (r.Ok, r.Message, r.CreationNumber);
        }));

    // ---- shared plumbing -------------------------------------------------

    /// <summary>Expected failures become clean MCP tool errors, never stack traces.</summary>
    private static T Run<T>(Func<T> body)
    {
        try { return body(); }
        catch (McpException) { throw; }
        catch (Exception ex) { throw new McpException(ex.Message); }
    }

    private static MapDocument LoadMap(string map)
    {
        if (string.IsNullOrWhiteSpace(map))
            throw new McpException("map path is required");
        if (!File.Exists(map))
            throw new McpException($"map not found: {map}");
        try { return MapDocument.Load(map); }
        catch (Exception ex) { throw new McpException($"could not load '{map}': {ex.Message}"); }
    }

    /// <summary>Load the map, apply a placement mutation, and save the edited copy to out_path.
    /// The input map is never modified in place (out_path must differ) - mirroring the no-clobber
    /// stance of render_model and port_unit. A rejected placement surfaces as a clean MCP error.</summary>
    private static PlacementToolResult SavePlacement(
        string map, string outPath, Func<MapDocument, (bool Ok, string Message, int CreationNumber)> place)
    {
        if (string.IsNullOrWhiteSpace(outPath))
            throw new McpException("out_path is required");
        string full = Path.GetFullPath(outPath);
        if (string.Equals(full, Path.GetFullPath(map), StringComparison.OrdinalIgnoreCase))
            throw new McpException("out_path must not be the input map file itself");

        var doc = LoadMap(map);
        var (ok, message, creationNumber) = place(doc);
        if (!ok) throw new McpException(message);

        if (Path.GetDirectoryName(full) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
        doc.Save(full);
        return new PlacementToolResult(full, message, creationNumber);
    }

    private static ObjectKind ParseKind(string kind)
    {
        try { return ObjectKinds.Parse(kind); }
        catch (ArgumentException ex) { throw new McpException(ex.Message); }
    }

    /// <summary>Explicit argument wins; else the WC3_GAME_DIR env var; else auto-detect.</summary>
    private static string? ResolveGameDir(string? game_dir) =>
        !string.IsNullOrWhiteSpace(game_dir) ? game_dir
        : Environment.GetEnvironmentVariable("WC3_GAME_DIR") is { Length: > 0 } env ? env
        : null;
}

/// <summary>render_model outcome: where the PNG landed and whether it was also inlined.</summary>
public sealed record RenderModelToolResult(string SavedTo, int SizeBytes, bool ImageInline);

/// <summary>port_unit outcome: where the new .ported map was written plus the full port report.</summary>
public sealed record PortUnitToolResult(string SavedTo, PortResult Report);

/// <summary>place_doodad / place_region outcome: where the edited map was written, a
/// human-readable message, and the creation number assigned to the new instance.</summary>
public sealed record PlacementToolResult(string SavedTo, string Message, int CreationNumber);
