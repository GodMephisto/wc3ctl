# Disconnects from thousands of distinct unit abilities (uabi)

## The report

Units carrying about 2,000 **distinct** ability ids in the Object Editor field
**Abilities - Normal** (`uabi`) disconnect players very frequently, often every game.
20 distinct ids spread over the same 2,000 units does not, and adding the abilities by
trigger at runtime does not either. The common workaround has been converting the map
from object data to SLK.

This is the players' report. It has not been confirmed in a multiplayer test yet, which is
what the maps below are for.

## The fix

Take the abilities out of `uabi` and give them to units by trigger when they are created.
No SLK conversion is needed. This gets the ids out of `uabi` just as SLK does, and the
object data stays as it is.

## Test maps (`maps/`)

| Map | uabi | Expected |
|---|---|---|
| `BaseMap_Repro_2000DistinctUabiIDs_TRUE2Player70.w3x` | 2,000 distinct ids | disconnects |
| `BaseMap_Control_20DistinctUabiIDs_2000Refs_TRUE2Player70.w3x` | 20 distinct over 2,000 refs | stays connected |
| `BaseMap_Preplaced_2000Units_RuntimeAdd_2000DistinctAbilities_TRUE2Player74.w3x` | empty, 2,000 abilities added by timer | stays connected |
| `BaseMap_Isolate_2000AbilObjects_0UabiRefs_TRUE2Player70.w3x` | empty, the same 2,000 ability objects exist | isolates whether the ability objects alone matter |
| `BaseMap_Repro_...uabiRuntime.w3x` | Repro after `wc3ctl repair uabi-runtime` | should stay connected if the fix works |
| `BaseMap_Control_...uabiRuntime.w3x` | Control after the same repair | should stay connected |

Repro and Control create no units, so the only difference between each and its
`.uabiRuntime` copy is whether the ids sit in `uabi`. If Repro disconnects while Repro
uabiRuntime and Isolate do not, `uabi` is the cause and this fix removes it.

## How to see what is broken, before fixing anything

Three read-only commands. None of them writes, and the map's hash is the same afterwards.

```
wc3ctl validate      map.w3x   # does it load at all
wc3ctl uabi-profile  map.w3x   # the unit ability lists, by the numbers
wc3ctl audit         map.w3x   # every behaviour check, errors first
```

MEASURED on the original, `GGGA_V0.07b.w3x`, 2026-09-26.

- **validate.** It loads, 0 errors, so the archive is not the problem.
- **uabi-profile.** 1,617 unit types carry a list, 5,525 references, 1,260 distinct ids. The
  Human units hold 1,141 of the distinct ids, Night Elf 34. **43 entries name an ability that
  exists nowhere**, all on 7 Human heroes. The profile is identical to `GGG_Base05_full.w3x`,
  so the variants below test exactly this map's shape.
- **audit.** Two error-level findings. `wc3ctl repair audit-errors` fixes both with the same
  detection, see "Fixed copies" below.
  - `dangling-reference`, 144. The 43 dead `uabi` entries, plus one ability, `A1A3`, whose
    101 levels each name buff `B06H`, which does not exist.
  - `requirement`, 101. Abilities inheriting a requirement the map never defines, 64 of them
    the Berserker upgrade `Robk`. This is the 3.0.0 inherited-requirement problem in
    [`docs/reforged-3.0.0-blizzard-bug-report.md`](../docs/reforged-3.0.0-blizzard-bug-report.md).
    It locks abilities in single player too, so it cannot be a disconnect cause.

None of this proves a disconnect. As
[`docs/reforged-3.0.0-findings.txt`](../docs/reforged-3.0.0-findings.txt) puts it, desync is
INFERRED, "a multiplayer failure. A map that misbehaves in single player is not desyncing."
A finding is the cause only when it is present in every map that disconnects and absent from
every map that does not, which is what the next section tests.

Every command, flag and output shape is in
[`docs/wc3ctl-command-reference.md`](../docs/wc3ctl-command-reference.md), the verified
reference, updated 2026-09-26.

## One map per suspect (the GGGA bisection)

