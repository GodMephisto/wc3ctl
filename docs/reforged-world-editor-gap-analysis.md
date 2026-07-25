# WC3 Reforged World Editor — Feature Inventory & Gap Analysis

> **Purpose:** Inventory the full feature set of the official Warcraft III: Reforged
> World Editor (WE), module by module, and map each capability to what **wc3ctl**
> (this repo) currently supports. This is the roadmap driver: it tells us where our
> CLI/library/MCP surface is complete, partial, or missing.
>
> **Scope note:** wc3ctl is a *headless, CLI-first, MCP-native, library-based* tool
> plus an Avalonia "Studio" GUI. It is **not** trying to be a pixel-for-pixel WE
> clone; the goal is programmatic + automatable map authoring. "Missing" below means
> "no programmatic path today", not "we must build a GUI for it".

## Legend

| Mark | Meaning |
|------|---------|
| ✅ **Have** | Supported today (CLI command, library API, MCP tool, and/or Studio panel) |
| 🟡 **Partial** | Some support (e.g. read-only, or subset of fields, or parse-but-no-edit) |
| ❌ **Missing** | No programmatic path today |
| ➖ **N/A** | Out of scope for a headless tool (pure interactive UI convenience) |

---

## 1. Terrain Editor

The WE's most powerful module: geometry, textures, environment, and *all in-map
placement* (units, doodads, regions, cameras). Reads/writes `war3map.w3e`
(environment), `war3map.wpm` (pathing), `war3map.w3i` (bounds/camera), `war3map.doo`
(doodads), `war3mapUnits.doo` (units/items), `war3map.w3r` (regions), `war3map.w3c`
(cameras), `war3map.shd` (shadows), `war3map.w3a`-independent terrain art.

