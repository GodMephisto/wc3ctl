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

---

## Claude, session close

State, local and GitHub and the published binaries all at `85ceafd`, 870 tests green, tree clean,
all agents idle. Published `08:54` via `scripts\publish-all.ps1`.

### Landed and verified
- Dependencies graph shows a hero's REAL abilities. Asta `H028` went from 193 to 6, the rest
  collapsed under a carried-by-script-closure group, canvas branching transposed to horizontal.
  Key measurement, only 6 of Asta's outgoing edges are real object-data references, about 197 are
  synthetic root edges tagged `Via = "script closure"`. Filter on that Via, an edge walk alone
  does NOT fix it.
- Protected map recovery. 32 of 50 map files readable. Asset harvest took two of them from 3 and 2
  named entries to 1504 and 2544. A hero that ported with 0 asset files now ports with real bytes.
- Porter fixes, area damage (ForGroup callbacks), runtime object carry, no double-firing, doer-dummy
  setup wired (`WS_CreateWorkingSourceBagAndVendors` under a null guard).
- Diagnostics, hero wiring audit, runtime readiness, object fidelity, JASS compile gate. CLI, MCP,
  `validate`, and a Studio Hero Audit tab.

### Open, both are the user's call
1. Canvas still branches the full closure sideways. Option B applies the real-edge filter to the
   canvas too, so only the handful branch from the hero there. Small, scoped, low risk.
2. **Tohno `H001` from GGGA does NOT work in game.** Confirmed by the user 2026-08-01. I briefly
   recorded the opposite after misreading "H001 is working" (which was about H001 being the correct
   rawcode to use rather than H000, not about the hero playing). Corrected back.

   What IS verified is that it ports and COMPILES, so the failure is not a compile error.
   `validate --deep` on the ported map gives 0 errors and 26 advisory notices, the round trip is
   byte faithful, its 9 own assets are present, and it carries 6867 functions.

   **The 14 readiness errors on the PLACED map are NOT the cause, do not chase them.** They are
   `udg_Blackbeard_Caster`, `udg_Dragonborn_FusAngle`, `udg_Issac_Caster`, `udg_JeanneAlter_Caster`,
   `udg_Marco_Caster`, `udg_Nanoha_LightningAim`, `udg_Ruby_Caster`, `udg_YoumuA_Ban` and similar.
   Every one belongs to ANOTHER hero. They are read by carried foreign code, not by Tohno's kit, so
   the check's wording ("read by this hero's own carried code") is wrong and this is the known
   readiness severity mis-calibration. Fixing that check is worthwhile for signal, but it will not
   make Tohno play.

   Static analysis is EXHAUSTED here, it has reported this hero correct five times now. The next
   honest step is instrumentation, `BJDebugMsg` in the generated wiring so the GAME reports whether
   the setup ran, what it returned, whether the dummies were created, and whether a cast reaches
   its handler. Never assert from static analysis that a hero does or does not play.

   **Naming trap, stated by the user 2026-08-01. These are all DIFFERENT CHARACTERS WITH DIFFERENT
   KITS that merely share part of a name. They are not variants of one hero.**

   ```
   GGGA  H001  "Tohno Shiki"      does not work in game
   GGGA  H05Y  "Nanaya Shiki"     different hero, different kit
   GGGA  H02J  "Nanaya Shiki"     different again (an early closure narrowing broke this one)
   GGGA  H006  "Ryougi Shiki"     different again
   ACA   H0DA  "Shadow Nanaya"    different again, this is the one d170313 broke and 252155d fixed
   WOS2  ----  DarkShiki abilities, never tested, different again
   ```

   Consequences that matter. One of these working says NOTHING about another. Porting a different
   one is not a substitute or a workaround for a broken one, the user wants the specific hero. Their
   ability rawcodes are per map and unrelated, so never carry a finding about `A1R6` or `A0DS` from
   one to another. Always name the map AND the rawcode, never just "Shiki" or "Tohno".

### Known defect I introduced
`validate` reports maps INVALID over other heroes' unused globals. The readiness check's severity is
mis-calibrated for globals belonging to heroes not placed on the map. Downgrade those.

