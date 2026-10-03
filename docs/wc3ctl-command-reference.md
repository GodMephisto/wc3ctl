# wc3ctl verified command reference

Produced 2026-09-19 by running `dist\wc3ctl.exe` and by
reading the repository source. Every usage line below is copied from the tool's own
`--help` output. Every JSON shape below came from a real invocation, not from the source
and not from memory. Where a command behaved unexpectedly the behaviour is recorded as
measured, with the output verbatim.

Probe map used for the JSON shapes, read only, never written to.

```
Documents\Warcraft III\Maps\Download\BleachVsOnepiece15.w3x
```

Scale of the surface. 30 top level verbs. 14 of them run directly, 16 are command groups.
44 leaf subcommands sit under the groups. 58 runnable commands in total.

Updated 2026-09-26 for the uabi disconnect work, with usage copied from `--help` on the build
published that day. Added since 2026-09-19. Two top level verbs, `replay` and `uabi-profile`,
five `repair` subcommands, `data-pointers`, `model-paths`, `uabi-runtime`, `audit-errors` and
`preload`, the `script leaks` subcommand, and the audit check `missing-model`. That makes 32 top
level verbs and 66 runnable commands.

## Global options

Present on every command.

```
  --json                 Emit machine-readable JSON.
  --game-dir <game-dir>  Warcraft III install directory (overrides auto-detection).
  --version              Show version information
  -?, -h, --help         Show help and usage information
```

## Top level

```
wc3ctl [command] [options]
```

```
  info <map>                     Show map metadata.
  ls <map>                       List internal files.
  roundtrip <map>                Verify byte-faithful round-trip.
  search <map> <query>           Search map contents.
  diff <map> <mapB>              Diff two maps.
  object                         Object data queries.
  extract <map> <internal-path>  Extract internal files to disk. []
  render <map>                   Render a top-down terrain image to PNG.
  render-model <map> <rawcode>   Render an object's model (.mdx/.mdl) to PNG - map-imported models first, base-game (CASC) models when a WC3 install is available.
  script                         Map script queries.
  bundle                         Dependency bundles for porting between maps.
  port                           Port content between maps.
  convert <input> <output>       Convert asset files between WC3 and standard formats (disk-to-disk).
  validate <map>                 Check a map for problems (missing/empty files, loader errors). Exits 2 if invalid.
  audit <map>                    Check abilities against the map's own tooltips, plus inherited requirements, portrait-risk models, dangling object references and model paths that load no file. Read-only. Exits 2 if any error-level issue is found.
  deprotect <map>                Recover the file names a protected map stripped, from its own data, its script, its models' texture declarations, and any listfiles supplied. Read-only without --apply.
  place                          Place objects on a map and save the edited copy.
  palette <map>                  List the doodad types placeable on a map (base-game catalog + the map's own object-data).
  terrain                        Terrain editing: height, cliffs, ramps, textures, water, blight.
  sound                          Sound catalog editing (war3map.w3s): list, add, set, remove.
  camera                         Camera catalog editing (war3map.w3c): list, add, set, remove.
  pathing                        Pathing-map editing (war3map.wpm): paint walk/build/fly/water bits over a brush.
  map-info                       Scenario fields (war3map.w3i): get, set (name, author, description, tileset, ...).
  player                         Player slots (war3map.w3i): list, set-force.
  force                          Forces / teams (war3map.w3i): list, set-flags.
  file                           Internal archive files, list, get-text, set (raw byte replace from disk).
  repair                         Repair generated/byproduct map issues and save the edited copy.
  new <out>                      Create a blank, World-Editor-openable map at the given path.
  trigger                        GUI trigger tooling (World-Editor catalog).
  gamedata                       Base game data, for tracking what a patch changes.
  replay <path>                  Read recorded games (.w3g) and list each one's map, length, and how every player left, flagging games where someone disconnected. Read-only. []
  uabi-profile <paths>           Measure every map's unit ability lists (uabi) side by side, without starting the game. Counts, distinct ids, hero and item abilities in normal lists, dangling ids, duplicates, and a per-race breakdown. Read-only.
```

## Read only verbs, no subcommands

```
wc3ctl info [<map>] [options]
wc3ctl ls [<map>] [options]
wc3ctl roundtrip [<map>] [options]
wc3ctl search [<map> <query>] [options]
wc3ctl diff [<map> <mapB>] [options]
wc3ctl validate [<map>] [options]
wc3ctl palette [<map>] [options]
```

Arguments.

```
info, ls, roundtrip, validate, palette
  <map>    Path to a .w3x/.w3m map.

search
  <map>    Path to a .w3x/.w3m map.
  <query>  Substring to search for.

diff
  <map>    Path to a .w3x/.w3m map.
  <mapB>   Path to the second map.
```

None of these take an option beyond the global set.

## audit

```
wc3ctl audit [<map>] [options]
```

```
Arguments:
  <map>  Path to a .w3x/.w3m map.

Options:
  --check <check>        Limit to named checks. Repeatable. Default runs all: level-gap, level-tooltip, tooltip-claim, orphan-ability, requirement, portrait-risk, dangling-reference, missing-model
```

The eight check names, read from `src/Wc3.Commands/AuditCommand.cs` (`AllChecks`).
`missing-model` was added 2026-09-26.

```csharp
public static readonly string[] AllChecks =
{
    "level-gap", "level-tooltip", "tooltip-claim", "orphan-ability",
    "requirement", "portrait-risk", "dangling-reference", "missing-model",
};
```

`missing-model` reports every model path in the script and the object data that resolves to
no file in the archive or the installed game, as warnings. The engine does not stop on a
missing model, it logs "model creation failed" and retries at every creation. Placeholders
that name no file at all, `.mdl` or `.mdl .mdl`, are reported separately, and a path with
exactly one intended file in the archive names it. `repair model-paths` applies those.

Measured run against `GGGA_V0.07b.w3x` on 2026-09-26, read only, sha256 prefix `2d43153a`.