The bisection maps in `maps/` (and inside the zips there) narrow the GGGA disconnect to the
custom Human units' `uabi` lists. `GGG_RemoveAllHuman38.w3x` is recorded as stable, with seven
clean hosts. `wc3ctl uabi-profile maps` measures every variant side by side without starting
the game, and one column separates the maps cleanly. 43 `uabi` entries on 7 Human heroes
(GG50, H08N, Hiz0, Hjv0, IM00, VE00, ZE00) name abilities that neither the map nor the game
defines, and every map that keeps those heroes' lists has them while every map that removes
them has none. That is a correlation, not yet a cause, because the same maps also carry far
more distinct ids.

These three variants each change ONE suspect from `GGG_Base05_full.w3x`, the ladder map that
matches the disconnecting shape, so one play session can tell the suspects apart.

| Map | Changed from Base05_full | refs | distinct | dead ids | If it stays connected |
|---|---|---|---|---|---|
| `GGG_Base05_full.w3x` (in the ladder zip) | nothing, should disconnect | 5,525 | 1,260 | 43 | the session is not reproducing, stop |
| `GGG_Base05_full_NoDanglingUabi43.w3x` | the 43 dead ids removed, nothing else | 5,482 | 1,217 | 0 | dead ids are the cause, the fix is 7 list edits |
| `GGG_Base05_full_NoAInvInUabi.w3x` | `AInv` added by script instead of `uabi` | 5,351 | 1,260 | 43 | the 3.0.0 inventory rework is involved |
| `GGG_Base05_full_uabiRuntime.w3x` | every list added by script | 1,948 | 199 | 1 | the lists are the cause, whatever they hold |
| `GGG_RemoveAllHuman38.w3x` (in its zip) | control, recorded stable | 2,852 | 147 | 0 | confirms the session is sound |

Why `AInv` is a suspect. Patch 3.0.0 added an equipment and bag inventory system
(`UnitEquipItem`, bag and loadout slots, `ITEM_TYPE_EQUIPMENT`), visible in the 2.0.2 to 3.0.0
diff of the game's own script layer in `Luashine/jass-history/lua-dump`, and all 7 broken
heroes carry `AInv` beside their dead ids. The dump shows the scripted layer only, so it points
at the change and cannot prove it.

Scoring needs no notes. `wc3ctl replay` reads every autosaved replay and flags a game where
players left with the disconnect result, and `wc3ctl replay --map Base05` limits it to these.

## Fixed copies of every map (`maps/fixed/`)

Every original in `maps/` and `maps/_extracted/` has a `_fixed` copy in `maps/fixed/`, made on
2026-09-26 with two commands in this order, and the originals are unchanged (hash checked).

```
wc3ctl repair audit-errors map.w3x --apply -o step1.w3x     # dead ids, missing buffs, locked requirements
wc3ctl repair uabi-runtime step1.w3x --apply -o map_fixed.w3x
```

`fix-results.csv` beside them records, per map, how many fields the first step changed, how many
references the second moved, and the checks run on the result. All 29 have 0 `dangling-reference`
and 0 `requirement` errors, pass `wc3ctl validate` with 0 errors, and their `war3map.j` passes
pjass against the game's own `common.j` and `Blizzard.j`. The loose
`GGGA_BaseMap_Pruned_Referenced_UnitsAbilities49.w3x` and the copy in its zip are the same
bytes, so they share one fixed file.

Use these to test the combined fix. To test ONE suspect at a time, use the variants in the
previous section instead, because each fixed copy changes both suspects at once.

The original map from the report, `GGGA_V0.07b.w3x`, got the same two steps as
`Documents\Warcraft III\Maps\Download\GGGA_V0.07b_fixed.w3x`. 43 dead `uabi` entries removed,
`A1A3`'s buff restored from `B06H` (defined nowhere) to its base's `BPSE` on all 101 levels,
101 inherited requirements cleared, and 3,535 references on 1,405 unit types moved to the script.

## How to test

A disconnect needs two game clients that disagree, so this needs two players on two PCs
with two Battle.net accounts. One player alone cannot reproduce it.

