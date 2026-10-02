# Bleach VS One Piece, full ability audit

Every hero ability checked against the map's own specification. 109 hero units, 88 distinct
hero names, 348 distinct learnable abilities out of 897 in the map.

Run against `BleachVsOnepiece16-levelfill.w3x`, sha `9E95D189`.

## How every hero was tested without running the game

The map documents itself. The author wrote the cooldown into the extended tooltip 1937
times, plus mana cost, cast range, area of effect, casting time and buff duration, per
level, by hand. That is a specification, and the object data either matches it or does not.

Checking one against the other tests the whole roster at once. It caught 12 wrong cooldowns
that a carry-forward fill had guessed, and then caught one that the fill and the tooltip
both got wrong.

### What this method can and cannot see

It **can** see a cooldown, mana cost, cast range, area or duration that disagrees with what
the ability promises the player. A level carrying no data. An ability nothing implements. A
requirement the player can never meet.

It **cannot** see whether a trigger's damage formula is right, whether a status effect
actually applies, or anything about timing and interaction. Nothing here proves an ability
works. It proves only that a specific claim is, or is not, contradicted by the data.

## Results

| check | result |
|---|---|
| abilities on a hero bar but missing from the map | **0** |
| levels declared but carrying no data at all | **0** |
| 6-level abilities incomplete at level 6 | **0**, was 7 |
| requirements the player cannot meet | **0**, was 20 |
| tooltip promises a duration while the data holds zero | **0** |
| active spells with no reference in the trigger script | **0** |
| abilities referenced nowhere in the archive at all | **1** |
| tooltip states a number the data contradicts | **25** under the rule in force then, since corrected. Re-measured, the current rule warns **0** times on the untouched v15 and **0** on the shipped 16.1, see below |

### The one ability nothing implements

`A0JM`, "Shikai", on Captain of the 13th Division, slot R. Base `AIpv`, no data fields set,
and the rawcode appears nowhere in `war3map.j`, `war3mapSkin.txt` or `war3map.w3i`, as a
four character literal or as its decimal form. Two control codes were searched the same way
and both were found, so this is absence rather than a failed search.

This is **pre-existing**, present identically in the untouched v15. It is a map design gap
rather than a patch regression, and it is not repaired here because nothing in the map says
what Shikai is supposed to do.

### Tooltips that disagree with the data

These do not stop an ability working. The engine uses the data and the player is told the
tooltip, so the ability is simply not what it advertises. Left as the author had them,
because changing behaviour to match prose is a design decision.

| ability | hero | what the tooltip says | what the data does |
|---|---|---|---|
| A09K | Aokiji [Q] | cooldown 14 down to 9 | 10 down to 7.5 |
| A081 | Captain of 11th Division [Q] | cooldown 15 at level 6 | 10 |
| A0KT | Substitute Shinigami [D] | cooldown 145 flat | 140 down to 125 |
| A0M8 | Hero of the Marines [W] | area 200 to 400 | `aare` is 0.1 |
| A0DK | Red-Haired Pirates [E] | area 500 to 900 | 340 flat |
| A0ES | The Second Hokage [E] | area 350 | 100 flat |

The three area rows are **no longer reported as warnings**, and the reason is recorded under
"Three corrections to the audit's own rules" below.

Two of them were never radius claims at all. Both lines read "Area of Damage", which the current
rule does not match. On `A0DK` the number on that line belongs to a field the base ability calls
"AOE Damage", and on `A0M8` the author's own prose puts the radius at 300 in the sentence above
it. `A0ES` does say "Area of Effect", promising 350 against an `aare` of 100, and the run prints
it as a note because the trigger script names the ability. In this map the script owns the real
radius for most spells, so `aare` in object data may not be what the spell actually uses, which
is the kind of claim this method cannot settle and now says so rather than warning.

### A false alarm worth recording

41 cooldown claims first appeared as "the tooltip promises a value the ability has no data
for". All 41 resolved correctly once the **inherited** base ability cooldown was taken into
account. An ability that overrides nothing uses its base's value, and in every one of those
41 cases the base value equals the promise exactly.

Reporting those as defects would have been a measurement attached to the wrong cause.

## What was repaired, and the evidence for each value

### Requirements, 20 abilities

Custom abilities never set a requirement. They inherit it from their base. `Absk`, Berserk,
carries `Requires=Robk` in `Units/OrcAbilityFunc.txt`, and `Robk` is the Berserker Upgrade.
That data is unchanged between 2.0.4 and 3.0.0, so enforcement changed rather than data.

