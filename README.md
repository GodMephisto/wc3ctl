# wc3ctl, a Warcraft III map toolkit

A byte-faithful toolkit for inspecting and editing Warcraft III `.w3x` and `.w3m`
maps, built as a World Editor replacement. It has three front-ends over one
shared command layer.

- `wc3ctl`, a command-line tool (pass `--json` on any command for scripting).
- Studio, an Avalonia desktop editor (terrain, objects, placement, map settings,
  triggers viewer, and more).
- An MCP server that exposes the same operations to MCP-capable agents.

Everything runs through `Wc3.Commands`, so the CLI, GUI, and MCP surfaces stay in
lockstep. Untouched files are written back byte for byte, and only edited
sub-files are re-serialized.

## What it does

- **Inspect and round-trip.** Open an MPQ-based map (including the 512-byte
  pre-archive header), parse every known `war3map.*` file into a typed model,
  preserve everything else as raw bytes, and save so untouched files compare
  byte-identical to the original. Parse failures degrade to diagnostics, never
  crashes.
- **Object data.** Merged base-game data (via the installed game's CASC storage)
  overlaid with the map's deltas, for all seven object kinds (units, items,
  abilities, destructables, doodads, buffs, upgrades), with per-level ability
  fields and byte-faithful write-back.
- **Placement.** Place and remove units, items, doodads, destructables, and
  regions, set start locations, and edit placed-unit properties.
- **Terrain.** Height, cliff, water, blight, and texture brushes with undo and
  redo, plus terrain and model rendering to PNG.
- **Map setup.** Scenario info (name, author, description, tileset, fog, and
  more), players and forces, cameras, sounds, and the imported-audio catalog.
- **Porting.** Resolve an object's full dependency closure (objects, buffs,
  summoned units, referenced files, and the JASS functions that implement its
  skills) and inject it into another map, auto-remapping rawcode collisions.

## Usage

### Command line

Build once (or use the published `dist/wc3ctl.exe`), then run commands against a
map. Wrap paths that contain spaces in quotes.

```bash
# Inspect a map
wc3ctl info "MyMap.w3x"
wc3ctl ls "MyMap.w3x"

# List the map's custom units, then read one unit's merged fields
wc3ctl object list "MyMap.w3x" --kind unit
wc3ctl object get  "MyMap.w3x" H000 --kind unit

# Change a field and write a copy (the source is never overwritten)
wc3ctl object set "MyMap.w3x" H000 unam "Raiden Ei" --kind unit -o "MyMap.edited.w3x"

# See everything a unit depends on before porting it
wc3ctl bundle unit "Source.w3x" H000

# Port a hero (its objects, assets, and skill triggers) into another map.
# The target is never touched, a sibling Target.ported.w3x is written.
wc3ctl port unit "Source.w3x" H000 "Target.w3x"

# Create a fresh, World-Editor-openable blank map
wc3ctl new "Blank.w3x" --name "My Arena"
```

Add `--json` to any command for machine-readable output. Base-game names and
icons need an installed Warcraft III (auto-detected, or pass `--game-dir <path>`).

### Studio (desktop editor)

Launch `dist-studio\Wc3.Studio.exe` (or `dotnet run --project src/Wc3.Studio`).

1. **File, then Settings** (once). Point it at your Warcraft III install folder
   and a default map folder. Both are remembered across launches.
2. **File, then Open Source Map**, and pick the map you want to inspect, edit, or
   port from.
3. Work through the tabs, terrain, objects, placement, map info, the triggers
   viewer, and Dependencies. The **Dependencies** tab lists an object's real
   dependencies (its model, textures, and icons), with the trigger-carried skill
   effects tucked under a separate, collapsed **Port assets** group.
4. **To port a hero**, open a Target map (File, then Open Target Map), select the
   hero in the source pane, and press **Port**. A sibling `.ported.w3x` is written
   and opened in the Target pane. Use **Preview** first to see the report without
   writing anything.

Test a ported map by loading the `.ported.w3x` **directly in Warcraft III**. Do
not open and save it in the World Editor first, the editor regenerates the
trigger script from its own tree and would delete the ported skill logic.

### MCP server

Publish it to `dist-mcp` and register it with an MCP-capable client. The
registration snippet is in `src/Wc3.Mcp/README.md`. It exposes the same
operations as the CLI over stdio, so an agent can inspect and edit maps directly.

## Projects

- `src/Wc3.MapDocument`, the core model (load and save, per-file raw plus dirty
  tracking, format registry).
- `src/Wc3.GameData`, Reforged base data via CASC (objects, strings, profiles).
- `src/Wc3.Modeling`, MDX and MDL geometry parsing plus BLP texture decode.
- `src/Wc3.Render`, terrain heightmap and model rendering.
- `src/Wc3.Commands`, the shared command layer (front-end-agnostic result records).
- `src/wc3ctl`, the CLI front-end (System.CommandLine).
- `src/Wc3.Studio`, the Avalonia desktop editor.
- `src/Wc3.Mcp`, the MCP server over stdio.
- `tests/Wc3.Tests`, the xUnit suite (hermetic tests plus opt-in corpus and
  GameData tests).

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
wc3ctl bundle unit <map> <rawcode>            port unit <source> <rawcode> <target> [-o out]
wc3ctl new <out> [--name ...] [--tiles N]     Create a blank, WE-openable map
wc3ctl render <map> -o out.png
```

Add `--json` to any command for machine-readable output.

Resolving base game data requires an installed Warcraft III (Reforged). Its data
lives in CASC storage. The install is auto-detected, or pass `--game-dir <path>`.
Without an install, commands still work on map deltas only and say so.

## Build and test

Requires the .NET 8 SDK.

```bash
dotnet build
dotnet test --filter "Category!=Corpus&Category!=GameData"   # hermetic (CI-safe)
dotnet test --filter "Category=GameData"                     # needs an installed WC3
dotnet test --filter "Category=Corpus"                       # needs a real .w3x on disk
```

Publish a self-contained win-x64 build.

```bash
dotnet publish src/wc3ctl        -c Release -r win-x64 --self-contained -o dist
dotnet publish src/Wc3.Studio    -c Release -r win-x64 --self-contained -o dist-studio
dotnet publish src/Wc3.Mcp       -c Release -r win-x64 --self-contained -o dist-mcp
```

`wc3ctl` needs the native `CascLib.dll` beside `wc3ctl.exe` (produced in the
publish output, keep them together).

## Fidelity

Untouched sub-files are written back byte-identical. Round-trip comparisons
exclude MPQ bookkeeping (`(listfile)`, `(attributes)`, `(signature)`), which are
regenerated or dropped by the archive builder and carry no map content.
