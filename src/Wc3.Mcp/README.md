# Wc3.Mcp, an MCP server for wc3ctl

An MCP (Model Context Protocol) server over **stdio** that exposes wc3ctl's map
toolkit to any MCP-capable client. Built on the official
[C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk)
(`ModelContextProtocol` 1.4.1, stable).

Every tool is a thin wrapper over the shared `Wc3.Commands` layer, the same
`Execute` methods the CLI and Studio call, returning the same result POCOs as
JSON. No map parsing or logic lives here.

## Build and publish

```
dotnet build src/Wc3.Mcp
dotnet publish src/Wc3.Mcp -c Release -o dist-mcp
```

The published server is `dist-mcp/Wc3.Mcp.exe`. The native `CascLib.dll` (for
base-game data queries) is resolved from `dist-mcp/runtimes/win-x64/native/`. If
game-data lookups ever report "could not open game data", copy that file beside
the exe (the same caveat as the CLI's `dist/`).

## Register with an MCP client

Add the server under `mcpServers` in your client's MCP config (for example a
project `.mcp.json`, or a desktop client's config file).

```json
{
  "mcpServers": {
    "wc3-mcp": {
      "command": "C:\\path\\to\\dist-mcp\\Wc3.Mcp.exe",
      "env": { "WC3_GAME_DIR": "C:\\path\\to\\Warcraft III" }
    }
  }
}
```

`WC3_GAME_DIR` is optional. It sets the Warcraft III install used to resolve
base-game data. Precedence per call is the tool's `game_dir` argument, then the
env var, then auto-detection.

## Tools

Map paths are parameters on every tool, so one server instance serves any map on
disk. The full tool set mirrors the CLI (object get, list, set, and new,
placement, terrain, map-info, players and forces, cameras, pathing, sounds,
bundle and port, render, and blank-map create). Write tools never edit in place,
they save to a separate output map. A representative subset follows.

- `map_info` (map). Map metadata, name, author, players, playable dimensions, load diagnostics.
- `list_files` (map). The archive's internal file list, names (null means unnamed or protected), sizes, and known and parsed flags.
- `object_list` (map, kind defaults to unit, optional game_dir). Custom or modified objects of one kind (unit, item, ability, destructable, doodad, buff, upgrade).
- `object_get` (map, rawcode, optional kind, optional game_dir). An object's merged fields (base game data overlaid with map deltas), names resolved, kind auto-detected when omitted.
- `bundle_unit` (map, rawcode, optional game_dir). The unit's dependency closure of objects, asset files, strings, edges, and JASS functions. The porting preview.
- `render_model` (map, rawcode or internal model path, out_path, optional kind, optional game_dir). Renders the object's map-imported model to a PNG at out_path, returns the path and byte size, and a PNG up to 1 MB also comes back inline as MCP image content.
- `port_unit` (source_map, rawcode, target_map, optional include_script, optional game_dir). Ports the unit's bundle into the target, auto-remapping rawcode collisions. It writes a NEW `<target>.ported.<ext>` file and never overwrites the source or target maps. Returns the full port report.

Errors (missing map, bad rawcode, unreadable archive) surface as clean MCP tool
errors, a one-line message, never a stack trace.

## Architecture

- `Program.cs`, the stdio host (`Microsoft.Extensions.Hosting`), all logging goes to stderr because stdout carries the protocol.
- `Wc3McpServer.cs`, the single source of truth for server identity and the tool set, shared by the exe and the tests.
- `Wc3Tools.cs`, one method per tool, `[McpServerTool]`-attributed, delegating to `Wc3.Commands`.

## Verified how

`tests/Wc3.Tests/McpServerTests.cs` drives the exact server wiring through the
SDK's own `McpClient` over in-memory pipes (hermetic, no processes, no WC3
install). It covers the initialize handshake, `tools/list` returning the full
tool set, a real `list_files` call against a synthetic map, and the clean-error
path. The published exe was additionally smoke-checked by piping raw JSON-RPC
(initialize, then notifications/initialized, then tools/list) into stdin.