1. **Both players** copy the same `maps/` files into `Documents\Warcraft III\Maps\` so
   nobody downloads a different copy from the lobby.
2. **Restart Warcraft III before every game.** Switching between maps without a restart is
   itself a known desync cause (Hive, "Known Causes of Desync"), and it would blur the result.
3. **Play each map 5 times**, alternating so no map gets all its games early or late:
   Repro, Repro uabiRuntime, Isolate, Control, then again.
4. **Each game lasts 10 minutes** or until someone disconnects, whichever comes first.
5. **Record each game** in the table below. The host can also run `tools\wc3watch` to keep
   the game log, and `Documents\Warcraft III\Replay\LastReplay.w3g` holds the replay.

| # | Map | Disconnected? | At minute | Notes |
|---|---|---|---|---|
| 1 | Repro | | | |
| 2 | Repro uabiRuntime | | | |
| 3 | Isolate | | | |
| 4 | Control | | | |

**Reading the result.**

| Repro | Repro uabiRuntime | Isolate | Means |
|---|---|---|---|
| disconnects | stays | stays | `uabi` is the cause, and the fix works |
| disconnects | disconnects | disconnects | the ability objects themselves are the cause, emptying `uabi` is not enough |
| disconnects | disconnects | stays | something else the tool changed matters, report it |
| stays | stays | stays | the report does not reproduce here, try more games or longer ones |

The report says Repro disconnects "very frequently if not every game". If that holds,
5 of 5 on Repro against 0 of 5 on the fixed copy is already a clear answer, since that
split happens by chance only about 1 time in 250 when the two maps behave the same.

## With wc3ctl

```
wc3ctl repair uabi-runtime map.w3x                          # counts only, changes nothing
wc3ctl repair uabi-runtime map.w3x --apply -o fixed.w3x     # writes the fixed copy
```

The original is never modified. The dry run prints how many unit types and references move
and the distinct id count in `uabi` before and after. Measured on real maps, GGGA goes from
1,257 to 198, and BleachVsOnepiece 17 from 247 to 64. What remains belongs to units that
must keep their list (see below).

What it does, per unit type.
1. Records the `uabi` list, then clears the field in whichever object layer holds it,
   `war3map.w3u` or `war3mapSkin.w3u`.
2. Writes the lists into `war3map.j` as a hashtable keyed by unit type.
3. Registers a "unit enters the map" trigger as the first statement of `main`, before any
   unit exists, which adds the abilities the moment each unit is created.
4. Sweeps every existing unit just before the map's initialization triggers run.
5. Makes each added ability permanent, so it survives a morph.

The result was checked with pjass against the game's own `common.j` and `Blizzard.j`, and
all four basemaps pass `wc3ctl validate` afterwards.

## Without wc3ctl, using `uabi-runtime-header.j` in the World Editor

This is the same fix done by hand. It suits a map with a few dozen unit types, or an author
who wants the fix to live in the triggers, where it survives saving in the World Editor.

### What the file gives you

[`uabi-runtime-header.j`](uabi-runtime-header.j) holds two JASS functions.

| Function | What it does |
|---|---|
| `UabiReg(unitType, ability)` | Remembers that this unit type should get this ability. Call it once per ability, per unit type. |
| `UabiAdd(unit)` | Gives a unit every ability remembered for its type, and makes each one permanent so it survives a morph. |

Both keep their data in a hashtable variable called `UabiTable`, which you create in step 1.
The file is JASS, so it works on a JASS map. A Lua map needs the same logic written in Lua.

### Before you start

Make a copy of the map and work on the copy.

Turn on raw data in the Object Editor with **View, Display Values As Raw Data** (Ctrl+D).
Unit types and abilities then show as four character codes such as `h000` and `A000`, which
is what the script needs.

### Step 1. Create the variable

**Trigger Editor, Variables** (Ctrl+B). Add a variable named `UabiTable` of type
**Hashtable**. The editor stores it as `udg_UabiTable`, which is the name the header uses.

### Step 2. Paste the header

In the **Trigger Editor**, click the map's name at the top of the trigger list. The right side
shows **Custom Script Code**. Paste the whole of `uabi-runtime-header.j` there. Everything in
that box is placed above every trigger, so any trigger can call the two functions.

### Step 3. Write down every ability list

For each unit type whose **Abilities - Normal** is not empty, write down the unit's code and
every ability code in that field. With raw data on, the field reads like `A000,A001,AInv`.

Do this BEFORE step 6. The editor cannot export the lists as text, and once a field is cleared
nothing can read it back.

Leave out the three kinds listed in "Leave these in the Object Editor" below.

### Step 4. Trigger "Uabi Setup"

Create a trigger named `Uabi Setup` and drag it to the TOP of the trigger list. Map
initialization triggers run in list order, and this one has to run before anything that
creates units.

```
Uabi Setup
    Events
        Map initialization
    Conditions
    Actions
        Custom script:   set udg_UabiTable = InitHashtable()
        Custom script:   call UabiReg('h000', 'A000')
        Custom script:   call UabiReg('h000', 'A001')
        Custom script:   call UabiReg('h001', 'A002')
        (one UabiReg line for every ability you wrote down in step 3)
        Unit Group - Pick every unit in (Entire map) and do (Actions)
            Loop - Actions
                Custom script:   call UabiAdd(GetEnumUnit())