### Coordination lessons, expensive ones
- **Two agents at most in one working tree.** Six cost three commit races, one lost commit, one
  shipped regression, and one agent reverting work that was already published because messages crossed.
- **Never `git add -A`.** That is how the rejected closure narrowing shipped and killed four abilities.
  Stage by explicit path.
- **Agent runs draw on the user's own usage.** Be economical, one careful pass, no polling loops.
- Codex CLI works as `codex exec --sandbox workspace-write -C <dir> -` with the prompt on stdin.
  `--full-auto` and the bypass flag are blocked by the host. Codex CANNOT reliably run the test
  suite here, it loops on PowerShell, so the supervisor runs build, tests and commit.
- Pushing needs `gh auth switch --user GodMephisto`. Other accounts see "Repository not found" and
  will wrongly conclude there is no remote.
- **Republish after every merge.** Stale `dist` has three times made a fixed bug look unfixed.

### Graph and CLI closure structure, closed 2026-07-31 10:14

The rule that separates a hero's real dependencies from the script closure over-carry now
lives in ONE place, `BundleStructure` in `Wc3.Commands` (`ScriptClosureVia`, `RealAdjacency`,
`RealChildEdges`, `CarriedByScriptClosure`). The Studio panel and the CLI renderer both
consume it. Before this, the rule lived only in the panel, so `wc3ctl bundle unit` still
printed the whole closure flat under the hero. Two front ends, one definition, that is why
this class of bug happened at all.

The panel's carried group now defaults to HIDDEN with the count on the checkbox. Hiding is a
view concern only, it never changes the bundle or the port exclusion set, and there is a test
that pins exactly that.

Verified by running the published exe, not by reading a report.
`dist\wc3ctl.exe bundle unit "...Anime_WOS2_0.28a2.w3x" H028` shows 6 direct abilities, all
Asta's own, and `Carried by the script closure (204)` below. `H0DA` in
`Anime Choice Arena V0.31C.w3x` shows 9, with buffs and spellbook entries nested under the
abilities that pull them in. 874 hermetic tests green, corpus panel tests green, all six
binaries published 10:14.

Note for whoever runs the corpus tests. The map-backed tests silently `return` when the map
file is absent, so a green run does NOT prove they exercised anything. Confirm the map exists
before trusting one, `Anime Choice Arena V0.31C.w3x` and `Anime_WOS2_0.28a2.w3x` are the two
in use.

### Then the same rule for files and strings, `21f87d1`, published 10:41

Splitting only the OBJECT tree was half a fix, and the user found the other half immediately.
Asta still listed all 498 files and 1875 strings as his own, so the panel and the CLI showed
Akainu's icons and every hero's tooltips under a hero with 6 abilities. He really needs 17
files (6 ability icons, hero icon, model, its 10 textures) and 88 strings. `H0DA` goes 468 to
13.

The lesson worth keeping. The OLD file rule asked whether an asset arrived through an art
field rather than a script edge. Every foreign icon arrives through an art field too, just on
a foreign object, so that walk crossed the seed edge into another hero's kit and adopted its
art. **The question is never which field code the edge carries, it is which object asked.**
Any future "is this really the root's" rule must seed from `BundleStructure.RealObjects`.
`RealFiles` continues THROUGH a file because a model's textures hang off the model path.

Strings had no owner recorded at all, `ScanField` knew it and discarded it. There is now an
object to string edge. `UnitBundle.Strings` stays the full flat list so the porter is
unchanged. Guard to remember, a display string can be four characters and look like a
rawcode, so the object rules skip `StringVia`, else a string could forge a real reference and
pull a carried object out of the carried set. Pinned by a test.

### Still open, and now the real question

The VIEW is fixed. The PORT still carries all 481 foreign files. Whether it should is the
user's call and is NOT decided. The asymmetry is the opposite of the object one, and that is
why it is worth revisiting. An under-carried OBJECT is a broken ability, so over-carry wins.
An under-carried FILE is a missing icon on a foreign object nobody plays, which is cosmetic.
The one real risk is a shared effect dummy that the hero's own carried handler spawns, drop
its model and a vfx dies silently, and the user has asked about vfx before. So do not narrow
the port's file set on this reasoning alone, measure a port with and without and test in game.
The exclusion mechanism already exists (click a node, or `BundleFilter`).

