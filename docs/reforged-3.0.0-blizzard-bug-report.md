# Warcraft III Reforged 3.0.0 defect report

Build under test **3.0.0.24268**, compared against **2.0.4.23745**.

Every item below was produced by decoding both builds from Blizzard's own shipped TACT
manifests and comparing the data files byte for byte. No third party tool, no community
data, no modified client. Each item names the file, the key and both values, so it can be
checked against a clean install without reproducing any of the work.

Coverage. All 931 text data files in 2.0.4 and all 934 in 3.0.0 were read in full, not
listed, with zero read failures on either side. Every file that lost an entry or changed a
value in this patch was examined.

Evidence labels used throughout.

- **PROVEN** means a byte level comparison of the two builds. Verifiable by anyone.
- **INFERRED** means a conclusion drawn from proven facts but not directly observed.

---

## A. References left dangling by 3.0.0

### A1. Destructable `YTsc` lost its definition but is still referenced, PROVEN

`Units/DestructableSkin.txt` lost the entire `[YTsc]` block in 3.0.0, all 23 keys, including
`Name`, `file`, `file:hd`, `armor`, `deathSnd`, `numVar`, `shadow` and both texture paths.

`Units/DestructableAddons.txt` **still opens a `[YTsc]` section at line 109** in 3.0.0, with
`addon=Environment`, unchanged from 2.0.4. The localized name `WESTRING_DEST_SCORCHED_TREE_WALL`
also survives.

`YTsc` is not a row in `Units/DestructableData.slk` in either build, so
`DestructableSkin.txt` was its only definition. Placing it now yields a destructable with no
model, no name, no armour type and no death sound.

Verification used a control taken from the 3.0.0 file itself. `YTct` is found in all three
files in both builds, so a zero result for `YTsc` means absence rather than a failed search.

| file | 2.0.4 | 3.0.0 |
|---|---|---|
| `Units/DestructableSkin.txt` | `[YTsc]` at line 784, 23 keys | **absent** |
| `Units/DestructableAddons.txt` | `[YTsc]` at line 109 | `[YTsc]` at line 109 |
| `Units/DestructableData.slk` | not a row | not a row |

### A2. Unit `Udrb` lost its definition but is still referenced, PROVEN

Identical shape. `Units/UnitSkin.txt` lost the entire `[Udrb]` block, all 20 keys, which
carried the Tichondrius model and `BTNTichondrius.blp` in SD and the Balnazzar model and
`BTNBalnazzar.blp` in HD, along with both team colour triples and both model scales.

`Units/UnitAddons.txt` **still opens a `[Udrb]` section at line 2629** in 3.0.0. A name string
for it also survives in `Units/UnitSkinStrings.txt` in **all 12 shipped locales**
(deDE, enUS, esES, esMX, frFR, itIT, koKR, plPL, ptBR, ruRU, zhCN, zhTW).

`Udrb` is not a row in `Units/UnitData.slk` in either build, so `UnitSkin.txt` was its only
definition. Control for this search was `Udre`, found in 49 files in both builds.

### A3. Five weather effects name models that are not in the build, PROVEN

`TerrainArt/Weather.slk` in 3.0.0 has five rows whose `modelFile` points at a file that does
not ship anywhere in the build, in any mod layer.

| weather id | modelFile in 3.0.0 | ships |
|---|---|---|
| `MEbs` | `Environment\Weather\EnergyFieldBlue.mdl` | **no** |
| `MEds` | `Environment\Weather\EnergyFieldArcane.mdl` | **no** |
| `MEgs` | `Environment\Weather\EnergyFieldGreen.mdl` | **no** |
| `MEhs` | `Environment\Weather\EnergyFieldHoly.mdl` | **no** |
| `MErs` | `Environment\Weather\EnergyFieldRed.mdl` | **no** |

**2.0.4 had zero weather rows pointing at a missing model**, across all 21 rows, so this is
introduced by 3.0.0. Of the five, only `MEds` existed in 2.0.4, and it carried no `modelFile`
at all rather than a broken one. The other four rows are new in 3.0.0.

