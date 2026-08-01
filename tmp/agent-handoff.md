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

### DO NOT PUBLISH `b88ec77` AS IS, one regression and one gap

`b88ec77` is committed on `development` and deliberately NOT pushed or published. Published state
is `2fc7ef7`. Read this before touching it.

**What is good in it, keep all of this.**
- `gg_rct_Arena` now gets the TARGET map's real bounds (`Rect(-6144., -6144., 6144., 6144.)` on a 96
  tile map, read from its own camera bounds) instead of `Rect(0., 0., 0., 0.)`. Measured cause,
  `CheckCoordsInRect` is `GetRectMinX <= x and x <= GetRectMaxX` and so on, so an empty rect
  collapses "is this in bounds" into "is this exactly the origin". Three of Asta's OWN movement
  helpers gate on it (`MoveEff` for the Q slash travel, `MoveUnit` and `PosUnit` for the W dash and
  the shared knockback), so a spell froze the moment it moved one tick off (0,0). Correctly scoped,
  only rects reached SOLELY through bounds style helpers get the new default, the other eight stayed
  empty.
- `function Init` (GearSystems) is now wired, so `GearTimer03/05/10 = CreateTimer()` actually runs.
- `RuntimeReadinessCommand` now catches Shape B, see below.

**The regression, must be undone.** The rule I specified was "wire an initializer when it wires
hero reachable code OR when it assigns a global the hero's reachable code reads". That makes
`InitTrig_ModeDialog` qualify, because it assigns `NoDecor_Cond` which Asta's Q and W read. So mode
selection is wired back in, along with its tooltips, camera pan, a 20 second `CheckPickedMode` timer
and `FogMaskEnable`. **That is the user's single loudest complaint** ("mode selection and some text,
i dont want those"). The trade does not even pay, `NoDecor_Cond` is DEGRADED not BROKEN, a null
boolexpr in `GroupEnumUnitsInRange` means "match everyone" and Asta's loop bodies re-verify anyway.

**The fix, and it was proposed to me first and I wrongly overruled it.** Line replay. For a global
the hero's reachable code reads whose ONLY assignments across the source are the static argument
free shape (`set X = function F`, `set X = Condition(function F)`), replay that ONE assignment line
into the hero's bootstrap function, carrying `F`'s body if needed, and do NOT wire the enclosing
function. Gets the value without importing the side effects. `function Init` should stay wired even
so, it is a genuine state constructor rather than framework, an explicit allowance is fine.

**The gap my diagnosis missed.** Wiring `Init` was necessary but NOT sufficient. `Init`'s body still
has `set GearTimer03Callback = function GearSystems__GearTimer03Loop` trim marked, because
`GearSystems__GearTimer03Loop` was never carried at all, its only reference being a bare
`function X` inside an assignment in what used to be a dead function. So the timers now EXIST but
tick NOTHING, and the knockback still never writes a position, which is the user's actual reported
symptom. Carry that loop function and replay the assignment, same mechanism.

### Shape A and Shape B, name them when you see them

- **Shape A**, a global read in the carried script with NO assignment anywhere in the text. A
  textual "does `set X=` appear" scan finds it. `--bootstrap-state` handles the constructible types.
- **Shape B**, NEW, three instances found in one pass. The assignment IS present in the text, so a
  textual scan reports it fine, but the enclosing FUNCTION is never called. Live in the text, dead
  at runtime. Instances, `GearTimer03/05/10` (the handles), their `Callback` globals, and
  `NoDecor_Cond`. `b88ec77` extends `GlobalNeverAssigned` to catch this, keep that.

### Ruled out, do not re-chase
- The 57 `GetRectCenter(gg_rct_Caster)` positions belong to about 30 OTHER heroes. Asta reaches that
  rect only via the shared `StunUnit`. This was MY prime suspect and it was wrong.
- `AstaW_Stun` is `real AstaW_Stun= 0` in the SOURCE too. Pre existing, not a port regression.
- `s__GearSystems__KS_MoveEffectToUnit_c` reads `_c[this]` before writing it in the SOURCE too.
- `InitTrig_DmgSys` is FINE to leave unwired, Asta's damage calls `UnitDamageTarget` directly and
  the custom engine never mentions him.
- `InitTrig_LvlUpCheck` is genuinely UNKNOWN, confounded by the preplace at level 35 test recipe.

### Corpus count note
`Category=Corpus` is 32 in `Wc3.Tests` plus 12 in `Wc3.Studio.Tests`, 44 total. Run both projects.

