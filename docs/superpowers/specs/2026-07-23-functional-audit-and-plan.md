# wc3ctl full functional audit and completion plan (2026-07-23)

Honest state of the whole toolkit from a 5-front read-only audit, plus what "functional"
means (bar = the WC3 World Editor and the project's CLI/MCP-first goal) and a ranked plan
to get there. Grades: WORKS, PARTIAL, STUB, MISSING.

## 1. State of each subsystem

### Object editing (strong foundation)
- WORKS: list/get/set/new for ALL 7 kinds, byte-faithful save, base+map merge with skin
  overlay, TRIGSTR resolution, reference-field names, add/remove/reorder list builder, enum
  dropdowns, search. (Correction: write-back is NOT units-only. The Studio CLAUDE.md note is
  stale and must be fixed.)
- Gaps: item/ability/buff/upgrade standard objects show bare rawcodes because their names and
  text live in Profile TXT files the toolkit never parses (biggest object gap). No per-level
  view of base ability data (one flattened value, not the 1-4 table). No base doodad variation
  data. No icons anywhere in the Object Editor. CLI has no field-options discovery.

### Terrain, placement, selection, rendering
- WORKS: 2D minimap and 3D GL view with camera controls. Placement of units and doodads from
  the palette (undoable). Unit click-select, shift-toggle, marquee, delete. Unit property edit
  (owner/team, level, hp/mana, gold, scale, facing), single and bulk. Textured model rendering
  with correct blend modes and team color.
- Gaps: NO terrain sculpting UI at all (TerrainCommand is CLI-only, TerrainEditCommand is
  tests-only). Doodads are unselectable and uneditable in the GUI (DoodadInstanceCommand is
  complete but never called). Cliffs and ramps have no real geometry (baked as smooth height).
  Placement hardcodes owner 0 (no owner picker). Items and destructables cannot be placed at
  all (no palette). Regions/cameras/sounds have no Studio UI. Unit and doodad property edits are
  not undoable. Models are a static Stand pose (no animation).

### Port and bundle (strong for object closure)
- WORKS: port a unit plus its full dependency closure (all 7 kinds), asset copy for
  object-field-reachable models and textures, TRIGSTR inlining, precise dependency crawl,
  rawcode collision remap, idempotent re-port, batch port, port into a blank/unsaved target.
- Gaps: CRITICAL script-port durability trap. Ported JASS is only appended to war3map.j, never
  written into war3map.wtg/wct, so the real World Editor regenerates the script on its next save
  and silently wipes the ported logic, with no warning surfaced. Port root is Unit-only in both
  CLI and Studio (command layer is generic but gated and untested for non-unit roots). Asset
  discovery misses model attachments (nested child models) and anything referenced only from
  script (special effects, sound paths). Lua-scripted maps get zero script porting. The
  rawcode-alias heuristic under-includes when a global is assigned inside a function.

### Map metadata, players, blank map
- WORKS: read/write of map name, author, description, recommended players. Player/force read and
  a partial edit (move a player between forces, set the 5 alliance/sharing flags). Validate and
  roundtrip fidelity tools (CLI).
- Gaps: Map Info exposes about 4 of ~54 w3i fields (no fog, weather, sound env, loading screen,
  prologue, tileset, camera bounds, or any Map Options flag). Players/forces cannot change
  race/controller/name/start-position or add/remove players or forces, and player color is not
  editable (a War3Net-6.x model limitation). Blank map is not real-World-Editor-openable (no
  HM3W header, no war3map.wpm pathing, no minimap). No CLI or MCP command for map-info or
  player/force editing or blank-map creation.

### Files, assets, strings, script, triggers
- WORKS: list/extract/import/save-as (byte-faithful, delete deliberately unsupported), text and
  BLP-image and audio preview, model-to-OBJ and BLP-to-image export, full Strings (war3map.wts)
  editing, function-grained Script (war3map.j/lua) editing.
- Gaps: TRIGGERS have no UI anywhere and no CLI/MCP access to a map's real trigger data (only
  the static reference catalog is wired). Trigger write-back is flag/rename only (no ECA editing,
  no add/remove trigger or variable). DDS textures are not previewed or exported despite a
  working DdsDecoder existing. Hex preview caps at 256 KB and does not virtualize. Files-tab
  model preview is a bare wireframe while a richer textured renderer sits unused. Script slice/
  edit has no CLI beyond list.