```

The `UabiReg` lines fill the table. The pick at the end gives preplaced units their
abilities, because those exist before any trigger runs.

A long list can hit the limit on how much one trigger may do at once, and the trigger then
stops silently partway. Keep each trigger to about 500 `UabiReg` lines. For more, make
`Uabi Setup 2`, `Uabi Setup 3` and so on, with no event, holding the next 500 lines each. Run
them from `Uabi Setup` right after `InitHashtable`, one **Trigger - Run (ignoring
conditions)** action each, and keep the pick at the end of `Uabi Setup`.

### Step 5. Trigger "Uabi Enter"

```
Uabi Enter
    Events
        Unit - A unit enters (Entire map)
    Conditions
    Actions
        Custom script:   call UabiAdd(GetTriggerUnit())
```

This gives every unit created later its abilities, whether trained, summoned, revived or
spawned by triggers. The event fires inside unit creation, so a trigger that creates a unit
and reads its abilities on the next line still finds them.

### Step 6. Clear the fields

In the Object Editor, clear **Abilities - Normal** on every unit type you registered in step 4.
This is the step that removes the ids from the object data, and it is what the fix is for.

### Step 7. Check it

1. Save and test the map (**File, Test Map**, Ctrl+F9).
2. Select a few of the changed units, both preplaced ones and ones made during play, and check
   that their command cards show the same abilities as before.
3. Then run the multiplayer test in "How to test" above. Only that shows whether the
   disconnects stopped.

### How this differs from what wc3ctl writes

The idea is the same. `repair uabi-runtime` writes its own copy of this code into `war3map.j`
instead of reading this file, for four reasons.

- It edits the finished script, where no Variable Editor exists to declare `udg_UabiTable`, so
  it declares its own hashtable.
- Its names all start with `wc3ctl_uabi`, so they cannot clash with a map that already has a
  `UabiAdd`, and it can see it was already applied and refuse to run twice.
- It writes the table directly in chunks of 400, each on its own thread, so thousands of
  entries never reach the per-trigger limit that step 4 works around.
- It starts the enter trigger as the first line of `main`, before any unit exists.

What the manual version has over it is that it survives saving in the World Editor.

### Why the tool exists

The steps are simple and the volume is the problem. A map with 1,400 unit types needs about
3,600 `UabiReg` lines and 1,400 fields cleared by hand, which the tool does in one pass.

## Leave these in the Object Editor

- **Hero Abilities** (`uhab`). They are learned, not granted.
- **Units something morphs into** (Bear Form, Metamorphosis, Chaos targets). A morph does
  not create a unit, so the enter trigger never fires for them. wc3ctl keeps any unit type
  that an ability's data names.
- **Locust** (`Aloc`). It behaves differently when added after creation.

## Caveats

- Reopening a fixed map in the World Editor and saving it regenerates `war3map.j` from the
  triggers, which drops the tool's code. Fix the map as the last step, or use the manual
  method, which lives in the triggers and survives saving.
- A map whose own code needs an ability on a unit before that unit is created would behave
  differently. Play-test before distributing.
- Lua maps are refused. The tool writes JASS.