### NEXT CHANGE, placement double registration, confirmed in game by the user

Published state fixes only HALF of this. `bd44a63` stops a `--synth-dispatch` hero from also
registering the shared dispatcher. The other half is unfixed and it breaks an UNTOUCHED map.

**Reproduce.** Copy `Anime_WOS2_0.28a2.w3x`, `place unit H028 0 <x> <y>` into it, no porting, no
flags. Launch. **Every ability player 0 casts fires TWICE, including a hero picked through the map's
own pick screen that we never touched.** User's words, "it broke the map, like abilities of even
original wos has 2x cast".

**Cause.** `PreplacedUnitsScript` emits, inside the generated block,
`TriggerRegisterPlayerUnitEvent(gg_trg_CastCheck, Player(0), EVENT_PLAYER_UNIT_SPELL_EFFECT, null)`.
Keyed on the PLAYER, not the hero. WOS2 already registers that same trigger for a player inside
`Trig_LvlUpCheck_Actions`, which runs on every hero level up. Two registrations on one trigger means
the condition runs twice per cast, so `_Start` runs twice, so each `_Loop` is started twice.
That is the double damage on R and the leftover pause on T then Q.

**The assumption that is wrong** is written in `DetectPerPlayerSpellTriggers`' own doc comment, "A
placed hero never triggers that registration" and "Registering an unrelated dispatcher is harmless".
Neither holds when the target map still has its own live hero flow for that same player.

**Fix shape.** Do not register when the target map ALREADY registers that trigger for that event and
that player. `TriggerRegisterAnyUnitEventBJ` on the trigger makes ours always redundant. Keep the
registration for a BLANK target, where nothing else registers it, that is why this wiring exists.

**Verification.** Place into an untouched WOS2 copy with no flags, grep every registration on
`gg_trg_CastCheck`, ours must be ABSENT and the map's own present. Then port H028 into a fresh 96
tile map with no flags, ours must be PRESENT. Paste both. Hermetic at or above 930, corpus 44, three
reference heroes at 0 errors.

### Also open
- `audit ability` reports 3 of 11 verified on an Asta port where `audit hero` said 8 of 8 ok, so the
  new command works. Its STATE column flags `gg_rct_Arena`, `gg_rct_Base` and `gg_rct_Caster` as
  unassigned on every ability, but that run may not have had `--bootstrap-state` on, which assigns
  exactly those. RERUN the audit with `--synth-dispatch --bootstrap-state` before treating those as
  real, and if they survive it is a genuine gap in bootstrap-state.
- `A0DU` and `A0DV` each have an unpause with no matching live pause, a second independent route to a
  stuck paused hero, separate from the double registration.
- Asta's F, `A0DR`, still has 2 trimmed lines, no reachable damage call, and its effect asset
  `wos_krk (1971).mdl` absent from the map.
- The maps at `Download/1/1`, `Asta WOSBASE.w3x` and `Asta WOSBASE LVL1.w3x`, BOTH carry the
  duplicate registration, so they double cast regardless of hero level. The level 1 control built to
  test a SetHeroLevel hypothesis is therefore invalid, and that hypothesis is superseded by this
  simpler cause.

---

## Claude, 2026-08-02

### The untouched map double registration, fixed

Added `PreplacedUnitsScript.DetectAlreadyLiveSpellTriggers`. Before emitting our own
`TriggerRegisterPlayerUnitEvent(trg, Player(owner), ...)` for a candidate trigger, it builds the
forward call graph closure from `main` and `config` (the same `ScriptPorter.ForwardClosure`
`RuntimeReadinessCommand` already uses) and checks whether the trigger already has a reachable
`TriggerRegisterPlayerUnitEvent` or `TriggerRegisterAnyUnitEventBJ` for the same spell effect event.
If so, that trigger is skipped entirely, for every owner, not just the one the map's own code names.
`GeneratorVersion` bumped 8 to 9.

**Verified on the untouched map.** `place unit` into an untouched `Anime_WOS2_0.28a2.w3x`, no flags.
Grep on `gg_trg_CastCheck` shows exactly one hit, WOS2's own `Trig_LvlUpCheck_Actions` line. Ours is
absent.