```
wc3ctl audit "<map>" --json
  ObjectsChecked 2532, exit code 2
  by check     dangling-reference 144 (Error), requirement 101 (Error),
               missing-model 142, portrait-risk 106, level-tooltip 57,
               level-gap 12, tooltip-claim 6, orphan-ability 4
  dangling-reference  101 are one ability, A1A3, whose 101 levels each name buff B06H,
                      43 are uabi entries on 7 Human heroes naming abilities that exist nowhere
  requirement         inherited Robk 64, Roch 6, Ruba 5, Rubu 3, Rowt 3, others 20
```

Measured run against the probe map.

```
wc3ctl audit "<map>" --json
  Ok False, ObjectsChecked 897, Issues 238
  by severity  Warning 218, Error 20
  by check     portrait-risk 178, level-gap 37, requirement 20, level-tooltip 2, orphan-ability 1
  exit code 2
```

## extract

```
wc3ctl extract [<map> [<internal-path>]] [options]
```

```
Arguments:
  <map>            Path to a .w3x/.w3m map.
  <internal-path>  Exact internal file path to extract. []

Options:
  -o, --out <out>        Output directory (or output file for a single named extraction). Default: current directory.
  --pattern <pattern>    Glob pattern(s) matched against internal names ('*' wildcard); repeatable or comma-separated.
  --models               Extract models (*.mdx, *.mdl).
  --textures             Extract textures (*.blp, *.tga, *.dds).
  --sounds               Extract sounds (*.mp3, *.wav).
  --all                  Extract every internal file (including unnamed entries).
```

## render and render-model

```
wc3ctl render [<map>] [options]
wc3ctl render-model [<map> <rawcode>] [options]
```

```
render
  -o, --out <out>  Output PNG path. Default: <map name>.png in the current directory.

render-model
  <rawcode>        Four-character object rawcode, or an internal model path (contains '\', '/' or '.').
  --kind <kind>    Object type: unit|item|destructable|doodad. Default: auto-detect.
  -o, --out <out>  Output PNG path. Default: <rawcode>.png in the current directory.
```

## convert

```
wc3ctl convert <input> <output> [options]
```

```
Arguments:
  <input>   Source file: an image (.blp/.png/.jpg/.jpeg/.bmp/.tga/.gif) or a model (.mdx/.mdl).
  <output>  Destination file; its extension picks the format. Images: .png/.jpg/.jpeg/.bmp/.tga/.gif/.blp. Models: .obj (a .mtl is written beside it).
```

Both positionals are disk paths. Neither is a map.

## deprotect

```
wc3ctl deprotect [<map>] [options]
```

```
Options:
  --listfile <listfile>  Extra name dictionary, one name per line. Repeatable. Community listfiles and the listfiles of sibling maps both work.
  --apply                Write the recovered names into the map as a real (listfile), so every later load names those entries. Changes no other byte.
  -o, --out <out>        Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.
```

Measured against the probe map without `--apply`. TotalBlocks 850, NamedBefore 57,
NamedAfter 837.

## new

```
wc3ctl new <out> [options]
```

```
Arguments:
  <out>  Path to write the new .w3x/.w3m map.

Options:
  --name <name>    Map name (default: Blank Map).
  --tiles <tiles>  Playable size in tiles per edge (default: 32).
```

This is the one verb whose first positional is an output path rather than a map to read.

## object

```
wc3ctl object [command] [options]
```

```
wc3ctl object get  [<map> [<rawcode>]] [options]
wc3ctl object list [<map>] [options]
wc3ctl object set  [<map> [<rawcode> <field> <value>]] [options]
wc3ctl object new  [<map> <kind> <base>] [options]
```

```
object get
  <map>                  Path to a .w3x/.w3m map.
  <rawcode>              Four-character object rawcode.
  --field <field>        Restrict output to a single field.
  --kind <kind>          Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default: auto-detect.

object list
  <map>                  Path to a .w3x/.w3m map.
  --kind <kind>          Object type: unit|item|ability|destructable|doodad|buff|upgrade. [default: unit]

object set
  <map>                  Path to a .w3x/.w3m map.
  <rawcode>              Four-character object rawcode.
  <field>                Four-character field code (e.g. uhpm), or code:N for an ability/upgrade level or doodad variation.
  <value>                New value for the field.
  --kind <kind>          Object type: unit|item|ability|destructable|doodad|buff|upgrade. [default: unit]
  -o, --out <out>        Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

object new
  <map>                  Path to a .w3x/.w3m map.
  <kind>                 Object type: unit|item|ability|destructable|doodad|buff|upgrade.
  <base>                 Four-character rawcode of the base object the new custom object derives from.
  -o, --out <out>        Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.
```

`object get` auto-detects the kind. `object list` and `object set` do not, they default to
`unit`. `object new` takes the kind as a required positional, not as an option.

## script, bundle, port

```
wc3ctl script functions [<map>] [options]
wc3ctl script leaks     [<map>] [options]

wc3ctl bundle unit   [<map> [<rawcode>]] [options]
wc3ctl bundle object [<map> [<rawcode>]] [options]

wc3ctl port unit <source-map> <rawcode> <target-map> [options]
```

```
script leaks
  --all           List every finding, including code that runs only once at init.

bundle object
  --kind <kind>   Root object type: unit|item|ability|destructable|doodad|buff|upgrade. [default: unit]

port unit
  <source-map>    Map to port FROM.
  <rawcode>       Four-character unit rawcode, or a comma-separated list (H000,H001,.) to port several units into the same target in one operation.
  <target-map>    Map to port INTO.
  -o, --out <out> Output directory (or output file for a single named extraction). Default: current directory.
  --no-script     Port object data + assets + strings only; skip the best-effort JASS script closure append.
  --dry-run       Preview the port: print the full report (remaps, objects, files, strings, script) without writing anything.
```

