# wc3ctl

Made by GodMephisto

[![CI](https://github.com/GodMephisto/wc3ctl/actions/workflows/ci.yml/badge.svg)](https://github.com/GodMephisto/wc3ctl/actions/workflows/ci.yml)

A byte-faithful toolkit for reading, auditing and editing Warcraft III `.w3x`
and `.w3m` maps, built to do the World Editor's jobs from a terminal, a desktop
editor or an AI assistant. Files you do not touch keep their bytes, and only the
files you edit are written again.

One shared command layer, `Wc3.Commands`, sits under three front ends, so a fix
in a command reaches all of them.

- **`wc3ctl`**, the command line tool. Every command takes `--json`. The same exe
  also runs the MCP server as `wc3ctl mcp serve` and registers it with your AI
  apps through `wc3ctl mcp install`.
- **Studio**, an Avalonia desktop editor for terrain, objects, placement, map
  settings, triggers, script and porting.
- **The MCP server**, 97 tools that let an AI app such as Claude, Cursor or
  VS Code inspect and edit maps. It ships inside `wc3ctl.exe` and also as its own
  `Wc3.Mcp.exe`.

## What it does

- **Round-trip faithfully.** Opens MPQ maps (including the 512-byte pre-archive
  header and protected maps), parses every known `war3map.*` file into a typed
  model, keeps everything else as raw bytes, and saves so untouched files are
  identical to the original. A parse failure becomes a diagnostic, never a crash.
- **Object data.** Base game data read from your install's CASC storage, overlaid
  with the map's own changes, for all seven kinds (units, items, abilities,
  destructables, doodads, buffs, upgrades), with per-level ability fields, the
  World Editor's field grouping and legal values, and write-back that changes
  only the field you set.
- **Search.** File names, script lines, string table entries and object data
  (rawcodes and field values) in one query.
- **Triggers and script.** Reads and writes the GUI trigger tree
  (`war3map.wtg` and `war3map.wct`), edits names, flags, categories, events,
  conditions and actions, recovers a tree from a standard script, and indexes
  `war3map.j` functions and references.
- **Placement and terrain.** Units, items, doodads, destructables, regions and
  start locations, plus height, cliff, ramp, texture, water, blight and pathing
  brushes, and terrain or model renders to PNG.
- **Map setup.** Scenario info, players and forces, cameras, sounds, the string
  table and the import table.
- **Audit and repair.** Checks abilities against the map's own tooltips,
  inherited requirements, dangling references, model paths that load nothing,
  portrait risks, hero wiring and runtime readiness, then repairs what can be
  repaired safely. `lint`, `validate` and `test-load` check a map before you ship
  it, the last by loading it in the game unattended.
- **Porting.** Resolves an object's full dependency closure (objects, buffs,
  summons, files, and the JASS that implements its skills) and copies it into
  another map, renaming rawcodes that collide.

## What you need

- 64-bit Windows. The game data reader (CascLib) is a Windows library.
- Warcraft III Reforged, only for the commands that read base game data. Without
  it, commands work on the map's own data and say so.
- Nothing else. The release carries its own .NET.

## Install

Open PowerShell and run

```powershell
irm https://raw.githubusercontent.com/GodMephisto/wc3ctl/master/install.ps1 | iex
```

That downloads the latest release and checks its SHA-256. It puts `wc3ctl` in
`%LOCALAPPDATA%\Programs\wc3ctl` and on your PATH, adds **wc3ctl Studio** to the
Start menu, and sets up every supported AI app it finds to use the MCP server.
No admin rights are needed. Run the same line again to update. Open a new
terminal afterwards so `wc3ctl` is found, and restart any AI app.

To skip parts of it, download `install.ps1` and run it with `-NoStudio`,
`-NoRegister` (no AI app setup) or `-NoPath`.