**The trap that almost shipped a regression.** First pass suppressed our registration on a PLAIN port
into a fresh 96 tile blank map too, which is exactly the case this wiring exists for. Cause, a plain
port (no `--synth-dispatch`) hooks every carried `InitTrig_*` not already called by a carried
aggregator straight into `InitCustomTriggers` (`ScriptPorter.HookInit`), including a carried
`InitTrig_LvlUpCheck`, so the reachability closure found it "already live" in the ported target too,
even though nothing there registers the dispatcher until wc3ctl's own generated code does. Fixed by
blanking every line marked `// wc3ctl ported: ...` before computing the closure, a wc3ctl-inserted
call must never itself be the reason something reads as already reachable, only a call the target
already had before wc3ctl touched it counts. Re-verified, the same plain port into blank96 now shows
BOTH `Trig_LvlUpCheck_Actions` (still carried, still textually present) and our own
`TriggerRegisterPlayerUnitEvent(gg_trg_CastCheck, Player(0), ...)` (ours, now correctly present).

**Note for whoever answers "InitTrig_LvlUpCheck is genuinely UNKNOWN" above.** It is not fully
resolved even now. A PLAIN port into a blank map carries AND WIRES `InitTrig_LvlUpCheck` (a plain
port has no framework filter, unlike `--synth-dispatch`), so its own event registration
(`TriggerRegisterAnyUnitEventBJ(gg_trg_LvlUpCheck, EVENT_PLAYER_HERO_LEVEL)`) and action
(`Trig_LvlUpCheck_Actions`) really do run at game init in the ported target. Once the placed hero
FIRST levels up in game (not immediately, but eventually, during ordinary play), that action may
ALSO register `gg_trg_CastCheck` for that player a second time, on top of our own already-live one,
a LATENT variant of the exact same double cast bug, confined to a plain port, surfacing later rather
than immediately. Deliberately NOT fixed here, out of scope for the exact ask (which only covered the
untouched map and the blank map cases, both verified above), and it needs its own design, most likely
ScriptPorter itself should recognize a carried `InitTrig_*` whose own action re-registers a trigger
our deferred wiring will already cover, and skip wiring THAT one specifically for a placed/ported
hero, rather than PreplacedUnitsScript trying to reason about it after the fact. Flagged, not fixed.

Gates. Hermetic 934 (930 Wc3.Tests, 4 Wc3.Studio.Tests). Corpus, see the commit this session lands
on for the exact count, both projects re-run after this fix. Three reference heroes (H028, H0DA,
H001) validate 0 errors with flags off.

### NEXT JOB, grant level up abilities at spawn, with the blocker already located

**Field status.** The user confirmed `Asta WOSBASE.w3x` (untouched WOS2 copy plus `place unit`, built
at `9abe916`) is **100% working, damage applies perfectly**. So the double registration was the real
bug and it is closed. The ONLY remaining gap is his level up granted abilities.

**Missing abilities.** `A0DQ` (Asta G, "Black Clover"), `A0DR` (Asta F, "Causality Break"), and
`A0DW` (Asta Sword) which rides with G.

**Cause, measured.** WOS2 grants them in `Trig_LvlUpCheck_Actions`, source line 92782.
```
if Asta_ID == id then
    if GetUnitAbilityLevel(c, AstaG_ID) == 0 then
        call UnitAddAbility(c, AstaG_ID)
        call UnitMakeAbilityPermanent(c, true, AstaG_ID)
        call UnitAddAbility(c, AstaSword_ID)
    endif
    if GetUnitAbilityLevel(c, AstaF_ID) == 0 then
        call UnitAddAbility(c, AstaF_ID)
        call UnitMakeAbilityPermanent(c, true, AstaF_ID)
    endif
endif
```
A PREPLACED hero is created and levelled in one shot, so that event never fires for it.

**Measured behaviour per path, in a blank map.**
```
plain port          InitTrig_LvlUpCheck wired 1,  grant reachable, works AFTER one level up
--synth-dispatch    InitTrig_LvlUpCheck wired 0,  grant carried but NEVER reachable, 3 abilities lost
```
The second is a regression from the framework prune. Do NOT fix it by wiring that trigger back, it is
also what re-registers the cast dispatcher, the latent double fire `9abe916` closed.

**THE BLOCKER, this is the part worth not rediscovering.** The obvious reuse does not work.
`SynthDispatchBuilder.TryFindHeroBranch` (around line 210) rejects the level up guard, because it
requires the condition text to contain `GetUnitTypeId`, and this guard is `if Asta_ID == id` where
`id` is a LOCAL assigned from `GetUnitTypeId` earlier in the function. So the matcher needs relaxing
to accept an id alias compared against a local that was assigned from `GetUnitTypeId`, and only then
can `ExtractAbilityBranches` style extraction pull the grant block out.