`script leaks` finds handle leaks in `war3map.j` by rule. `discarded` is a creator called as a
statement. `inline` is a location, group or force created as an argument to anything but its
destroyer or a `Save*Handle`. `never-destroyed` is a local from a creator that the function never
destroys, returns, stores, starts as a timer, gives an action, or hands to a map function. Each
finding carries a heat, `hot` when a periodic timer or trigger reaches it, `repeat` for any
callback, `once` for init only, and the periodic timers are listed fastest first. It does not
follow handles through hashtables, so YDWE code that saves a handle and destroys it from a timer
is silent rather than guessed at. Measured 2026-09-26 on `Otaku Defense v2.6.2.w3x`, 2,762
functions, 304 periodic timers, 6 findings, 5 of them talent auras attached for the whole game.

## place

```
wc3ctl place doodad         [<map> <rawcode> [<x> [<y>]]] [options]
wc3ctl place region         [<map> <name> <left> <bottom> <right> <top>] [options]
wc3ctl place unit           [<map> <rawcode> <owner> [<x> [<y>]]] [options]
wc3ctl place start-location [<map> <player> [<x> [<y>]]] [options]
wc3ctl place item           [<map> <rawcode> [<x> [<y>]]] [options]
```

```
place doodad   --z 0, --rotation 0, --scale 1, --variation 0, -o/--out
place region   -o/--out only. left=min X, bottom=min Y, right=max X, top=max Y
place unit     <owner> is a 0-based player id (0 = red). --z 0, --rotation 0, --scale 1, -o/--out
place start-location  <player> is 0-based. -o/--out only. One per player, an existing one is moved
place item     no owner argument. --z 0, --rotation 0, --scale 1, -o/--out
```

`place unit` is the only one of the five with an `<owner>` positional, and it sits between
the rawcode and the coordinates.

## terrain

```
wc3ctl terrain stats  [<map>] [options]
wc3ctl terrain deform [<map> [<cx> [<cy> [<radius> <op>]]]] [options]
wc3ctl terrain cliff  [<map> [<cx> [<cy> [<radius> <op>]]]] [options]
wc3ctl terrain ramp   [<map> [<cx> [<cy> [<radius>]]]] [options]
wc3ctl terrain paint  [<map> [<cx> [<cy> [<radius> <texture>]]]] [options]
wc3ctl terrain water  [<map> [<cx> [<cy> [<radius> <op>]]]] [options]
wc3ctl terrain blight [<map> [<cx> [<cy> [<radius>]]]] [options]
```

Operator positionals as the help prints them.

```
deform  <Flatten|Lower|Raise|Set|Smooth>   raise|lower|set|flatten|smooth
cliff   <Lower|Raise|Set>                  raise|lower|set
water   <Lower|Raise|Remove|Set>           set|raise|lower|remove
paint   <texture>                          Ground texture slot index in the map's tileset table.
```

Options.

```
deform  --amount <amount>        Step for raise/lower; target for set. Default: 1. [default: 1]
cliff   --level <level>          Step count for raise/lower; absolute level for set. Default: 1. [default: 1]
water   --amount <amount>        Absolute height for set; delta for raise/lower. Default: 0. [default: 0]
ramp    --off                    Clear the ramp flag instead of setting it. [default: False]
blight  --clear                  Clear blight instead of setting it. [default: False]
paint   --variation <variation>  Tile variation; omit for default. []
all six writers
        --shape <Circle|Square>  Brush footprint: Circle or Square. [default: Circle]
        -o, --out <out>          Output map path. Default: '<map>.edited.<ext>' next to the input
```

`<cx>` and `<cy>` are tile coordinates into war3map.w3e, not world coordinates. `radius` is
in tiles.

## pathing

```
wc3ctl pathing paint [<map> <cx> <cy> <radius> <flags>] [options]
```

```
  <cx>      Brush centre pathing-cell X.
  <cy>      Brush centre pathing-cell Y.
  <radius>  Brush radius in pathing cells.
  <flags>   Pathing bits to affect (comma-separated): Walk, Fly, Build, Blight, Water. A set bit RESTRICTS that capability (Walk set = units cannot walk there).

  --op <Clear|Set|Toggle>  How the bits combine: Set, Clear or Toggle. [default: Set]
  --shape <Circle|Square>  Brush footprint: Circle or Square. [default: Circle]
  -o, --out <out>          Output map path. Default: '<map>.edited.<ext>' next to the input
```

Pathing cells, not tiles. A set bit restricts rather than permits.

## sound and camera

```
wc3ctl sound list   [<map>] [options]
wc3ctl sound add    [<map> [<name>]] [options]
wc3ctl sound set    [<map> [<name> <field> <value>]] [options]
wc3ctl sound remove [<map> [<name>]] [options]

wc3ctl camera list   [<map>] [options]
wc3ctl camera add    [<map> [<name> <x> <y>]] [options]
wc3ctl camera set    [<map> [<name> <field> <value>]] [options]
wc3ctl camera remove [<map> [<name>]] [options]
```

```
sound add   --file <file>   Audio file path inside the map (e.g. war3mapImported\snd.wav).
sound set   <field>  Name|File|Eax|Volume|Pitch|PitchVariance|FadeIn|FadeOut|Priority|Channel|Flags|MinDistance|MaxDistance|DistanceCutoff|ConeInside|ConeOutside|ConeOutsideVolume.
camera set  <field>  Name|TargetX|TargetY|ZOffset|Rotation|AngleOfAttack|TargetDistance|Roll|FieldOfView|FarClippingPlane|NearClippingPlane|LocalPitch|LocalYaw|LocalRoll.
```

Every writer here takes `-o, --out <out>` with the usual `.edited.` default.

## map-info, player, force

```
wc3ctl map-info get [<map>] [options]
wc3ctl map-info set [<map> <field> <value>] [options]

wc3ctl player list      [<map>] [options]
wc3ctl player set-force [<map> <player> [<force>]] [options]

wc3ctl force list      [<map>] [options]
wc3ctl force set-flags [<map> [<force> [<allied> [<alliedVictory> [<sharedVision> [<sharedControl> [<sharedAdvControl>]]]]]]] [options]
```

