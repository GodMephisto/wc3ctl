# wc3ctl Studio — fixes & feature backlog (2026-07-23)

Living backlog from a QA sweep. Roadmap agreed: stay on .NET/Avalonia; sequence
**W0 dev-loop → W1 Text/ID → W2 correctness → W3 UX → W4 features**. Dev loop is now
`dotnet run` from source (no publish); title bar carries a git-hash + build-time stamp.

## ✅ Fixed in source — queued for the next rebuild (not yet in a running exe unless noted)
- **Object-list switching** — `SelectionMode="Multiple"` accumulated on plain click and the
  pane/port seam followed `selected[0]`, so it looked frozen. Now plain-click switches,
  Ctrl toggles, Shift ranges, double-click previews. *(in the running build)*
- **Port → Target reload** — `RunPort` wrote `<target>.ported.<ext>` but never loaded it;
  Target pane now reloads the ported map.
- **GL camera movable** — `OpenGlControlBase` isn't hit-test-visible, so its pointer handlers
  never fired. Added a transparent input-catcher overlay → orbit/pan/zoom/keys. *(in the running build)*
- **Port copies custom models** — copy step used a lookup that missed the `.mdl↔.mdx` swap, so
  custom models were discovered-but-skipped. Now model-aware (also fixes re-port "already there"
  detection). Regression test added (PortCommandTests, 4/4 pass).
- **Dev loop + build stamp** — `dotnet run` iteration; git-hash + build-time in title bar.
- **Review hardening (applied after independent code review)** — object list now takes keyboard
  focus on click; range/single-select refreshes the field pane ONCE (was re-running the merge
  per row); modified double-clicks no longer collapse a multi-selection; per-frame GL disk
  logging removed; dead GL pointer overrides deleted; `DriveZoom` ignores zero-delta wheel; GL
  drag clears on lost pointer capture; port output de-chains `.ported.ported`. PortCommand
  model-copy fix independently verified clean.

### Feature batch 2 (shipped — built by parallel Fable subagents + orchestrator, all verified)
- **New Blank Map** — wired `BlankMap.Create()` into the Target pane (create in-memory; Save
  prompts for a location on first save). Command layer already tested (`BlankMapTests`).
- **Object-editor reference-field names + list expansion** — reference fields (metadata type
  ends unit/abil/item/tech/upgrade/buff/effect + Code/List) now show names: single ref inline
  `A000 (Naginata Combo)`, list fields expand into indented read-only sub-rows. Names resolved
  once per map (cached), display-only sub-rows excluded from editing. Test `ObjectEditorRefFieldsTests`.
- **Smarter Palette** — loads off the UI thread (generation stamp, no more freeze on first CASC
  open) and groups by kind → source (map content above the base bulk). Selection contract
  preserved. Test `PaletteViewTests`.
- Verified: solution build 11 projects 0/0; **556 hermetic + 5 headless UI tests green**.
- (superseded by batch 3 below)

### Feature batch 3 (shipped — 5 parallel Fable agents + orchestrator, all verified)
- **Editable reference-list builder** (ObjEdit) — RefList editor mode: add (searchable picker) /
  remove / reorder entries; writes comma-joined rawcodes via the existing Apply path.
- **Dependency graph = tldraw canvas** (DepGraph) — drag empty space to pan, wheel zoom-to-cursor,
  drag nodes (edges follow), zoom bar (±/Fit/Reset), kind badge in the picker.
- **Palette icons** (PalIcons) — units show their icon (uico→unitskin Art), decoded lazily off-thread
  per visible row, cached. (Grouping still by source, not Reforged category/race — optional follow-up.)
- **Port correctness** (PortFix) — single-port object dedup (re-porting the same unit no longer
  duplicates); dependency-crawl precision (only metadata-typed reference fields → kills the
  Tohno→Nanaya false node); Port button tracks the live selection.
- **Real placed units in GL** (Viewport, redone after "no dots" feedback) — actual model geometry
  per placement, posed/scaled/rotated on the terrain, per-geoset color + baked shading (reuses the
  marker shader); upright-box fallback when a model can't resolve (never a flat dot). GL
  click-to-place (ray-picks terrain). GL is now the 3D mode; brush options (rnd rotation/scale).
- Integration fixes by orchestrator: internal-`Execute` call in TerrainView → public overload;
  port-dedup gated to single-port (fixed 2 batch-test regressions + preview/port parity);
  stripped the `· kind` tag from the Port button label.