All 20 affected abilities are built on `Absk`, including Hurricane, Release Eye Patch and
Rinnegan. Each now carries an explicit empty requirement at **level 0**, which is where the
engine reads a whole-object field.

The map's other 39 requirements were left alone. They are the map's form gating, where base
unit ids are redefined as invisible markers, `oshm` as Bankai, `okod` as Non-Bankai
Condition, `opeo` as 100% Quincy Power.

### Level 6 completion, 7 abilities

| ability | field | value | source |
|---|---|---|---|
| A0NU Blue Rose Sword | `nca1` | 0.80 | tooltip says 80%, and tooltip equals data at all 5 other levels |
| A01M Senbonzakura | `acdn` | 8.0 | tooltip says cooldown 8 seconds |
| A0I9 Bushogoma | `acdn` | 8.0 | tooltip says cooldown 8 seconds |
| A055, A05P | `Nbf5` | 0.0 | constant across all 5 levels |
| A0K7 Tailed Beast Rasengan | `Ivs1` | 5.0 | constant across all 5 levels |
| A0AK | `Wrs1` | 1100.0 | **inferred** from 600/700/800/900/1000, no tooltip to check |

A0AK is the only number in the build not read from the author.

### Engine values, 136 fills on 59 abilities

Cast range, cooldown, mana cost, area, stun duration and casting time, filled by carrying
the nearest defined level forward, which reproduces the clamping the engine used to do.

Per level Data fields were deliberately excluded, so Distributed Damage Factor and the
damage fields are untouched.

Where the author had stated the real number, theirs was used instead, correcting 12 of the
carried values.

### One correction to a repair

`A09K` was filled at level 1 from its tooltip, giving 14, which made level 1 a slower
cooldown than level 2 at 9.5. That ability's tooltip disagrees with its data at every level,
so it is not a usable source for it. Corrected to 10.0, continuing the data's own uniform
half second step. The series now reads 10, 9.5, 9, 8.5, 8, 7.5.

A scan for other fills that break their series direction returned **0**, and would not have
caught this one either, since 14 then 9.5 is still decreasing. The tooltip comparison is
what caught it.

## The trigger side, and why it is not the cause

The obvious suspicion for "the data is right and the ability still misbehaves" is that a
JASS native changed. `Luashine/jass-history` is the usual reference, but its newest tag is
`Reforged-v1.32.10.19202`, which is long before either build here, so it cannot answer it.
Both builds are reachable through CASC, so the shipped scripts were diffed directly.

| script | added | removed | signature changed |
|---|---|---|---|
| `common.j` | 137 natives | **0** | **0** |
| `Blizzard.j` | 71 functions | **0** | **0** |
| `common.ai` | byte identical | | |

The API is fully backward compatible. No trigger in this map can be failing because a
function vanished or changed shape. That is a clean negative and it rules out a whole class
of explanation.

Two constants moved, an enum inserted mid-sequence. `ITEM_TYPE_UNKNOWN` 7 to 8 and
`ITEM_TYPE_ANY` 8 to 9. Exposure in this map is negligible. Of 290 apparent `GetItemType`
occurrences, 289 are `GetItemTypeId(`, the rawcode lookup, which the enum does not touch.
One real `GetItemType(` call and six constant references remain, and constants resolve at
runtime so they stay self consistent.

## Two corrections to this document's own earlier numbers

A first version of the drift sweep reported `AIs2 DataA1` changing 0.2 to 0 across six
abilities. **That was wrong and is withdrawn.** One SLK column is claimed by many field
codes, `DataA1` alone belongs to `nca1`, `Htb1`, `Efk1`, `Ivs1`, `Wrs1` and `Osh1`, each
applying to the base abilities named in its `useSpecific`. The sweep kept one code per
column, so it looked up the wrong field on the map object. Resolving the code per base fixes
it, and the corrected sweep finds `ANbf` and nothing else.

The corrected sweep was then run against the untouched v15 as a control. It reports **9**
there, exactly the `ANbf` set, and **0** against the repaired build. A sweep that reports
zero without a control has told you about the sweep, not the map.

## Inherited drift, the defect no map-only check can see

If the API is intact and the map is untouched, what is left is inheritance. A custom ability
stores only what it overrides, so one changed cell in a base ability silently changes every
custom ability built on it, while the map's own data looks perfect throughout.