`map-info set` field list, verbatim.

```
MapName|Author|Description|RecommendedPlayers|Tileset|LightEnvironment|GlobalWeather|SoundEnvironment|WaterTintColor|FogStyle|FogStartZ|FogEndZ|FogDensity|FogColor|LoadingScreenIndex|LoadingScreenPath|LoadingScreenTitle|LoadingScreenSubtitle|LoadingScreenText|PrologueTitle|PrologueSubtitle|PrologueText|MeleeMap|HideMinimapInPreview|ModifyAllyPriorities|MaskedAreasPartiallyVisible|FixedPlayerSettings|UseCustomForces|UseCustomTechtree|UseCustomAbilities|UseCustomUpgrades|WaterWavesOnCliffShores|WaterWavesOnRollingShores|HasTerrainFog|HasWaterTint|ItemClassification|AccurateProbabilities
```

`force set-flags` takes five booleans in a fixed order after the force index. Allied,
allied victory, shared vision, shared unit control, shared advanced unit control.

## file

```
wc3ctl file list     [<map>] [options]
wc3ctl file get-text [<map> [<internal-path>]] [options]
wc3ctl file set      [<map> [<internal-path>]] [options]
```

```
file set
  --from <from> (REQUIRED)  Disk file whose exact bytes become the new payload.
  -o, --out <out>           Output map path. Default: '<map>.edited.<ext>' next to the input
```

`--from` is the only REQUIRED option anywhere in the surface.

## repair

```
wc3ctl repair generated-heroes [<map>] [options]
wc3ctl repair reforged-3       [<map>] [options]
wc3ctl repair portraits        [<map>] [options]
```

```
generated-heroes  -o, --out <out>   (no --apply, it always writes)
reforged-3        --apply           Apply the detected Warcraft III 3.0.0 compatibility repairs and save a new map.
                  -o, --out <out>
portraits         --apply           Rewrite the affected models. Without it, nothing is changed.
                  -o, --out <out>
```

`reforged-3` and `portraits` are detect only without `--apply`. `generated-heroes` has no
`--apply` and writes on every run.

Added since 2026-09-19, usage copied from `--help` on 2026-09-26.

```
wc3ctl repair data-pointers [<map>] [options]
wc3ctl repair model-paths   [<map>] [options]
wc3ctl repair uabi-runtime  [<map>] [options]
wc3ctl repair audit-errors  [<map>] [options]
wc3ctl repair preload       [<map>] [options]
```

```
data-pointers  --apply              Correct the pointers. Without it, nothing is changed.
               -o, --out <out>
model-paths    --apply              Rewrite the paths that have exactly one intended file. Without it, nothing is changed.
               --keep-placeholders  Leave placeholder models such as '.mdl' untouched. By default they are pointed at a bundled model with no geometry, which still draws nothing but loads.
               -o, --out <out>
uabi-runtime   --ids <ids>          Move only these ability ids, comma or space separated, and leave every other uabi entry in place.
               --apply              Move the lists and write the adder into the script. Without it, only the counts are shown.
               -o, --out <out>
audit-errors   --apply              Make the edits. Without it, every edit is listed and nothing is changed.
               -o, --out <out>
preload        --apply              Write the preload into the script. Without it, only what would be preloaded is shown.
               --within <within>    A trigger on a single timer at or below this many seconds counts as early. [default: 5]
               --function <name>    Also walk from these script functions, for a map that starts its heavy work another way.
               -o, --out <out>
```

All five are detect only without `--apply`.

`model-paths` rewrites script string literals and object model fields, and adds
`war3mapImported\wc3ctl_empty.mdx` when it points a placeholder at it. That model is built
byte for byte as the game's own `doodads\cinematic\empty\empty.mdx`, 536 bytes, the model the
base unit `ndum` (Dummy) uses, and a GameData test compares the two.

A script literal joined to anything by `+` is a fragment of a path built at runtime, such as
`"...Emoji_0" + I2S(k) + ".mdx"` in Otaku Defense v2.6.2, and is neither reported nor rewritten.
Before 2026-09-26 the bare `".mdx"` read as a placeholder and would have been rewritten.

`uabi-runtime` clears each unit type's `uabi` in whichever object layer holds it, writes the
lists into `war3map.j` as a hashtable keyed by unit type, registers a whole-map enter trigger
as the first statement of `main`, and sweeps existing units before `RunInitializationTriggers`.
It keeps `uhab`, Locust (`Aloc`), any unit type an ability's data names (a morph creates no
unit), and any entry that is not a 4 character id, exactly as written. It refuses a Lua map and
a script that already carries the adder. Measured 2026-09-26 on `GGG_Base05_full.w3x`.

```
wc3ctl repair uabi-runtime "<map>"                     3577 refs on 1405 unit types, distinct in uabi 1260 -> 199
wc3ctl repair uabi-runtime "<map>" --ids AInv          174 refs on 174 unit types
```

Every output was checked with pjass against the build's own `common.j` and `Blizzard.j`.

`audit-errors` fixes the two error-level audit findings that have one safe answer, with the
same detection code the audit runs, so a clean audit afterwards is the proof. A `dangling-reference`
in a `uabi`, `uhab` or `iabi` list is removed and the other entries stay verbatim. A dangling
buff in `abuf` or `aeff` gets back its base ability's value at that level (the base's last
level past its end), or is dropped when the base has none. A `requirement` finding gets `areq`
level 0 set to `_`. Every edit goes through `object set`, so it lands in the layer that holds
the field. Measured 2026-09-26 on `GGGA_V0.07b.w3x`.

```
wc3ctl repair audit-errors "<map>" --apply     209 fields, 7 lists cleaned, 101 A1A3 buffs B06H -> BPSE, 101 requirements cleared
```

Run it before `uabi-runtime`, so the dead ids are gone before the lists move into the script.