- Verified: solution build 11 projects 0/0; **572 hermetic + 7 headless UI tests green**.
### Feature batch 4 (shipped — verified 577 hermetic + 7 UI, build 11/0/0)
- **Per-geoset textured placed models** (Viewport) — real per-pixel model textures (UV per geoset,
  alpha-cutout foliage, two-sided lambert), neutral-gray for team-color/unresolved geosets, box only
  when a model won't parse. Average-color tint deleted. Textures upload once per unique model.
- **Blank maps get a war3map.j** (BlankScript) — standard globals/InitCustomTriggers/config/main
  skeleton (BOM-stripped via UTF8(false)); port-into-blank now splices + hooks the script.
- **Port into a blank/unsaved target** (orchestrator) — RunPort ports into the live in-memory target
  when it has no file (was the "port broke on blank map" regression); Save then prompts for a path.
- **Collapsible palette groups** (orchestrator) — click a `kind · source` header to expand/collapse;
  base catalogs collapsed by default; search auto-expands. Selection contract intact.
- FOLLOW-UPS still open: team color (fill neutral-gray ReplaceableId sections w/ player color);
  animation (static Stand pose); async first-open model resolution; anisotropic filter on model
  textures; blank-map full WE playability (w3i player/force lists, camera bounds, wpm/minimap);
  Palette Reforged-style category grouping. NEXT: click-select a placed unit + set owner/team.

### Base-game model resolution fix (the "real solution", not box band-aids) — ModelResolve, verified
- Root cause: classic doodads with variations store ONE model file PER VARIATION with a numeric
  suffix (`ruins_flower0.mdx`…`4.mdx`) and NO bare file; `dfil` names the extensionless stem, so
  the CASC lookup missed them → placeholder boxes. Not a mod-layer/prefix issue (all under
  `war3.w3mod:`), confirmed by CASC listfile enumeration.
- Fix: `RenderModelCommand.PathCandidates` now yields a `<stem>0.mdx/.mdl` variation candidate LAST
  (exact/ext-swap forms still win first → no regression). Added `CascGameDataSource.EnumerateFileNames`
  + `GameDataContext.FindFiles(substring)` as the asset-discovery tool. No extra prefixes needed.
- Result on GGGA: doodad/destructable resolve 103/125 → **121/125, 0 Prepare failures**; the last 4
  (APms/D00A/D00V/D02A) have no model field = correct skip. Verified 581 hermetic + 29 GameData +
  24 corpus (ModelResolveTests, ClassicDoodadResolveTests). Source-only (Commands/GameData).
- STILL OPEN: the "black" placed models (they resolve WITH textures per diagnostics, so it's a
  render/texture-upload quirk for specific models, not a resolution miss) — needs which-models info.

### 100% placement render — zero fallback boxes (orange + white eliminated)
- White boxes were START LOCATIONS: war3mapUnits.doo stores them as units with TypeId `sloc`,
  which carry no model → skip (PlacementScene.StartLocationRawcode).
- Orange boxes were (a) custom doodads with no own model field that never inherited their base's
  art (D00A→ARrk, D00V→Corn, D02A→Ruins_Flower), and (b) one particle-only doodad (ZWbg
  BubbleGeyser, 0 geosets) + one model-less doodad (APms).