The lookup was checked against every file path in the build across all mod layers, with
`Units/Human/Footman/Footman.mdl` as a control resolving true through the same code path.

These ids are reachable from map script through `AddWeatherEffect`.

---

## B. Data entry errors

### B1. `Ocr2` in `AbilityMetaData.slk` has a case broken ability id, PROVEN

`Units/AbilityMetaData.slk` row `Ocr2`, column `useSpecific`, reads `AOcr,ACct,Andb` in
3.0.0. It read `AOcr,ACct,ANdb` in 2.0.4. The third entry lost the capital on its second
character.

This is not a style question. Ability ids are case sensitive four character codes. `ANdb` is
a row in `Units/AbilityData.slk` in both builds. **`Andb` is not a row in either build.**

The five sibling rows make the intent unambiguous, since every one of them still carries the
correct spelling.

| row | `useSpecific` in 2.0.4 | `useSpecific` in 3.0.0 |
|---|---|---|
| `Ocr1` | `AOcr,ACct,ANdb` | `AOcr,ACct,ANdb,AIxr,AIsc` |
| `Ocr2` | `AOcr,ACct,ANdb` | **`AOcr,ACct,Andb`** |
| `Ocr3` | `AOcr,ACct,ANdb` | `AOcr,ACct,ANdb` |
| `Ocr4` | `AOcr,ACct,ANdb` | `AOcr,ACct,ANdb` |
| `Ocr5` | `AOcr,ACct,ANdb` | `AOcr,ACct,ANdb` |

Effect. `useSpecific` is what makes a field appear for an ability in the World Editor, so the
second Data field for ability `ANdb` no longer offers itself where the other four do.

### B2. Two cells were edited past the ability's own declared level count, PROVEN

Both of these are the only balance shaped edits in the entire patch to ability rows that
already existed, and both land on level 4 of an ability that declares 3 levels.

| ability | `levels` | field | 2.0.4 | 3.0.0 |
|---|---|---|---|---|
| `ANbf` Breath of Fire | **3** | `Area4` | 90 | **60** |
| `ANdr` Drunken Haze | **3** | `Cost4` | 30 | **35** |

Either these columns are read past `levels`, in which case this is an undocumented balance
change to two Brewmaster abilities, or they are not, in which case the edit is inert and the
intended change did not land. Both readings are worth checking. Reading the live 3.0.0 client
data, the merged value comes back as the level 3 value rather than the level 4 value, which
suggests the edit is inert, but that is **INFERRED** from one implementation rather than
confirmed against the engine.

---

## C. Internal inconsistencies in the shipped data

### C1. Ability metadata still caps at 4 levels while ability data now declares 6, PROVEN

3.0.0 extended `Units/AbilityData.slk` from 99 columns to 201, adding a complete level 5 and
level 6 column set. The `levels` column now reaches 6.

`Units/AbilityMetaData.slk` was **not** updated to match. Its `repeat` column, which is what
tells a consumer how many levels a field has, still tops out at 4, and no row anywhere sets 5
or 6.

| build | `AbilityMetaData` repeat values | `AbilityData` levels values |
|---|---|---|
| 2.0.4 | 0 on 55 rows, 3 on 4, **4 on 718** | up to 4 |
| 3.0.0 | 0 on 55 rows, 3 on 4, **4 on 901** | **5 on 37 rows, 6 on 3 rows** |

Anything sizing a levelled field from `repeat` allocates four slots and silently truncates
levels 5 and 6 of the 40 abilities that now declare them.

### C2. The UTF-8 BOM was stripped from some files in the same directory and not others, PROVEN

| file | 2.0.4 | 3.0.0 |
|---|---|---|
| `Units/UnitSkin.txt` | BOM | **none** |
| `Doodads/DoodadSkins.txt` | BOM | **none** |
| `Units/DestructableSkin.txt` | BOM | BOM |

Three files of the same kind, edited in the same patch, now disagree on encoding. A reader
decoding as plain UTF-8 keeps the BOM glued to the first section header, so that header stops
matching and every key under it reads as removed. This is a real source of wrong community
analysis of this patch, and it costs nothing to make consistent.