`preload` fixes the freeze right after the loading screen. The engine loads a model, its
textures and an ability the first time something uses them, on the game thread. Every trigger
on a single timer at or below `--within` is an entry, the call graph is walked from each, and
every unit type and ability the MAP defines that the walked code names (a literal rawcode, or an
integer global holding one) is collected. `main` gains, before `InitBlizzard` and so before any
trigger or unit exists, calls that create one unit of each type and add each ability to a
carrier, then remove them. The map's own code is untouched, so its timer still runs when the
author chose. Measured 2026-09-26 on `Anime_WOS2_0.32f.w3x`.

```
wc3ctl repair preload "<map>"     BuildsForChars at 1 s runs InitBuilds, 6 functions walked, 32 unit types, 213 abilities
```

Those are exactly the 32 `CreateUnit` and 213 `UnitAddAbility` calls in that map's
`MyHeroIdInit`. How long the freeze was can only be measured in the running game.

## replay and uabi-profile

Added 2026-09-26. Both are read only.

```
wc3ctl replay [<path>] [options]
wc3ctl uabi-profile <paths>... [options]
```

```
replay
  <path>        A .w3g file or a folder searched recursively. Default: every Battle.net account's replays under Documents\Warcraft III\BattleNet.
  --map <map>   Only games whose map path contains this text.

uabi-profile
  <paths>       Maps, or folders searched recursively.
```

`replay` lists each recorded game's map, length and players, and every LeaveGame record with
its reason and result code. A game is flagged `DISCONNECT` when any player left with result
`0x01`. Result `0x0D` is not in the classic replay specification. On Reforged replays here it
marks ordinary leaving, the saver's own exit included, so it is read as "left". That reading is
INFERRED from the pattern, not documented.

Measured 2026-09-26 against this machine's autosaves.

```
wc3ctl replay
  75 replays read, 0 unreadable
  2 flagged DISCONNECT, both Otaku Defense, every player dropping in the same second
```

Two shapes broke the first reader and are pinned by tests. A private lobby stores its password
where the specification says "a null byte" (14 of 75 failed), and build 10100 multiplayer
replays mark the Reforged metadata block `0x38` where build 10200 local ones use `0x39` (26 of
75 failed). Leave times come from the action stream's timeslots, and on local games they can
exceed the header's length field, so both are reported rather than one chosen.

`uabi-profile` prints one row per map. `refs` and `dist` are `uabi` references and distinct
ids, `heroR`, `heroD` and `heroNH` count hero abilities (`aher` 1) and those on non-hero units,
`item` counts item abilities (`aite` 1), `dangl` counts entries neither the map nor the game
defines, `dupe` counts units listing an id twice, and `uhab` counts references also learnable
as a hero ability. Measured 2026-09-26, the 32 maps in `uabi-disconnect\maps` in 1 min 14 s.

## trigger

```
wc3ctl trigger catalog [command] [options]
wc3ctl trigger catalog list [options]
wc3ctl trigger catalog describe <name> [options]
```

```
list      --file <file>      Read the catalog from an explicit TriggerData.txt instead of the installed game.
          --kind <kind>      Filter by kind: event|condition|action|call.
          --search <search>  Case-insensitive substring filter over function name and display name.
describe  <name>             Function name (e.g. DoNothing).
          --file <file>      Read the catalog from an explicit TriggerData.txt instead of the installed game.
```

Neither takes a map. Both read the installed World Editor catalog, or an explicit
TriggerData.txt via `--file`.

## gamedata

```
wc3ctl gamedata snapshot <out-dir> [options]
```

```
  <out-dir>              Directory to write the snapshot into.
  --locales              Include the per-locale string tables (much larger, rarely what a diff needs).
  --no-binary-strings    Skip the client/editor string dump. That dump is the only place editor-only changes show up, since the World Editor's behaviour is not in the game data.
```

# JSON SHAPES

All property names are PascalCase. Every shape below is from a real run, not from the
source. Root type is stated for each.

## Object roots