| Feature | wc3ctl status | Notes |
|---|---|---|
| Read/write `war3map.w3e` (tiles, heights, cliffs, variations, water, blight) | ✅ Have | `MapEnvironment` parse/write; round-trips clean |
| Blank terrain synthesis (new map) | ✅ Have | `BlankMap` — synthesizes valid `w3e`+`w3i` MPQ (task #1) |
| Tile/texture painting (per-tile ground + cliff textures) | ✅ Have | `TerrainCommand` paint — validated tile-type brush over `war3map.w3e`, circle/square + radius; tested |
| Cliffs raise/lower/ramp; add cliff types | ✅ Have | `TerrainCommand` cliff + ramp — cliff-level set/raise/lower and ramp-toggle brushes on `war3map.w3e`; tested |
| Height/deformation brushes | ✅ Have | `TerrainCommand` — HiveWE-style raise/lower/plateau/smooth/flatten, circle/square brush + radius, on `war3map.w3e`; reports height stats; tested |
| Water height editing | ✅ Have | `TerrainCommand` water — area brush sets/clears water flag + absolute height over `war3map.w3e`, circle/square + radius; tested |
| Blight painting | ✅ Have | `TerrainCommand` blight — set/clear blight-flag brush over `war3map.w3e`, circle/square + radius; tested |
| Pathing / blocker map (`war3map.wpm`) | ✅ Have | `PathingCommand` — HiveWE-style paint/set/clear/toggle brush over walk/fly/build pathing bits, circle/square + radius, on `war3map.wpm`; tested |
| Fog / weather / sky / environment settings | 🟡 Partial | Stored in `w3i`/`w3e`; exposed via MapInfo, not all edited |
| Camera bounds | ✅ Have | In `w3i` (MapInfo read/write) |
| **Unit / item placement** (`war3mapUnits.doo`) | ✅ Have | `PlacementCommand` place unit — owner/position/rotation/scale/variation/skin, auto creation numbers; tested |
| **Doodad placement** (`war3map.doo`) | ✅ Have | `PlacementCommand` place doodad — same shape as units, on `war3map.doo`; tested |
| **Region placement** (`war3map.w3r`) | ✅ Have | `PlacementCommand` place region — rect + name/weather/ambient, on `war3map.w3r`; tested |
| **Camera objects** (`war3map.w3c`) | 🟡 Partial | Bounds yes; named camera objects no |
| Start locations | 🟡 Partial | Count in `w3i`; no placement helper |
| Tile variations / tileset swap (up to 64 tiles) | 🟡 Partial | Data model supports; no convenience op |

**Biggest terrain gaps (remaining):** the terrain-brush family now ships end to end —
placement (`PlacementCommand`), height/deform, texture-paint, cliff/ramp, water, and
blight (`TerrainCommand`), plus pathing (`PathingCommand`), all tested and reachable from
both the CLI and MCP surfaces (guarded by `CliMcpParityTests`). What's left: **named
camera-object placement** (`war3map.w3c`), **tileset-swap / tile-variation convenience
ops**, and full **fog / weather / sky** editing beyond `w3i` field access.

---

## 2. Object Editor

Custom game data: units, items, destructibles, doodads, abilities, buffs/effects,
upgrades (techtree). Reads/writes `war3map.w3u/.w3t/.w3b/.w3d/.w3a/.w3h/.w3q`
(custom object data), against the base game data (SLK/CASC).

| Feature | wc3ctl status | Notes |
|---|---|---|
| Read all object types (unit/item/dest/doodad/ability/buff/upgrade) | ✅ Have | `ObjectListCommand`, `ObjectGetCommand`, `ObjectKinds` |
| Edit / set object fields | ✅ Have | `ObjectSetCommand`, `ObjectDataWriter`; level/variation-aware |
| Create new custom objects (rawcode alloc) | ✅ Have | `ObjectNewCommand` + `RawcodeAllocator` |
| Field metadata / valid options (dropdowns) | ✅ Have | `ObjectFieldOptionsCommand`, `DropdownFilter` |
| Base game data merge (SLK/CASC + custom) | ✅ Have | Object-data merge (CASC) design implemented |
| Import/export object data files | ✅ Have | Per-type `.w3*` read/write |
| Port objects between maps | ✅ Have | `PortCommand` / `port_unit` MCP tool |
| Studio object editor (browse/edit UI) | ✅ Have | `ObjectEditorView` panel |
| Reforged surfaced fields (Move Speed Factor, Max Creep Level, Detonation Delay, 3-decimal cooldowns, Feedback cap fix) | 🟡 Partial | Editable as raw fields; **no named/validated affordance** |

**Object Editor is our strongest module** — near feature-complete for data editing.

---

## 3. Trigger Editor

GUI triggers + JASS/Lua custom script. Reads/writes `war3map.wtg` (GUI trigger
definitions), `war3map.wct` (custom text/JASS), `war3map.wts` (trigger strings),
and generates `war3map.j` / `war3map.lua` (compiled script).

| Feature | wc3ctl status | Notes |
|---|---|---|
| Trigger strings `war3map.wts` (read/write/resolve) | ✅ Have | `StringsCommand`, `TriggerStringResolver`, `StringImportView` |
| Custom script (`war3map.j` / `.lua`) porting | ✅ Have | `ScriptCommand`, `ScriptPorter`, `ScriptView` |
| GUI trigger model (`war3map.wtg`) | ❌ Missing | Not parsed to an editable GUI-trigger AST |
| Custom text triggers (`war3map.wct`) | 🟡 Partial | Read via MapDocument; no structured editing |
| GUI event/condition/action catalog | ❌ Missing | No trigger-function catalog |
| Variables editor | ❌ Missing | — |
| New Reforged trigger events ("Unit takes damage", set-stat actions) | ➖ N/A | These are GUI-catalog entries; only relevant if we build GUI-trigger support |
| JassNewGen / vJass (off-by-default in 1.33+) | ➖ N/A | See survey — external toolchains (JassHelper/Wurst) |

**Trigger gap:** we handle *strings* and *raw script* well, but there is **no GUI
trigger (`.wtg`) parse/edit path**. That's the largest single missing module.

---

## 4. Sound Editor

Sound sets + imported audio. Reads/writes `war3map.w3s` (sound definitions),
`war3map.w3d`-style snd, and imported `.wav`/`.mp3` assets.

| Feature | wc3ctl status | Notes |
|---|---|---|
| Import/preview audio assets | 🟡 Partial | Audio file preview exists (AudioPlayer / FilePreview) |
| Sound definitions `war3map.w3s` | ❌ Missing | Not parsed |
| Sound sets / 3D sound params | ❌ Missing | — |

---

## 5. Import / Asset Manager

| Feature | wc3ctl status | Notes |
|---|---|---|
| Import list `war3map.imp` (read/write) | ✅ Have | `ImportsCommand` |
| Add/extract arbitrary files to/from MPQ | ✅ Have | `ExtractCommand`, `BundleCommand`, `FileEditCommand`, `ListCommand` |
| Path handling / custom paths | ✅ Have | MPQ listfile + `.imp` path table |
| Supported asset types (tga/blp, mdx, wav/mp3) | 🟡 Partial | Read/preview; MDX render supported; no format *conversion* for all |

---

## 6. Object Manager

Map-wide cross-referenced listing of every unit/doodad/region/trigger/etc.

| Feature | wc3ctl status | Notes |
|---|---|---|
| Enumerate map contents by category | 🟡 Partial | `ListCommand` (files), object commands (data); **no unified in-map instance manager** |
| Dependency graph / cross-references | ✅ Have | `DependencyGraphView` panel |

---

## 7. AI Editor & 8. Campaign Editor

| Feature | wc3ctl status | Notes |
|---|---|---|
| AI scripts (`war3map.wai` / `.ai`) | ❌ Missing | Not parsed |
| Campaign files (`.w3n`, campaign info) | ❌ Missing | Single-map focus today |

Both are **low priority** for a programmatic map tool.

---

## 9. Regions / Cameras / Pathing (placement editors)

Covered inline under Terrain Editor above. Summary: **region placement (`w3r`) and the
pathing brush (`wpm`) now ship and are tested** (`PlacementCommand`, `PathingCommand`).
Remaining gap: **named camera-object placement (`w3c`)** — camera *bounds* are already
handled via `MapInfo`, but discrete camera objects are not yet placeable.

---

## Reforged-specific considerations

- **2.0 Classic-HD / Addon asset system** (Nov 2024) — a *runtime/asset* feature, not
  an editor data format change. Object data rawcodes are unchanged; ➖ mostly N/A to us.
- **2.0.4** — extra cliff types, imported-tileset crash fix, Lua `%` fix. Our `w3e`
  model should tolerate extra cliff types (verify).
- **1.33 JassNewGen off-by-default**; PopcornFX 2.5.1 replaced the classic particle
  system (affects model/FX assets, not map data formats).
- **PopcornFX** effects are a rendering concern (relevant to our MDX/render path, not
  map authoring).

---

## Prioritized gap → roadmap

Ranked by value for an automatable map tool:

1. ✅ **Unit/item placement** (`war3mapUnits.doo`) — **shipped** (`PlacementCommand`, tested).
   Unlocks automated test-map generation.
2. ✅ **Doodad placement** (`war3map.doo`) — **shipped** (`PlacementCommand`, tested).
3. ✅ **Region placement** (`war3map.w3r`) — **shipped** (`PlacementCommand`, tested).
   Camera-object placement (`.w3c`) still open.
4. ✅ **Pathing-map brush ops** (`war3map.wpm`) — **shipped** (`PathingCommand`, tested) —
   HiveWE's signature capability.
5. ✅ **Terrain brush primitives** — height/deform, texture-paint, cliff/ramp, water, and
   blight brushes all **shipped** (`TerrainCommand`, tested, CLI+MCP) — HiveWE-style
   generative terrain is done; only tileset-swap/tile-variation convenience ops remain.
6. **Camera-object placement** (`war3map.w3c`) — discrete named cameras; small, completes
   in-map placement automation.
7. **GUI trigger (`.wtg`) parse/edit** — largest missing module; big effort, defer
   unless demand.
7. **Sound editor (`.w3s`)** — low effort, low demand.
8. AI/Campaign — lowest priority.

---

## Sources

- [Revisiting the Warcraft III Editor — Blizzard News](https://news.blizzard.com/en-us/article/23395649/revisiting-the-warcraft-iii-editor)
- [Warcraft III World Editor — Wowpedia](https://wowpedia.fandom.com/wiki/Warcraft_III_World_Editor)
- [Warcraft III World Editor — Warcraft Wiki](https://warcraft.wiki.gg/wiki/Warcraft_III_World_Editor)
- [Upgraded World Editor Functions and Details — Blizzard Forums](https://us.forums.blizzard.com/en/warcraft3/t/upgraded-world-editor-functions-and-details/244)
- [What are the new features added to World Editor in WC3 Reforged? — Hive Workshop](https://www.hiveworkshop.com/threads/what-are-the-new-features-added-to-world-editor-in-wc3-reforged.353085/)
- [Patch 1.33.0 — Liquipedia](https://liquipedia.net/warcraft/Patch_1.33.0)
- [Patch 2.0.0 — Blizzard News](https://news.blizzard.com/en-us/article/24167122/warcraft-iii-reforged-patch-notes-patch-2-0-0)
- [Patch 2.0.4 — Liquipedia](https://liquipedia.net/warcraft/Patch_2.0.4)
- [Warcraft III: Reforged — Wikipedia](https://en.wikipedia.org/wiki/Warcraft_III:_Reforged)

_Generated as part of the wc3ctl feature-parity effort. Verify format specifics
against `Wc3.MapDocument` parsers before relying on any "Partial" row._
