# wc3ctl, a Warcraft III map tool

Open a `.w3x`/`.w3m` map, inspect it, round-trip it byte-faithfully, and query
object data (map deltas merged over base game data).

Slice 1 delivers the read/query core: `MapDocument.Load` opens an MPQ-based map
(including maps with a 512-byte pre-archive header), parses every *present,
known* `war3map.*` file into a typed model via [War3Net](https://github.com/Drake53/War3Net),
preserves everything else (unknown or unnamed files) as raw bytes, and can save
the archive back out such that every map-data file compares byte-identical to
the original. Parse failures never crash a load, they are recorded as
diagnostics and the file stays available raw.

Slice 2 adds object-data queries for **units**: `object get` shows a unit's
full merged stats (base game data, read from an installed Warcraft III via
CASC, overlaid with the map's `war3map.w3u` deltas) and `object list`
enumerates the map's custom/modified units.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Wc3.MapDocument` | Core model: `MapDocument`, `MapFileEntry`, format registry, War3Net parser wiring |
| `src/Wc3.GameData` | Base game data: CASC storage access, SLK parsing, unit field metadata, base unit resolution |
| `src/Wc3.Commands` | Shared command layer (front-end-agnostic result records) |
| `src/wc3ctl` | CLI front-end (System.CommandLine) |
| `src/Wc3.Mcp` | MCP server seam (a stub, proving the shared-command boundary only) |
| `tests/Wc3.Tests` | xUnit suite: fast synthetic-fixture tests + opt-in corpus/GameData tests |

## Commands

```
wc3ctl ls <map>                             List internal files
wc3ctl info <map>                           Map metadata (name, author, players, ...)
wc3ctl roundtrip <map>                      Verify byte-faithful save
wc3ctl search <map> <query>                 Search map contents (filename-only for now)
wc3ctl diff <mapA> <mapB>                   Compare two maps (added/removed/modified files)
wc3ctl object get <map> <rawcode> [--field CODE] [--game-dir PATH]
                                            Full merged unit stats (base game data ⊕ map deltas)
wc3ctl object list <map> [--game-dir PATH]  List the map's custom/modified units
wc3ctl repair reforged-3 <map>               Check for Reforged 3.0.0 SLK incompatibilities
wc3ctl repair reforged-3 <map> --apply [-o FIXED.w3x]
                                            Repair them and save a new map
wc3ctl audit <map> [--check NAME] [--game-dir PATH]
                                            Behavioural audit, read-only. Exits 2 on an error
```

Add `--json` to any command for machine-readable output.

### Reforged 3.0.0 legacy-map repair

Reforged 3.0.0 (build 24268) rejects or misreads several data shapes emitted by
older SLK-mode editors such as KKWE and YDWE. `repair reforged-3` checks a map
without changing it by default. With `--apply`, it writes a separate
`.reforged-3-fixed.w3x`/`.w3m` copy unless `--out` is supplied.

The repair removes obsolete `file` columns from `Units\\UnitUI.slk` and
`Units\\ItemData.slk` while preserving their model paths in the corresponding
`*Skin.txt` profiles, normalizes quoted numeric cells and incomplete button
positions, removes unmatched `*/` tokens from FDF files, and adds missing
ability level 5/6 columns copied from level 4. Keep the original map and test
the repaired copy in Warcraft III before distributing it.

### Name recovery on a protected map (`deprotect`)

A protected map ships no `(listfile)`, so most of its entries have no name. That is not
cosmetic. `MapDocument.GetFile(name)` matches on the listfile name, so on such a map it returns
null for files the archive plainly holds, and that reads exactly like absence. On one real
9.5 MB map, 793 of its 850 entries were nameless, which made an audit of imported models report
**3** affected units when the real number was **178**.

Reading is not the problem. An MPQ block's key comes from its own basename, but a compressed
block also stores a sector offset table whose first entry is always that table's own size,
which is known plaintext, so the key is recoverable from the file itself. War3Net does this
already, and all 793 nameless entries on that map read correctly. Only the **name** is at stake.

```
wc3ctl deprotect <map> [--listfile PATH]... [--apply -o OUT.w3x]
```

Names come from six sources, and the report gives each one's contribution separately so a
source that adds nothing is visible rather than assumed useful. The figures below were measured
on 2026-09-19 against that map with no `--listfile` supplied, which is why the dictionary row
contributes nothing here.

| source | what it reads | contribution on that map |
|---|---|---|
| object data | model, icon and effect paths on every object | 442 |
| script literal | quoted strings in `war3map.j` and the override text | 193 |
| model TEXS | texture paths each MDX declares, the only thing that can name a BLP | 90 |
| icon twin | `DISBTNFoo.blp` and `PASBTNFoo.blp` derived from a `BTNFoo.blp` that was found | 51 |
| content sweep | asset-shaped names in every block's bytes | 4 |
| dictionary | any listfile passed with `--listfile`, repeatable | 0 here |

That reached **837 of 850**, from 57, which is 98.5%. With `--apply` the recovered names are
stamped onto their entries so the saved copy's listfile carries them. Those entries are
recompressed and are no longer byte-faithful, which is the deliberate cost of naming them, and
every other entry is untouched.

The icon twin row is worth its own line, because those names appear in no field, no script, no
model and no dictionary. The engine derives the disabled and passive icon paths from the normal
one by rewriting the stem, so they can only be reached by doing the same rewrite.

The 13 that remain are 6 BLP textures and 7 MDX models whose content reads fine and which
nothing in the map names at all. Several announce their own name internally, for example
`WispExplode` and `BlinkTarget`, but a model's self-declared name is not its archive path, so
it cannot be used as one.

Writing a `(listfile)` as an ordinary file does **not** work, because `MpqArchiveBuilder`
regenerates it from the names it knows and discards the supplied one. That was tried first and
reported the same named count before and after, a clean silent no-op.

### Behavioural audit (`audit`)

`validate` answers whether a map can load. `audit` answers whether its abilities do what they
claim, which is a different and much larger question, and one you cannot settle by playing a
map with a hundred heroes.

The leverage is that a hand authored map documents itself. The author wrote the cooldown, the
mana cost, the cast range and the area into the tooltip, per level, by hand. That is a
specification, and the object data either matches it or does not.

Seven checks, each narrowable with `--check`.

| check | severity | what it reports |
|---|---|---|
| `requirement` | error | an ability inheriting a requirement the map can never satisfy, so it is permanently locked |
| `dangling-reference` | error | a buff or ability id in `abuf`, `aeff`, `uhab`, `uabi` or `iabi` that neither the map nor the installed game defines |
| `level-gap` | warning | a declared level with no value, which silently falls back to the base ability |
| `level-tooltip` | warning | a reachable level with no tooltip of its own while another reachable level has one, so the command card falls back to the base ability's text |
| `tooltip-claim` | warning | prose promising a cooldown, mana cost, cast range or area of effect the data contradicts |
| `orphan-ability` | warning | an ability that zeroes its own effect fields, so a trigger must own it, whose rawcode appears in no trigger in either byte order |
| `portrait-risk` | warning | a unit model shipping a camera below `VERS` 900, which renders a black portrait under Reforged 3.0.0 |

`requirement` and `dangling-reference` need an installed game, and say so in a diagnostic
rather than silently reporting clean when it is missing. The exit code is 2 when any error
level issue is found, so the command works as a gate.

Two conditions are reported as notes rather than warnings, because both are real and neither is
something a player can observe. Tooltip text above the declared level count is unreachable, and
a claim on an ability the trigger script names sits on a field the script can overwrite at
runtime. `tooltip-claim` also reads "Area of Effect" only, because "Area of Damage" states the
damage dealt inside the area rather than the radius, which was confirmed on two abilities of one
real map where the field carrying the tooltip's number is the base ability's own "AOE Damage"
column.

Measured on 2026-09-19, that map audits to 20 errors and 218 warnings untouched, and to **0
errors and 1 warning** after repair, with the notes reporting 38 abilities carrying unreachable
tooltip text and 6 claims set aside on abilities the script names.

**What it cannot see.** Whether a trigger's damage formula is right, whether a status effect
lands, or anything about timing. A clean result means no claim in the map contradicts its
data. It does not mean the map is correct. `docs/bvo-ability-audit.md` carries the measured
boundary on a real map, including why damage is not statically checkable there.

### Object data (`object get` / `object list`)

`object get` resolves a unit rawcode (e.g. `hfoo`, or a custom unit like
`H000`) to its complete field set: the base unit's stats from the game's SLK
data files, overlaid with the map's `war3map.w3u` deltas. Each field is tagged
`[base]` or `[map]` so you can see what the map changed. `--field CODE`
restricts output to a single field (by rawcode, e.g. `uhpm`).

Resolving base stats requires an **installed Warcraft III (Reforged)**, because the
game's data lives in its CASC storage. The install is auto-detected (registry /
common paths); use `--game-dir PATH` to point at it explicitly. Without an
install, the commands still work but show **map deltas only**, with a
diagnostic explaining that base data is unavailable.

CASC access uses the CascLib.NET package, a managed wrapper around the native
[CascLib](https://github.com/ladislav-zezula/CascLib) `CascLib.dll`, and the
native DLL ships next to `wc3ctl.exe` in published output and must stay
alongside it (win-x64).

## Build & test

Requires the .NET 8 SDK.

```bash
dotnet build
dotnet test --filter "Category!=Corpus&Category!=GameData"  # fast hermetic suite (synthetic fixtures)
dotnet test --filter "Category=GameData"                    # against an installed WC3 (opt-in)
dotnet test --filter "Category=Corpus"                      # against a real map (opt-in)
```

The corpus tests exercise parse coverage and byte-faithful round-trip against a
real ~60 MB map; the GameData tests exercise CASC access and base-unit
resolution against a local Warcraft III install. Both use hardcoded local
paths (`tests/Wc3.Tests/ParseCoverageTests.cs`,
`tests/Wc3.Tests/GameDataIntegrationTests.cs`) and self-skip (pass without
asserting) when the map/install is absent.

To publish a distributable exe:

```bash
dotnet publish src/wc3ctl -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

This produces `dist/wc3ctl.exe` plus the native `CascLib.dll` beside it (keep
them together).

## Format coverage

Parsed into typed models: `war3map.w3i` (info), `.w3e` (terrain), `.wpm`
(pathing), `.doo` / `war3mapUnits.doo` (doodads/units), `.w3r` (regions),
`.w3c` (cameras), `.w3s` (sounds), `.wtg` / `.wct` (triggers), `.wts` (trigger
strings), `.j` / `.lua` (script), `.imp` (imports), and the object-data set
`.w3u .w3a .w3t .w3b .w3d .w3h .w3q`.

Everything not in that list is preserved as raw bytes and round-trips
unchanged.

## Known limitations / deferred (later slices)

- **Read-only.** Editing/write-back is deferred: `MapDocument.SerializeEntry`
  throws by design. Saving re-emits the original raw bytes of each file.
- **Object data covers units only** (`war3map.w3u`) this slice. Items,
  abilities, destructables, doodads, buffs, and upgrades are later slices.
- **Field names display as raw `WESTRING_*` keys** (e.g.
  `WESTRING_UEVAL_UHPM`), so resolving them to English display names ("Hit
  Points") is a planned follow-on.
- **Base unit *names* aren't resolved**, since unit names and other profile-TXT
  fields (from `units\*.txt`) aren't read yet, only SLK stats. `object list`
  shows rawcodes (plus base rawcode), not display names.
- **`search` matches filenames only**, with no content search yet.
- **MPQ bookkeeping files**: `(listfile)` and `(attributes)` are regenerated
  by the archive builder on save; `(signature)` is **not** carried over or
  re-signed (the rebuilt map is unsigned, and unsigned custom maps are
  unaffected). They are archive metadata, not map data, and are excluded from
  the round-trip fidelity comparison; `wc3ctl roundtrip` reports any that were
  dropped or rewritten as notes.
- **`war3map.mmp` and `war3map.shd`** (minimap positions, shadow map) are not
  yet in the format registry, so they are preserved raw, not parsed.
- **MCP server is a seam-only stub**, proving the shared command layer is
  front-end-agnostic but exposes no real MCP transport yet.