```jsonc
// wc3ctl info <map> --json
{ "Name": str, "Author": str, "Players": int, "Width": int, "Height": int,
  "Diagnostics": [] }

// wc3ctl ls <map> --json
{ "Files": [ { "Name": str, "SizeBytes": int, "Known": bool, "Parsed": bool } ] }

// wc3ctl validate <map> --json
{ "Valid": bool, "Errors": int, "Warnings": int,
  "Issues": [ { "Severity": str, "Category": str, "FileName": str, "Message": str } ] }

// wc3ctl audit <map> --json
{ "Ok": bool, "ObjectsChecked": int,
  "Issues": [ { "Severity": str, "Check": str, "Rawcode": str, "Owner": str|null, "Message": str } ],
  "Diagnostics": [ str ] }

// wc3ctl object get <map> <rawcode> --json
{ "Rawcode": str, "Found": bool, "BaseRawcode": str|null, "Name": str|null,
  "Fields": [ { "Code": str, "Name": str, "Value": str, "Source": str, "Display": str } ],
  "Diagnostics": [] }
// Source is "base" or "map". A levelled field appears as Code "aran" and "aran:1".

// wc3ctl object list <map> --json
{ "Items": [ { "Rawcode": str, "BaseRawcode": str, "Name": str } ] }

// wc3ctl roundtrip <map> --json
{ "Faithful": bool, "Mismatches": [], "ExcludedNotes": [ str ] }

// wc3ctl search <map> <query> --json
{ "Hits": [] }

// wc3ctl diff <mapA> <mapB> --json
{ "Entries": [ { "Name": str, "Change": str } ] }   // Change was "modified"

// wc3ctl extract <map> <internal-path> -o <dir> --json
{ "Files": [ { "Name": str, "Bytes": int, "Path": str } ], "Count": int, "TotalBytes": int }

// wc3ctl palette <map> --json
{ "Ok": bool, "Message": str,
  "Entries": [ { "Rawcode": str, "Name": str, "Source": str, "BaseRawcode": str, "IconPath": str } ],
  "Diagnostics": [] }

// wc3ctl terrain stats <map> --json
{ "Ok": bool, "Message": str, "TileCount": int, "MinHeight": float, "MaxHeight": float,
  "MeanHeight": float, "MinCliff": int, "MaxCliff": int }

// wc3ctl map-info get <map> --json
{ "MapName": str, "Author": str, "Description": str, "RecommendedPlayers": str,
  "PlayableWidth": int, "PlayableHeight": int, "Players": int, "CameraBounds": str,
  "Tileset": str, "LightEnvironment": str, "GlobalWeather": str, "SoundEnvironment": str,
  "WaterTintColor": str, "FogStyle": str, "FogStartZ": int, "FogEndZ": int,
  "FogDensity": float, "FogColor": str, "LoadingScreenIndex": int, "LoadingScreenPath": str,
  "LoadingScreenTitle": str, "LoadingScreenSubtitle": str, "LoadingScreenText": str,
  "PrologueTitle": str, "PrologueSubtitle": str, "PrologueText": str, "MeleeMap": bool,
  "HideMinimapInPreview": bool, "ModifyAllyPriorities": bool, ... }

// wc3ctl script functions <map> --json
{ "ScriptFile": str, "Functions": [] }

// wc3ctl bundle unit <map> <rawcode> --json
{ "RootRawcode": str, "RootName": str,
  "Objects": [ { "Rawcode": str, "Kind": str, "Name": str, "CustomToMap": bool } ], ... }

// wc3ctl deprotect <map> --json
{ "TotalBlocks": int, "NamedBefore": int, "NamedAfter": int,
  "Recovered": [ { "BlockIndex": int, "Name": str, "Source": str, "SizeBytes": int } ],
  "StillUnnamed": [ { "BlockIndex": int, "SizeBytes": int, "Kind": str, "SelfName": str } ],
  "Diagnostics": [] }

// wc3ctl repair reforged-3 <map> --json
{ "Ok": bool, "Message": str, "Applied": bool, "IssueCount": int, "FileColumnsRemoved": int,
  "ModelsMoved": int, "NumericCellsNormalized": int, "ButtonPositionsCompleted": int,
  "StrayCommentTerminatorsRemoved": int, "AbilityLevelColumnsAdded": int,
  "ChangedFiles": [], "SavedTo": null }

// wc3ctl repair portraits <map> --json
{ "UnitsScanned": int, "ModelsResolved": int, "ModelsAtRisk": int, "ModelsRepaired": int,
  "Models": [ { "Name": str, "Version": int, "Camera": bool, "Action": str,
                "BytesBefore": int, "BytesAfter": int } ] }

// wc3ctl new <out> --json
{ "Ok": bool, "MapName": str, "TileEdge": int, "Bytes": int, "SavedTo": str }

// wc3ctl terrain deform ... --json   (shape shared by the terrain/pathing writers)
{ "Ok": bool, "Message": str, "TilesChanged": int, "SavedTo": str }

// wc3ctl trigger catalog list --json
{ "Source": str, "Total": int,
  "Functions": [ { "Name": str, "Kind": str, "DisplayName": str, "ReturnType": str|null,
                   "ArgumentTypes": [], "Category": str } ] }

// wc3ctl trigger catalog describe <name> --json
{ "Name": str, "Kind": str, "GameVersion": int, "UsableInEvents": bool, "ReturnType": str|null,
  "ArgumentTypes": [], "DisplayName": str, "ParametersLayout": str, "Defaults": str,
  "Category": str }
```

## Array roots

These five return a bare JSON array. There is no wrapper object and no `Items` key.

```jsonc
// wc3ctl file list <map> --json        850 elements on the probe map
[ { "Name": str, "SizeBytes": int, "IsDirty": bool, "HasOverride": bool } ]

// wc3ctl player list <map> --json      12 elements on the probe map
[ { "Id": int, "Name": str, "Color": str, "Race": str, "Controller": str,
    "FixedStartPosition": bool } ]

// wc3ctl force list <map> --json       2 elements on the probe map
[ { "Index": int, "Name": str, "PlayerIds": [int], "Allied": bool, "AlliedVictory": bool,
    "SharedVision": bool, "SharedUnitControl": bool, "SharedAdvUnitControl": bool } ]

// wc3ctl camera list <map> --json      0 elements on the probe map
[ ]

// wc3ctl sound list <map> --json       0 elements on the probe map
[ ]
```

## Commands that ignore --json

Measured. `render`, `render-model` and `convert` print a plain line whatever `--json` says.
Confirmed in `src/wc3ctl/Program.cs` at lines 418, 449, 576 and 582, all `Console.WriteLine`
outside the JSON emitter.

```
$ wc3ctl render-model "<map>" ninf -o out.png --json
Wrote 123,929 bytes to out.png
```

# GOTCHAS

Each one was measured on 2026-09-19 unless marked otherwise.

**1. The map path is always the FIRST positional.** `object get <map> <rawcode>`, never the
reverse. Reversing it produces a file-not-found against the rawcode, because the rawcode was
taken as the path.

```
$ wc3ctl object get ninf "<map>"
error: file not found: <current folder>\ninf
$ wc3ctl search Ichigo "<map>"
error: file not found: <current folder>\Ichigo
```

**2. `port unit` puts the rawcode BETWEEN two map paths.**

```
wc3ctl port unit <source-map> <rawcode> <target-map>
```

**3. Four commands take no map at all as their first positional.**

```
wc3ctl convert <input> <output>            two disk paths
wc3ctl new <out>                           an output map path
wc3ctl gamedata snapshot <out-dir>         an output directory
wc3ctl trigger catalog describe <name>     a trigger function name
```

**4. The JSON root is an object for most commands and an ARRAY for five of them.** `file
list`, `player list`, `force list`, `camera list` and `sound list` all return a bare array.
Everything else measured returns an object.

**5. Property names are PascalCase.** `Fields`, not `fields`. `Rawcode`, not `rawcode`.
`SizeBytes`, not `size_bytes`.

**6. `object list` and `object set` default to `--kind unit`. `object get` auto-detects.**
So `object list <map> --json` lists units only, and an ability query needs
`--kind ability` explicitly.