Of the 58 base abilities this hero kit inherits from, exactly two changed a behavioural cell
in 3.0.0.

| base | column | 2.0.4 | 3.0.0 | effect |
|---|---|---|---|---|
| `ANbf` Breath of Fire | `Area4` | 90 | **60** | 8 abilities read level 4 and lose a third of its radius, 1 does not (below) |
| `ANdr` | `Cost4` | 30 | 35 | none, the one ability using it sets its own cost |

The nine affected are Kendo Slash on Captain of 11th Division [D], El Directo on Hollowfist
Fighter [Q], Hissatsu Kaen Boshi on Soge King [Q], Swing Arm on Navigator of Strawhat
Pirates [Q], Desert Spada on Leader of Baroque Works [Q], Ice Nova on Captain of 10th
Division [W], and three not on a learnable bar.

Each now carries an explicit `aare` of 90 at level 4, pinning the value the map was balanced
against. Two further abilities on the same base already set their own area and were left
alone.

### Correcting this document's third number, the ANbf reach

An earlier version of the row above said nine abilities lose a third of their level 4 radius.
**That was stated more confidently than the evidence supported, and the precise version is
below.** `ANbf` itself declares `alev` of 3, so its own `Area4` cell sits past its declared
level count and the engine never reads it on the base. Whether it reaches anything at all
depends entirely on how many levels the CHILD declares, which the first claim never checked.

Measured across the 11 abilities built on `ANbf`, in both the untouched v15 and the shipped
build.

| what | how many | why it matters |
|---|---|---|
| set their own `aare` at every level | 2 (`A0FK`, `A0JR`, both at 1.0) | trigger owned, correctly left alone |
| inherit `aare` and declare 5 or 6 levels | 8 | level 4 exists, so they DO read the base cell and the 90 to 60 change is real |
| inherit `aare` and declare 1 level | 1 (`A04L`) | level 4 does not exist for it, so both the 3.0.0 change and the pin are inert |

So the pin is correct and load bearing on 8 abilities, and on `A04L` it is a write past the
declared level count that the engine will never read. It is harmless, it costs one float, and
it is recorded here rather than quietly left, because a write past the declared count is the
exact class of mistake that put 20 requirement writes at level 1 earlier in this repair.

## Tooltips corrected so the game stops lying, 11 edits

Three abilities stated a cooldown the engine does not use, at every level. In each the data
is a clean progression and the prose is flat or stale, which is what rebalancing the numbers
and not revisiting the text looks like. The engine uses the data, so the data is the
behaviour and the sentence was simply wrong.

| ability | hero | tooltip said | now says |
|---|---|---|---|
| A09K | Aokiji [Q] | 14 down to 9 | 10 down to 7.5 |
| A081 | Captain of 11th Division [Q] | 15 at level 6 | 10 |
| A0KT | Substitute Shinigami [D] | 145 flat | 140 down to 125 |

No gameplay changed. Only the digits inside the Cooldown phrase were rewritten, and that was
verified by extracting every other number from both strings and comparing, 11 of 11 clean.

The alternative, changing the data to match the prose, would rebalance three heroes on the
strength of a sentence. That is a design decision, not a repair.

## A deliberate idiom that was nearly "fixed"

Three abilities state an area of effect far larger than their data holds, `A0M8` promising
200 to 400 against an `aare` of 0.1, `A0DK` promising 500 to 900 against 340, `A0ES`
promising 350 against 100.

Raising them would have been wrong. Across the map, 104 abilities set an area of 1.0 or
less and **97 of those are trigger driven**, with the values clustering on exactly three
constants, 1.0 used 88 times, 0.0 used 8 and 0.1 used 8. That is the author neutralising the
base ability's built-in area so the trigger owns the real radius. All three flagged
abilities are trigger driven, so raising their area would add damage nothing intended.

They are correct as they stand, and the audit now encodes that instead of leaving it to the
reader. An "Area of Damage" line is not read as a radius at all, and a claim on an ability the
trigger script names is reported as a note. The count of 15 was measured under the earlier rule
and does not describe the current one.

## Still unresolved, the black portrait

Three remedies were tried and all three failed. Setting the `CAMS` flag byte changed
nothing, re-stamping `VERS` 800 to 1800 crashed the map, and a `_Portrait` companion was
never consulted, even when the companion was Blizzard's own working `VERS` 1800 Death Knight
portrait model installed under both the `.mdx` and `.mdl` name.