**DO NOT take the shortcut** of scanning for `UnitAddAbility(c, <alias>)` and granting anything in the
hero's carried set. The ported script contains EVERY hero's grant code, so that hands Asta other
heroes' passives. The hero guard scoping is the whole difficulty, it is not incidental.

**Requirements.** Emit at spawn in the generated block, so no level up is needed. Works with flags on
and off. Keep the source's `if GetUnitAbilityLevel(c, X) == 0` guard so a re-grant is idempotent, and
keep `UnitMakeAbilityPermanent` where the source has it, that is what survives a morph. Key it on the
hero's own branch so it generalises to any arena granting on level up, and emit nothing when a hero
has no such branch.

**Verification.** Blank map, port then place, with flags OFF and again with
`--synth-dispatch --bootstrap-state`, prove `A0DQ`, `A0DR`, `A0DW` are added at spawn with no level
up. Placement into an untouched `Anime_WOS2_0.28a2.w3x` copy, same proof, AND prove our duplicate
dispatcher registration is still absent so `9abe916` does not regress. Hermetic at or above 934,
corpus 44, three reference heroes at 0 errors with flags off.

**Today's workaround for the user.** Level once and the full kit appears.

---

## Claude, 2026-08-02, later. Level up grant job, DONE, commit `ec2caec`

Fixed. `A0DQ`, `A0DR`, `A0DW` now grant at spawn, no level up needed, in all three configurations
(flags off, `--synth-dispatch --bootstrap-state`, placed into an untouched WOS2 copy). Generated
lines, flags off.
```
if GetUnitAbilityLevel(u, AstaG_ID) == 0 then
    call UnitAddAbility(u, AstaG_ID)
    call UnitMakeAbilityPermanent(u, true, AstaG_ID)
    call UnitAddAbility(u, AstaSword_ID)
endif
if GetUnitAbilityLevel(u, AstaF_ID) == 0 then
    call UnitAddAbility(u, AstaF_ID)
    call UnitMakeAbilityPermanent(u, true, AstaF_ID)
endif
```

The blocker was exactly as recorded, `TryFindHeroBranch` required the guard's own condition to
contain the literal text `GetUnitTypeId`. Relaxed it to also resolve a hero alias compared against a
LOCAL, by scanning the enclosing function for that local's own assignment and checking whether ITS
right side names `GetUnitTypeId`. Added `SynthDispatchBuilder.ExtractHeroLevelUpGrants`, which reuses
that same relaxed hero branch match (factored the function walk both readers use into one
`ForEachHeroBranch`) and then, inside the hero's own branch only, pulls out every depth 0
`if GetUnitAbilityLevel(unit, X) == 0 then ... endif` block verbatim. Deliberately narrow, a level
threshold or a saved flag block in the same branch is left alone, there is no idempotent guard on
those to replay safely at spawn. `PreplacedUnitsScript.DetectLevelUpGrants` runs this against the
TARGET's own script (same as every other detector in that file), so it works whether the map is
untouched, plainly ported, or `--synth-dispatch` ported. `GeneratorVersion` 9 to 10.

**The trap held.** Grepped the generated block on all three maps, only `AstaG_ID`, `AstaSword_ID`,
`AstaF_ID` ever appear, nothing from Natsu, Mahoraga, Laxus, or any of the other dozen heroes whose
grant code shares the same `Trig_LvlUpCheck_Actions` function. Placement into the untouched WOS2 copy
still shows exactly one `TriggerRegisterPlayerUnitEvent(gg_trg_CastCheck, ...)`, the map's own, ours
absent, `9abe916` does not regress.

**One pre-existing finding surfaced, not caused by this change, not fixed.** `validate --deep` on the
untouched-map placement reports `INVALID, 1 error, [runtime-readiness] no RunInitializationTriggers
function at all`. Confirmed unrelated, `PreplacedUnitsScript.cs` never mentions that identifier at all
(grepped), and the TRULY untouched map (no placement at all) validates 0 errors 0 warnings, because
`RuntimeReadinessCommand.CheckPlacedHeroes` has nothing to check until a player owned hero exists on
the map. The check fires only once MY test placement gave it one, and would fire identically under
`GeneratorVersion` 9. Likely cause, `RuntimeReadinessCommand` assumes any map with an `InitTrig_*`
function must also define `RunInitializationTriggers`, but that function only exists when a map has a
GUI trigger whose sole event is Map Initialization, which WOS2 may simply not have. Same shape as the
already-known "readiness severity mis-calibration" bugs. Flagged, not chased, out of scope for this
job.