### C3. Path separators are now mixed inside one file, PROVEN

`Units/AbilitySkin.txt` changed exactly two values in this patch, both `Casterart` on the Moon
Glaive abilities, and both new values use forward slashes where every surrounding value in the
same file uses backslashes.

| key | 2.0.4 | 3.0.0 |
|---|---|---|
| `[Amgl] Casterart` | `Abilities\Spells\NightElf\SpiritOfVengeance\SpiritOfVengeanceBirthMissile.mdl` | `Abilities/Spells/NightElf/MoonGlaive/MoonGlaiveCaster.mdl` |
| `[Amgr] Casterart` | `Abilities\Spells\NightElf\SpiritOfVengeance\SpiritOfVengeanceBirthMissile.mdl` | `Abilities/Spells/NightElf/MoonGlaive/MoonGlaiveCaster.mdl` |

### C4. A boolean changed its value form inside one config file, PROVEN

`PostProcessingConfig.txt` previously wrote booleans as `true`. Two of them are now `0`.

| section | key | 2.0.4 | 3.0.0 |
|---|---|---|---|
| `ASSAO` | `Enabled` | `true` | `0` |
| `Bloom` | `Enabled` | `true` | `0` |

Both effects are therefore off by default, which is a visible change to every map, and worth
confirming as intended rather than as a side effect of a format change.

---

## D. Compatibility breaks that affect existing custom maps

These are not necessarily defects. They are the changes most likely to be reported as maps
breaking, listed so the cause is not mistaken for something else.

### D1. An enum value was inserted in the middle of an existing sequence, PROVEN

`Scripts/common.j`.

```
2.0.4                          3.0.0
  6  ITEM_TYPE_MISCELLANEOUS     6  ITEM_TYPE_MISCELLANEOUS
  7  ITEM_TYPE_UNKNOWN           7  ITEM_TYPE_EQUIPMENT   <- inserted
  8  ITEM_TYPE_ANY               8  ITEM_TYPE_UNKNOWN     <- shifted
                                 9  ITEM_TYPE_ANY         <- shifted
```

A map that references the constant by name recompiles correctly. A map that was optimised or
obfuscated, which is standard practice for published custom maps, has the integer inlined and
now passes a value meaning something else. Appending the new value at 9 would have avoided
this entirely.

#### Independently confirmed against a second source

Both D1 and D2 were first measured by diffing the shipped `common.j` out of CASC. They were
then re-measured against `Luashine/jass-history`, whose `lua-dump` folder carries runtime
dumps for `2.0.2.22796` and `3.0.0.24268`, a different extraction route and a different
baseline build.

The two routes agree, and the Lua dump makes the whole surface visible at once. Between
those builds `common.j` loses exactly **three** lines and nothing else.

```
-TypeDefine('framehandle', 'handle')
-ITEM_TYPE_UNKNOWN = ConvertItemType(7)
-ITEM_TYPE_ANY = ConvertItemType(8)
```

The cause of the enum shift is now named rather than inferred. `ITEM_TYPE_EQUIPMENT` was
**inserted at position 7** for the new equipment system, which pushed `ITEM_TYPE_UNKNOWN`
from 7 to 8 and `ITEM_TYPE_ANY` from 8 to 9. Appending it at position 9 would have cost
nothing and broken nothing.

The same cross-check confirms the API is otherwise additive. `Blizzard.j` goes from 985 to
1056 declarations by both routes, with **0 removed**, and `common.ai` is byte identical in
both.

### D2. `framehandle` changed its parent type, PROVEN

`Scripts/common.j`. `type framehandle extends handle` became `type framehandle extends agent`.
`agent` is reference counted, so frame lifetime and leak behaviour change for every map
building custom UI. 51 natives take or return a `framehandle`.

### D3. The MDX format went from 1200 to 1800 and the camera record changed, PROVEN

Sampled across 120 models present in both builds.