The affected set is now measured exactly, by resolving each of the 109 hero units' model
files out of the archive and reading their headers.

| group | hero units | portrait |
|---|---|---|
| model ships a `CAMS` camera at `VERS` 800 | **68** | black |
| model has no camera | 29 | fine, the engine uses its own |
| model path does not resolve in the archive | 12 | falls back to a base game model |

That is why it presents as some heroes and not others. Kirito, Luffy, Nagato, Madara,
Itachi, Ukitake, Shunsui, Law, Garp, Shanks, Robin, Nami, Brook, Usopp and Kakashi are all
in the affected group. Every one of the 29 working heroes ships a model with no camera at
all.

So the correlation is confirmed at hero granularity, and the mechanism behind it is still not
established.

### A fourth attempt, derived from the working cases rather than the broken ones

The three failed attempts all tried to make the camera work. The measurement nobody had acted
on is the opposite one. The 29 hero units whose models carry **no** camera all render
correctly, because the engine falls back to its own.

So the fourth attempt deletes the `CAMS` chunk from the 47 affected model files, to make them
behave like the 29 that already work. MDX is a flat chunk list, `MDLX` then tag plus uint32
size plus body, so removal is an exact splice with no offset to repair, and it does not touch
the geoset layout that made the `VERS` re-stamp crash.

All 47 rewrote cleanly, all 47 re-parse with their chunk list equal to the original minus
`CAMS`, and the map's object data is byte identical, 897 of 897 abilities unchanged.

This ships as `BleachVsOnepiece16-portraittest.w3x`, deliberately **not** as the main build,
because it is an untested hypothesis and a structural model edit is exactly what crashed the
map before. It is the only remedy of the four that follows from something measured working
rather than from the shape of Blizzard's diff.

## Not repaired, and why

`A0JM` "Shikai" on Captain of the 13th Division [R] is implemented nowhere in the archive.
Repairing it means writing new trigger behaviour into a 4.5 MB script with no statement
anywhere of what the ability is meant to do. That needs the author's intent, not a guess.

## What the audit covers, and what it structurally cannot

The question this section answers is "how can we be certain it covers everything", and the
honest answer is that it does not cover everything. Below is the measured boundary, taken
from the shipped build rather than from the design. All counts are from `BVO16g.w3x`.

### The population, and how much of it is opened

| object kind | count | what the audit does with it |
|---|---|---|
| ability | 897 | every check |
| unit | 654 | `portrait-risk` reads the model header, `dangling-reference` reads `uhab` and `uabi` |
| item | 327 | `dangling-reference` reads `iabi`, and it supplies ability ownership |
| buff | 241 | id only, as a resolution target |
| destructable | 8 | id only |
| doodad | 12 | id only |
| upgrade | 0 | none exist |

The summary line prints "897 abilities checked". That is accurate about abilities and it is
**42%** of the 2,139 objects in the map, so read it as what it says rather than as coverage.
Buffs, destructables and doodads have no check of their own, and nothing verifies a unit's
own statistics.

### Tooltip claims, strong inside a narrow table

488 abilities carry 2,082 authored tooltip levels. Within the four fields the claim table
knows, coverage is high.

| claim | stated in tooltips | compared against data | skipped because the field is inherited |
|---|---|---|---|
| cooldown `acdn` | 1,909 | 1,866 | 43 |
| mana cost `amcs` | 15 | 15 | 0 |
| cast range `aran` | 11 | 11 | 0 |
| area of effect `aare` | 20 | 20 | 0 |
| total | 1,955 | 1,912 (**98%**) | 43 |

Outside those four the tooltips make claims nothing compares. Counted by tooltip levels that
state one, damage appears 1,876 times, a percentage 375, a duration 343, a stun 226, a chance
146, a slow 143 and a heal 21. Those are the user visible promises the audit reads straight
past.

### Damage is not checkable from this map, and that was measured rather than assumed

The natural next check is to compare a tooltip's damage against the trigger that deals it.
That was investigated and it does not work here, for reasons specific to how this map is
built.

The script is 4.5 MB, GUI authored and name obfuscated, so every damage call is the BJ
wrapper rather than the native. There are **353** `UnitDamageTargetBJ` call sites and zero
bare `UnitDamageTarget` ones.

| the amount argument is | sites | share |
|---|---|---|
| computed from a variable or an ability level at runtime | 304 | 86% |
| a plain number | 46 | 13% |
| another expression | 3 | 1% |