Prefer to do it by hand? The [Releases page](https://github.com/GodMephisto/wc3ctl/releases)
has `wc3ctl-<version>-win-x64.zip` (the CLI and MCP server) and
`wc3ctl-studio-<version>-win-x64.zip` (the editor), each with a `.sha256`.
Unzip them anywhere and keep `CascLib.dll` beside each exe.

## Build from source

With the .NET 8 SDK installed, in PowerShell,

```powershell
git clone https://github.com/GodMephisto/wc3ctl
cd wc3ctl
dotnet test --filter "Category!=Corpus&Category!=GameData"
dotnet publish src/wc3ctl/wc3ctl.csproj -c Release --self-contained -r win-x64 -o dist
dotnet publish src/Wc3.Studio/Wc3.Studio.csproj -c Release --self-contained -r win-x64 -o dist-studio
```

`dist\wc3ctl.exe` is the CLI and the MCP server, and `dist-studio\Wc3.Studio.exe`
is the editor. Keep `CascLib.dll` beside each exe. To build the standalone
server as well, publish `src/Wc3.Mcp/Wc3.Mcp.csproj` to `dist-mcp` the same way.

## Command line

Wrap paths that contain spaces in quotes. Writing commands never overwrite their
input. They save to `-o <path>` or to a sibling file they name.

```powershell
# Inspect
wc3ctl info "MyMap.w3x"
wc3ctl ls "MyMap.w3x"
wc3ctl search "MyMap.w3x" "fireball"

# Read and change object data
wc3ctl object list "MyMap.w3x" --kind unit
wc3ctl object get  "MyMap.w3x" H000 --kind unit
wc3ctl object set  "MyMap.w3x" H000 unam "Raiden Ei" --kind unit -o "MyMap.edited.w3x"
wc3ctl unit abilities "MyMap.w3x" H000

# Check a map before shipping it
wc3ctl audit "MyMap.w3x"
wc3ctl lint "MyMap.w3x"

# Port a hero into another map (writes Target.ported.w3x, touches neither input)
wc3ctl bundle unit "Source.w3x" H000
wc3ctl port unit "Source.w3x" H000 "Target.w3x"

# A fresh map the World Editor opens
wc3ctl new "Blank.w3x" --name "My Arena"
```

`wc3ctl --help` lists every command group, and `wc3ctl <command> --help` shows
each one's arguments. The groups are

| Area | Commands |
|---|---|
| Inspect | `info`, `ls`, `file`, `search`, `diff`, `roundtrip`, `extract`, `strings`, `imports`, `mpq-diff`, `mpq-hash` |
| Objects | `object`, `asset`, `bundle`, `port`, `render-model`, `convert` |
| Placement | `place`, `unit`, `doodad`, `region`, `palette` |
| Terrain | `terrain`, `pathing`, `render` |
| Map setup | `map-info`, `player`, `force`, `camera`, `sound`, `new` |
| Triggers and script | `trigger`, `script`, `editor` |
| Checks | `audit`, `lint`, `validate`, `test-load`, `contract`, `hero` |
| Repair | `repair`, `repair-generated`, `deprotect`, `debug` |
| Replays and patches | `replay`, `uabi-profile`, `gamedata` |
| AI apps | `mcp` |

The Warcraft III folder is found from the Windows registry and the usual install
paths. When yours is elsewhere, pass `--game-dir <folder>`.

## Studio

Run `dist-studio\Wc3.Studio.exe`.

1. Open **File, Settings** once and point it at your Warcraft III folder and your
   maps folder. Both are remembered.
2. Open a map with **File, Open Source Map**.
3. Work through the panels for terrain, objects, placement, regions, cameras, map
   info, players, triggers, script and dependencies. Edits mark the map as
   changed until you save.
4. To port a hero, open a target with **File, Open Target Map**, select the hero
   in the source, and press **Port**. **Preview** shows the report without
   writing anything.

Test a ported map by loading it directly in Warcraft III. Opening and saving it
in the World Editor first regenerates the script from the editor's own trigger
tree and drops the ported skill code.

## Use it from an AI app

The installer already did this. To redo it, or after building from source, run

```powershell
wc3ctl mcp install --all
```

That registers `wc3ctl.exe mcp serve` with every supported AI app it finds and
adds the exe's folder to your user PATH once, so plain `wc3ctl` works in new
terminals (`--no-path` skips that). Restart the app afterwards.

```
wc3ctl mcp install --all            set up every supported app found on this PC
wc3ctl mcp install cursor vscode    set up only the apps named
wc3ctl mcp uninstall --all          remove it again
wc3ctl mcp config <app>             print the settings to paste by hand
wc3ctl mcp clients                  list supported apps and whether each is set up
wc3ctl mcp doctor                   check the install, the game folder and the apps
```

The supported apps are Claude Code, Claude Desktop, Cursor, VS Code (GitHub
Copilot), Windsurf, Gemini CLI, Codex CLI, Cline, LM Studio and Zed. Setup
changes only its own `wc3ctl` entry and copies each file to `NAME.wc3ctl.bak`
before writing it. A settings file with comments in it is never rewritten, and
the settings to paste are printed instead. Claude Code is set up through its own
`claude mcp add` command.

Any other MCP app can start `wc3ctl.exe` with the arguments `mcp serve`. To
point the server at a Warcraft III folder it cannot find, install with
`--game-dir "<folder>"`, which sets `WC3_GAME_DIR` in each app's entry, or pass
`game_dir` to a single tool call.

The standalone `Wc3.Mcp.exe` takes the same setup verbs (`Wc3.Mcp.exe install
--all`) and is the server when started with no arguments.

The 97 tools cover map info and files, search, object data and forms, placed
units and doodads, regions, terrain, pathing, cameras, sounds, scenario,
players and forces, the trigger tree and the trigger catalog, script functions
and references, strings and imports, audits and lint, heroes, bundling and
porting, model renders and new maps. Write tools take an `out_path` that must
differ from the input map, so the original is never overwritten. Every tool has
a CLI command, and a test fails if the two drift apart.

## Projects

| Project | Role |
|---|---|
| `src/Wc3.MapDocument` | the map model, load and save with per-file raw bytes and dirty tracking (namespace `Wc3.Model`) |
| `src/Wc3.GameData` | Reforged base data through CASC, objects, strings and profiles |
| `src/Wc3.Modeling` | MDX and MDL geometry and BLP textures |
| `src/Wc3.Render` | terrain and model rendering |
| `src/Wc3.Commands` | the shared commands, returning plain result records |
| `src/wc3ctl` | the CLI and `wc3ctl mcp` |
| `src/Wc3.Studio` | the desktop editor |
| `src/Wc3.Mcp` | the MCP server and its client setup |
| `tests/` | xUnit suites for the commands, the CLI, the MCP server and the Studio |

## Tests

```powershell
dotnet test --filter "Category!=Corpus&Category!=GameData"   # hermetic, what CI runs
dotnet test --filter "Category=GameData"                     # needs a Warcraft III install
dotnet test --filter "Category=Corpus"                       # needs real maps on disk
```

Corpus tests read maps from your Documents\Warcraft III\Maps folder, or from
`WC3_CORPUS_DIR`. Round-trip comparisons leave out the MPQ bookkeeping files
`(listfile)`, `(attributes)` and `(signature)`, which the archive writer
regenerates and which hold no map content.

Contributions are welcome, see [CONTRIBUTING.md](CONTRIBUTING.md). Report
security problems through [SECURITY.md](SECURITY.md).

## Credits

Built on

| Library | Author |
|---|---|
| War3Net (War3Net.Build, War3Net.IO.Mpq, War3Net.Drawing.Blp) | Drake53 and contributors |
| CascLib | Ladislav Zezula |
| CascLib.NET | Kizari |
| ImageSharp | Six Labors and contributors |
| Avalonia | AvaloniaUI and contributors |
| AvaloniaEdit | Eli Arbel and contributors |
| System.CommandLine | .NET Foundation and contributors |
| MCP C# SDK (ModelContextProtocol) | Model Context Protocol project |
| Microsoft.Extensions.Hosting | .NET Foundation and contributors |
| NAudio | Mark Heath and contributors |
| Silk.NET | .NET Foundation and contributors |
| Inter typeface | The Inter Project Authors |

It also uses Luashine/jass-history (inventory change study), pjass (JASS syntax
checking), the SLK repair guide by devoltz and Arakunido, and the Hive Workshop
community's notes on the game's command line arguments.

Learned from, among many others, HiveWE (stijnherfst), Warsmash and Retera's
Model Studio (Retera), WC3MapTranslator (ChiefOfGxBxL), Wurst, TheHelper and the
Hive Workshop. The full list is in [CREDITS.md](CREDITS.md), and each library's
license text is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Warcraft III is made by Blizzard Entertainment. This project is unofficial, is
not affiliated with or endorsed by Blizzard, reads your own install and ships no
game files.