**7. A miss is exit 0, not an error.** `object get` on a rawcode of the wrong kind or on a
rawcode that does not exist returns `"Found": false` and exits 0.

```
$ wc3ctl object get "<map>" ninf --kind ability --json
{ "Rawcode": "ninf", "Found": false, "BaseRawcode": null, "Name": null, "Fields": [], "Diagnostics": [] }
exit 0
```

**8. `audit --check <unknown>` runs nothing and reports OK.** No error, no warning that the
name was not recognised, exit 0. Only the seven names in `AllChecks` do anything.

```
$ wc3ctl audit "<map>" --check bogus
OK, 897 abilities checked, 0 error(s), 0 warning(s)
exit 0
```

**9. `audit` exits 2 on error-severity issues only.** Warnings alone leave exit 0, which is
consistent with the help text and worth stating because the JSON `Ok` field goes false for
warnings too.

```
audit (all checks)          Ok=false, 20 errors, 218 warnings   exit 2
audit --check requirement   Ok=false, 20 errors                 exit 2
audit --check level-gap     Ok=false, 37 warnings, 0 errors     exit 0
validate                    Valid=true, 0 errors, 11 warnings   exit 0
```

**10. Terrain and pathing operator arguments accept both casings.** The usage line prints
`<Flatten|Lower|Raise|Set|Smooth>` while the description prints
`raise|lower|set|flatten|smooth`. Both were run against a blank map and both worked, with
identical results (29 tiles changed each time).

**11. `WC3_GAME_DIR` is read by the MCP server only, not by the CLI.** The single
`Environment.GetEnvironmentVariable` call in `src/` is at `src/Wc3.Mcp/Wc3Tools.cs:806`. The
CLI takes `--game-dir` or auto-detects.

**12. CLAUDE.md's CLI publish path is stale.** It names
`src\Wc3.CLI\Wc3.CLI.csproj`, and that directory does not exist. The real project is
`src\wc3ctl\wc3ctl.csproj`, whose `AssemblyName` is `wc3ctl` (line 9).

```
$ ls src/Wc3.CLI
ls: cannot access 'src/Wc3.CLI': No such file or directory
```

**13. `file set --from` is REQUIRED.** It is the only required option in the whole surface.

**14. `repair generated-heroes` has no `--apply` and writes on every run.** Its two siblings
`repair reforged-3` and `repair portraits` are detect-only without `--apply`.

**15. `place unit` alone carries an `<owner>` positional**, between the rawcode and the
coordinates. `place item` has none, because a preplaced item has no owning player.

**16. Terrain coordinates are tiles, pathing coordinates are pathing cells.** They are not
the same grid, and neither is world coordinates, which is what `place` uses.

**17. A pathing bit that is SET restricts the capability.** `Walk` set means units cannot
walk there.

**18. `script functions` returned an empty `Functions` array on the protected probe map**,
while `ls` reports `war3map.j` at 4,522,651 bytes. Recorded as measured behaviour on this
map, not as a general claim about the command.

# WRITE RULES

The toolkit never writes over its input. That is one rule with three shapes.

**Default `.edited.` sibling.** Every map writer accepts `-o, --out <out>` and, without it,
writes `<map>.edited.<ext>` in the input's directory. Confirmed in
`src/wc3ctl/Program.cs:183-185`.

```csharp
var dest = p.GetValueForOption(setOut) ?? Path.Combine(
    Path.GetDirectoryName(map) ?? "",
    Path.GetFileNameWithoutExtension(map) + ".edited" + Path.GetExtension(map));
```

**`--apply` gate.** Eight commands are read-only until `--apply` is passed. `deprotect`,
`repair reforged-3`, `repair portraits`, `repair data-pointers`, `repair model-paths`,
`repair uabi-runtime`, `repair audit-errors`, `repair preload`.

**Explicit output path.** `convert <input> <output>`, `new <out>`,
`gamedata snapshot <out-dir>`, `render -o`, `render-model -o`, `extract -o`.

## Which commands write

```
READ ONLY (no write path at all)
  info, ls, roundtrip, search, diff, validate, audit, palette, replay, uabi-profile
  object get, object list
  script functions, bundle unit, bundle object
  terrain stats, sound list, camera list, map-info get, player list, force list
  file list, file get-text
  trigger catalog list, trigger catalog describe

WRITE A MAP (default .edited. sibling, override with -o/--out)
  object set, object new
  place doodad, place region, place unit, place start-location, place item
  terrain deform, terrain cliff, terrain ramp, terrain paint, terrain water, terrain blight
  pathing paint
  sound add, sound set, sound remove
  camera add, camera set, camera remove
  map-info set, player set-force, force set-flags
  file set
  repair generated-heroes
  port unit  (writes unless --dry-run)

WRITE ONLY WITH --apply
  deprotect, repair reforged-3, repair portraits
  repair data-pointers, repair model-paths, repair uabi-runtime, repair audit-errors, repair preload

WRITE A NON-MAP FILE TO AN EXPLICIT PATH
  extract, render, render-model, convert, new, gamedata snapshot
```

# MCP TOOLS

48 tools as of 2026-09-26 (45 on 2026-09-19, plus `replay_summary`, `uabi_profile` and `script_leaks`), read from `src/Wc3.Mcp/Wc3Tools.cs` by parsing every `[McpServerTool(Name = ...)]`
attribute and the method signature that follows it. Read-only status is the attribute's own
`ReadOnly` flag. Parameter names are the C# parameter identifiers, with defaults shown.

Note the shape difference from the CLI. Every MCP writer takes an explicit `out_path` and
refuses to equal the input, so there is no `.edited.` default on this surface.

