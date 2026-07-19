# Third-Party WC3 Editor & Tooling Survey — What to Adopt

> **Purpose:** Survey community/open-source Warcraft III map editors and libraries,
> and recommend which capabilities/approaches **wc3ctl** should adopt or learn from.
> Focus on things that fit a headless, CLI/library/MCP-native tool that already
> parses/writes `war3map.*`, edits object data, renders, and ports script.

## Recommendation legend

- 🟢 **Adopt** — bring the capability/approach into wc3ctl.
- 🔵 **Learn-from** — study the design/format handling; don't depend on it.
- ⚪ **Ignore** — out of scope or not worth the cost.

---

## HiveWE — `stijnherfst/HiveWE`  🟢 / 🔵

- **Link:** [github.com/stijnherfst/HiveWE](https://github.com/stijnherfst/HiveWE) ·
  [Hive Workshop project](https://www.hiveworkshop.com/projects/hivewe.4/)
- **Language / license:** C++20 (VS 17.14+, C++ modules), open-source.
- **What it is:** A fully custom, fast, open-source WC3 world editor — a ground-up
  replacement for the vanilla WE, built for speed on large maps (loads a sample map
  in ~4s vs ~32s; renders the whole map at ~120 fps).
- **Formats:** Reads/writes WC3 map formats directly; relies on the game's **CASC**
  files (points the editor at the install folder — no assets bundled, for legal reasons).
- **Standout features (relevant to us):**
  - **Pathing Brush** — its signature feature. Edit the pathing map (`war3map.wpm`)
    *directly with a brush* and save it, instead of placing pathing-blocker doodads.
    Finer granularity than blockers, and maps load faster (fewer doodads). This is the
    "SC2-style terrain collision painting tool" the requester referenced — it is
    **pathing/blocker painting on the pathing grid**, not a physics collision system.
  - **Tile Pathing setter** + shows pathing texture on cliffs (v0.3+).
  - Direct **water-height** editing, **heightmap import**, huge brush sizes (1000+),
    **global tile-pathing** editing, doodad variation control.
  - Terrain / Unit / Doodad / **Pathing** palettes; tileset up to 64 tiles.
  - Unified global search (double-shift); modern UI/UX.
- **Design rationale worth internalizing:** the author argues a *custom* editor (vs.
  memory-hacking the vanilla WE like JNGP/WEX did) is what unlocks new capabilities
  such as direct pathing editing and raised limits. wc3ctl is already "custom from the
  ground up," so this validates our approach.
- **Activity:** Actively maintained; v0.10 among recent releases (as of 2026).
- **Recommendation:**
  - 🟢 **Adopt the pathing-brush *concept*** as a library op: `wpm` load → paint
    walk/build/fly flags over a region/mask → save. Maps directly to gap-analysis item #4.
  - 🔵 **Learn-from** its terrain/tileset handling (64-tile support, cliff pathing
    texture) and its CASC-only asset model (we already do CASC merge).
  - ⚪ We don't need its GUI; our Studio + CLI cover the interactive surface.

---

## mdx-m3-viewer — `flowtsohg/mdx-m3-viewer`  🔵

- **Link:** [github.com/flowtsohg/mdx-m3-viewer](https://github.com/flowtsohg/mdx-m3-viewer)
- **Language / license:** JavaScript/TypeScript (WebGL), open-source.
- **What it is:** The most complete open-source MDX/MDL (and M3) model viewer/parser;
  also parses many WC3 formats (MPQ, BLP, SLK, W3*).
- **Recommendation:** 🔵 **Learn-from** — it is the de-facto reference for MDX
  animation/geoset/particle semantics and BLP decoding. Cross-check our WarNet/MDX
  parser and render pipeline against it. Not a dependency (different language).

---

## Wc3MapTranslator — `ChiefOfGxBxL/Wc3MapTranslator`  🔵

- **Link:** [github.com/ChiefOfGxBxL/Wc3MapTranslator](https://github.com/ChiefOfGxBxL/Wc3MapTranslator)
- **Language / license:** TypeScript, open-source.
- **What it is:** Translates `war3map.*` binaries ⇄ JSON (units, doodads, regions,
  cameras, sounds, w3i, imports, object data). Powers text-based map workflows.
- **Recommendation:** 🔵 **Learn-from** — excellent **field-by-field reference** for
  the exact `war3mapUnits.doo` / `war3map.doo` / `.w3r` / `.w3c` / `.w3s` layouts we
  need for placement (tasks #2/#3). Validate our binary layouts against its schemas.

---

## WurstScript / Wurst — `wurstscript/WurstScript`  🔵 / ⚪

- **Link:** [github.com/wurstscript/WurstScript](https://github.com/wurstscript/WurstScript)
- **Language / license:** Java (compiler), open-source; a modern language + build tool
  that compiles to JASS, with a package manager and VS Code support.
- **Recommendation:** 🔵 **Learn-from** for how a modern toolchain integrates with map
  builds (build/run flow, script injection into `war3map.j`). ⚪ We won't embed a JASS
  compiler, but our `ScriptPorter` should stay *interoperable* with Wurst output.

---

## JassHelper / vJass / Zinc, JNGP, WEX  ⚪ / 🔵

- **What they are:** The classic script-extension toolchain (vJass/Zinc via JassHelper)
  and the memory-hack editor mods (Jass NewGen Pack, World Editor Unlimited).
- **Recommendation:** ⚪ **Ignore** as dependencies (legacy, Windows-hack based).
  🔵 One lesson: HiveWE explicitly rejects the "memory-hack the vanilla WE" model —
  reinforcing wc3ctl's clean-reimplementation stance.

---

## w3x2lni (“插件”)  🔵

- **What it is:** Popular (esp. CN community) map-processing toolkit: converts maps
  between `.w3x`/`.w3m` and a Lua/YAML object-data representation, optimizes/obfuscates,
  slims object data against base game data.
- **Recommendation:** 🔵 **Learn-from** its **object-data slimming/diffing against base
  game data** (we do CASC/SLK merge — compare our diff minimization) and its Lua-based
  object representation for scripting ergonomics.

---

## StormLib / CascLib — `ladislav-zezula`  🟢(concept) / 🔵

- **Link:** [github.com/ladislav-zezula/StormLib](https://github.com/ladislav-zezula/StormLib)
- **What they are:** The canonical C libraries for MPQ (StormLib) and CASC (CascLib)
  archive read/write.
- **Recommendation:** 🔵 **Learn-from** — the reference for MPQ/CASC edge cases
  (compression, encryption, `(listfile)`/`(attributes)`, hash/block tables). Validate
  our managed MPQ reader/writer against StormLib behavior. 🟢 Adopt its *test corpus*
  mindset for archive robustness.

---

## Ceres — `ceres-wc3/ceres`  🔵

- **What it is:** A Lua-based build tool/framework for WC3 map projects (layout,
  compile, script bundling).
- **Recommendation:** 🔵 **Learn-from** for project/build ergonomics (map-as-a-project,
  reproducible builds) — aligns with wc3ctl's project/bundle model.

---

## Warsmash — `Retera/warsmash`  🔵 / ⚪

- **Link:** [github.com/Retera/WarsmashModEngine](https://github.com/Retera/WarsmashModEngine)
- **What it is:** An open-source reimplementation of the WC3 game engine (Java/libGDX);
  includes deep parsers for WC3 data + a model editor (Retera Model Studio).
- **Recommendation:** 🔵 **Learn-from** its data-driven simulation and format parsers
  (independent implementation to cross-check against). ⚪ Not a dependency — it's a
  whole engine.

---

## Prioritized "Recommended to adopt" shortlist

1. **🟢 Pathing-brush op (from HiveWE)** — `war3map.wpm` region/mask paint of
   walk/build/fly flags as a library+CLI operation. Highest-value new capability;
   directly closes gap-analysis item #4.
2. **🔵 Placement-format references (Wc3MapTranslator + Warsmash)** — use their
   field layouts to implement `war3mapUnits.doo` (task #2), then `war3map.doo`,
   `.w3r`, `.w3c`. Verify our binary reads/writes against theirs.
3. **🔵 MDX reference (mdx-m3-viewer)** — cross-check our model/render pipeline.
4. **🔵 Archive robustness (StormLib/CascLib)** — adopt their edge-case test corpus
   for our MPQ/CASC layer.
5. **🔵 Object-data slimming (w3x2lni)** — minimize our object-data diffs vs. base.
6. **🔵 Build/project ergonomics (Wurst, Ceres)** — keep `ScriptPorter`/bundle
   interoperable with modern toolchains.

**Do NOT adopt:** GUI editor code (HiveWE C++/Qt), JASS compilers, or memory-hack
editor mods — all out of scope for a clean CLI/library tool.

---

## Sources

- [HiveWE — GitHub](https://github.com/stijnherfst/HiveWE) ·
  [README](https://github.com/stijnherfst/HiveWE/blob/main/README.md) ·
  [Releases](https://github.com/stijnherfst/HiveWE/releases)
- [Introducing HiveWE — Hive Workshop](https://www.hiveworkshop.com/threads/introducing-hivewe.303183/)
- [HiveWE — Hive Workshop project](https://www.hiveworkshop.com/projects/hivewe.4/)
- [Warcraft 3 tools repository — Hive Workshop](https://www.hiveworkshop.com/repositories/tools.560/)
- mdx-m3-viewer, Wc3MapTranslator, WurstScript, StormLib/CascLib, Warsmash — see
  each project's GitHub (linked inline above). [UNVERIFIED] exact current versions/
  activity for these were not re-checked this pass; verify before depending on any.

_Generated as part of the wc3ctl tool-adoption review. HiveWE facts verified via
web search (2026); other tools drawn from domain knowledge and should be
re-verified before adoption decisions._
