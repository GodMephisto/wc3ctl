# wc3ctl — Warcraft III map tool

Open a `.w3x`/`.w3m` map, inspect it, round-trip it byte-faithfully, and query
object data (map deltas merged over base game data).

Slice 1 delivers the read/query core: `MapDocument.Load` opens an MPQ-based map
(including maps with a 512-byte pre-archive header), parses every *present,
known* `war3map.*` file into a typed model via [War3Net](https://github.com/Drake53/War3Net),
preserves everything else (unknown or unnamed files) as raw bytes, and can save
the archive back out such that every map-data file compares byte-identical to
the original. Parse failures never crash a load — they are recorded as
diagnostics and the file stays available raw.

Slice 2 adds object-data queries for **units**: `object get` shows a unit's
full merged stats — base game data (read from an installed Warcraft III via
CASC) overlaid with the map's `war3map.w3u` deltas — and `object list`
enumerates the map's custom/modified units.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Wc3.MapDocument` | Core model: `MapDocument`, `MapFileEntry`, format registry, War3Net parser wiring |
| `src/Wc3.GameData` | Base game data: CASC storage access, SLK parsing, unit field metadata, base unit resolution |
| `src/Wc3.Commands` | Shared command layer (front-end-agnostic result records) |
| `src/wc3ctl` | CLI front-end (System.CommandLine) |
| `src/Wc3.Mcp` | MCP server seam (stub — proves the shared-command boundary only) |
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

### Object data (`object get` / `object list`)

`object get` resolves a unit rawcode (e.g. `hfoo`, or a custom unit like
`H000`) to its complete field set: the base unit's stats from the game's SLK
data files, overlaid with the map's `war3map.w3u` deltas. Each field is tagged
`[base]` or `[map]` so you can see what the map changed. `--field CODE`
restricts output to a single field (by rawcode, e.g. `uhpm`).

Resolving base stats requires an **installed Warcraft III (Reforged)** — the
game's data lives in its CASC storage. The install is auto-detected (registry /
common paths); use `--game-dir PATH` to point at it explicitly. Without an
install, the commands still work but show **map deltas only**, with a
diagnostic explaining that base data is unavailable.

CASC access uses the CascLib.NET package, a managed wrapper around the native
[CascLib](https://github.com/ladislav-zezula/CascLib) `CascLib.dll` — the
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
- **Object data covers units only** (`war3map.w3u`) this slice — items,
  abilities, destructables, doodads, buffs, and upgrades are later slices.
- **Field names display as raw `WESTRING_*` keys** (e.g.
  `WESTRING_UEVAL_UHPM`) — resolving them to English display names ("Hit
  Points") is a planned follow-on.
- **Base unit *names* aren't resolved** — unit names and other profile-TXT
  fields (from `units\*.txt`) aren't read yet, only SLK stats. `object list`
  shows rawcodes (plus base rawcode), not display names.
- **`search` matches filenames only** — no content search yet.
- **MPQ bookkeeping files**: `(listfile)` and `(attributes)` are regenerated
  by the archive builder on save; `(signature)` is **not** carried over or
  re-signed (the rebuilt map is unsigned — unsigned custom maps are
  unaffected). They are archive metadata, not map data, and are excluded from
  the round-trip fidelity comparison; `wc3ctl roundtrip` reports any that were
  dropped or rewritten as notes.
- **`war3map.mmp` and `war3map.shd`** (minimap positions, shadow map) are not
  yet in the format registry — they are preserved raw, not parsed.
- **MCP server is a seam-only stub** — it proves the shared command layer is
  front-end-agnostic but exposes no real MCP transport yet.