| measurement | result |
|---|---|
| `VERS` chunk | **1200 to 1800** on 112 of 112 |
| `CAMS` record leading uint32, top byte | **0x00 to 0x03** on 39 of 39 models with a camera |
| models with identical byte length | 51 of 120 |
| bytes differing in those | 2, the version, or 3, the version plus the camera byte |

On a portrait model of 52,811 bytes the entire file differs in exactly three places.

```
VERS  b0040000 -> 08070000      1200 -> 1800
CAMS  78000000 -> 78000003      the record header gains 0x03 in its top byte
```

All 8,778 `.mdx` files shared by both builds were re-stamped. Every model a custom map
imported is still stamped 800 or 1200 and is now rendered by 1800 camera logic. The camera
node is what the unit portrait renders from, which is where this surfaces.

What `0x03` means is **not established**. It sits where the record size was a plain uint32.

#### The observable consequence, CONFIRMED on a live map

**A custom model that carries a camera renders a BLACK portrait pane in 3.0.0.** The unit
renders correctly in the world and its health and mana read correctly, so only the portrait
window is wrong, which makes it read as a cosmetic oddity rather than a format problem.

Measured on one published custom map carrying 622 distinct model references.

| group | count | portrait |
|---|---|---|
| resolves to a base-game model | 199 | fine, Blizzard re-stamped these |
| custom model in the archive, **no `CAMS` chunk** | about 241 | fine, the engine falls back to its own camera |
| custom model in the archive, **`CAMS` chunk at VERS 800** | **47** | **black** |

The damage is confined to custom models that actually ship a camera, which is why it
presents as a handful of heroes broken while the rest are fine, and why it looks random
rather than systematic. Most ripped models omit the camera entirely and are unaffected.

#### Three attempted remedies, all of which FAILED, which is why no workaround is offered

An earlier draft of this report claimed that re-stamping the three header bytes restores
the portraits. **That claim was wrong and is withdrawn.** It was written from the shape of
Blizzard's own diff rather than from a test, and testing it produced the opposite result
each of three ways.

| attempted remedy | measured outcome |
|---|---|
| set the `CAMS` leading byte to `0x03`, leaving `VERS` at 800 | map loads, portraits **still black** |
| re-stamp `VERS` 800 to 1800 as Blizzard did to its own models | **map crashes on load** |
| add a `<name>_Portrait.mdx` companion carrying a camera | companion **never consulted** |

The `VERS` crash is explained and is not a packaging error. At version 900 and above a
geoset carries an extra level-of-detail block, a uint32 plus an 80 byte name, so stamping
1800 onto a file authored at 800 misaligns every geoset that follows. A model's version is
therefore not a label that can be corrected in place, it selects the parse layout for the
whole file.

The companion test is the informative one, because it used **Blizzard's own** Death Knight
portrait model, already at `VERS` 1800 with a valid camera, installed under both the `.mdx`
and `.mdl` name. The portrait stayed black, so the engine is not falling back to a
`_Portrait` companion for these units at all. Measured on a client running SD graphics.

So the correlation in the table above is measured and the mechanism behind it is **not
established**, and no remedy available to a map author is known. That is precisely why this
needs an answer from Blizzard rather than a community workaround.

**What would resolve this for the community.** Read each model's own `VERS` and apply the
matching camera layout, so an 800 or 1200 model keeps behaving as it did. Failing that,
publish what the `0x03` flag means and ship a converter. There are tens of thousands of
custom models in circulation, and their authors cannot edit a byte inside an encrypted,
protected archive without building a tool first.

### D4. `AbilityData.slk` doubled its column count, PROVEN

99 columns to 201, with 102 columns inserted rather than appended in a single block at the
end. Any consumer reading this table by column index rather than by header name now reads the
wrong field. Documenting the new layout would help the tool authors the map community depends
on.

### D5. `fileVerFlags` value 2 was retired with no stated meaning, PROVEN

In `Units/UnitSkin.txt`, every object using this field moved off value 2 onto value 6. Not one
was left behind.

| build | objects at 0 | objects at 2 | objects at 6 |
|---|---|---|---|
| 2.0.4 | 846 | 17 | 0 |
| 3.0.0 | 906 | **0** | **20** |