```
map_info (read-only): map
list_files (read-only): map
object_list (read-only): map, kind="unit", game_dir=null
object_get (read-only): map, rawcode, kind=null, game_dir=null
object_set (writes): map, out_path, rawcode, field, value, kind="unit"
object_new (writes): map, out_path, base_rawcode, kind="unit"
bundle_unit (read-only): map, rawcode, game_dir=null
render_model (writes): map, rawcode, out_path, kind=null, game_dir=null
port_unit (writes): source_map, rawcode, target_map, include_script=true, game_dir=null
palette_doodad (read-only): map, game_dir=null
place_doodad (writes): map, out_path, type_rawcode, x, y, z=0f, rotation=0f, scale=1f, variation=0
place_region (writes): map, out_path, name, left, bottom, right, top
place_unit (writes): map, out_path, type_rawcode, owner_id, x, y, z=0f, rotation=0f, scale=1f
place_start_location (writes): map, out_path, player, x, y
place_item (writes): map, out_path, type_rawcode, x, y, z=0f, rotation=0f, scale=1f
terrain_stats (read-only): map
terrain_deform (writes): map, out_path, center_x, center_y, radius, op="raise", amount=1f, shape="circle"
terrain_cliff (writes): map, out_path, center_x, center_y, radius, op="raise", level=1, shape="circle"
terrain_ramp (writes): map, out_path, center_x, center_y, radius, on=true, shape="circle"
terrain_paint (writes): map, out_path, center_x, center_y, radius, texture_index, variation=null, shape="circle"
terrain_water (writes): map, out_path, center_x, center_y, radius, op="set", amount=0f, shape="circle"
terrain_blight (writes): map, out_path, center_x, center_y, radius, on=true, shape="circle"
deprotect_map (read-only): map, out_path=null, listfiles=null
file_list (read-only): map
file_get_text (read-only): map, internal_path
file_set (writes): map, out_path, internal_path, from
audit_map (read-only): map, check=null, game_dir=null
sound_list (read-only): map
sound_add (writes): map, out_path, name, file=null
sound_set (writes): map, out_path, name, field, value
sound_remove (writes): map, out_path, name
camera_list (read-only): map
camera_add (writes): map, out_path, name, target_x, target_y
camera_set (writes): map, out_path, name, field, value
camera_remove (writes): map, out_path, name
pathing_paint (writes): map, out_path, center_x, center_y, radius, flags, op="set", shape="circle"
map_info_get (read-only): map
map_info_set (writes): map, out_path, field, value
player_list (read-only): map
player_set_force (writes): map, out_path, player_id, force_index
force_list (read-only): map
force_set_flags (writes): map, out_path, force_index, allied, allied_victory, shared_vision, shared_unit_control, shared_adv_unit_control
new_map (writes): out_path, name=null, tiles=null
trigger_catalog_list (read-only): file=null, game_dir=null, kind=null, search=null
trigger_catalog_describe (read-only): name, file=null, game_dir=null
replay_summary (read-only): path=null, map=null
script_leaks (read-only): map
uabi_profile (read-only): paths, game_dir=null
```

`audit_map` gained the `missing-model` check on 2026-09-26, so its default runs eight checks.

MCP coverage gaps against the CLI. There is no MCP equivalent of `roundtrip`, `search`,
`diff`, `extract`, `render` (terrain), `convert`, `validate`, `script functions`,
`bundle object`, `repair generated-heroes`, `repair reforged-3`, `repair portraits`,
`repair data-pointers`, `repair model-paths`, `repair uabi-runtime`, `repair audit-errors`,
`repair preload` or `gamedata snapshot`. The five newest repairs are CLI only by the same rule as the others, a whole-map rewrite that
has to be play-tested. Their findings reach agents read only, through `audit_map` and
`uabi_profile`.

`port_unit` also differs from the CLI. It writes `<target>.ported.<ext>` beside the target
map rather than taking an output directory.

# TESTS

Two test projects.

```
tests/Wc3.Tests/Wc3.Tests.csproj
tests/Wc3.Studio.Tests/Wc3.Studio.Tests.csproj
```

Two trait categories exist in the whole tree, confirmed by enumerating every
`Trait("Category", ...)` occurrence under `tests/`.

```
("Category", "Corpus")
("Category", "GameData")
```

The three filter strings, quoted from `CLAUDE.md` lines 38 to 41.

```
dotnet build
dotnet test --filter "Category!=Corpus&Category!=GameData"   # hermetic (CI-safe)
dotnet test --filter "Category=GameData"                     # needs WC3 install at C:\Warcraft III
dotnet test --filter "Category=Corpus"                       # needs a real .w3x on disk
```

# PUBLISH

Quoted from `CLAUDE.md` lines 51 to 53, with the CLI line corrected against the real project
layout (see GOTCHA 12).

```
Studio: dotnet publish src\Wc3.Studio\Wc3.Studio.csproj -c Release --self-contained -r win-x64 -o dist-studio
CLI:    dotnet publish src\wc3ctl\wc3ctl.csproj          -c Release --self-contained -r win-x64 -o dist
MCP:    dotnet publish src\Wc3.Mcp\Wc3.Mcp.csproj        -c Release --self-contained -r win-x64 -o dist-mcp
```

CLAUDE.md writes the CLI line as `src\Wc3.CLI\Wc3.CLI.csproj`, which does not exist on disk.

Rules that go with a publish, from CLAUDE.md.

1. Close `dist-studio\Wc3.Studio.exe` before publishing Studio, because the running app locks
   its DLLs and the publish fails while it is open.
2. Keep `CascLib.dll` beside `wc3ctl.exe` in `dist`.
3. Verify freshness afterwards so the user's binary actually carries the change.

```powershell
Get-Item dist-studio\Wc3.Studio.exe | Select LastWriteTime
```

`dotnet run`, `dotnet build` and `dotnet test` all run from source. They do not update the
published binaries in `dist`, `dist-studio` or `dist-mcp`.

# COMMANDS THAT FAILED TO RUN

None. Every `--help` invocation and every probe listed above returned. Two runs produced
output worth calling out rather than a failure, and both are recorded above as measured
behaviour. `audit --check bogus` reports OK with exit 0, and `render-model --json` prints a
plain line instead of JSON.
