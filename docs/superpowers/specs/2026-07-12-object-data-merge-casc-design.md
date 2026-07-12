# Object-Data Merge via CASC — Design (Slice 2)

**Date:** 2026-07-12
**Status:** Approved (pending written-spec review)
**Slice:** 2 — first step toward unit/hero porting: know *what a unit is made of*.

---

## 1. Context

Slice 1 gave us a lossless `MapDocument`, read/inspect commands, and asset `extract`. The next step toward the marquee goal (port a hero — model + skills + triggers — between maps) is to answer **"what is unit `X` actually made of?"** — its full stats, its model/icon/sounds, and its abilities. That is the input to bundling and porting.

WC3 maps store object data (`war3map.w3u`/`.w3a`/…) as **deltas**: only the fields a custom object overrides, plus its base rawcode. The full field set lives in the game's base data. This machine has **Warcraft III Reforged** installed at `D:\Warcraft III` (CASC storage), so we can read the base data — but only through CASC.

## 2. Goal & scope

**Goal:** `wc3ctl object get <map> <rawcode>` shows the **full merged field set** for a **unit** — base fields (from the installed game data) overlaid with the map's deltas — with human-readable field names. Plus `object list` to enumerate a map's custom objects.

### In scope
- Native CASC read layer (CascLib P/Invoke) to open `D:\Warcraft III` and read base data files.
- SLK parsing (via `War3Net.IO.Slk`) of the base **unit** data + unit field metadata.
- Field-code → column + human-name resolution (unit metadata + WorldEditStrings).
- Merge: base unit fields ⊕ map deltas → full field list (code, name, value, source=`base`|`map`).
- `object get` (units) and `object list`, human + `--json`.
- Graceful degradation: if the install/CASC/base data is unavailable, `object get` still shows the map's **deltas** with a diagnostic (never crash).

### Out of scope (follow-on slices, same machinery)
- Abilities/items/destructables/doodads/buffs/upgrades full-merge (units-first proves the pipeline; others reuse it).
- Editing/write-back, bundling, porting (later slices).
- Full display-name localization beyond WorldEditStrings basics.

## 3. Key decisions

| Decision | Choice | Why |
|---|---|---|
| Base-data access | **Native CascLib via P/Invoke** (ship x64 `CascLib.dll`) | `War3Net.IO.Casc` is unpublished ("coming soon"); no managed reader exists. CascLib is the canonical, robust CASC reader and supports WC3 Reforged local storage. |
| Read strategy | **Live** from `D:\Warcraft III` each run | Always matches the installed version; no bundled Blizzard data (licensing) and no stale snapshot. |
| First object type | **Units only** | Primary port target; proves the whole CASC→SLK→metadata→merge pipeline; other types reuse it. |
| Layering | New `Wc3.GameData` project | Isolates all game-install/CASC/SLK concerns behind a clean interface; `Wc3.Commands` stays install-agnostic; the CLI/MCP seam holds. |
| Install location | Detect (registry `HKCU\...\Blizzard Entertainment\Warcraft III`, uninstall `InstallLocation`, common paths), allow `--game-dir` override | Robust to non-default installs; testable. |
| Missing base data | **Degrade to deltas + diagnostic** | Principle #5 spirit: the command stays useful without the install; never crash. |

## 4. Architecture

```
Wc3.GameData (new project; depends on: War3Net.IO.Slk, native CascLib.dll)
  IGameDataSource          abstraction: read a base data/metadata file by name → bytes (or null)
  CascGameDataSource       P/Invoke CascLib: open local WC3 storage, CascOpenFile/ReadFile
  GameInstall              locate the install (registry/paths) + --game-dir override
  SlkTable                 parse SLK bytes → addressable rows × columns (via War3Net.IO.Slk)
  UnitMetadata             unit field-code (4-char) → { slk file, column, display name }
                           built from Units\UnitMetaData.slk + WorldEditStrings
  BaseUnitStore            resolve a base rawcode → full unit field set (reads the unit SLKs)

Wc3.Commands (depends on Wc3.Model + Wc3.GameData)
  ObjectGetCommand         merge: BaseUnitStore(baseRawcode) ⊕ map w3u deltas → MergedObjectResult
  ObjectListCommand        enumerate custom objects from the map's object-data models

wc3ctl                     `object get` / `object list`, human + --json; global --game-dir option
```