A custom skin still setting `fileVerFlags = 2` is now using a value the base game abandoned
entirely. What the engine does with it is not documented anywhere.

### D6. Critical strike became a global mechanic, and every map inherits its constants, PROVEN

This is the largest silent gameplay change in the patch for custom content, and it is not in
the patch notes.

`Units/MiscGame.txt` gained exactly **three** keys, all in `[Misc]`, and two of them are a new
mechanic rather than tuning.

```
+ AICallForHelp      = 900
+ BaseMeleeCritDamage = 150
+ BaseSpellCritDamage = 150
```

`UI/MiscData.txt` `[Misc]` gained the two floating text colours that go with them.

```
+ CriticalStrikeHealTextColor  = 255,0,255,0
+ CriticalStrikeSpellTextColor = 255,0,0,255
```

A separate colour for **heal** crits and for **spell** crits means critical strike now applies
to healing and to spell damage, not only to attacks. `BaseSpellCritDamage` exists for the same
reason. Neither concept existed in 2.0.4.

`Units/AbilityMetaData.slk` row `Ocr1`, the critical strike chance field, had `minVal` moved
from `0` to `-99999` while `maxVal` stayed at `100`, so the field now accepts negative chance.

| where | key | 2.0.4 | 3.0.0 |
|---|---|---|---|
| `Units/MiscGame.txt` `[Misc]` | `BaseMeleeCritDamage` | absent | `150` |
| `Units/MiscGame.txt` `[Misc]` | `BaseSpellCritDamage` | absent | `150` |
| `Units/MiscGame.txt` `[Misc]` | `AICallForHelp` | absent | `900` |
| `UI/MiscData.txt` `[Misc]` | `CriticalStrikeHealTextColor` | absent | `255,0,255,0` |
| `UI/MiscData.txt` `[Misc]` | `CriticalStrikeSpellTextColor` | absent | `255,0,0,255` |
| `Units/AbilityMetaData.slk` `Ocr1` | `minVal` | `0` | `-99999` |

**Why it reaches every map.** A map only overrides `Units/MiscGame.txt` if its author chose
to, which most did not. Every map that does not override it now inherits a 150 base melee
crit multiplier and a 150 base spell crit multiplier that did not exist when the map was
balanced and tested.

**What is NOT the cause, checked so the search does not go there.** The critical strike
ability rows themselves are untouched. `AOcr`, `ACct` and `ANdb` each have **zero** changed
cells across all 99 columns present in both builds. The change is entirely in the global
constants and in the field's allowed range.

### D7. Every field applicability change targets new content only, PROVEN, reported as a non issue

Stated because it looks alarming in a raw diff and is not.

`Units/AbilityMetaData.slk` gained 183 rows and changed 29 cells, and most of those cells are
`useSpecific` or `notSpecific` lists gaining ability codes. That reads like fields being
retrofitted onto existing abilities. **It is not.** Of the 13 distinct ability codes added to
any applicability list, **0 existed in 2.0.4**.

| code added | exists in 2.0.4 | exists in 3.0.0 | added to |
|---|---|---|---|
| `AHap` | no | yes | `Ens1` to `Ens3` |
| `AHes` | no | yes | `Eev1` |
| `AHmc` | no | yes | `Pos1`, `adur` |
| `AHss` | no | yes | `Ssk1` to `Ssk5` |
| `AHsw` | no | yes | `aare`, `adur`, `ahdu` |
| `AIsc` | no | yes | `Ocr1` |
| `AIss` | no | yes | `isr1`, `isr2` |
| `AIvx` | no | yes | `Ivam` |
| `AIxr` | no | yes | `Ocr1` |
| `ATal` | no | yes | `abuf` |
| `AUss` | no | yes | `Uts1` to `Uts3` |
| `AUwc` | no | yes | `Uls1` to `Uls5`, `Ulsu` |
| **`Andb`** | **no** | **no** | `Ocr2` |

No pre-existing ability gained or lost a field, so nothing inherited by an existing custom
ability changed here.