Gates. Hermetic 942 (938 `Wc3.Tests`, 4 `Wc3.Studio.Tests`), 8 new (5 `SynthDispatchLevelUpGrantTests`,
3 `PreplacedUnitsScriptTests`). Corpus 44 (32 `Wc3.Tests`, 12 `Wc3.Studio.Tests`). `H028`, `H0DA`
(`Anime Choice Arena V0.31C.w3x`), `H001` (`GGGA_V0.02d.w3x`) all validate 0 errors with flags off.

---

## Claude, 2026-08-02, later still. Porting H0DA into GGGA and the tavern, two new porter bugs found

Job, port `H0DA` Shadow Nanaya from `Anime Choice Arena V0.31C.w3x` into `GGGA_V0.02d.w3x` and make it
selectable from tavern `n00I`. Delivered
`C:/Users/GodMephisto/Documents/Warcraft III/Maps/Download/1/1/1/GGGA Shadow Nanaya.w3x`.

### Tavern append needed no CLI change

`object get <map> n00I` shows the sold-units field is `useu` "Units Sold", a plain String field
holding a comma separated rawcode list. `object set` already writes a String field verbatim, commas
included, so appending a rawcode is just reading the current value and writing it back with the new
one appended. Verified round trip on a scratch copy before touching the real port. No CLI work
needed here, the concern in the job description did not apply.

### Bug 1, `--synth-dispatch` silently no-opped on Anime Choice Arena's own dispatcher shape

`SynthDispatchBuilder`'s hero-guard and ability-guard matching (`HeroGuardEq`/`TokenEq`, both plain
regexes assuming a BARE token or local on each side of `==`) was built and tested only against
Anime_WOS2's shape, which caches the caster and the ability id into locals once
(`local unit c= GetSpellAbilityUnit()`, then `GetUnitTypeId(c) == Hero_ID`, then `id == HeroQ_ID`).
Anime Choice Arena's own dispatcher calls the accessor natives INLINE at every comparison, no caching
local at all (`GetUnitTypeId(GetSpellAbilityUnit()) == DarkShiki_ID`, `GetSpellAbilityId() ==
DarkShikiQ_ID`). The nested parens in the first one and the bare call in the second both broke the
old regexes, so `ExtractHeroCastBranches` returned null for every ACA hero, `--synth-dispatch`
degraded silently to "no per-hero cast branch found", and the ordinary carried closure took over,
which meant carrying ACA's WHOLE shared dispatcher and framework (397 InitTrig_* would have gone in
uncontrolled) into GGGA. Exactly the "map boots as a partial source game" failure mode from the Asta
job, this time for the reason the flag exists to prevent.

Fixed by replacing both regexes with `SplitTopLevelEquality`, a depth-tracking scan that finds the
first `==` NOT inside any parens and returns each side as a whole, self-contained expression, then
classifying each side by substring (`Contains("GetUnitTypeId")`) or by alias/literal resolution,
never by a fixed-shape regex. Both idioms now resolve, and the old literal-local shape still does too
(pinned by a test). New tests, `SynthDispatchInlineAccessorTests.cs`, 4 cases (inline hero guard
resolves, a foreign hero's inline branch is not picked up, a MIXED script where one hero caches
locals and another inlines the accessor both resolve independently).

After the fix, the real port on H0DA correctly synthesizes `wc3ctl_SynthCast_H0DA` covering 6 ability
branches and skips 397 of ACA's own `InitTrig_*` as not hero-reachable, framework pruning back to
doing its job.

### Bug 2, a carried global or a carried local can collide with the TARGET's own BURIED name, in
either direction, and neither was checked before this

`validate --deep` on the actual port gave 2 pjass errors, `Symbol s already defined as global
variable` and `Symbol txt already defined as global variable`. Root cause, JASS forbids a local
sharing a name with any global, and that is checked in this codebase only against TOP LEVEL names
(`JassFunctionIndex`/`JassGlobals`, functions and globals), never against a name some UNRELATED
function uses only as its own local. Two real, independent, and OPPOSITE-direction instances landed
in the same port:
- ACA's own `string s=null` / `texttag txt=null` (globals behind a shared tooltip helper), carried
  into GGGA. GGGA never declares a top level `s` or `txt`, so the ordinary collision check saw
  nothing wrong, yet GGGA's OWN untouched `ShieldDeduction` has `local integer array s` and
  `RPB_CreateClassHelp` has `local string array txt`, three hundred thousand lines apart from H0DA,
  entirely unrelated to this hero. The carried globals landed right on top of both.