**Boundary contracts**
- `Wc3.GameData` — *what:* given an install (or override), resolve base object fields + metadata. *how:* `var store = GameData.OpenUnits(gameDir?); store.TryGetUnit(rawcode, out fields);`. *depends on:* CascLib.dll + War3Net.IO.Slk only. No map/CLI types.
- `ObjectGetCommand` — *what:* full merged fields for a rawcode. *depends on:* `Wc3.Model` (map deltas) + `Wc3.GameData` (base). Returns a plain result POCO.

## 5. Data flow (`object get <map> <rawcode>`)

1. Load the `MapDocument`; find the custom unit `rawcode` in the parsed `war3map.w3u` → its **base rawcode** + its **delta fields**.
2. Open game data (`CascGameDataSource` over the detected/`--game-dir` install).
3. `UnitMetadata`: read `Units\UnitMetaData.slk` (+ WorldEditStrings) once → field-code → {slk, column, name}.
4. `BaseUnitStore.TryGetUnit(baseRawcode)`: read the base unit SLKs, gather the base object's row across the relevant SLK files → base field values keyed by field-code.
5. Merge: start from base fields; overlay the map's delta fields; produce `MergedObjectResult` = list of `{ Code, Name, Value, Source }` sorted by name.
6. Render (human table or `--json`).

If step 2/3/4 fails (no install, CASC open error, missing file): skip base resolution, return **deltas only** with a `Diagnostic("base game data unavailable: <reason>")`, exit 0.

## 6. De-risking: spike first (mandatory step 0)

Before any merge logic: a spike that (a) obtains a prebuilt x64 `CascLib.dll`, (b) opens `D:\Warcraft III` as a local WC3 storage via P/Invoke, (c) reads `Units\UnitMetaData.slk` and prints its byte length + first row. If this cannot open/read, **stop and reconsider** (bundled-snapshot fallback) before building the rest. The spike pins the exact CascLib open-string, P/Invoke signatures, and the real in-CASC file paths.

## 7. Error handling
- Install not found and no `--game-dir` → `object get` degrades to deltas + diagnostic; a clear one-line message names how to point at the install.
- CASC open/read failure → same graceful degradation, diagnostic carries the CascLib error.
- Unknown rawcode (not in map, not in base) → `Found=false`, clean message.
- Never a stack trace (reuse the CLI `RunSafely` pattern from Slice 1).

## 8. Testing
- **Spike/integration** (`[Trait("Category","GameData")]`, self-skip if `D:\Warcraft III` absent): open storage, read `UnitMetaData.slk` (bytes > 0); resolve a **known base unit** — Footman `hfoo` — and assert a sane name + positive hit points.
- **Unit tests (no install needed):** `SlkTable` parsing on a small SLK fixture; the merge function on synthetic base+delta dictionaries (delta overrides base; base-only fields retained; source labeled correctly); `GameInstall` detection given a fake dir/override.
- **Command test:** a synthetic map custom unit (base `hfoo`, one overridden field) → `ObjectGetCommand` shows base ⊕ delta with the delta's `Source=map`; and the deltas-only degradation path when the source is unavailable.

## 9. Success criteria
1. Spike proves CascLib P/Invoke can open `D:\Warcraft III` and read a base SLK.
2. `wc3ctl object get <map> <custom-unit-rawcode>` shows full merged unit fields (base ⊕ delta) with human field names; `--json` works.
3. `object get` on a map whose install is unreachable returns deltas + a clear diagnostic, exit 0 (no crash).
4. `Wc3.GameData` has no dependency on `Wc3.Commands`/CLI; the command layer stays the single source of truth.

## 10. Risks
- **CASC interop:** sourcing/matching the x64 `CascLib.dll`, the WC3 local storage open-string, P/Invoke marshaling. (Mitigated by the step-0 spike.)
- **Base-data intricacy & version drift:** the editor merges several unit SLKs and resolves names via WorldEditStrings; exact paths/columns are version-specific. Units-first + the `hfoo` assertion bounds this.
- **Distribution:** the single-file exe now needs the native `CascLib.dll` beside it (documented in README).
