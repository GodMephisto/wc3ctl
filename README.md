# wc3ctl — Warcraft III map toolkit

A byte-faithful toolkit for inspecting and editing Warcraft III `.w3x`/`.w3m`
maps, built as a World Editor replacement with three front-ends over one shared
command layer:

- **`wc3ctl`** — a command-line tool (`--json` on every command for scripting).
- **Studio** — an Avalonia desktop editor (terrain, objects, placement, map
  settings, triggers viewer, and more).
- **MCP server** — exposes the same operations to MCP-capable agents.

Everything runs through `Wc3.Commands`, so the CLI, GUI, and MCP surfaces stay
in lockstep. Untouched files are written back byte-for-byte; only edited
sub-files are re-serialized.

## What it does

- **Inspect & round-trip** — open an MPQ-based map (including the 512-byte
  pre-archive header), parse every known `war3map.*` file into a typed model,
  preserve everything else as raw bytes, and save so untouched files compare
  byte-identical to the original. Parse failures degrade to diagnostics, never
  crashes.
- **Object data** — merged base-game data (via the installed game's CASC
  storage) overlaid with the map's deltas, for all seven object kinds (units,
  items, abilities, destructables, doodads, buffs, upgrades), with per-level
  ability fields and byte-faithful write-back.
- **Placement** — place and remove units, items, doodads, destructables, and
  regions; set start locations; edit placed-unit properties.
- **Terrain** — height/cliff/water/blight/texture brushes with undo/redo, plus
  terrain and model rendering to PNG.
- **Map setup** — scenario info (name, author, description, tileset, fog, …),
  players and forces, cameras, sounds, and the imported-audio catalog.
- **Porting** — resolve an object's full dependency closure (objects, buffs,
  summoned units, referenced files, and the JASS functions that implement its
  skills) and inject it into another map, auto-remapping rawcode collisions.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Wc3.MapDocument` | Core model: load/save, per-file raw + dirty tracking, format registry |
| `src/Wc3.GameData` | Reforged base data via CASC (objects, strings, profiles) |
| `src/Wc3.Modeling` | MDX/MDL geometry parsing + BLP texture decode |
| `src/Wc3.Render` | Terrain heightmap and model rendering |
| `src/Wc3.Commands` | Shared command layer (front-end-agnostic result records) |
| `src/wc3ctl` | CLI front-end (System.CommandLine) |
| `src/Wc3.Studio` | Avalonia desktop editor |
| `src/Wc3.Mcp` | MCP server (stdio) |
| `tests/Wc3.Tests` | xUnit suite: hermetic tests + opt-in corpus/GameData tests |

## CLI

```
wc3ctl info <map>                     Map metadata
wc3ctl ls <map>                       List internal files
wc3ctl roundtrip <map>                Verify byte-faithful save
wc3ctl object get <map> <rawcode> [--kind unit|item|ability|...]
wc3ctl object list <map> --kind <kind>
wc3ctl object set <map> <rawcode> <field> <value> [--kind <kind>]
wc3ctl place unit|item|doodad|region|start-location <map> ... [-o out]
wc3ctl terrain deform|cliff|paint|water|blight <map> ...
wc3ctl map-info get|set <map> ...
wc3ctl player list|set-force <map> ...        force list|set-flags <map> ...
wc3ctl camera list|add|set|remove <map> ...   pathing paint <map> ...
wc3ctl sound list|add|set|remove <map> ...
wc3ctl bundle unit <map> <rawcode>            port unit <map> <rawcode> --into <target>
wc3ctl new <out> [--name ...] [--tiles N]     Create a blank, WE-openable map
wc3ctl render <map> -o out.png
```

Add `--json` to any command for machine-readable output.

Resolving base game data requires an installed Warcraft III (Reforged); its data
lives in CASC storage. The install is auto-detected, or pass `--game-dir <path>`.
Without an install, commands still work on map deltas only and say so.

## Build & test

Requires the .NET 8 SDK.

```bash
dotnet build
dotnet test --filter "Category!=Corpus&Category!=GameData"   # hermetic (CI-safe)
dotnet test --filter "Category=GameData"                     # needs an installed WC3
dotnet test --filter "Category=Corpus"                       # needs a real .w3x on disk
```

Publish (self-contained, win-x64):

```bash
dotnet publish src/wc3ctl        -c Release -r win-x64 --self-contained -o dist
dotnet publish src/Wc3.Studio    -c Release -r win-x64 --self-contained -o dist-studio
dotnet publish src/Wc3.Mcp       -c Release -r win-x64 --self-contained -o dist-mcp
```

`wc3ctl` needs the native `CascLib.dll` beside `wc3ctl.exe` (produced in the
publish output; keep them together).

## Fidelity

Untouched sub-files are written back byte-identical. Round-trip comparisons
exclude MPQ bookkeeping (`(listfile)`, `(attributes)`, `(signature)`), which are
regenerated or dropped by the archive builder and carry no map content.
