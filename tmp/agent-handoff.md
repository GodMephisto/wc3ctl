# Agent handoff, wc3ctl porter work

Shared scratch file between Claude Code and Codex. Append, do not rewrite someone else's
section. Put your name and a timestamp on anything you add.

**Repo that matters** `D:/playground/Programming/wc3ctl` on branch `development`.
`D:/playground/Programming/Wc3_CLI` is an OLD reference copy, do not edit it, it only
receives published binaries.

---

## Claude, 2026-07-31

### State

Branch `development` at `4df56ab`, pushed. Hermetic suite green, 825 passing.
All six binaries published 05:47 (CLI, Studio, MCP into both trees) via
`scripts\publish-all.ps1`. Run it as `& "scripts\publish-all.ps1" -Also
"D:\playground\Programming\Wc3_CLI"`, this box has Windows PowerShell 5.1 only, no `pwsh`.

Commits landed this session, newest first.
- `4df56ab` fix (a), carry objects a hero's carried handlers spawn or grant at runtime
- `ac3ff9c` fix (b) wiring half, emit the guarded per-player doer-dummy setup call
- `ca48f2e` fix (c), carry a trigger's own ForGroup callback children so area damage survives
- `1fa04b4` `audit fidelity`, compare a ported object and its closure against its source
- earlier, `HeroWiringAudit` plus `wc3ctl audit hero`, the JASS compile gate, generated-block
  version stamping with a no-downgrade guard

### THE ONE REMAINING BUG, small and precisely located

`PreplacedUnitsScript.DetectDoerDummyAssigner` never matches on real maps, so the guarded
doer-dummy call is never emitted and Tohno's stun plus damage-attribution stay dead.

Cause. The detector requires the array index to textually name the player parameter
(roughly `if (!Regex.IsMatch(idx, playerParam)) continue`). The real routine
`WS_CreateWorkingSourceBagAndVendors` instead assigns a local first,
`set slot = 1 + GetPlayerId(p)`, then `set udg_Doerdummy_Stun[slot] = bj_lastCreatedUnit`.
The index is `slot`, which does not name `p`, so zero candidates are found and the port
reports "no per-player doer-dummy setup routine detected so none wired".

The existing unit test used a direct `udg_Doer[1 + GetPlayerId(p)]` index, which passes and
hides this. That is why it went unnoticed.

Fix.
1. Accept an index that is a LOCAL derived from the player parameter, resolving
   `slot = ... GetPlayerId(p) ...` back to the parameter.
2. Rebuild the emitted null-guard index from the player expression, so the guard reads
   `if udg_Doerdummy_Stun[1 + GetPlayerId(Player(<owner>))] == null then ...`.
3. Add a test using the REAL slot-local shape, keep the direct-index test too, both occur.
4. Bump `PreplacedUnitsScript.GeneratorVersion` (currently 6), since the emitted block gains
   behaviour an older build would silently strip.

Everything else for this path is already done. `WS_CreateWorkingSourceBagAndVendors` IS
carried (reason recorded as "populates udg_Doerdummy_Stun, read by a carried handler") and
the dummy objects `n00Q`, `n00S`, `n00N` ARE in the ported map. Only the runtime call is
missing.

### Then rebuild the user's test map

Deliver `C:/Users/GodMephisto/Documents/Warcraft III/Maps/Download/1/Shiki_Tohno_v3.w3x`.
Leave `Shiki_Tohno.w3x` and `Shiki_Tohno_v2.w3x` untouched, the user compares versions.

Recipe, each step via `dotnet run --project src/wc3ctl/wc3ctl.csproj -c Release --no-build --`
so you are certainly on the fixed code and not the published `dist`.
1. `new <out> --name "Shiki Tohno" --tiles 96`. The 96 matters, Tohno's dash range is 1600 and
   a 32-tile map lets a dash leave the playable area and CRASH Warcraft.
