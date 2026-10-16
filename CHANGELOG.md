# Changelog

All notable changes to wc3ctl. One file per release lives under
[`changelog/`](changelog/), and this file shows the latest one. The format
follows Keep a Changelog, and the project follows Conventional Commits.

## 0.4.8 - 2026-10-16

The front door and the engine behind it. A new user can now reach the tool without ever seeing
.NET, through an npx launcher, a one-click installer and a self-updater that replaces a running exe.
The Studio greets you with a drop zone and a Connect AI Apps button. Behind that, the protected-map
typing pass runs in parallel and a custom-text trigger body is written through a byte-level
war3map.wct splice.

### Added

- `npx -y wc3-mcp@latest` runs the MCP server with no install step. A zero-dependency Node launcher
  downloads the matching release into a per-version cache folder, verifies its sha256 and spawns the
  exe. The per-version folders are the reason an upgrade never hits a Windows lock on a running
  `wc3-mcp.exe` or `CascLib.dll`. The launcher writes every status line to stderr, because the MCP
  stdio transport owns stdout.
- `wc3ctl update` and `wc3-mcp update` reinstall the latest release over the running install. A
  locked exe or dll is renamed to a `.old` sibling and the new file copied in, so an AI app holding
  the old binary open no longer has to be closed first.
- `wc3ctl associate` registers an "Edit in wc3ctl Studio" entry on the Explorer right-click menu
  for `.w3x` and `.w3m`, and `Wc3.Studio.exe <map>` opens a map passed on the command line.
- The Studio opens on a welcome view, a drop zone for a `.w3x` or `.w3m`, the ten most recent maps,
  and up to six maps found under `Documents\Warcraft III\Maps`. A map can be dragged onto the
  workspace, and `Ctrl+O`, `Ctrl+Shift+O` and `Ctrl+S` do the menu actions.
- A Connect AI Apps dialog lists every supported client with its detection and registration state
  and sets all the detected ones up with one button.
- A `quickstart` MCP tool returns the ten core workflows, inspect, port a hero, repair a Reforged
  map, fix black portraits, shrink a map, hunt leaks, edit objects, terrain and placement, Lua
  conversion and patch compatibility, each with the tools and CLI commands that do it. Five MCP
  prompts wrap the same workflows so a client can offer them directly.
- `trigger var list|add|set|remove` adds, edits and removes war3map.wtg variables, and
  `trigger set-code` writes a custom-text body through a byte-level war3map.wct splice that copies
  every untouched slot byte for byte. The wct decoder is lossy for bytes that are not valid UTF-8,
  so writing back through its model was never safe.
- Studio 3D terrain sculpt strokes journal one undo entry each, the way the 2D minimap strokes
  already did, including the case where a drag loses window capture mid-stroke.

### Changed

- The installer edits a client config that holds comments. VS Code and Zed ship comments in their
  default JSON and the old writer refused any file with a comment, which forced a hand copy. The new
  writer splices only the bytes of the one entry and leaves every comment and every other setting
  untouched, and it removes the entry's own comma so a strict client never sees a trailing comma.
- `wc3ctl quickstart [--goal <text>]` exposes the workflow router on the command line.

### Fixed

- Reading SLK-defined object rawcodes resolved the file name with `System.IO.Path.GetFileName`,
  which treats only the forward slash as a separator off Windows, so it kept the whole archive path
  of `Units\AbilityBuffData.slk` and a map audited on macOS or Linux missed every SLK-defined
  rawcode. `Wc3Path` handles both separators now.
- SLKs and `Units\UnitSkin.txt` were read by name only, so a protected map with a stripped
  `(listfile)` lost them. They resolve by MPQ hash now.

### Performance

- The first typing pass over a protected map opened one shared archive under a lock and read 64,738
  nameless entries on one thread, 22.6 seconds. The pass now reopens the archive per thread and
  types the entries in parallel.

### Tests

- A positive test for the byte-level wct splice, which sets one slot and asserts every other slot
  survives verbatim, and a case where replacing a slot with the text it already holds returns the
  same bytes. The riskiest new contract had only a refusal test before.
- A test for removing the last entry from a commented client config, parsed with trailing commas
  disallowed, because the removal left a trailing comma and a lenient parse hid it.
- `trigger_set_code`, `trigger_var_*` and `quickstart` join `ExpectedTools` and `ToolToCliPath`, and
  the new mutating tools join `MutatingPrefixes`.
