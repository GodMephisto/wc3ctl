# Wc3.Mcp — MCP server for wc3ctl

An MCP (Model Context Protocol) server over **stdio** that exposes wc3ctl's map
toolkit to MCP clients (Claude Code, Claude Desktop, any MCP-capable agent).
Built on the official [C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk)
(`ModelContextProtocol` 1.4.1, stable).

Every tool is a thin wrapper over the shared `Wc3.Commands` layer — the same
`Execute` methods the CLI and Studio call — returning the same result POCOs as
JSON. No map parsing or logic lives here.

## Build & publish

```
dotnet build src/Wc3.Mcp
dotnet publish src/Wc3.Mcp -c Release -o dist-mcp
```

The published server is `dist-mcp/Wc3.Mcp.exe`. The native `CascLib.dll` (for
base-game data queries) is resolved from `dist-mcp/runtimes/win-x64/native/`;
if game-data lookups ever report "could not open game data", copy that file
beside the exe (same caveat as the CLI's `dist/`).

## Register with Claude Code / Claude Desktop

Claude Code (`.mcp.json` in your project, or `claude mcp add`):

```json
{
  "mcpServers": {
    "wc3-mcp": {
      "command": "D:\\playground\\Programming\\Wc3_CLI\\dist-mcp\\Wc3.Mcp.exe",
      "env": { "WC3_GAME_DIR": "D:\\Warcraft III" }
    }
  }
}
```

Claude Desktop: the same snippet goes under `mcpServers` in
`%APPDATA%\Claude\claude_desktop_config.json`.

`WC3_GAME_DIR` is optional — it sets the Warcraft III install used to resolve
base-game data. Precedence per call: the tool's `game_dir` argument, then the
env var, then auto-detection.

## Tools

Map paths are parameters on every tool, so one server instance serves any map
on disk. All tools are read-only except `port_unit` (which still never touches
its input maps).

| Tool | Arguments | What it does |
| --- | --- | --- |
| `map_info` | `map` | Map metadata: name, author, players, playable dimensions, load diagnostics. |
| `list_files` | `map` | The archive's internal file list: names (null = unnamed/protected), sizes, known/parsed flags. |
| `object_list` | `map`, `kind` (default `unit`), `game_dir?` | Custom/modified objects of one kind: `unit\|item\|ability\|destructable\|doodad\|buff\|upgrade`. |
| `object_get` | `map`, `rawcode`, `kind?`, `game_dir?` | An object's merged fields (base game data ⊕ map deltas), names resolved; kind auto-detected when omitted. |
| `bundle_unit` | `map`, `rawcode`, `game_dir?` | The unit's dependency closure — objects, asset files, strings, edges, JASS functions. The porting preview. |
| `render_model` | `map`, `rawcode` (or internal model path), `out_path`, `kind?`, `game_dir?` | Renders the object's map-imported model to a PNG at `out_path`; returns path + byte size, and PNGs ≤ 1 MB also come back inline as MCP image content. |
| `port_unit` | `source_map`, `rawcode`, `target_map`, `include_script?`, `game_dir?` | Ports the unit's bundle into the target, auto-remapping rawcode collisions. **Writes a NEW `<target>.ported.<ext>` file — never overwrites the source or target maps.** Returns the full port report. |

Errors (missing map, bad rawcode, unreadable archive) surface as clean MCP tool
errors — a one-line message, never a stack trace.

## Architecture

- `Program.cs` — stdio host (`Microsoft.Extensions.Hosting`); all logging goes
  to stderr because stdout carries the protocol.
- `Wc3McpServer.cs` — single source of truth for server identity + tool set,
  shared by the exe and the tests.
- `Wc3Tools.cs` — one method per tool, `[McpServerTool]`-attributed, delegating
  to `Wc3.Commands`.

## Verified how

`tests/Wc3.Tests/McpServerTests.cs` drives the exact server wiring through the
SDK's own `McpClient` over in-memory pipes (hermetic — no processes, no WC3
install): initialize handshake, `tools/list` returning all seven tools, a real
`list_files` call against a synthetic map, and the clean-error path. The
published exe was additionally smoke-checked by piping raw JSON-RPC
(`initialize` → `notifications/initialized` → `tools/list`) into stdin.