The last row is the defect from B1 seen from the other side. `Andb` is the only code added to
any applicability list in this patch that does not name an ability in either build, which is
independent confirmation that it is a typo for `ANdb` rather than a forward reference.

### D8. Four object tables gained columns, and a unit's model file became three fields, PROVEN

| table | new columns |
|---|---|
| `Units/unitUI.slk` | `forceDisplayHP`, `occlusion`, `showAirToGround` |
| `Units/ItemData.slk` | `equipment`, `tag`, `invulnerable`, `teamColor`, `customTeamColor` |
| `Doodads/Doodads.slk` | `customTeamColor`, `decal`, `decalSort`, `occlusion`, `shadowHD`, `teamColor`, `water` |
| `Units/DestructableData.slk` | `customTeamColor`, `occlusion`, `teamColor` |

Separately, `Units/UnitMetaData.slk` changes `umdl` from `index = -1`, a single scalar, to
`index = 0`, and adds `umd1` at index 1 and `umd2` at index 2. A unit can now carry three model
files where it carried one. That single changed cell reads like a typo on its own, and only
makes sense beside the two new rows.

Two further new unit rows, `uepa` and `uevi`, are equipment preview fields, tying this table to
the `equipment` column on `ItemData.slk` and to the `ITEM_TYPE_EQUIPMENT` insertion in D1.

Documenting the new fields and the `umdl` change would save every third party editor author
from rediscovering it by trial.

### D9. Ten more skin objects now inherit from a different object, PROVEN

In `Units/UnitSkin.txt`, `skinnableID` points a skin at the object it inherits from. The count
of objects pointing somewhere other than themselves went from **54 to 64**, and one
pre-existing redirect changed target.

| key | 2.0.4 | 3.0.0 |
|---|---|---|
| `[Ubal] skinnableID` | `Ubal` | `Udre` |

So Balnazzar's skin now resolves through the Dreadlord rather than through itself. Anything
overriding either object inherits differently as a result.

### D10. An FDF include and two template names were removed, PROVEN

`UI/FrameDef/UI/ObserverSimpleInfoPanel.fdf` no longer does
`IncludeFile "UI\FrameDef\UI\SimpleInfoPanel.fdf"`, and its two string frames stopped
inheriting `SimpleInfoPanelTitleTextTemplate` and `SimpleInfoPanelDescriptionTextTemplate` in
favour of new locally defined `ObserverSimpleInfoPanel` equivalents. Custom UI that relied on
that include chain to have already defined those templates no longer gets them.

Separately, `UI/FrameDef/UI/ProductionPanelUnit.fdf` dropped `LayerStyle "IGNORETRACKEVENTS"`,
so that layer now receives mouse tracking events it previously ignored.

---

## E. The two highest impact items, where the mechanism is not proven here

### E1. Ability requirements naming a tech that no longer exists appear to block the ability

**PROVEN part.** 110 base abilities carry a `Requires=` line naming a tech. Berserk requires
the Berserker Upgrade, and others require Sorceress Training, Unholy Strength and Hardened
Skin. A custom ability built on one of those inherits the requirement unless the author clears
it. Blizzard changed none of this in 3.0.0. `Units/UpgradeData.slk` is **byte identical**
between the two builds, 36,769 bytes and 90 upgrades, and `[Absk] Requires=Robk` is byte
identical. Searching the entire patch for `Absk`, `Robk` or `berserk` returns zero hits.

**INFERRED part, and it is the load bearing one.** Before 3.0.0 a requirement naming a tech
that does not exist in the map appears to have been ignored. It now appears to count as unmet,
which locks the ability. This cannot be observed directly without running the old build, so it
is stated as inference rather than as fact.

**What players are told, which is the clearest sign this is the mechanism.** The blocked
ability reports that it requires the **Berserker Upgrade**, by name, on a hero in a map that
has no orc tech tree and no upgrade system at all. That string can only come from `Robk`
being evaluated as an unmet requirement. On the map measured below, every ability players
reported as locked resolves to `Requirements = Robk` inherited from `Absk`, and clearing that
inheritance is the only change that addresses it.