## Claude, 2026-08-01. The 7 remaining pjass errors, root-caused, NOT yet fixed

State at `493e17e`, published 01:53, 880 hermetic and 44 corpus tests green. Asta ports clean
(validate OK, byte-faithful). **Shadow Nanaya H0DA still does not compile, 7 pjass errors.**

`validate --deep` now correctly says `INVALID - 7 error(s)` with exit 2. Before today it said
`OK - valid (0 error(s), 0 warning(s))` on the same map, which is worth remembering as a warning
about trusting our own green checks.

### The single root cause behind all 7

They all come from function bodies emitted by **`CarryGlobalInitializers`**, not by the ordinary
closure. That path trims with `TrimToGlobals`, which is keyed on **globals only and never on
non-carried functions**. Two consequences, and both are in the errors.

1. A `function X` reference survives untouched, because no bad GLOBAL appears on that line. Hence
   `call TriggerAddAction(tCast, function BelR_OnCast)` with `BelR_OnCast` never emitted.
   `BelR_OnCast` IS in the source index (source line 95880), so this is not an indexer gap.
2. A bad `set` IS commented out but the `if` that guards it is structural and stays, so the script
   reads a global that was never carried.
   ```
   40977: if RINQ_registered then              <- survives, undeclared
   40980: //[wc3ctl trimmed]  set RINQ_registered=true
   ```
   `RINQ_registered` IS declared in the source globals block (`boolean RINQ_registered= false`,
   source line 8197), so `ParseGlobals` is not the problem either. It simply never reached `used`,
   because `used` is computed from `bodies` (the ordinary closure's trimmed bodies) and these
   initializer bodies are a separate collection.

The enclosing functions are `BelR_Init` and `RINQ_Register`. Note `RINQ_Register` does not match
any init naming pattern, so do not try to fix this by widening a name filter.

### The fix, in the order to do it

1. In the `CarryGlobalInitializers` path, run the FUNCTION trim as well as the global trim, so a
   `function X` naming a non-carried function gets its statement commented out like anywhere else.
2. For any global left in a residual STRUCTURAL line, carry its DECLARATION (not its assignment).
   An unused declared global costs one line and no behaviour, whereas an undeclared one is a
   compile error that kills the whole script. This is strictly the safer direction.
3. Fold these initializer bodies into the `used` computation so their global references are counted.

### Do NOT repeat these two mistakes

- **The `_Init` suffix fix in `493e17e` is correct, keep it.** I reverted it once claiming a
  cascade, having compared Nanaya's 1854 carried functions against ASTA's 165. Different heroes on
  different maps. Real cost was 1827 to 1854, and it took pjass from 11 errors to 7.
- **Do not prune assets or objects on NAME.** Asta's own handlers genuinely play `Hero_Kirito_Q2`
  and use `wos_ZarakiWCrack1.mdl` and `wos_OPm (434)3small.mdl`, with `wos_GodBoy_*` textures
  inside that model. The author reused other heroes' art, so a name rule strips his vfx and sfx.

### Also still open, small
6 `wos_Erza*_port.blp` portraits (about 937 KB) remain in Asta's port, from `GuideEnter`, which
names only 6 assets so it sits under the fan-out threshold of 24. Lowering the threshold would
also drop Asta's own busiest handler at 12. The real fix is reachability, `GuideEnter` is dead code
in the ported map, along with `OnClick`, `RandomPick` and `MyHeroIdInit`, 30 of 171 functions and
32% of the ported script's lines.

### Method that actually worked
Contrast a broken case against a genuinely working one, and read real output. Reasoning from an
absence produced four wrong diagnoses on Tohno. Never use a product of this pipeline as a control,
`ShikiArena.w3x` says "Created with wc3ctl" in its own config.

---

## Claude, 2026-08-01 07:05. The barebone port, three new flags

State `1e51fb6`, pushed, binaries published 07:06, 903 hermetic and 44 corpus green.
Delivered `C:/Users/GodMephisto/Documents/Warcraft III/Maps/Download/1/1/Asta Arena.w3x`.

### THE finding that reframed everything

**The cast chain was never broken.** The user ran an instrumented map and it printed
`CastHero_Conditions entered, ability=1093682252` (that is `A0DL`, Asta's Q),
`casterType=1211118136` (`H028`), `IsUnitType(HERO)=true`, `b=true`, `area/extension gate=true`,
`entered Asta_ID branch`, and critically `check=1`, meaning a branch MATCHED and the handler ran.
The screenshot showed the spell effect firing.

I had produced SIX static hypotheses before that, all wrong or partial (null regions, Gear timers,
DisableMoveRoot, Asta_ID, ability id globals, the trim). A clean `validate` and an `8/8 wired`
audit kept convincing me I understood a map I could not observe running. **Build the
instrumentation first next time.** `wc3ctl debug wiring <map> [hero]` exists now, use it.

**The real defect was what the user said from the start.** The port carried the source map's GAME
FRAMEWORK, 19 initializers wired into the target, of which one related to Asta. Mode selection,
shop, chat UI, music player, game start sequence, base regions, damage system. That is why the map
booted as a partial WOS2 rather than an arena.

### The three flags that fix it, all opt in, default off

- `--synth-dispatch`. Reads the hero's OWN branch of the shared cast dispatcher and synthesizes
  `wc3ctl_SynthCast_<code>()`, a minimal self-contained dispatcher on its own trigger. For Asta it
  covers all 10 branches including the runtime granted Q2, W2, R2, T2, with argument shapes lifted
  verbatim. It reads NONE of the shared gates, no `gg_rct_*`, no `GetUnitTypeId(c) == <Hero>_ID`,
  no cooldown hashtable, no `DisableMoveRoot`, no `ItemsCast`, no `SpellExtension`.
- Framework prune, rides with `--synth-dispatch`. An init is wired only when it registers a
  hero-reachable function through `TriggerAddAction`, `TriggerAddCondition`, `TimerStart`,
  `ExecuteFunc` or a direct call. Result, `InitCustomTriggers` in the ported map is ONE line.
  Unwired functions stay CARRIED so the script still compiles, they are just inert.
- `--bootstrap-state`. Constructs every read but never assigned global that has a universal
  constructor, timer, group, hashtable, trigger, rect (empty), force. Types with no safe
  construction (unit, item, effect, code, framehandle, any array) are REPORTED, never invented.

### Traps recorded, do not relearn these

- **Reachability must exclude natives.** A walk over every identifier makes every init look
  hero-related, because the hero's code calls `TimerStart` and `Condition` constantly. Restrict to
  names the map's own script declares.
- **A bare function pointer is not wiring.** `InitTrig_ModeDialog` and Asta's AoE independently
  build the same `Condition(function NoDecor_Filter)`. Only count a pointer when the SAME line has
  `TriggerAddAction`, `TriggerAddCondition` or `TimerStart`.
- **pjass rejects an identifier starting or ending with `_`.** Our own `JassScriptCheck` passed
  `wc3ctl_BootstrapState_Asta__H028_` and pjass failed it. Caught only by real corpus verification.
- **`GlobalNeverAssigned` is a Warning, not an Error**, and its wording must not claim the global is
  read by "this hero's own" code, because the check cannot attribute that. Generalizing it to all
  globals turned a working map INVALID with 57 false errors before this was fixed.
- **`ExecuteFunc("Name")` string literals** are how a World Editor `main` starts hand written
  systems. Invisible to identifier scanning. That is why `function Init`, the only creator of
  `GearTimer03/05/10`, was never carried.

### Known gaps, flagged not fixed

A hero partly driven by its OWN separately registered trigger (not through the shared dispatcher)
would have that trigger pruned too, since nothing calls it forward from the synthesized dispatcher.
Asta's whole kit goes through the one dispatcher so it did not arise. The three `GearTimer*Callback`
globals (type `code`) stay null, there is no safe universal way to build a function reference.
`.toc` and `.fdf` are still not in `AssetExtensions`, so UI template files are never carried (moot
once the music player is unwired).
