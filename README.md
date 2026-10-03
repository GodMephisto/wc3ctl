# wc3ctl

Made by GodMephisto

[![CI](https://github.com/GodMephisto/wc3ctl/actions/workflows/ci.yml/badge.svg)](https://github.com/GodMephisto/wc3ctl/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/GodMephisto/wc3ctl)](https://github.com/GodMephisto/wc3ctl/releases/latest)

wc3ctl reads, checks and edits Warcraft III maps (`.w3x` and `.w3m` files). It
does the jobs of the World Editor, and it keeps every part of a map you did not
touch exactly as it was, byte for byte.

You can use it three ways, and one install gives you all three.

| | What it is | How you start it |
|---|---|---|
| **Studio** | A desktop editor with windows and tabs, for terrain, units, objects, triggers, script and more | Start menu, **wc3ctl Studio** |
| **The command line** | Commands you type in PowerShell, good for quick checks and for scripts | type `wc3ctl` in a PowerShell window |
| **AI apps** | Lets an assistant such as Claude, Cursor or VS Code Copilot open and edit your maps when you ask it to | ask your AI app, it starts wc3ctl by itself |

**Contents.** [What you need](#what-you-need) ·
[Install](#install) · [Open Studio](#open-studio) ·
[Use the command line](#use-the-command-line) ·
[Use it from an AI app](#use-it-from-an-ai-app) · [Update](#update) ·
[Uninstall](#uninstall) · [Troubleshooting](#troubleshooting) ·
[What it can do](#what-it-can-do) · [Build from source](#build-from-source) ·
[Credits](#credits)

## What you need

- **Windows 10 or 11, 64-bit.** wc3ctl reads the game's data with a Windows library.
- **Warcraft III Reforged**, installed, for anything that shows base game data
  (unit names, icons, abilities, models). Without it wc3ctl still opens maps and
  works with the map's own data, and says when something needed the game.
- **Nothing else.** No .NET, no Visual Studio, no admin rights. The download
  carries everything it needs.

## Install

### The easy way, one line in PowerShell

1. Click **Start**, type `PowerShell`, and click **Windows PowerShell**. A blue
   (or black) window opens with a blinking cursor.
2. Copy this line.

   ```powershell
   irm https://raw.githubusercontent.com/GodMephisto/wc3ctl/master/install.ps1 | iex
   ```

3. Click inside the PowerShell window, right-click once to paste it, and press
   **Enter**.
4. Wait about a minute. You will see lines such as `wc3ctl v0.1.0`,
   `checksum ok`, `installed to ...`, `added wc3ctl Studio to the Start menu` and
   `added to your PATH`, then a short report from `wc3ctl mcp doctor` listing your
   AI apps and the Warcraft III folder it found.
5. **Close that PowerShell window.** A new window is needed before the `wc3ctl`
   command is recognised. If an AI app was open, quit it fully and start it again.

That is all. Here is what it did, so nothing is a surprise.

| What | Where |
|---|---|
| The command line tool and the AI app server, `wc3ctl.exe` | `%LOCALAPPDATA%\Programs\wc3ctl` (that is `C:\Users\<you>\AppData\Local\Programs\wc3ctl`) |
| Studio, `Wc3.Studio.exe` | `%LOCALAPPDATA%\Programs\wc3ctl\studio` |
| A Start menu entry | **wc3ctl Studio** |
| Your user PATH | gains the wc3ctl folder once, so `wc3ctl` works in any new terminal |
| Your AI apps | each supported app found on this PC gets a `wc3ctl` entry, and its settings file is first copied to `NAME.wc3ctl.bak` |

It checks the SHA-256 of every download before unpacking it, needs no admin
rights and changes nothing outside your own user account.

To leave a part out, download `install.ps1` from this repository and run it with
one or more switches.

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -NoStudio -NoRegister -NoPath
```

`-NoStudio` skips the editor, `-NoRegister` skips the AI app setup and `-NoPath`
leaves your PATH alone.

### By hand, from the zip files

1. Open the [latest release](https://github.com/GodMephisto/wc3ctl/releases/latest).
2. Under **Assets**, download `wc3ctl-studio-v<version>-win-x64.zip` for the
   editor and `wc3ctl-v<version>-win-x64.zip` for the command line tool. Each has
   a `.sha256` file beside it if you want to check the download.
3. Before unzipping, right-click each zip, choose **Properties**, tick
   **Unblock** at the bottom if it is there, and click **OK**. Otherwise Windows
   may stop the program the first time with "Windows protected your PC". If you
   already see that message, click **More info**, then **Run anyway**.
4. Right-click each zip, choose **Extract All...**, and pick a folder you will
   keep, for example `C:\Tools\wc3ctl` and `C:\Tools\wc3ctl-studio`.
5. Start the editor by double-clicking `Wc3.Studio.exe` in its folder. Keep every
   file in each folder together. Moving the exe out on its own breaks it.
6. To use the command line or AI apps from that folder, open PowerShell there
   (in File Explorer, click the address bar, type `powershell` and press
   **Enter**) and run `.\wc3ctl.exe mcp install --all`. That adds the folder to
   your PATH and sets up your AI apps.

## Open Studio

Click **Start** and open **wc3ctl Studio**. If you installed by hand, double-click
`Wc3.Studio.exe` in the folder you unzipped it to.

### The first time

1. Click **File**, then **Settings...**.
2. **Warcraft III install folder.** Leave it empty and Studio finds the game
   itself. Only if it does not (unit names and icons stay blank), click the
   folder button and choose the folder that holds your Warcraft III install, for
   example `C:\Program Files (x86)\Warcraft III`.
3. **Default map folder.** Choose where your maps live, usually
   `Documents\Warcraft III\Maps`. Every open and save dialog then starts there.
4. Click **Save**. Studio remembers both from now on.

### Open a map and look around

1. **Make a copy of your map first** (see [Saving](#saving) below for why).
2. Click **Open Map...** at the top of the left pane, or **File**, then
   **Open Source Map...**, and choose a `.w3x` or `.w3m` file.
3. The map opens in the left pane, with a row of tabs.

| Tab | What you do there |
|---|---|
| **Terrain** | See the map from above, and paint ground textures, raise and lower ground, cliffs, ramps, water and blight |
| **Palette** | Pick a unit, item, doodad or destructable to place on the terrain |
| **Unit** | Edit the placed unit you selected, its owner, position, facing, hero level, stats and more |
| **Doodad** | Edit the placed doodad or destructable you selected |
| **Players** | Player slots, their race, controller and team (force) |
| **Map Info** | Map name, author, description, suggested players, tileset, fog and other scenario settings |
| **Objects** | The object editor, units, items, abilities, buffs, upgrades, doodads and destructables, with every field the World Editor has, grouped the same way |
| **Files** | Every file inside the map, with a viewer, replace and extract, and **Save Map As...** |
| **Script** | The map's script (`war3map.j` or `.lua`), with a list of its functions to jump to, search, and a jump to everywhere a name is used |
| **Triggers** | The GUI trigger tree, with rename, enable and disable, and adding or removing events, conditions and actions |
| **Strings** | The map's text strings and its imported files |
| **Regions** | Every region, by name and size |
| **Cameras** | Every camera, with its target, angle and distance |
| **Sounds** | Every sound definition and its settings |
| **Dependencies** | Everything an object needs, its models, textures, icons, other objects and script |
| **Hero Audit** | Checks that each hero's abilities are actually wired up in the script, not just present |

### Saving

The **Save** button writes your changes **back over the map file you opened**.
That is how the World Editor behaves too, but it means a mistake is saved into
your only copy. So keep a backup, or start by using **Files**, then
**Save Map As...**, to work on a new file.

- **Save** is greyed out until you change something.
- **Test in WC3** saves, then starts Warcraft III on the map, so you can play it
  straight away.
- **New Blank Map** makes an empty map in memory. The first **Save** asks where
  to put it.

### Copy a hero from one map to another

1. Open the map that has the hero (the **source**) in the left pane.
2. Click **File**, then **Open Target Map...**, and open the map that should
   receive it. It opens in the right pane.
3. In the source, select the hero in **Objects**.
4. Click **Preview...** to see everything that would be copied (its abilities,
   buffs, models, icons and the script that runs its spells) without writing
   anything.
5. Click **Port selected → Target**. Studio writes a **new** file next to the
   target, named `<target>.ported.w3x`, and opens it in the right pane. Neither
   original is changed.

Play-test the ported map by loading it **directly in Warcraft III**. Do not open
and save it in the World Editor first, because the editor rebuilds the script
from its own trigger list and drops the copied spell code.

## Use the command line

Open a **new** PowerShell window after installing (Start, type `PowerShell`).
Type a command and press **Enter**.

```powershell
wc3ctl --help
```

That lists every command. Add `--help` after any command to see what it takes,
for example `wc3ctl object set --help`.

A few things to know.

- Put a map path in **double quotes** when it contains spaces, for example
  `"C:\Users\you\Documents\Warcraft III\Maps\My Map.w3x"`. You can drag a map
  file from File Explorer into the PowerShell window to paste its full path.
- Commands that change a map **never overwrite it**. They write to the file you
  name with `-o`, or to a new file next to the original.
- Add `--json` to any command for output another program can read.
- If the game is not found automatically, add
  `--game-dir "C:\Program Files (x86)\Warcraft III"` (your own folder).

Some examples, each with what it does.

```powershell
# Show the map's name, author, players and size
wc3ctl info "MyMap.w3x"

# List every file inside the map
wc3ctl ls "MyMap.w3x"

# Find a word anywhere, in file names, the script, the text strings and object data
wc3ctl search "MyMap.w3x" "fireball"

# List the map's custom units, then show every field of one of them
wc3ctl object list "MyMap.w3x" --kind unit
wc3ctl object get  "MyMap.w3x" H000 --kind unit

# Rename unit H000, saving the result as a new file
wc3ctl object set "MyMap.w3x" H000 unam "Raiden Ei" --kind unit -o "MyMap.edited.w3x"

# Every ability a unit has, and where each one comes from
wc3ctl unit abilities "MyMap.w3x" H000

# Check a map for problems before you share it
wc3ctl audit "MyMap.w3x"
wc3ctl lint "MyMap.w3x"

# See everything hero H000 needs, then copy it into another map
# (writes Target.ported.w3x, both originals stay as they are)
wc3ctl bundle unit "Source.w3x" H000
wc3ctl port unit "Source.w3x" H000 "Target.w3x"

# Make a new empty map the World Editor can open
wc3ctl new "Blank.w3x" --name "My Arena"
```

The command groups, by job.

| Job | Commands |
|---|---|
| Look inside a map | `info`, `ls`, `file`, `search`, `diff`, `roundtrip`, `extract`, `strings`, `imports`, `mpq-diff`, `mpq-hash` |
| Object data | `object`, `asset`, `bundle`, `port`, `render-model`, `convert` |
| Placed units and doodads | `place`, `unit`, `doodad`, `region`, `palette` |
| Terrain | `terrain`, `pathing`, `render` |
| Map settings | `map-info`, `player`, `force`, `camera`, `sound`, `new` |
| Triggers and script | `trigger`, `script`, `editor` |
| Checks | `audit`, `lint`, `validate`, `test-load`, `contract`, `hero` |
| Repairs | `repair`, `repair-generated`, `deprotect`, `debug` |
| Replays and patches | `replay`, `uabi-profile`, `gamedata` |
| AI apps | `mcp` |

## Use it from an AI app

The installer has already set this up for every supported app it found. Quit the
app fully (for Claude Desktop, right-click its icon by the clock and choose
**Quit**) and start it again. The app now lists the wc3ctl tools, 97 of them.

Then just ask, and give the map's full path. For example

- "Open `C:\Users\you\Documents\Warcraft III\Maps\MyMap.w3x` and list its custom heroes."
- "Which abilities does unit H000 have in that map, and where does each come from?"
- "Audit the map and tell me which problems are errors."
- "Rename the trigger called Init to Setup and save it as `MyMap.edited.w3x`."

The assistant can read anything, and any change it makes is saved to a **new**
file. It cannot overwrite the map you gave it.

The supported apps are Claude Code, Claude Desktop, Cursor, VS Code (GitHub
Copilot), Windsurf, Gemini CLI, Codex CLI, Cline, LM Studio and Zed. These
commands manage the setup.

```
wc3ctl mcp install --all            set up every supported app found on this PC
wc3ctl mcp install cursor vscode    set up only the apps named
wc3ctl mcp uninstall --all          remove it from every app
wc3ctl mcp config <app>             print the settings, to paste in by hand
wc3ctl mcp clients                  list the supported apps and whether each is set up
wc3ctl mcp doctor                   check the install, the game folder and the apps
```

Setup changes only its own `wc3ctl` entry and keeps a copy of each settings file
as `NAME.wc3ctl.bak`. A settings file with comments in it (common in Zed and
VS Code) is never rewritten, and the lines to paste are printed instead. Claude
Code is set up through its own `claude mcp add` command.

Any other MCP app can start `wc3ctl.exe` with the arguments `mcp serve`. If the
game is installed somewhere unusual, run
`wc3ctl mcp install --all --game-dir "<your Warcraft III folder>"`.

## Update

Run the same install line again. It replaces the program files with the newest
release. Your Studio settings (kept in `%APPDATA%`) and your AI app setup stay as
they are. Close Studio and quit your AI apps first,
because Windows will not replace a program that is running.

## Uninstall

Close Studio and quit your AI apps, then in PowerShell run

```powershell
wc3ctl mcp uninstall --all
Remove-Item "$env:LOCALAPPDATA\Programs\wc3ctl" -Recurse -Force
Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\wc3ctl Studio.lnk"
```

The first line removes wc3ctl from your AI apps and from your PATH, the second
deletes the program, and the third removes the Start menu entry. Your maps are
not touched.

## Troubleshooting

| What you see | What to do |
|---|---|
| `wc3ctl : The term 'wc3ctl' is not recognized` | Open a **new** PowerShell window. The PATH change only reaches windows opened after the install |
| The install line stops with a red download error | Check your internet connection and run the install line again |
| "Windows protected your PC" | Click **More info**, then **Run anyway**, or unblock the zip before extracting (see [By hand](#by-hand-from-the-zip-files)) |
| Unit names, icons and abilities are blank | wc3ctl did not find Warcraft III. In Studio set the folder in **File**, **Settings...**. On the command line add `--game-dir "<folder>"` |
| The AI app does not show the wc3ctl tools | Quit the app fully and start it again. Then run `wc3ctl mcp doctor` to see whether it is set up |
| A settings file was not changed | It has comments in it or is not valid JSON, so the lines to paste were printed instead. `wc3ctl mcp config <app>` prints them again |
| Update says it could not replace the folder | Close Studio and any AI app using wc3ctl, then run the install line again |
| Studio's **Save** is greyed out | Nothing has changed since the last save |

Anything else, open an [issue](https://github.com/GodMephisto/wc3ctl/issues) with
the map (if you can share it), what you did and what it printed.

## What it can do

- **Keep maps intact.** Opens any MPQ map, including protected ones and the
  512-byte header some maps carry, reads every known `war3map.*` file, keeps the
  rest as raw bytes, and saves so untouched files are identical to the original.
  A file it cannot read becomes a warning, never a crash.
- **Object data.** All seven kinds (units, items, abilities, destructables,
  doodads, buffs, upgrades), with the game's base values from your install under
  the map's own changes, per-level ability fields, the World Editor's grouping
  and allowed values, and saving that changes only the field you set.
- **Search.** File names, script lines, text strings and object data in one go.
- **Triggers and script.** Reads and edits the GUI trigger tree (names, flags,
  categories, events, conditions and actions), rebuilds a tree from a standard
  script for maps that lost theirs, and lists the script's functions and where
  each is used.
- **Placement and terrain.** Units, items, doodads, destructables, regions and
  start locations, with height, cliff, ramp, texture, water, blight and pathing
  brushes, and pictures of the terrain or a model as PNG.
- **Map settings.** Scenario info, players and teams, cameras, sounds, the text
  strings and the import list.
- **Checks and repairs.** Compares abilities with their own tooltips, finds
  broken requirements, references to objects that do not exist, model paths that
  load nothing, portrait problems and heroes whose abilities are not wired up,
  and repairs what can be repaired safely. `test-load` starts the game unattended
  and reports whether the map loads.
- **Copying between maps.** Finds everything an object needs (objects, buffs,
  summoned units, files and the script behind its spells) and copies it into
  another map, renaming any ids that clash.

## Build from source

You need Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
In PowerShell,

```powershell
git clone https://github.com/GodMephisto/wc3ctl
cd wc3ctl
dotnet test --filter "Category!=Corpus&Category!=GameData"
powershell -ExecutionPolicy Bypass -File .\scripts\publish-all.ps1
```

That builds `dist\wc3ctl.exe` (the command line tool and AI app server),
`dist-studio\Wc3.Studio.exe` (the editor) and `dist-mcp\Wc3.Mcp.exe` (the server
on its own). Keep `CascLib.dll` beside each exe. Then run
`.\dist\wc3ctl.exe mcp install --all` to put it on your PATH and set up your AI
apps.

| Project | Role |
|---|---|
| `src/Wc3.MapDocument` | the map model, load and save with per-file raw bytes and change tracking (namespace `Wc3.Model`) |
| `src/Wc3.GameData` | Reforged base data through CASC, objects, strings and profiles |
| `src/Wc3.Modeling` | MDX and MDL models and BLP textures |
| `src/Wc3.Render` | terrain and model pictures |
| `src/Wc3.Commands` | every operation, shared by the three front ends |
| `src/wc3ctl` | the command line tool and `wc3ctl mcp` |
| `src/Wc3.Studio` | the desktop editor |
| `src/Wc3.Mcp` | the AI app server and its setup |
| `tests/` | tests for the commands, the command line, the server and Studio |

```powershell
dotnet test --filter "Category!=Corpus&Category!=GameData"   # what CI runs, needs nothing else
dotnet test --filter "Category=GameData"                     # needs a Warcraft III install
dotnet test --filter "Category=Corpus"                       # needs real maps on disk
```

Corpus tests read maps from `Documents\Warcraft III\Maps\Download`, or from the
folder in `WC3_CORPUS_DIR`. Round-trip comparisons leave out the MPQ bookkeeping
files `(listfile)`, `(attributes)` and `(signature)`, which are rebuilt on save
and hold no map content.

Contributions are welcome, see [CONTRIBUTING.md](CONTRIBUTING.md). Report
security problems privately, see [SECURITY.md](SECURITY.md). The AI app server
is also published on its own as
[wc3-mcp](https://github.com/GodMephisto/wc3-mcp), built from this same source.

## Credits

Built on

| Library | Author |
|---|---|
| War3Net (War3Net.Build, War3Net.IO.Mpq, War3Net.Drawing.Blp, War3Net.CodeAnalysis.Decompilers) | Drake53 and contributors |
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