### Orphaned capability (built, no front-end)
- CameraCommand (war3map.w3c cinematic cameras): no CLI, no UI.
- PathingCommand (war3map.wpm brush editor): no CLI, no UI.
- TerrainEditCommand (per-corner terrain primitives): tests-only.
- DoodadInstanceCommand: no Studio UI.
- CLI/MCP parity gaps: many Commands features (map-info set, players/forces, validate and
  roundtrip in MCP, extract/render/search/diff in MCP, bundle-object generic, port batch in MCP,
  blank-map) have no entry point on one or both front-ends.

## 2. Ranked master gap list (cross-cutting, by user impact)

1. Triggers: no read or write UI, and no CLI/MCP access to real per-map trigger data. Largest
   missing subsystem versus the World Editor.
2. Port script-durability trap: ported logic is silently destroyed on the target's next WE save,
   with no warning. Highest invisible-correctness risk. (Cheap interim fix: warn loudly.)
3. Terrain sculpting has no UI (command layer is done and CLI-tested). Biggest Terrain-Editor gap.
4. Item/ability/buff/upgrade name and Profile-field blackout: 4 of 7 kinds show rawcodes for
   standard objects.
5. Doodads are inert in the GUI after placement (no select, edit, or delete) though the command
   exists.
6. Map Info covers about 4 of ~54 fields.
7. Port root is Unit-only (cannot port a standalone item/ability/etc.).
8. CLI/MCP do not expose map-info, players/forces, validate/roundtrip, blank-map, and more.
9. Orphaned commands (Camera, Pathing) have zero front-end.
10. Smaller: owner picker at placement, DDS preview/export, per-level base ability view, object
    icons, items/destructables placement, animation in the viewport.

## 3. Completion plan (waves, cheapest-high-value first)

Wave A, wire what already exists (fast, reuses tested command layers):
- Terrain sculpting brush UI (height/tile/cliff/ramp/water/blight) over TerrainCommand.
- Doodad selection + a Doodad properties panel over DoodadInstanceCommand (make PlacementScene
  and the GL pick doodad-aware).
- Owner picker at placement time.
- DDS preview and export (wire the existing DdsDecoder into FilePreviewCommand/ConvertCommand).
- Port: surface a loud durability warning until real wtg/wct porting exists.
- Fix the stale Studio CLAUDE.md write-back note.

Wave B, content and coverage:
- Object editor: Profile-TXT parser so item/ability/buff/upgrade names and text resolve, plus
  per-level ability and per-variation doodad base views, plus icons in the list and grid.
- Map Info: expose the full w3i field set (fog, weather, sound env, loading screen, prologue,
  tileset, Map Options flags, camera bounds).
- Players/Forces: race/controller/name/start-position edit, add/remove forces.
- Blank map: HM3W header, war3map.wpm, minimap, so the real World Editor opens it.

Wave C, front-end parity:
- CLI and MCP commands for map-info set, players/forces, validate, roundtrip, blank-map,
  port object --kind, and the remaining CLI-only or MCP-only features. Wire Camera and Pathing.

Wave D, big subsystems:
- Trigger editor: read UI (category tree + ECA view), then ECA/variable editing writing into
  war3map.wtg/wct. Proper port that writes the trigger tree (closes the durability trap).
- Items/destructables placement (palettes + placement path). Regions/cameras/sounds UI.

Every wave self-verifies with tests, real CLI runs, and window-only Studio screenshots.