**Supporting sign from your own data.** The single edit 3.0.0 makes to
`Units/UndeadAbilityFunc.txt` is the removal of `[Aap2] Requires = Rupc`, which is exactly this
class of dangling requirement being cleaned up.

**Why it matters.** Many published custom maps empty `Units/UpgradeData.slk`, because a hero
arena has no use for the upgrade system. Every custom ability derived from a requirement
bearing base then locks at once, so it reads to players as most of a map's heroes breaking
simultaneously rather than as one ability failing.

Measured on one 897 ability map. 67 of its abilities derive from a requirement bearing base,
spread across 10 base abilities and 8 distinct required techs.

| base | required tech | abilities |
|---|---|---|
| `Absk` Berserk | `Robk` Berserker Upgrade | 21 |
| `Aivs` Invisibility | `Rhst` | 20 |
| `Acri` Critical Strike | `Rune` | 9 |
| `Assk` Hardened Skin | `Rehs` | 7 |
| `Ainf` Infernal | `Rhpt` | 4 |
| `Auhf` Unholy Frenzy | `Rune` | 2 |
| `ANen`, `Aihn`, `Alsh`, `Arej` | `Rnen`, `Rhpm`, `Rost`, `Redc` | 1 each |

Of those 67, the author had already written an explicit empty requirement on 39 and a
deliberate requirement of their own on 8, which leaves **20 that inherit**. All 20 inherit
`Robk`, and all 20 are exactly the abilities players report as locked. The ones players named
first are Hurricane, Release Eye Patch and Rinnegan, each of which is built on Berserk.

If this behaviour change was deliberate, saying so publicly would let map authors fix it in an
afternoon. If it was not, it is the single most impactful regression in the patch for custom
content.

---

### E2. Maps storing object data in SLK tables crash on first unit spawn, with no error

**REPORTED by the community, prevalence MEASURED here.** Maps built with the older Chinese
and Korean editors (KKWE, YDWE) keep object data in SLK tables rather than in the binary
object files the World Editor writes. 3.0.0 reads fields in those tables that it previously
ignored, and such a map crashes the client when the first unit spawns. **No error message is
shown.**

The field that actually crashes is the `file` column in `Units/UnitUI.slk` and
`Units/ItemData.slk`, which now has to be moved into a per object section in `UnitSkin.txt`
and `ItemSkin.txt`. Five repairs are documented by the community at
`forum.wc3edit.net/viewtopic.php?t=39987`.

**MEASURED.** Across 256 real published custom maps, 3 carry this defect and 253 do not. So
the affected population is small, but for those maps the game is unplayable rather than
degraded.

**The reportable part is the failure mode rather than the cause.** A malformed or unexpected
value in a map's own SLK override should raise a load error naming the file and the field, not
crash the client silently at spawn time. Whatever 3.0.0 now reads there, failing loudly would
have made this self diagnosing for every affected author.

---

## What was checked and found clean

Stated so the list above is read as complete rather than as a sample.

- **No JASS native was removed and none changed signature.** `common.j` went from 1,544
  declarations to 1,681, `Blizzard.j` from 985 to 1,056, with zero removals and zero signature
  changes in either.
- **No JASS constant was removed**, and only the two itemtype values in D1 changed value.
- **`Scripts/common.ai` is unchanged**, 120 functions with zero body changes and 457 globals
  with zero changes.
- **Only five existing `Blizzard.j` function bodies changed**, four of them additive campaign
  plumbing. No global changed value.
- **No GUI trigger function was removed.** `UI/TriggerData.txt` went from 9,973 keys to
  10,891, with 918 added, 59 changed and zero removed.
- **No base unit was rebalanced.** `UnitBalance.slk`, `UnitWeapons.slk`, `UnitData.slk` and
  `UnitAbilities.slk` each gained the same 64 new units, and between all four exactly one cell
  changed.
- **No command button icon was removed.** The base game went from 3,097 icon stems to 3,969.
- **No rawcode collisions.** 3.0.0 added 709 abilities, 64 units, 365 items and 203 doodads,
  and on a map carrying 869 custom ability ids there were zero collisions.