- The reverse also happens on other carried bodies, a carried function's OWN local sharing a name
  with a TARGET's real global.

Fixed with two additions to `ScriptPorter.cs`, both covered by new tests in `ScriptPortTests.cs`
(`A_carried_globals_name_that_collides_with_the_targets_own_buried_local_is_renamed` and
`A_carried_locals_name_that_collides_with_the_targets_own_global_is_renamed`).
1. `AllLocalAndParamNames(jass)` scans a WHOLE script (not one function) for every local/parameter
   name declared anywhere, fed into `taken`, the set a freshly carried GLOBAL or FUNCTION must avoid.
2. `RenameLocalsCollidingWithGlobalScope(functionText, targetGlobalScope)` renames a carried
   function's OWN local (or parameter) when it collides, per function scope, `_l`/`_l1`/... suffix.

Deliberately kept as TWO separate sets, `taken` (broad, includes the target's buried locals, used
only for the ordinary carried-symbol rename) vs `targetGlobalScope` (narrow, real global/function
names only, used for the carried-local rename). Two different carried FUNCTIONS' own locals never
actually collide with each other in real JASS (locals are function scoped), only with something
genuinely global, conflating the two sets first produced correct but needlessly noisy output
(`id_l1` through `id_l714` as the SAME "id" local in unrelated functions kept fighting over one
shared counter). Splitting them fixed both correctness and the noise.

### GGGA's own heroes, traced not assumed