2. `port unit "C:/Users/GodMephisto/Documents/Warcraft III/Maps/Download/GGGA_V0.02b.w3x" H001 <map> -o <next>`.
   Use `H001`, NOT `H000`. GGGA ships three Tohno units and only `H001` is the variant its
   setup routine knows, `H000` appears zero times in it. Source is 240 MB, allow long timeouts.
3. `object new <map> unit hfoo -o <next>` for a fresh rawcode, then `object set` that rawcode
   `uhpm` 100000, `uhpr` 5000, `unam` "Training Dummy".
4. Place the dummy and two `hpea` as owner **24** (Neutral Hostile, that is what makes them
   enemies of every player), and `H001` as owner 0 at centre. ONE hero per player, three heroes
   sharing player 0 makes them fight over the per-player identity arrays.
5. Hero level 50. No CLI for this, either set `HeroLevel` on the placed `UnitData` and re-run
   `PreplacedUnitsScript.Sync`, or use the scratchpad tool
   `C:/Users/GodMephisto/AppData/Local/Temp/claude/D--playground-Programming-Wc3-CLI/0ae37796-bd82-494b-a710-d920a75ea15f/scratchpad/patchmap`
   which has `--maxheroes <in> <out> <rawcodes> <level>`.
6. Verify before delivering. `validate <map> --deep` clean, `audit hero <map> H001` zero
   problems, the guarded doer-dummy call actually EMITTED (quote the line), `n00Q`/`n00S`/`n00N`
   present, and the five `ForGroupBJ` callbacks live and not trim-marked.

### Hard-won rules, please do not undo these

- **Closure narrowing is REJECTED.** Three principled attempts each false-cut one of the
  hero's OWN loop or leaf helpers (they reference effects and dummies, not the ability id, so
  they look foreign) and regressed abilities `A0QO` and `e025`. Over-carry is bloat on a map
  that works, under-carry is a broken ability. Prefer carrying too much and reporting it. The
  rejection is recorded in `4df56ab`'s commit message.
- **Do not chase object attribution.** `e025` is a shared effect dummy that a dozen heroes
  spawn, so no static rule can attribute it to one hero. Six measured configs proved this.
- **The 10 "could not carry safely" array diagnostics are BENIGN.** All are zero-qualified,
  never several. Six are other heroes' runtime scratch arrays filled inline at cast time, four
  are UI selection, pick-screen and repick state. The guard correctly excludes them. Only the
  wording is misleading, "none qualified, or several did" is always the former here.
- **Republish ALL targets before the user tests.** A stale `dist-studio` once made an
  already-fixed bug look unfixed and cost hours.
- **Prose punctuation rule, absolute.** No em dash, en dash, dash as punctuation, colon, or
  semicolon in any prose, comments, or commit messages. Commas, periods, parentheses only.
  Literal code and Windows paths are exempt. This is a standing user rule.
- **No `Co-Authored-By` or AI attribution** in commits on this repo.

### Known open, lower priority

- The 20 uninitialised-variable notices `validate --deep` reports are non-fatal and shared with
  maps that work in game, treat as advisory.
- 25 corpus findings remain after false positives were removed, down from 91. Re-run with
  `dotnet test --filter "Category=Corpus&FullyQualifiedName~PortTriage"`, report lands at
  `%TEMP%\wc3ctl-port-triage.md`.
- 25 of the user's 37 custom maps are protected with stripped MPQ listfiles, so nothing can be
  ported from them. Deferred by the user's choice.
- User-requested feature, in Studio's `DependencyGraphView`, left-click a node to toggle port
  or unport (default all on), and regroup so each ability's triggers nest UNDER that ability
  instead of forming a parallel limb. Feasible, the port already takes a filterable bundle and
  `BundleFunction.Reason` carries the attribution needed for nesting. Open design question, does
  unporting a node cascade to its dependents or only warn.

### Note

Two subagents stopped mid-task on an org monthly spend limit, which is why the detector fix and
the v3 rebuild are not done. Nothing is half-written, the tree is clean at `4df56ab`.

---

## Codex, add your section below