Attribution fails independently of that. Grouping by trigger rather than by function, which
is the correct grouping because a GUI trigger compiles the ability test into the condition
and the damage into the action, **107** triggers contain a damage call, 63 name at least one
rawcode anywhere, and only **2** name exactly one. A further 230 damage carrying functions
are not wired to any trigger at all, so reaching them needs a call graph.

So a static damage check would resolve two abilities out of roughly 490. Comparing damage on
this map needs a JASS interpreter, not a better pattern. Saying so is the finding.

### One trap worth recording for anyone analysing this script

`war3map.j` here uses **CR only** line endings, 138,158 carriage returns against 66 line
feeds. Any regular expression anchored with `^` or `$` under multiline mode matches nothing
and returns a clean zero rather than an error. A first pass at the damage analysis reported
zero functions parsed for exactly this reason, and it was caught only because the script
asserted its own function count before using it.

### What was added, and what it found

`dangling-reference` is new. It walks every rawcode list that names another object, `abuf`
and `aeff` on abilities, `uhab` and `uabi` on units, `iabi` on items, and reports any target
that neither the map nor the installed game defines. This is the shape behind "the spell
casts and nothing happens", because an ability whose buff id resolves nowhere applies no
buff and reports no error.

Both halves of the lookup are required. Checking only the map reports 284 false positives on
this map, since `Bfro` is simply Frost Armor. Checking only the game reports every custom
object the map defines.

On the shipped build it examined **3,726** references, 2,271 resolved by the map and 1,455 by
the installed game, and found **0** dangling. That population count is printed on every run
on purpose, because a check that has only been seen returning nothing has reported on the
check rather than on the map.

### The level-gap check, and the two mistakes made sharpening it

`level-gap` originally reported every field with a partial level series, which was 333
warnings on the shipped build against 83 from every other check combined. Noise at that
ratio teaches a reader to skip the check, so it was sharpened twice and both attempts were
wrong before the third worked.

The first attempt resolved each gap against the base ability and raised the severity to
error. It cut 333 to 33, and then measurement showed most of the 33 were false. More than 20
shared one shape, `Osh2` absent at level 1, and across the 35 abilities that set `Osh2`, 26
start their series at level 2 with a flat 99999 sentinel and the field is written as an
explicit zero exactly ONCE in the entire map. Absence there is how this author writes "off",
not an omission. Data fields are now excluded and the severity is back to warning, so the
exit code stays owned by `requirement` and `dangling-reference`.

The second attempt then reported **0** on both maps, including the untouched v15 that the
repair had filled 136 engine field gaps in. Two facts that cannot both be about the same
thing, so it was controlled rather than believed. Counting engine field gaps straight out of
the archive gives **129** on v15 and **71** on the shipped build, against the CLI's 0. The
cause was that the merged view emits a bare `acdn` alongside `acdn:1` through `acdn:N`, the
bare one being a convenience duplicate of level 1 rather than a fallback, and accepting it
made every level resolve. A check that cannot fire reports a clean result.

Corrected, the two routes reconcile. 129 raw on v15, of which the base genuinely supplies
30, leaving **99** reported. 71 raw on the shipped build, base supplies 9, leaving **62**.

Both figures have since fallen, and the reconciliation above is kept only as the history of how
the check was sharpened. The code now requires a non-zero value at a level BELOW the hole before
it calls a gap a cliff, which those counts predate. Re-measured on 2026-09-19 through
`dist\wc3ctl.exe`, `level-gap` reports **37** on the untouched v15 and **0** on the shipped
`BleachVsOnepiece16.1.w3x`.

### Three corrections to the audit's own rules

All three live in `src/Wc3.Commands/AuditCommand.cs`. Every number below was measured on
2026-09-19 by running `dist\wc3ctl.exe audit` against two maps, the untouched
`BleachVsOnepiece15.w3x` and the shipped `BleachVsOnepiece16.1.w3x`.

| map | errors | warnings |
|---|---|---|
| untouched v15 | 20 | 218 |
| shipped 16.1 | **0** | **1** |

The one warning left on the shipped build is `A0CF`, a dead item active on Shiva's Guard, which
needs the author's decision rather than a repair.

#### "Area of Damage" is not a radius, so `tooltip-claim` stopped reading it as one

The area pattern matched any "Area of" line against `aare`. It now matches **"Area of Effect"
only**.