- Fix — new `Wc3.Commands/PlacementModelResolver.ResolveModelPath` (shared path resolution, moved
  out of the Studio GUI per Principle #3): own model field → skin profile → BASE object's model
  (custom types inherit). Studio's TerrainView delegates to it.
- Fix — `PlacementScene.Build`: skip `sloc`; and when a REAL resolver is supplied, a type with no
  drawable geometry (null model OR mesh-less/particle-only) is SKIPPED, not boxed. The box remains
  only for the no-resolver debug path (Build(doc)).
- WHITE BOXES (round 2): not fallback boxes at all — GeneralHeroGlow (D032/D033) resolved fine but
  its only texture is `ReplaceableId:2` (team-color glow). No replaceable-texture decode / additive
  blend → rendered as a solid gray≈white quad. Fix: PlacementScene.IsTeamColorEffect skips models
  whose whole surface is ReplaceableId/none (glow/effect planes) — mixed models (real units with
  team-color trim) still render. GGGA now 3881 instances / 121 meshes / 0 boxes / 0 fully-untextured.
- FOLLOW-UP: real team-color (decode ReplaceableId→player color) + additive-blend materials would let
  glow planes and unit team parts render properly instead of being skipped/gray.

- Result on GGGA: 3922 placements, 123 unique meshes, **0 fallback boxes** (was orange+white).
  Verified 581 hermetic + Corpus (PlacementResolveTests: zero-boxes gate + D00A/D00V/D02A
  inheritance; ClassicDoodadResolveTests 121/125). PlacementSceneTests updated (empty model now
  skips instead of boxing when a resolver is present).

### 🌊 WAVE: WC3 player/team editing + selection + weird-texture — SHIPPED (build 11/0/0, 639 hermetic + 7 UI + 23 placement corpus)
Integrated all 5 agents. Seam fix by orchestrator: added `TerrainView.RefreshPlacements()` passthrough
(panels called it; the method lived on the inner TerrainGlView). Runtime-only bits (GL shader, live
click-pick) are build+logic verified and need user eyes; commands/pick-math/panels are test-covered.
- MatMode: per-geoset FilterMode (MDX+MDL). RenderSel: PlacementInstance.OwnerId/CreationNumber,
  PlacementMesh local AABB, PlacementMeshSection.FilterMode/IsTeamColor. ViewportSel: GL click-select +
  highlight, shader honors FilterMode (discard only on Transparent → fixes weird-texture holes; additive
  glow), owner-color team tint, anisotropic. UnitEdit: UnitInstanceCommand + PlayerForceCommand +
  PlayerColors. PanelsTeam: UnitPropertiesView (owner dropdown grouped by team + level/hp/mana/scale/
  facing/gold) + PlayerForceView (team membership + ally/shared flags), wired to viewport selection.
- FOUNDATIONS ready for next waves (all command-layer, hermetic-tested, 678 hermetic green total):
  TerrainEditCommand (w3e cell primitives + area brush), TriggerReadCommand (read-only trigger/
  variable model), DoodadInstanceCommand (doodad get/list/edit/delete by CreationNumber; name from
  w3b+w3d). Next UI waves (need Studio stopped to central-build): doodad-select → terrain-sculpt brush
  → trigger-editor. Holding those until the user validates the live player/team + selection runtime
  (GL shader + click-pick are runtime-only, not headless-testable).

### 🌊 WAVE (superseded — see SHIPPED above): WC3 player/team editing + selection + weird-texture
User ask: select a placed unit on the map; change its owner/player & the map's forces/teams
(Team 1/2…) like WC3; fix the "weird texture" on placed units; "also refer others".
- Phase A (parallel Fable agents, disjoint files, building):
  - MatMode (Wc3.Modeling): per-geoset `FilterMode` (None/Transparent/Blend/Additive/…) from MDX+MDL
    materials → lets the shader alpha-test ONLY transparent geosets (fixes weird-texture holes from the
    blanket `a<0.5 discard`) and enables additive glow.
  - UnitEdit (Wc3.Commands, new files): UnitInstanceCommand (Get/List + Set owner/level/hp/mana/scale/
    facing/gold by CreationNumber), PlayerForceCommand (read players + forces/teams; edit force
    membership + ally/shared-vision flags), PlayerColors (Reforged 28-slot palette).
- Phase B (after A): rendering (GL click-select + highlight, team-color tint by owner, honor FilterMode,
  anisotropic) + Studio panels (unit properties w/ owner dropdown grouped by team; player/force editor).
- Phase C: orchestrator integrates, wires select→panel, central-verify, relaunch.
- Deferred follow-up: palette Reforged-category grouping, animation, async first-open resolution.

### Follow-up fixes on the player/team wave (build 11/0/0, 678 hermetic + 7 UI)
- POINTER MODE (couldn't stop placing): once a Palette item was armed every click placed, and a
  right-click even placed (bug). Now Esc OR right-click in the GL view cancels the brush → pointer
  mode (left click then selects a unit); TerrainView.PlacementBrushCleared → PaletteView.ClearSelection()
  keeps the palette highlight in sync; brush text shows "▶ Pointer mode…" / "…right-click or Esc to cancel".
- WEIRD POLYGON (persisted after the alpha-cutout fix): unit/doodad models use Blend/AddAlpha/Modulate
  geosets (diag: GGGA doodads = Blend 16, Additive 14, AddAlpha 3, Modulate 1; Blademaster has AddAlpha)
  that the shader still drew OPAQUE → a hard solid polygon. Now the draw loop sets the right GL blend per
  FilterMode (Blend=SrcAlpha/1-SrcAlpha, Additive+AddAlpha=SrcAlpha/One, Modulate=Zero/SrcColor,
  Modulate2x=DstColor/SrcColor), DepthMask off for all translucent; shader outputs texture alpha for
  Blend/Additive/AddAlpha. FOLLOW-UP: translucent geosets aren't depth-sorted yet (minor ordering
  artifacts possible; no more opaque box).

### Blank-map ground + multi-select/remove (build 11/0/0, 684 hermetic + 7 UI)
- BLANK MAP looked groundless: BuildEnvironment never populated env.TerrainTypes, so
  TerrainArtCatalog fell back to a flat gray fill. Fixed: seed Lordaeron Summer ground tiles
  (Lgrs/Lgrd/Ldrt/Ldro/Lrok, grass at index 0) → real textured ground. Corrected 4 TerrainEdit/
  TerrainCommand tests that had baked in the "blank map has no ground types" bug; new test
  Create_PopulatesGroundTiles.
- STOP-PLACING: Esc/right-click were unreliable (focus for Esc; camera-orbit conflict for right;
  GL-only). Added a visible "✋ Stop placing" toolbar button (enabled while a brush is armed) → the
  reliable pointer-mode exit in both 2D and GL.
- MULTI-SELECT + REMOVE (MultiSelect agent, optimized editor scheme): left-click select, Shift+left
  toggle, left-DRAG marquee box (Shift adds), Delete/Backspace removes; camera moved to RIGHT-drag
  orbit + MIDDLE-drag pan (left is now selection). Commands: UnitInstanceCommand.Delete/DeleteMany/
  SetOwnerMany (+13 tests). Viewport: HashSet selection, SetSelectedUnits, PickUnitsInRect (world→
  screen), marquee overlay Canvas. Panel: ShowUnits — 1 = full editor, 2+ = bulk owner + Remove.

### Terrain workspace dock + icon palette + proper unit names (build 11/0/0, 684 hermetic + 7 UI)
- UNIT NAME: the panel showed the rawcode only for base-game units (hermetic UnitInstanceCommand
  returns null Name). Fixed: UnitPropertiesView resolves "Name (rawcode)" via ObjectGetCommand (base
  data + deltas + TRIGSTR), cached per type; single + multi headers show names.
- RND-ROT / SCALE: verified WORKING (PlaceViaHistory reads the toggle+slider; PlaceUnit stores
  Rotation/Scale; renderer applies them; PlacementSceneTests covers rotation). No fix needed.
- LAYOUT (DockLayout): Terrain tab = viewport (elastic) + GridSplitter + a resizable side dock with
  sub-tabs Palette · Unit · Players (auto-switch: palette-armed→Palette, unit-selected→Unit). Those
  three left the top-level tab strip.
- PALETTE (PaletteIcons): icon-only grid (WrapPanel tiles), hover tooltip = "Name (rawcode) · kind ·
  source"; search + kind filter + selection contract (PlacementChanged/SelectedPlacement/ClearSelection)
  preserved.

### Reusable UI controls + dock-fit + Files column fix (build 11/0/0, 684 hermetic + 7 UI)
- CLAUDE.md now mandates: no hardcoded/duplicated code — extract reusable Avalonia UserControls
  (src/Wc3.Studio/Controls/) or reuse existing; logic shared per layer (Principle #3 to front-ends).
- New controls (UiComponents agent): LabeledField (label+editor+hint), OwnerTeamPicker (grouped
  owner dropdown w/ swatches — deduped from 3 copies), PlayerChip (swatch+name), SectionHeader
  (visible collapsible bar — fixes invisible palette headers), Card. Unit/Players/Palette rebuilt
  on them → also fixes Unit overflow (no MinWidth 380, fits ~400 dock), Players row overlap
  (Grid Auto,*,Auto + trimming), invisible palette category (SectionHeader #66888888 + BaseHigh).
- FILES column overlap: list pane was ~31% (*,4,2.2*) but table had 280px fixed cols → Name star
  collapsed and Size/Known/Parsed drew over it. Fixed: split 1.5*,4,* + cols *,84,58,58 + Name
  ellipsis + hover tooltip for the full name.

### Palette rebuilt (grouped) — invisible-header hack replaced (build 11/0/0, 684 hermetic + 7 UI)
- Root cause of "category still not visible / lots broken": the icon grid mixed SectionHeader items
  and tile items in ONE WrapPanel-backed ListBox with a MinWidth=4000 hack → headers never rendered
  visibly and the mixed WrapPanel was fragile (2 failed attempts).
- Rebuilt: PaletteView is now a ScrollViewer → ItemsControl of PaletteGroup, each group = a
  full-width SectionHeader (a NORMAL vertical child → always visible) stacked over a WrapPanel of
  icon tiles (IsVisible bound to !Collapsed). Selection is tile-click → PaletteRow.IsSelected
  (Classes.selected highlight) → Select() → PlacementChanged; no ListBox. Contract preserved
  (PlacementChanged/SelectedPlacement/ClearSelection/ShowMap). Search/filter/collapse/off-thread
  load/lazy icons all kept. PaletteViewTests updated to the grouped model (reflect _groups + invoke
  Select) — both corpus UI tests green.

### Palette icon-or-name tiles (not a bug — WC3 has no doodad icons)
- Diagnosed "lots missing icon": Doodads have 0/619 icons (WC3 doodads have NO command-button icon —
  the World Editor lists them by name); Units 597/950 decode, 352 have no icon path (hero/custom
  variants). So icon-ONLY was impossible (~62% blank).
- Fix: PaletteRow.HasIconArt/TileText → the tile shows the icon when the type has art (most units),
  otherwise the NAME as text (doodads + icon-less units); hover still shows "Name (rawcode) · …".

### 🧭 Big-subsystem roadmap (user: "yeah do them" — the ⬜ not-yet-scoped WE features)
Sequenced AFTER the player/team wave integrates (terrain sculpt + doodad-select touch the same
viewport/shell files the Phase-B agents own → must wait to avoid conflicts).
- FOUNDATIONS started now (Commands-layer, disjoint new files, no conflict):
  - TerrainEdit — war3map.w3e cell primitives (get/set ground height, water, tile, cliff) +
    AddGroundHeightArea (radius+falloff) → the raise/lower/paint brush backend.
  - TriggerRead — read-only war3map.wtg/wct parse → TriggerModel (categories, triggers, ECA
    function trees, variables, script language). Foundation for the trigger editor; write-back later.
- THEN (each its own wave):
  1. Doodad selection + region/camera object editing (extends unit selection).
  2. Terrain sculpting brush UI on the GL viewport (needs a short brush-semantics design).
  3. Trigger editor UI (GUI tree + ECA editor + variable manager + JASS/Lua view) — largest WE
     subsystem; gets a design pass before the build wave.
- BLOCKED on repro (asked user): weird-texture screenshot (to confirm alpha-cutout is the symptom),
  Test-in-WC3, Map Info parsing, Files column, MP3/cache, black models (which ones).

### Review items deferred (need a decision, not applied)
- **Port runs on a fresh disk load, not the live Target session** — unsaved Target-pane edits
  aren't included and are dropped from view after the port rebinds to the `.ported` copy. Fix
  needs porting into the live session (or an explicit "port uses on-disk map" note). (W2/W4)
- **Build stamp recompiles every build** — intentional (fresh build-time each `dotnet run`);
  revisit only if the dev loop feels slow.

## ❓ Reported — need a concrete repro before fixing (no guessing)
- **Test-in-WC3 fails** — exact status-bar text on click? (launch code in `GameInstall`/`OnTestClick` looks correct → likely env/Reforged-launch quirk.)
- **Map Info "broken parsing"** — which field is wrong/missing/garbled?
- **Palette "not working"** — empty list? selecting doesn't arm placement? placing fails?
- **Files column "stacks above Name"** — screenshot (XAML grid looks correct).
- **MP3 / "cache everywhere"** — exact symptom? plays wrong/old file? BLP image stale? error text mentioning cache? (`AudioPlayer` writes a fresh temp per play — not caching audio itself.)

## 🏗️ Feature-builds — need design, belong in workstreams
- **Dependency precision (W2, correctness)** — closure crawl scans *all* fields for 4-char
  tokens, so a value that merely looks like a rawcode (e.g. "Nanaya") gets pulled in as a node.
  Fix: only follow fields whose game-data *metadata type* is an object reference. Also surface
  "why included (via field)" on each node.
- **TRIGSTR display resolution (W1/W2) — FIXED.** Automated audit against the real map found raw
  `TRIGSTR_` in two surfaces: object-editor field VALUES (unam/utip/upro, ides/utub, anam/atp1/
  aub1/auu1…, bnam, dnam, fnam/ftip/fube) and the Map Info tab (name/author/desc/recommended).
  Object *names* in lists and the Palette were already resolved (0 leaks). Fix: `MergedField` now
  carries a wts-resolved `Display` (raw `Value` kept for edit/write-back); `ObjectGetCommand.Merge`
  fills it; the object-editor grid binds `DisplayValue` (editor still edits raw, with a "string-
  table reference" note); MapInfoView shows resolved text, edits raw. Hermetic test
  `ObjectGetDisplayTests` locks it in. NOTE: port is the opposite case and already correct — it
  INLINES strings (source wts → literal) since the target lacks the source's string table.

- **Terrain undo/redo markers (W2)** — undo/redo *data* is correct (one placement each), but
  `AfterHistoryEdit` clears ALL breadcrumb dots per op, so undo looks like it deletes everything
  and redo looks like a no-op (no dot restored; units aren't rendered). Fix: track markers in
  lockstep with the history stacks (pop on undo, restore on redo); unify 2D/3D marker paths
  (3D draws dots outside `_markers`). Long-term: render actual placed units/doodads.
- **3D view perf (W2/W3)** — CPU `RenderPerspectivePng` re-rasterizes per drag → laggy. GL works
  well now → make the GL viewport the 3D mode; retire/soften the CPU path.

- **Abilities show empty in Studio — FIXED (was a stale search filter, NOT a parser bug).**
  Root-caused with a headless UI test: war3map.w3a parses fine (540 abilities via ObjectListCommand;
  save round-trip preserves all 540 — no tolerant reader needed). The real cause: switching the
  Type dropdown did NOT clear the search box, so a query typed while browsing Units carried over
  and filtered the new kind to nothing → looked empty. Fix: `RefreshObjectList` now clears the
  search on every kind/map switch. Locked in by `ObjectEditorViewTests` (clean switch + stale-search
  switch), both green.

### Audit note — id vs name resolution (answer to "extract by id")
Swept the resolution paths: objects key on **rawcode** (Ordinal), files/models on **path**,
dropdowns on **id** (`FindById`). Core resolution is already id-based, not name-based, so
duplicate display names don't misresolve. The Tohno→Nanaya node is NOT a name collision — it's
the over-eager 4-char-token scan above (a non-reference field whose value matches a rawcode).
- **Dependency graph interaction (W3)** — tldraw-style infinite canvas: drag empty space to
  pan, drag nodes to move, Ctrl+scroll / buttons to zoom, click-select. (Hover tooltips already exist.)
- **Object-editor reference fields (W1/W2/W3)** — for fields whose metadata type is an object
  reference: (a) show the referenced object's NAME next to its rawcode; (b) expand comma-separated
  lists into a sub-row per entry ("A000 — Naginata Combo"); (c) an editable list builder — add
  rows (searchable name+id picker), remove rows, unlimited entries, reorder. Field-level edit +
  read-only detail sub-rows. All off the same reference-field metadata as dependency precision.
- **Port polish (W2/W4)** — don't re-port objects already ported (idempotency for object data, not just assets); top Port button should track the live selection / reset after a port.
- **Text ⇄ ID separation (W1)** — clean name/text vs rawcode split so search is easy everywhere.
- **Model-view improvements (W3)** — richer in-app model preview.
- **New blank map (W4)** — currently a stub; synthesize a minimal valid `.w3x`.
- **UX overhaul (W3)** — layout, theming, typography, consistent searchable name+id controls, IA.
- **Render existing placements (W3/rendering)** — the viewport shows terrain only; units/doodads
  ALREADY placed in the map aren't drawn, so you can't see the map's real contents (and undo/redo
  of placements has nothing to show). Foundational for the HiveWE-level viewport.
- **Dependencies dropdown: show kind (W3)** — the object picker doesn't indicate whether an entry
  is a Unit/Ability/etc. Add a kind badge/label per item (and/or a kind selector).
- **HiveWE-level 3D viewport (rendering roadmap)** — doodads/units with real models, WASD+mouse cam.