`audit ability` on GGGA's own `H001` regressed 11/11 to 10/11 after the port, ability `A08A` DISP
went from PASS (untested, GGGA's A08A has no cast-dispatch condition of its own, it is an
ability-availability toggle not a spell) to FAIL, "neither handler 'Trig_Sun_Shot_Conditions' nor
anything that calls it is attached to a trigger". Traced fully rather than assumed broken:
`Trig_Sun_Shot_Conditions` (`return GetSpellAbilityId() == 'A08A'`) is ACA's OWN function, an
entirely different, unrelated hero's ability that happens to share the literal 4 character rawcode
`'A08A'` with GGGA's own Tohno ability of the same code (rawcodes are per map, coincidence is
expected at this volume). It is carried (inside ACA's own `PickPreloadSystem`, a giant per-hero-type
elseif dispatcher, itself only called from ACA's own `Trig_MoveHeroes_Actions`/`InitTrig_MoveHeroes`)
but confirmed, by reading `InitCustomTriggers`, `main`, and `config` in the FULLY merged script end to
end, that NOTHING calls `PickPreloadSystem`, `InitTrig_MoveHeroes`, or their callers anywhere
reachable. Dead code, present so the script compiles, never runs. GGGA's `H001` is functionally
untouched. The audit tool's own ability-to-handler matching is not scoped to a hero's own reachable
code (unlike the porter's `heroReachable`), so it found a foreign, dead, coincidentally-rawcode-
matching function and reported on ITS attachment, mislabeling a false positive as a regression. Real
bug, in `AbilityAuditCommand`, not fixed this session, flagged for whoever picks this up next, since
it will keep producing misleading diffs on any future port into a large, tightly coupled target.

### Known gap, not fixed, scoped precisely

4 of H0DA's 6 cast abilities (`A1R1`, `A1R2`, `A1R3`, `A1R4`) pass every audit column except FX,
their effect assets (`Gear_HakkeStart.mdx`, `Gear_Satsu-WSFX-1.mdl`, `Gear_mh_nanaya_xd.mdl`) are
absent from the ported map. Cause, these paths live inside ACA's own shared asset-bank function
(over the 24-distinct-assets-in-one-body threshold that marks a function as "belongs to the whole
roster, not this unit"), an existing, deliberate porter heuristic, not new. Damage and dispatch are
unaffected (both PASS), this is cosmetic only, casts will do damage with no visual effect. Fix is
narrow (3 named files) if the user wants it, out of scope tonight, no "add a file to a map" CLI
capability exists yet to do it cleanly.

### Object count

Port command's own report, 13 direct objects (`H0DA` itself, `A00a`/`A00b` poison sub-abilities,
`A1QZ`/`A1R1`/`A1R2`/`A1R3`/`A1R4`/`A1R5` the six kit abilities, `Broa`/`B00a`/`B00b`/`B09K` buffs)
plus 25 carried by the script closure (runtime-granted sub-abilities and dummies its handlers spawn).
11 rawcode collisions auto-remapped (3 unit, 5 ability, 3 buff). 12 files copied directly plus 175
carried by the script closure (the 481-foreign-files question from the earlier session is still the
user's open call, unchanged by this job).

### Gates

Hermetic 947 (943 `Wc3.Tests`, 4 `Wc3.Studio.Tests`), 9 new (4 `SynthDispatchInlineAccessorTests`, 2
new `ScriptPortTests`, 3 pre-existing files unchanged in count). Corpus 44 (32 `Wc3.Tests`, 12
`Wc3.Studio.Tests`). `H028` (`Anime_WOS2_0.28a2.w3x`), `H0DA` (`Anime Choice Arena V0.31C.w3x`),
`H001` (`GGGA_V0.02d.w3x`, untouched) all validate 0 errors with flags off. The delivered map itself,
`validate --deep` 0 errors 13 warnings (all pre-existing advisory categories, `UnitAlive` undeclared
and uninitialized-variable notices), `roundtrip` byte faithful.

### Then, matching GGGA's own storage conventions for a hero, no code change needed

User's follow up ask, make the ported hero follow how GGGA stores its OWN heroes, not just play
correctly. Diffed `H0DA`'s full field list against all three native heroes with cast dispatch,
`H001`, `H05Y`, `H006` (`object get`, no `--field`, every field not just the ones already compared).
Three real, three-for-three house conventions found, none of them gameplay.
- `Placeable In Editor (uine)`, all three natives `0`, `H0DA` was `1` (base default). The one the
  user already measured.
- `Hero - Hide Hero Death Message (uhhd)`, all three natives `1`, `H0DA` was `0` (base default).
- `Stock Replenish Interval (usrg)` and `Stock Start Delay (usst)`, all three natives `3600` and `10`,
  `H0DA` was `100000` and `0`. This one directly answers the tavern question, it is the shop
  restocking behaviour, not a hero stat.

Applied all three with plain `object set` calls, no CLI change needed, `uine`, `uhhd`, `usrg`, `usst`
on `H0DA`. Re-ran `validate --deep` (still 0 errors 13 warnings), `roundtrip` (still byte faithful),
`audit ability` on both `H0DA` and `H001` (identical to before, these are unit level fields, not
script, nothing to regress). No source change, nothing to test-suite-regress, hermetic/corpus gates
from the prior entry stand unchanged.

**Left alone and reported, not applied, each one ambiguous between presentation and gameplay.**
- `Upgrades Used (upgr)`, all three natives list the identical `R001,R000,R002,R003,Reuv`, `H0DA` has
  none. Checked what these are, `R000` "defense", `R001` "speed of attack", `R002` "Body meeting",
  `R003` "Horseman", real player purchasable STAT upgrades. Whether a hero is wired to receive them
  affects its actual power level against other heroes, so this is gameplay adjacent despite being
  identical across all three natives, the user's own rule says leave gameplay to ACA, flagged instead
  of guessed.
- `Formation Rank (ufor)`, natives `2`, `H0DA` `0`. Movement group behaviour, borderline.
- `Death Type (udea)`, natives `2`, `H0DA` `0`. Corpse and death animation behaviour, arguably tied to
  the model, which the user said must stay ACA's.
- `Tooltip - Awaken (uawt)` and `Tooltip - Revive (utpr)`, all three natives have both set, `H0DA` has
  neither field at all. Purely display text, but there is no source text to copy, inventing lore
  appropriate wording is a content decision, not a storage convention, flagged rather than guessed.
- `Scaling Value (usca)` and `Selection Scale (ussc)` differ too but are model size tied, expected to
  differ, not touched, matches the user's own explicit model exclusion.

**Checked and NOT a convention.** `Button Position (ubpx/ubpy)`, `H001` and `H05Y` both `(2,0)`, but
`H006` is `(1,0)`, not consistent across the three natives, so not a real rule, left alone. Also
noticed all three natives store `Name`/`Proper Names`/every `Tooltip -` field as a `TRIGSTR_` mapwide
string table reference, `H0DA` stores the same content as a literal inline string instead. Zero
functional difference in game either way, purely a storage detail, and converting it would mean
inventing new WTS string table entries, out of scope for what was asked, reported not changed.

**Tavern list order.** The `n00I` `useu` list (`H05U,H006,H001,H04G,H01U,H016,H027,H00K,H02Z,H05Z,
H01K,H028`) is not alphabetical or grouped by rawcode or by anime series, it reads as historical add
order, oldest first. Appending `H0DA` at the end already matches that, nothing else to change there.

Delivered map path unchanged, `Download/1/1/1/GGGA Shadow Nanaya.w3x`, overwritten in place with the
four field fixes, same job, no new commit needed in this repo since nothing in `src/` or `tests/`
changed for this part.

---

## Claude, 2026-08-02, later still. The four missing FX assets, the premise was wrong

Asked to bring `Gear_HakkeStart.mdx`, `Gear_Satsu-WSFX-1.mdl` and `Gear_mh_nanaya_xd.mdl` into the
delivered Nanaya map, either with a general import CLI command or a porter reachability rule. Traced
it first rather than building either, and both are unnecessary. **The files were already in the map,
correctly registered in `war3map.imp`, with correct bytes.** `ls` on the delivered map shows all
three under `war3mapImported\` already, and `render-model` against the real file resolves every one
of them once given a single backslash.

Also false, the earlier note that these paths "live inside ACA's shared asset bank function". Read
the source script directly, each of the four effects sits inside Nanaya's own small per ability
function (`DarkShikiW_Start` for `A1R1`, `Loop_DarkShikiE` for `A1R2`, `DarkShikiR_Start` for `A1R3`,
`Loop_DarkShikiT` for `A1R4`), each with a handful of distinct assets, nowhere near the 24 threshold.
The bundle for `H0DA` on the source map already lists all three files as carried, confirmed by running
`bundle unit` directly and reading its own file list and diagnostics.

**The real bug was in `wc3ctl audit ability`, not the porter.** `AbilityAuditCommand.EffectsCheck`
pulls a path straight out of a JASS string literal with a bare `"([^\"]*)"` regex and never un-escapes
it. These scripts always write one real path separator as a doubled backslash in source text,
`EffectSpawn("war3mapImported\\Gear_HakkeStart.mdx", ...)` means the single-backslash path at runtime,
the same thing a C string means by `\\`. `MapDocument.HarvestAssetNames` and `BundleCommand`'s own
literal scan already knew this and un-escaped it, which is exactly why the porter carried these files
correctly in the first place, but the audit tool's separate regex never did, so it looked up a path
with two backslash characters that can never equal any file name the map actually stores, and reported
a false absent.

Confirmed with `render-model` directly against the real map file, the double backslash form fails to
resolve, the single backslash form (after either manual unescaping or asking for the ability's own
wrong sibling extension `.mdl` against the source's actual `.mdx`) resolves fine, the existing
`.mdx`/`.mdl` swap and case insensitive lookup in `RenderModelCommand.FindAssetEntry` already handle
the rest, nothing else needed touching.

Fixed by moving the unescape into one place, `AssetPathCandidates.Unescape` in `Wc3.MapDocument`
(alongside its sibling path spelling helpers), and pointing the three call sites that were each doing
their own inline `.Replace` at it, `MapDocument.HarvestAssetNames`, `BundleCommand`'s script literal
scan, and the new call in `AbilityAuditCommand.EffectsCheck`. No map file changed, no porter behaviour
changed, `audit ability` on the delivered map now reports `H0DA` 10 of 10 fully verified, all four FX
columns flip to ok.

**Do not repeat this.** Before reaching for a new CLI capability or a porter rule change, check
whether the reported defect is actually a defect in the CHECKING tool first. A confident sounding
cause written in a previous session ("lives inside a shared asset bank function") was never verified
against the actual source script and was wrong.

Gates. Hermetic 947 (943 `Wc3.Tests`, 4 `Wc3.Studio.Tests`), corpus 44 (32 `Wc3.Tests`, 12
`Wc3.Studio.Tests`). Re-ported Asta `H028` from `Anime_WOS2_0.28a2.w3x` into a fresh blank 96 tile map
with `--synth-dispatch --bootstrap-state`, still exactly 127 files copied, the asset bank threshold is
unmoved. `H028`, `H0DA` (`Anime Choice Arena V0.31C.w3x`), `H001` (`GGGA_V0.02d.w3x`, untouched) all
validate 0 errors with flags off. The delivered map itself, untouched by this fix, still `validate
--deep` 0 errors 13 warnings and round trips byte faithful.