Two independent proofs, both read out of v15 with `object get`. `A0DK` "Combat Area" sits on
base `AHtc`, whose own tooltip calls it Thunder Clap. The field carrying exactly the tooltip's
500, 600, 700, 800, 900 is `Htc1`, named "AOE Damage", while `aare` is a flat 340 at all five
levels. `A0M8` "Metal Punch" states "damage to all enemies within 300 AoE" in the same sentence
whose next line reads "Area of Damage 200", so the author's own prose puts the radius at a
number the line never mentions, and its `aare` is `0.1` at every level.

In both the author means the damage dealt inside the area. The number was never a radius, so
comparing it against `aare` compared two different quantities and called the difference a
defect. `tooltip-claim` now reports **0** warnings on both maps.

#### A claim on an ability the trigger script names is a note, not a warning

A script that names an ability can set any of its values at runtime, so the stored field is not
what the player gets and the tooltip is not contradicted by it. `tooltip-claim` now reports such
a claim as a note. Measured, it sets aside **16** claims on **5** abilities on v15 and **6** on
**2** on the shipped build.

`A0ES` "Water Style: Water Wall" is one of them, promising an area of effect of 350 against an
`aare` of 100 at every level, which the shipped build's run prints as a note in those words. It
is the idiom recorded above, the author neutralising the base so a trigger owns the radius.
Writing the tooltip's number into that field would apply the area twice.

It is reported rather than dropped, because the other reading is a stale tooltip and only the
author can tell the two apart.

The same `NamedInScript` helper now backs `orphan-ability`, which had carried its own copy of
the byte-order logic. One helper and two callers, so the byte-order lesson recorded above cannot
decay in one of them while holding in the other.

#### `level-tooltip` now reports the condition a player can observe

The old rule warned when the highest authored tooltip level disagreed with the declared level
count, which is a statement about the data rather than about the game. That condition is still
counted and now prints as a note, **38** abilities on v15 and the same 38 on the shipped build,
and every one of them is an ability whose reachable levels all carry their own tooltip, so no
player can observe any of it.

The new rule warns when a **reachable** level, 1 to the declared count, has no authored tooltip
while another reachable level does. That level falls through to the base ability's own text, so
the command card on a custom spell reads "Inner Fire" or "War Stomp". Tooltip text **above** the
declared count is unreachable, so it is a note rather than a warning.

The reframe made the check strictly stronger rather than weaker, and it proved that immediately
by finding two real defects the old rule was blind to. Both are on v15 and both are fixed in the
shipped build.

| ability | declares | authors a tooltip for | what the player saw |
|---|---|---|---|
| `A0DP` "Fast Stun", base `AOws` | 5 | 4, 5 | levels 1, 2 and 3 read Blizzard's own "War Stomp" |
| `A0E5` | 5 | 1, 2, 3 | levels 4 and 5 read the base ability's text |

`A0DP` is the sharper of the two. Its highest authored level is 5 and it declares 5, which is
exactly the equality the old rule tested, so the old rule was silent on an ability whose first
three levels are visibly Blizzard's.

### How the checks are known to work

**Twenty-seven** tests in `tests/Wc3.Tests/AuditCommandTests.cs`, counted in the file, each
check with a case watched firing and a case watched silent. The three corrections above brought
their own, including one that pins "Area of Effect" as a claim and one that pins "Area of
Damage" as not a claim, since without the first the second would prove only that the check had
stopped working.

On top of that, `sabotage_audit.py` reintroduces one real shipped defect per case, asserts the
naming test goes red, and restores the file. It is not checked into this repository, so its
tally is not reproducible from the tree and is not quoted here.

One of its earlier cases only became honest after being watched. The bare-key sabotage stayed
GREEN, because the test aimed at it used a base ability nothing defines, so the code returned
before reaching the line under sabotage. A test that structurally excludes the failure is a comment,
so a second case was added against a real base, `AHbz`, which carries three levels, and it
went red correctly.

The suite level gate is two sided as well, and both sides were re-run on 2026-09-19 through
`dist\wc3ctl.exe`. The untouched `BleachVsOnepiece15.w3x` **FAILS**, 20 errors and 218 warnings,
exit code 2. The shipped `BleachVsOnepiece16.1.w3x` is **OK**, 0 errors and 1 warning. A gate
that has only been seen passing proves nothing.

The warning counts this paragraph used to carry, 192 and 145, predate the corrections above and
no longer describe a run. The error side is unchanged by them, because all three touch warning
level checks and the exit code is owned by `requirement` and `dangling-reference`.
