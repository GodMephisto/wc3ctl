# wc3ctl command reference

Generated from the live command tree by `wc3ctl docs cli`. Do not edit by hand. Regenerate with `wc3ctl docs cli --force`, and the CI guard fails when this file is stale.

The tree holds 64 top level verb(s) and 318 command path(s) in total.

## Global options

Present on every command.

- `--json`. Emit machine-readable JSON.
- `--game-dir <game-dir>`. Warcraft III install directory (overrides auto-detection).

Every command also accepts `-h` and `--help`, and the root accepts `--version`.

## replay

Read recorded games (.w3g) and list each one's map, length, and how every player left, flagging games where someone disconnected. Read-only.

Usage

```
wc3ctl replay [<path>] [options]
```

Arguments

- `[<path>]` (optional). A .w3g file or a folder searched recursively. Default: every Battle.net account's replays under Documents\Warcraft III\BattleNet.

Options

- `--map <map>`. Only games whose map path contains this text.

Subcommands

- `diff`. Compare two recorded games. Reports same map or not, length delta, and each player's leave delta.
- `players`. Per-player table for one recorded game. Columns are name, slot, team, race, result, leave time and leave reason.
- `timeline`. Ordered event list for one recorded game. Reports game start, each chat line and player leave with its reason, and the game end.
- `find-map`. The map path a replay records, and whether that map file exists in the Maps folder, resolving the .w3x or .w3m.
- `stats`. Aggregate stats for one recorded game. Reports duration, player count, actions per minute per player if the record carries them, and the disconnect count.

### replay diff

Compare two recorded games. Reports same map or not, length delta, and each player's leave delta.

Usage

```
wc3ctl replay diff <a> <b>
```

Arguments

- `<a>` (required). Path to the first .w3g recorded game.
- `<b>` (required). Path to the second .w3g recorded game.

### replay players

Per-player table for one recorded game. Columns are name, slot, team, race, result, leave time and leave reason.

Usage

```
wc3ctl replay players <replay>
```

Arguments

- `<replay>` (required). Path to a .w3g recorded game.

### replay timeline

Ordered event list for one recorded game. Reports game start, each chat line and player leave with its reason, and the game end.

Usage

```
wc3ctl replay timeline <replay>
```

Arguments

- `<replay>` (required). Path to a .w3g recorded game.

### replay find-map

The map path a replay records, and whether that map file exists in the Maps folder, resolving the .w3x or .w3m.

Usage

```
wc3ctl replay find-map <replay> [options]
```

Arguments

- `<replay>` (required). Path to a .w3g recorded game.

Options

- `--maps <maps>`. Folder to search for the recorded map. Default Documents\Warcraft III\Maps.

### replay stats

Aggregate stats for one recorded game. Reports duration, player count, actions per minute per player if the record carries them, and the disconnect count.

Usage

```
wc3ctl replay stats <replay>
```

Arguments

- `<replay>` (required). Path to a .w3g recorded game.

## uabi-profile

Measure every map's unit ability lists (uabi) side by side, without starting the game. Counts, distinct ids, hero and item abilities in normal lists, dangling ids, duplicates, and a per-race breakdown. Read-only.

Usage

```
wc3ctl uabi-profile <paths...>
```

Arguments

- `<paths...>` (required). Maps, or folders searched recursively.

## doctor

Run Devo's Map Doctor (MIT, github.com/devoltzz/devos-map-doctor) on a map. Found through WC3_DOCTOR or %LOCALAPPDATA%\wc3ctl\doctor\<version>\, and only a pinned build runs.

Usage

```
wc3ctl doctor
```

Subcommands

- `check`. Will the map run on Reforged 3.0, and what stops it. Read-only, never starts the game.
- `inspect`. A read-only doctor report: protection and editor needs (diag), files, card, rawcodes or recipes.
- `compare`. What differs between two versions of a map, with names recovered on protected archives.
- `fix`. Repair a COPY of the map for Reforged 3.0 with the doctor's Fix map. The original is never written.

### doctor check

Will the map run on Reforged 3.0, and what stops it. Read-only, never starts the game.

Usage

```
wc3ctl doctor check <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--game <game>`. Check an older game instead, such as 1.27.

### doctor inspect

A read-only doctor report: protection and editor needs (diag), files, card, rawcodes or recipes.

Usage

```
wc3ctl doctor inspect <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--report <report>`. One of diag, files, card, rawcodes, recipes. Default `diag`.

### doctor compare

What differs between two versions of a map, with names recovered on protected archives.

Usage

```
wc3ctl doctor compare <map> <other>
```

Arguments

- `<map>` (required). The first map.
- `<other>` (required). The map to compare it with.

### doctor fix

Repair a COPY of the map for Reforged 3.0 with the doctor's Fix map. The original is never written.

Usage

```
wc3ctl doctor fix <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--step <step>`. A fix step or extra, such as listfile, data, portraits, uabi. Repeatable. Prefix no- to drop a default step. None applies the steps the doctor picks for this map.
- `-o, --out <out>`. Where to write the repaired copy. Default <map>_doctor.<ext>. Never overwritten.

## asset

Asset paths available to an object field.

Usage

```
wc3ctl asset
```

Subcommands

- `list`. List the asset paths an icon or model field can be set to, from the map's own imports and from the base game. Use this instead of guessing a path, because a field naming a file that does not resolve renders as nothing with no error.
- `unused`. List imported files no object-data field value and no script string literal references, with the size each holds. Read-only.
- `duplicates`. Group imported files whose bytes are identical (by sha256) and report the space each duplicate group wastes. Read-only.
- `audit`. Flag suspicious imported assets. It reports entries over a size threshold, BLP textures whose dimensions are not powers of two, a .mdx with no sibling .mdl, and zero-byte entries. Read-only.
- `size-report`. Per-folder total size of the archive's imported entries, largest first. Read-only.
- `replace`. Replace an asset path everywhere it is referenced (object-data field values, script string literals and the import table) and save a copy. Refuses if the old path is referenced nowhere. The archive entry is not renamed.
- `model-lint`. Scan every imported model (.mdx/.mdl) and report the structural problems a map cannot render or animate, a geoset with no material, a material naming a texture the map does not hold, a sequence with a zero duration, and an out of range bounding box. Read-only.
- `thumbnails`. Render a thumbnail PNG for every model in a folder, or for every imported model inside every map in the folder. Read-only over the source.
- `convert-batch`. Convert every texture in a folder to one target format, reusing the shared converter. Read-only over the source, the caller writes the copies.
- `import-wire`. Add disk files to a map as imports and, when a file is a model or an icon and --object names an object, point its matching field at the new import. Saves a copy, refuses to overwrite the input map.

### asset list

List the asset paths an icon or model field can be set to, from the map's own imports and from the base game. Use this instead of guessing a path, because a field naming a file that does not resolve renders as nothing with no error.

Usage

```
wc3ctl asset list [<map>] [options]
```

Arguments

- `[<map>]` (optional). Optional .w3x/.w3m map. Without one, only the base game's paths are listed.

Options

- `--family <family>`. Asset family: icon|model. Taken from the field's metadata type, so an icon field is 'icon' and a model field is 'model'. Default `icon`.
- `--source <source>`. Where to look: map|game|all. Default `all`.

### asset unused

List imported files no object-data field value and no script string literal references, with the size each holds. Read-only.

Usage

```
wc3ctl asset unused <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### asset duplicates

Group imported files whose bytes are identical (by sha256) and report the space each duplicate group wastes. Read-only.

Usage

```
wc3ctl asset duplicates <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### asset audit

Flag suspicious imported assets. It reports entries over a size threshold, BLP textures whose dimensions are not powers of two, a .mdx with no sibling .mdl, and zero-byte entries. Read-only.

Usage

```
wc3ctl asset audit <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--max-bytes <max-bytes>`. Flag imported entries larger than this many bytes. Default `4194304`.

### asset size-report

Per-folder total size of the archive's imported entries, largest first. Read-only.

Usage

```
wc3ctl asset size-report <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### asset replace

Replace an asset path everywhere it is referenced (object-data field values, script string literals and the import table) and save a copy. Refuses if the old path is referenced nowhere. The archive entry is not renamed.

Usage

```
wc3ctl asset replace <map> <old> <new> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<old>` (required). The asset path currently referenced.
- `<new>` (required). The asset path to reference instead.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### asset model-lint

Scan every imported model (.mdx/.mdl) and report the structural problems a map cannot render or animate, a geoset with no material, a material naming a texture the map does not hold, a sequence with a zero duration, and an out of range bounding box. Read-only.

Usage

```
wc3ctl asset model-lint <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### asset thumbnails

Render a thumbnail PNG for every model in a folder, or for every imported model inside every map in the folder. Read-only over the source.

Usage

```
wc3ctl asset thumbnails <folder> [options]
```

Arguments

- `<folder>` (required). Folder holding model (.mdx/.mdl) files, or map (.w3x/.w3m) files whose imported models are rendered.

Options

- `-o, --out <out>`. Output directory for the PNG thumbnails. Defaults to ./thumbnails
- `--size <size>`. Thumbnail edge length in pixels. Default `256`.

### asset convert-batch

Convert every texture in a folder to one target format, reusing the shared converter. Read-only over the source, the caller writes the copies.

Usage

```
wc3ctl asset convert-batch <folder> [options]
```

Arguments

- `<folder>` (required). Folder holding texture files.

Options

- `--to <to>` (required). Target format extension, e.g. .png or .blp.
- `-o, --out <out>`. Output directory for the converted textures. Defaults to ./converted

### asset import-wire

Add disk files to a map as imports and, when a file is a model or an icon and --object names an object, point its matching field at the new import. Saves a copy, refuses to overwrite the input map.

Usage

```
wc3ctl asset import-wire <map> <files...> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<files...>` (required). One or more disk files to add as imports.

Options

- `--object <object>`. Rawcode of the object whose model or icon field is pointed at each imported file.
- `--kind <kind>`. Object type, one of unit|item|destructable|doodad. Defaults to auto-detect from the object.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## model

Model asset operations.

Usage

```
wc3ctl model
```

Subcommands

- `export-obj`. Export a map-imported model to Wavefront OBJ, with an MTL and any map-resolvable textures converted to PNG beside it.
- `audit`. Audit a model for the resource waste and draw-nothing problems the optimiser can remove, and report what it holds, its geosets, vertices, faces, textures, sequences, nodes and effects. Effects are counted from the chunk list, particle emitters, particle emitters 2, ribbons and popcorns, and each particle emitter's node and texture references are checked, reporting one that names a node the model does not define or a texture outside its texture list.
- `optimize`. Decimate a model's geometry with the quadric error metric, drop zero-area faces and duplicate textures, and write a smaller MDX, leaving the other chunks byte for byte. Optionally strip its effects when they are not wanted. Reports the before and after vertex and face counts.
- `import`. Import a foreign model into a Warcraft III MDX. Accepts Wavefront OBJ, glTF 2.0 (.gltf and .glb), STL (binary and ASCII), PLY (ASCII and binary little-endian) and FBX ASCII. Builds one v800 geoset per source primitive with normals and UVs, triangulating an n-gon face as a fan, computing a per-face normal when the source carries none and zeroing UVs when it has none. Each glTF material's baseColorTexture is written as .blp beside the MDX and referenced from a texture entry, one per distinct texture. Each glTF animation is written as a named SEQS sequence whose frame interval is its time range at the fps (30 by default). Reports the geoset, vertex, face, texture and material counts, the sequence count and names, and the written texture files.

### model export-obj

Export a map-imported model to Wavefront OBJ, with an MTL and any map-resolvable textures converted to PNG beside it.

Usage

```
wc3ctl model export-obj <map> <model> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<model>` (required). Internal model path (.mdx/.mdl) inside the map.

Options

- `-o, --out <out>`. Output .obj path. A .mtl and any map-resolvable textures are written beside it.

### model audit

Audit a model for the resource waste and draw-nothing problems the optimiser can remove, and report what it holds, its geosets, vertices, faces, textures, sequences, nodes and effects. Effects are counted from the chunk list, particle emitters, particle emitters 2, ribbons and popcorns, and each particle emitter's node and texture references are checked, reporting one that names a node the model does not define or a texture outside its texture list.

Usage

```
wc3ctl model audit <model>
```

Arguments

- `<model>` (required). Path to a .mdx or .mdl file.

### model optimize

Decimate a model's geometry with the quadric error metric, drop zero-area faces and duplicate textures, and write a smaller MDX, leaving the other chunks byte for byte. Optionally strip its effects when they are not wanted. Reports the before and after vertex and face counts.

Usage

```
wc3ctl model optimize <model> [options]
```

Arguments

- `<model>` (required). Path to a .mdx or .mdl file.

Options

- `-o, --out <out>` (required). Output model path. The input is never modified.
- `--ratio <ratio>`. Target fraction of faces to keep, between 0 and 1. Default `0.5`.
- `--strip-effects`. Drop the model's effect chunks, particle emitters (PREM), particle emitters 2 (PRE2), ribbons (RIBB) and corns (CORN), for a smaller MDX. This changes how the model looks, so it is off by default.

### model import

Import a foreign model into a Warcraft III MDX. Accepts Wavefront OBJ, glTF 2.0 (.gltf and .glb), STL (binary and ASCII), PLY (ASCII and binary little-endian) and FBX ASCII. Builds one v800 geoset per source primitive with normals and UVs, triangulating an n-gon face as a fan, computing a per-face normal when the source carries none and zeroing UVs when it has none. Each glTF material's baseColorTexture is written as .blp beside the MDX and referenced from a texture entry, one per distinct texture. Each glTF animation is written as a named SEQS sequence whose frame interval is its time range at the fps (30 by default). Reports the geoset, vertex, face, texture and material counts, the sequence count and names, and the written texture files.

Usage

```
wc3ctl model import <obj> [options]
```

Arguments

- `<obj>` (required). Path to a foreign model, one of Wavefront OBJ (.obj), glTF 2.0 (.gltf or .glb), STL (.stl, binary or ASCII), PLY (.ply, ASCII or binary little-endian) or FBX ASCII (.fbx). The format is detected from the content, so the extension alone is not needed. Binary FBX is not supported.

Options

- `-o, --out <out>` (required). Output .mdx path. The input is never modified.
- `--texture <texture>`. Texture path to embed, relative to the map, overriding every material. Default, the model's own materials, or war3mapImported\<name>.blp when it names none.
- `--fps <fps>`. Frames per second used to convert a glTF animation's time range into a sequence frame interval. The Warcraft III default is 30. Default `30`.

## texture

Texture asset operations.

Usage

```
wc3ctl texture
```

Subcommands

- `optimize`. Shrink a texture to fit a maximum side, preserving aspect ratio, and write it back in the same format. The other half of making a model compact.

### texture optimize

Shrink a texture to fit a maximum side, preserving aspect ratio, and write it back in the same format. The other half of making a model compact.

Usage

```
wc3ctl texture optimize <texture> [options]
```

Arguments

- `<texture>` (required). Path to a .blp, .dds or standard image file.

Options

- `-o, --out <out>` (required). Output path. The input is never modified.
- `--max <max>`. Maximum side length in pixels. A texture larger than this on either side is scaled down. Default `256`.

## strings

The map's string table (war3map.wts).

Usage

```
wc3ctl strings
```

Subcommands

- `list`. List the map's string table (war3map.wts). Every TRIGSTR_ reference resolves here.
- `missing`. List the TRIGSTR_ keys the script and the trigger tree reference that have no war3map.wts entry. Read-only.
- `unused`. List war3map.wts entries no object field, map-info field, trigger or script line references. Read-only.
- `diff`. Compare two maps' string tables and report entries added, removed and changed. Read-only.
- `find`. Find war3map.wts entries whose text contains the given text (case-insensitive), with the id. Read-only.
- `set`. Set one war3map.wts entry's text, or add it when the id is absent, and save a copy. The input map is never overwritten.

### strings list

List the map's string table (war3map.wts). Every TRIGSTR_ reference resolves here.

Usage

```
wc3ctl strings list <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### strings missing

List the TRIGSTR_ keys the script and the trigger tree reference that have no war3map.wts entry. Read-only.

Usage

```
wc3ctl strings missing <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### strings unused

List war3map.wts entries no object field, map-info field, trigger or script line references. Read-only.

Usage

```
wc3ctl strings unused <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### strings diff

Compare two maps' string tables and report entries added, removed and changed. Read-only.

Usage

```
wc3ctl strings diff <mapA> <mapB>
```

Arguments

- `<mapA>` (required). Path to the first .w3x/.w3m map.
- `<mapB>` (required). Path to the second .w3x/.w3m map.

### strings find

Find war3map.wts entries whose text contains the given text (case-insensitive), with the id. Read-only.

Usage

```
wc3ctl strings find <map> <text>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<text>` (required). Text to find in entry text, a case-insensitive substring.

### strings set

Set one war3map.wts entry's text, or add it when the id is absent, and save a copy. The input map is never overwritten.

Usage

```
wc3ctl strings set <map> <id> <text> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<id>` (required). war3map.wts entry id to set or add.
- `<text>` (required). New text for the entry.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## imports

The map's import table (war3map.imp).

Usage

```
wc3ctl imports
```

Subcommands

- `list`. List the import table (war3map.imp) against what the archive holds, so an entry with no file and a file absent from the table both show up.

### imports list

List the import table (war3map.imp) against what the archive holds, so an entry with no file and a file absent from the table both show up.

Usage

```
wc3ctl imports list <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

## unit

Placed units (war3mapUnits.doo): list, get, set, remove. Use 'place unit' to add one.

Usage

```
wc3ctl unit
```

Subcommands

- `list`. List every placed unit, with its creation number.
- `get`. Show one placed unit's full state.
- `set`. Set a field on a placed unit and save the edited map. Fields: Owner|X|Y|Position|Facing|Scale|HeroLevel|Strength|Agility|Intelligence|HpPercent|ManaPercent|Gold|TargetAcquisition.
- `remove`. Remove a placed unit and save the edited map.
- `abilities`. List all abilities for a unit, grouped by source (normal, hero, spellbook, placed unit, morph form, script).

### unit list

List every placed unit, with its creation number.

Usage

```
wc3ctl unit list <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### unit get

Show one placed unit's full state.

Usage

```
wc3ctl unit get <map> <creation-number>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<creation-number>` (required). The instance's creation number, as shown by 'list'. Stable for the life of the map.

### unit set

Set a field on a placed unit and save the edited map. Fields: Owner|X|Y|Position|Facing|Scale|HeroLevel|Strength|Agility|Intelligence|HpPercent|ManaPercent|Gold|TargetAcquisition.

Usage

```
wc3ctl unit set <map> <creation-number> <field> <value> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<creation-number>` (required). The instance's creation number, as shown by 'list'. Stable for the life of the map.
- `<field>` (required). Field to set.
- `<value>` (required). New value. A position or scale takes a comma-separated tuple, and a single number scales uniformly.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### unit remove

Remove a placed unit and save the edited map.

Usage

```
wc3ctl unit remove <map> <creation-number> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<creation-number>` (required). The instance's creation number, as shown by 'list'. Stable for the life of the map.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### unit abilities

List all abilities for a unit, grouped by source (normal, hero, spellbook, placed unit, morph form, script).

Usage

```
wc3ctl unit abilities <map> <rawcode> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Unit type's four-character rawcode.

Options

- `--placed <placed>`. Show abilities for a placed unit instance by creation number instead.

## doodad

Placed doodads (war3map.doo): list, get, set, remove. Use 'place doodad' to add one.

Usage

```
wc3ctl doodad
```

Subcommands

- `list`. List every placed doodad, with its creation number.
- `get`. Show one placed doodad's full state.
- `set`. Set a field on a placed doodad and save the edited map. Fields: X|Y|Z|Position|Rotation|Scale|Variation|LifePercent.
- `remove`. Remove a placed doodad and save the edited map.

### doodad list

List every placed doodad, with its creation number.

Usage

```
wc3ctl doodad list <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### doodad get

Show one placed doodad's full state.

Usage

```
wc3ctl doodad get <map> <creation-number>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<creation-number>` (required). The instance's creation number, as shown by 'list'. Stable for the life of the map.

### doodad set

Set a field on a placed doodad and save the edited map. Fields: X|Y|Z|Position|Rotation|Scale|Variation|LifePercent.

Usage

```
wc3ctl doodad set <map> <creation-number> <field> <value> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<creation-number>` (required). The instance's creation number, as shown by 'list'. Stable for the life of the map.
- `<field>` (required). Field to set.
- `<value>` (required). New value. A position or scale takes a comma-separated tuple, and a single number scales uniformly.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### doodad remove

Remove a placed doodad and save the edited map.

Usage

```
wc3ctl doodad remove <map> <creation-number> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<creation-number>` (required). The instance's creation number, as shown by 'list'. Stable for the life of the map.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## test-load

Load a map in Warcraft III unattended and report whether it reached the end of main(). Injects a PreloadGenEnd marker into a COPY, launches the game windowed, waits for the marker, and on timeout measures whether the game is spinning or blocked.

Usage

```
wc3ctl test-load <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--timeout <timeout>`. Seconds to wait for the marker. Default `120`.
- `--documents <documents>`. Warcraft III documents folder. Default: DocumentsWarcraft III (the game writes markers there).

## contract

Discover what a map requires before it will treat a unit as a playable hero: roster registration calls, spell dispatchers, and per-player hero arrays.

Usage

```
wc3ctl contract <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--reference <reference>`. Rawcode of a hero the map ALREADY supports. Lists every function that mentions it, which is the map's real integration contract stated by example rather than guessed from shape.
- `--against <against>`. Rawcode of a candidate hero. With --reference, reports only what the reference is wired into and the candidate is not, which is the checklist to complete.

## hero

Hero definitions: the portable, reviewable form of a hero.

Usage

```
wc3ctl hero
```

Subcommands

- `export`. Export a hero from a map as a reviewable definition (hero.json), its assets, and its script. Reports what it deliberately left out.
- `install`. Install a hero definition into any map. Refuses asset collisions, remaps rawcodes, and reports what the target must still be wired with.
- `lint`. Validate a hero definition before installing it: assets present and hash-matched, objects installable, declared script functions present, no dangling references.
- `roster`. List the playable heroes the map registers, and which trigger holds each one's code. A hero is not a trigger: a map can register 170 heroes whose code is bundled into a handful of triggers, so counting triggers under-counts heroes.

### hero export

Export a hero from a map as a reviewable definition (hero.json), its assets, and its script. Reports what it deliberately left out.

Usage

```
wc3ctl hero export <map> <rawcode> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character rawcode of the hero to export.

Options

- `-o, --out <out>`. Output directory. Default: './<rawcode>-hero'.

### hero install

Install a hero definition into any map. Refuses asset collisions, remaps rawcodes, and reports what the target must still be wired with.

Usage

```
wc3ctl hero install <definition> <map> [options]
```

Arguments

- `<definition>` (required). Folder containing hero.json.
- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output directory. Default: './<rawcode>-hero'.
- `--force`. Overwrite the target's own assets on a hash collision. Off by default, because silently replacing a target's textures changes how ITS content renders.
- `--role <role>`. Role to register the hero under, when the target's roster takes one (GGGA: Stalker, Bruiser, Striker, Tanker, Tech, Supporter). Without this the role is copied from a neighbouring registration, which is a guess and usually the wrong tab.
- `--keep-source-stats`. Keep the attributes and hit/mana pools the hero was exported with. By default the target's own heroes are measured and their stat convention is applied, because a hero carries the stat MODEL of the map she came from (GGGA pins every hero's attributes at 0 and states a flat pool instead, so source attributes make her unplayable there). Use this when both maps model heroes the same way.

### hero lint

Validate a hero definition before installing it: assets present and hash-matched, objects installable, declared script functions present, no dangling references.

Usage

```
wc3ctl hero lint <definition>
```

Arguments

- `<definition>` (required). Folder containing hero.json.

### hero roster

List the playable heroes the map registers, and which trigger holds each one's code. A hero is not a trigger: a map can register 170 heroes whose code is bundled into a handful of triggers, so counting triggers under-counts heroes.

Usage

```
wc3ctl hero roster <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

## lint

Run pre-flight checks on a map: does the script compile, is war3map.imp consistent, do referenced assets resolve, are all file types loadable, and (with --against) did a rebuild lose or clobber anything.

Usage

```
wc3ctl lint <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--against <against>`. Original map to compare against, enabling the checks that need a before/after: no file lost, and none of the target's own assets overwritten by a port.
- `--target <target>`. Target build name or schema folder path to check compatibility against.
- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.
- `--baseline <baseline>`. CI gate. Compare against a baseline file written by 'lint baseline' and fail only on a finding the baseline does not already hold.

Subcommands

- `baseline`. Write the map's current lint findings to a baseline file. A later 'lint <map> --baseline <file>' then fails only on a finding this file does not already hold.

### lint baseline

Write the map's current lint findings to a baseline file. A later 'lint <map> --baseline <file>' then fails only on a finding this file does not already hold.

Usage

```
wc3ctl lint baseline <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>` (required). File to write the baseline to.
- `--against <against>`. Original map to compare against, so the baseline records the before/after checks too.

## mpq-diff

Compare two archives structurally: header, sector size, hash table capacity and crowding, and each file's storage flags, compressed size and offset. Use when two maps have identical contents but behave differently.

Usage

```
wc3ctl mpq-diff <map> <mapB>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<mapB>` (required). Second map to compare against.

## mpq-hash

Inspect an archive's hash table: occupied, deleted and never-used slot counts, the worst-case probe length for a name that is absent, and whether a failed lookup can loop forever.

Usage

```
wc3ctl mpq-hash <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--slots <slots>`. Also dump this many slots from the start of the table. Default `0`.

## mpq

MPQ container operations. Verify readability, report listfile coverage, recover names from a dictionary, strip bookkeeping, and compare payload bytes.

Usage

```
wc3ctl mpq
```

Subcommands

- `verify`. Open every archive entry and read it to its end, reporting the ones that fail with their error and a summary count. Read-only.
- `listfile`. Report listfile coverage, how many entries the listfile names, how many the standard name probe recovered, and how many carry no name at all. Read-only.
- `name-recover`. Resolve a dictionary of candidate names against the archive's hash table and report the names that hit unnamed blocks. Read-only.
- `strip-bookkeeping`. Save a copy with (listfile) and (attributes) dropped, using the same switch the toolkit already reads, and confirm the copy opens again. The input is never overwritten.
- `bytes-diff`. Compare two archives entry by entry by name. For each name present in both, report whether the payload SHA256 matches, and list names only one side carries. Read-only.
- `rebuild`. Rebuild the archive from scratch into a healthy one, re-placing every readable entry and dropping the blocks protection made unreadable or left nameless. Reports the entry counts before and after. Writes a copy.
- `merge`. Merge two archives into one, taking every readable named entry from both. A name in both is resolved by the preferred side and reported as a conflict. Writes a copy.
- `add`. Add or replace one internal file from a file on disk, byte-faithfully. Reports whether it was added or replaced. Writes a copy.
- `remove`. Remove one internal file by rebuilding the container without it, and report whether the rebuild actually dropped the name. Writes a copy.
- `extract-all`. Extract every entry of the archive to a directory, including unnamed protected blocks, through the same path-safety rule 'extract' uses. Read-only over the map.

### mpq verify

Open every archive entry and read it to its end, reporting the ones that fail with their error and a summary count. Read-only.

Usage

```
wc3ctl mpq verify <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### mpq listfile

Report listfile coverage, how many entries the listfile names, how many the standard name probe recovered, and how many carry no name at all. Read-only.

Usage

```
wc3ctl mpq listfile <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### mpq name-recover

Resolve a dictionary of candidate names against the archive's hash table and report the names that hit unnamed blocks. Read-only.

Usage

```
wc3ctl mpq name-recover <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--dictionary <dictionary>`. Path to a dictionary file, one candidate name per line. Needed by name-recover.

### mpq strip-bookkeeping

Save a copy with (listfile) and (attributes) dropped, using the same switch the toolkit already reads, and confirm the copy opens again. The input is never overwritten.

Usage

```
wc3ctl mpq strip-bookkeeping <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. Default is '<map>.edited.<ext>' next to the input, the original is never overwritten.

### mpq bytes-diff

Compare two archives entry by entry by name. For each name present in both, report whether the payload SHA256 matches, and list names only one side carries. Read-only.

Usage

```
wc3ctl mpq bytes-diff <map> <mapB>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<mapB>` (required). Second map to compare against.

### mpq rebuild

Rebuild the archive from scratch into a healthy one, re-placing every readable entry and dropping the blocks protection made unreadable or left nameless. Reports the entry counts before and after. Writes a copy.

Usage

```
wc3ctl mpq rebuild <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. The input map is never overwritten.
- `--grow-hash`. Force a larger hash table on the rebuilt archive. Doubles the source capacity, then raises it until 3/4 occupancy fits. Off by default, most maps need no growth.

### mpq merge

Merge two archives into one, taking every readable named entry from both. A name in both is resolved by the preferred side and reported as a conflict. Writes a copy.

Usage

```
wc3ctl mpq merge <map> <mapB> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<mapB>` (required). Second map to compare against.

Options

- `-o, --out <out>`. Output map path. The input map is never overwritten.
- `--prefer <prefer>`. Which side wins a name both maps carry, 'a' or 'b'. Default 'a'. Default `a`.

### mpq add

Add or replace one internal file from a file on disk, byte-faithfully. Reports whether it was added or replaced. Writes a copy.

Usage

```
wc3ctl mpq add <map> <name> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). Internal archive path, for example war3map.j.

Options

- `--file <file>` (required). Path to the file whose bytes become the internal file.
- `-o, --out <out>`. Output map path. The input map is never overwritten.

### mpq remove

Remove one internal file by rebuilding the container without it, and report whether the rebuild actually dropped the name. Writes a copy.

Usage

```
wc3ctl mpq remove <map> <name> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). Internal archive path, for example war3map.j.

Options

- `-o, --out <out>`. Output map path. The input map is never overwritten.

### mpq extract-all

Extract every entry of the archive to a directory, including unnamed protected blocks, through the same path-safety rule 'extract' uses. Read-only over the map.

Usage

```
wc3ctl mpq extract-all <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output directory. Default '<map>.extracted' beside the input. Created if absent.

## file

Internal archive files, list, get-text, set (raw byte replace from disk).

Usage

```
wc3ctl file
```

Subcommands

- `list`. List archive entries with their CURRENT size, reflecting pending replacements.
- `get-text`. Print an internal file decoded as UTF-8, with any leading BOM stripped.
- `set`. Write a disk file into the map, replacing that entry or adding it, and save the edited map.

### file list

List archive entries with their CURRENT size, reflecting pending replacements.

Usage

```
wc3ctl file list <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### file get-text

Print an internal file decoded as UTF-8, with any leading BOM stripped.

Usage

```
wc3ctl file get-text <map> <internal-path>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<internal-path>` (required). Internal archive path, for example war3mapSkin.txt.

### file set

Write a disk file into the map, replacing that entry or adding it, and save the edited map.

Usage

```
wc3ctl file set <map> <internal-path> [<source>] [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<internal-path>` (required). Internal archive path, for example war3mapSkin.txt.
- `[<source>]` (optional). Disk file whose bytes are written into the archive verbatim (or use --from).

Options

- `--from <from>`. Disk file whose exact bytes become the new payload (or give it as the third argument).
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## leak

Handle-leak investigation. The static ranked list from script leaks, an in-map harness that drives the hot candidates, and the join to a wc3watch session's runtime samples.

Usage

```
wc3ctl leak
```

Subcommands

- `list`. The ranked static leak list and the periodic drivers that feed it. Read-only.
- `harness`. Emit a small in-map harness that drives the named hot leak candidates on a short timer so the runtime slope is measurable in seconds, plus a handle count sampler. Emits valid jass or lua and does not start the game.
- `join`. Read a wc3watch session's handle and memory slope, measured from its forced garbage collection when one was marked, and join each rising window to the periodic candidates from the static list and the session events and game log lines near it. Read-only.
- `ab`. Diff two wc3watch sessions and report whether the handle and memory slope fell, each measured from its own forced garbage collection. With --map, name the candidate whose predicted rate is nearest the drop. Read-only.
- `benchmark`. Run the detector over synthetic maps with a known injected leak, one per creator kind, and report true positive and false positive counts. Read-only.
- `model`. Model-level leak report. Joins the map's imported models and textures to what the map and those models reference, and reports a leaked import, a missing texture, a duplicate import and an unused texture. Read-only.

### leak list

The ranked static leak list and the periodic drivers that feed it. Read-only.

Usage

```
wc3ctl leak list <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### leak harness

Emit a small in-map harness that drives the named hot leak candidates on a short timer so the runtime slope is measurable in seconds, plus a handle count sampler. Emits valid jass or lua and does not start the game.

Usage

```
wc3ctl leak harness <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--lang <lang>` (required). Harness language, jass or lua.
- `--target <target>` (required). A hot leak candidate function name, or all.
- `-o, --out <out>` (required). Path to write the harness.

### leak join

Read a wc3watch session's handle and memory slope, measured from its forced garbage collection when one was marked, and join each rising window to the periodic candidates from the static list and the session events and game log lines near it. Read-only.

Usage

```
wc3ctl leak join <map> <session-folder>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<session-folder>` (required). A wc3watch session folder holding samples.csv and, optionally, events.csv.

### leak ab

Diff two wc3watch sessions and report whether the handle and memory slope fell, each measured from its own forced garbage collection. With --map, name the candidate whose predicted rate is nearest the drop. Read-only.

Usage

```
wc3ctl leak ab <sessionA> <sessionB> [options]
```

Arguments

- `<sessionA>` (required). First wc3watch session folder.
- `<sessionB>` (required). Second wc3watch session folder.

Options

- `--map <map>`. Map to attribute the drop to a periodic candidate.

### leak benchmark

Run the detector over synthetic maps with a known injected leak, one per creator kind, and report true positive and false positive counts. Read-only.

Usage

```
wc3ctl leak benchmark
```

### leak model

Model-level leak report. Joins the map's imported models and textures to what the map and those models reference, and reports a leaked import, a missing texture, a duplicate import and an unused texture. Read-only.

Usage

```
wc3ctl leak model <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

## info

Show map metadata.

Usage

```
wc3ctl info <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

## ls

List internal files.

Usage

```
wc3ctl ls <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--harvest`. Recover unnamed entries' names from the map's own script and object data before listing (a second pass worth paying for on a protected map, wasted on a healthy one).

## overview

One call per map, metadata, every archive file, the custom object counts by kind and the diagnostics.

Usage

```
wc3ctl overview <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

## roundtrip

Verify byte-faithful round-trip.

Usage

```
wc3ctl roundtrip <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

## search

Search a map for text, ignoring case: file names, script lines, string-table entries and object data (custom rawcodes and field values).

Usage

```
wc3ctl search <map> <query>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<query>` (required). Substring to search for.

## diff

Diff two maps.

Usage

```
wc3ctl diff <map> <mapB>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<mapB>` (required). Path to the second map.

## object

Object data queries.

Usage

```
wc3ctl object
```

Subcommands

- `form`. Show an object the way an editor should: fields grouped and ordered like the World Editor, fields that do not apply to this object hidden, legal ranges shown, and which object-data layer each field is written to.
- `get`. Get an object's merged fields (base game data ⊕ map deltas).
- `list`. List the map's custom/modified objects of one kind.
- `set`. Set an object field (any kind) and save the edited map.
- `new`. Create a custom object derived from a base rawcode and save the edited map. Prints the fresh rawcode.
- `search`. Search every custom/modified object's field values (base and map) for text. Read-only.
- `noop-fields`. List fields whose map value equals the base game value, so the edit changes nothing. Read-only. Needs --game-dir for the base, or it reports map-only.
- `deprecated`. List object fields the target build's schema does not define but the map still sets. Read-only.
- `rawcode-suggest`. Propose free four-character rawcodes that collide with neither the map nor the base game data for the kind. Read-only.
- `clone`. Deep-clone a map object (its field deltas, and level data for an ability) under a new rawcode and save the edited map. Refuses a rawcode already taken.
- `migrate`. Rewrite the map's object data to the target build's field set, dropping fields the target does not define and reporting each drop. Saves an edited copy.
- `bulk-set`. Set one field on every object of a kind matching every --where clause, and save the edited copy. Reports how many changed.
- `export`. Export the objects of a kind (base game data overlaid by the map) to CSV, one row per object, one column per field code. Read-only.
- `import`. Import a CSV of one kind back into the map, adding or updating its objects. Refuses an unknown column. Saves an edited copy.
- `rename`. Rename an object rawcode and optionally rewrite every field and script literal that references it. Saves an edited copy.
- `options`. The legal values for an object field, taken from what the base game data uses for it. Ask before 'object set' on an enumerated field.

### object form

Show an object the way an editor should: fields grouped and ordered like the World Editor, fields that do not apply to this object hidden, legal ranges shown, and which object-data layer each field is written to.

Usage

```
wc3ctl object form <map> <rawcode> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character object rawcode.

Options

- `--kind <kind>`. Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default: auto-detect.

### object get

Get an object's merged fields (base game data ⊕ map deltas).

Usage

```
wc3ctl object get <map> <rawcode> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character object rawcode.

Options

- `--field <field>`. Restrict output to a single field.
- `--kind <kind>`. Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default: auto-detect.

### object list

List the map's custom/modified objects of one kind.

Usage

```
wc3ctl object list <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--kind <kind>`. Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default `unit`.

### object set

Set an object field (any kind) and save the edited map.

Usage

```
wc3ctl object set <map> <rawcode> <field> <value> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character object rawcode.
- `<field>` (required). Four-character field code (e.g. uhpm), or code:N for an ability/upgrade level or doodad variation.
- `<value>` (required). New value for the field.

Options

- `--kind <kind>`. Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default `unit`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### object new

Create a custom object derived from a base rawcode and save the edited map. Prints the fresh rawcode.

Usage

```
wc3ctl object new <map> <kind> <base> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<kind>` (required). Object type: unit|item|ability|destructable|doodad|buff|upgrade.
- `<base>` (required). Four-character rawcode of the base object the new custom object derives from.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### object search

Search every custom/modified object's field values (base and map) for text. Read-only.

Usage

```
wc3ctl object search <map> <query> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<query>` (required). Text to find in object field values (case-insensitive substring).

Options

- `--field <field>`. Restrict the search to one field code.
- `--kind <kind>`. Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default: auto-detect.

### object noop-fields

List fields whose map value equals the base game value, so the edit changes nothing. Read-only. Needs --game-dir for the base, or it reports map-only.

Usage

```
wc3ctl object noop-fields <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--kind <kind>`. Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default: auto-detect.

### object deprecated

List object fields the target build's schema does not define but the map still sets. Read-only.

Usage

```
wc3ctl object deprecated <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--target <target>` (required). Target build name or path to a schema folder.
- `--schemas <schemas>`. Directory of schema folders. Default: search <base>/schemas then walk up to data/schemas.

### object rawcode-suggest

Propose free four-character rawcodes that collide with neither the map nor the base game data for the kind. Read-only.

Usage

```
wc3ctl object rawcode-suggest <map> <kind> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<kind>` (required). Object type: unit|item|ability|destructable|doodad|buff|upgrade.

Options

- `--count <count>`. How many rawcodes to propose. Default `10`.

### object clone

Deep-clone a map object (its field deltas, and level data for an ability) under a new rawcode and save the edited map. Refuses a rawcode already taken.

Usage

```
wc3ctl object clone <map> <kind> <rawcode> <new-rawcode> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<kind>` (required). Object type: unit|item|ability|destructable|doodad|buff|upgrade.
- `<rawcode>` (required). Four-character source rawcode to clone.
- `<new-rawcode>` (required). Four-character rawcode for the clone.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### object migrate

Rewrite the map's object data to the target build's field set, dropping fields the target does not define and reporting each drop. Saves an edited copy.

Usage

```
wc3ctl object migrate <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--to <to>` (required). Target build name or path to a schema folder (read like compat).
- `--schemas <schemas>`. Directory of schema folders. Default: search <base>/schemas then walk up to data/schemas.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### object bulk-set

Set one field on every object of a kind matching every --where clause, and save the edited copy. Reports how many changed.

Usage

```
wc3ctl object bulk-set <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--kind <kind>`. Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default `unit`.
- `--set <set>` (required). The field and value to write, as code=value (e.g. uhpm=1000).
- `--where <where>`. A match clause code=value; repeat for several. An object must match every clause.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### object export

Export the objects of a kind (base game data overlaid by the map) to CSV, one row per object, one column per field code. Read-only.

Usage

```
wc3ctl object export <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--kind <kind>`. Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default `unit`.
- `-o, --out <out>` (required). CSV output path.

### object import

Import a CSV of one kind back into the map, adding or updating its objects. Refuses an unknown column. Saves an edited copy.

Usage

```
wc3ctl object import <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--kind <kind>`. Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default `unit`.
- `--csv <csv>` (required). Path to the CSV file to import.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### object rename

Rename an object rawcode and optionally rewrite every field and script literal that references it. Saves an edited copy.

Usage

```
wc3ctl object rename <map> <old> <new> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<old>` (required). The rawcode to rename.
- `<new>` (required). The new rawcode.

Options

- `--kind <kind>`. Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default `unit`.
- `--rewrite-refs`. Also rewrite every object-data field value and script string literal that names the old rawcode.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### object options

The legal values for an object field, taken from what the base game data uses for it. Ask before 'object set' on an enumerated field.

Usage

```
wc3ctl object options <field> [options]
```

Arguments

- `<field>` (required). The field's four-character code.

Options

- `--kind <kind>`. Object type: unit|item|ability|destructable|doodad|buff|upgrade. Default `unit`.

## extract

Extract internal files to disk.

Usage

```
wc3ctl extract <map> [<internal-path>] [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `[<internal-path>]` (optional). Exact internal file path to extract.

Options

- `-o, --out <out>`. Output directory (or output file for a single named extraction). Default: current directory.
- `--pattern <pattern>`. Glob pattern(s) matched against internal names ('*' wildcard); repeatable or comma-separated.
- `--models`. Extract models (*.mdx, *.mdl).
- `--textures`. Extract textures (*.blp, *.tga, *.dds).
- `--sounds`. Extract sounds (*.mp3, *.wav).
- `--all`. Extract every internal file (including unnamed entries).

## render

Render a top-down terrain image to PNG.

Usage

```
wc3ctl render <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output PNG path. Default: <map name>.png in the current directory.

## render-model

Render an object's model (.mdx/.mdl) to PNG - map-imported models first, base-game (CASC) models when a WC3 install is available.

Usage

```
wc3ctl render-model <map> <rawcode> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character object rawcode, or an internal model path (contains '\', '/' or '.').

Options

- `--kind <kind>`. Object type: unit|item|destructable|doodad. Default: auto-detect.
- `-o, --out <out>`. Output PNG path. Default: <rawcode>.png in the current directory.

## script

Map script queries.

Usage

```
wc3ctl script
```

Subcommands

- `functions`. List functions declared in the map script.
- `leaks`. Find handle leaks in war3map.j (locations, groups, forces, effects, timers, text tags, lightning, triggers created and never destroyed), ranked by how often the code runs, and list the periodic timers. Read-only.
- `unused-globals`. Find globals declared in war3map.j and never read after their declaration, including ones that are only ever assigned. The World Editor's generated gg_ and bj_ globals are excluded. Read-only.
- `unused-functions`. Find functions no other function calls and nothing else reaches, excluding the engine's entry points and anything an InitTrig_* function or ExecuteFunc("...") names. Read-only.
- `null-locals`. Find handle locals (location, group, force, effect, timer, trigger, text tag, lightning) destroyed with Remove/Destroy but never set to null, the classic JASS handle leak. Read-only.
- `complexity`. Per-function cyclomatic complexity (if, loop, and, or), ranked worst first, with the line. Read-only.
- `callgraph`. The call edges between functions the script declares, as a text adjacency list or, with --dot, Graphviz DOT. Read-only.
- `refs`. Find every use of a name in the map script: direct calls, and the places it is passed as a code value (Condition, Filter, TriggerAddAction), which a text search for 'name(' cannot see.
- `reachable`. List the functions reachable from main and the InitTrig entry points, and the dead functions nothing reaches, each with its line. Read-only.
- `api-usage`. List the natives and functions the script uses that the target build does not declare. Read-only.
- `semantic-diff`. Compare two scripts line by line after normalizing whitespace and ignoring pure renames of local variables, reporting the real changes. Read-only.
- `duplicate-bodies`. Find functions whose normalized bodies are identical, up to comments, whitespace and local variable names. Read-only.
- `modernize`. Rewrite deprecated or renamed natives in the script to the target build's spelling where a mapping is known, leaving everything else untouched.
- `loops`. Find JASS loops that may never terminate: no exitwhen at all, or an exit condition naming only values the loop body never assigns.
- `roots`. List functions reachable only via a string literal, e.g. ExecuteFunc("Name"). A dead-code pass must pin these or it will delete live code.
- `strip`. Remove unreachable functions from war3map.j by call-graph closure from the engine's entry points plus every function named in a string literal. Globals are untouched.
- `repair`. Repair a map whose script cannot compile (restores declarations whose initializer was dropped by a port). An un-compilable war3map.j is why a hosted map shows no player slots.
- `check`. Check the map script for errors that stop it loading. A Lua script is checked against Lua 5.3 exactly as luac would, a JASS script by the built-in JASS checks. Exits 1 on an error.
- `format`. Reprint a Lua map script, pretty or canonical. The result compiles to the same Lua instructions as the original. JASS maps are refused.
- `to-lua`. Convert a JASS map to a Lua map. The script becomes war3map.lua with the JASS integer, division and null string rules kept, and the w3i script language is set to Lua.
- `decompile`. Turn Lua entries shipped as Lua 5.3 bytecode back into Lua source that runs the same. Nothing is written when the map holds no bytecode.
- `lua-leaks`. Find handle leaks in a Lua map script: a creator dropped as a statement, a creator on a period that is never released, and a handle kept in a table the script never clears. Read-only.
- `lua-globals`. List the globals a Lua map defines, where each is read and written, and any global that is written but never read. Read-only.
- `lua-check`. Parse every Lua file in a map and report the syntax errors with their line and column, the way luac 5.3 refuses them. Exits 1 on an error.
- `lua-api`. List the natives and functions a Lua script calls that the target build does not declare. Read-only.
- `lua-to-jass`. Best effort conversion of a Lua map back to JASS. Only the constructs that map back without guessing are converted, and every construct left is reported.

### script functions

List functions declared in the map script.

Usage

```
wc3ctl script functions <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### script leaks

Find handle leaks in war3map.j (locations, groups, forces, effects, timers, text tags, lightning, triggers created and never destroyed), ranked by how often the code runs, and list the periodic timers. Read-only.

Usage

```
wc3ctl script leaks <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--all`. List every finding, including code that runs only once at init.

### script unused-globals

Find globals declared in war3map.j and never read after their declaration, including ones that are only ever assigned. The World Editor's generated gg_ and bj_ globals are excluded. Read-only.

Usage

```
wc3ctl script unused-globals <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### script unused-functions

Find functions no other function calls and nothing else reaches, excluding the engine's entry points and anything an InitTrig_* function or ExecuteFunc("...") names. Read-only.

Usage

```
wc3ctl script unused-functions <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### script null-locals

Find handle locals (location, group, force, effect, timer, trigger, text tag, lightning) destroyed with Remove/Destroy but never set to null, the classic JASS handle leak. Read-only.

Usage

```
wc3ctl script null-locals <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### script complexity

Per-function cyclomatic complexity (if, loop, and, or), ranked worst first, with the line. Read-only.

Usage

```
wc3ctl script complexity <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### script callgraph

The call edges between functions the script declares, as a text adjacency list or, with --dot, Graphviz DOT. Read-only.

Usage

```
wc3ctl script callgraph <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--dot`. Emit Graphviz DOT instead of a text adjacency list.

### script refs

Find every use of a name in the map script: direct calls, and the places it is passed as a code value (Condition, Filter, TriggerAddAction), which a text search for 'name(' cannot see.

Usage

```
wc3ctl script refs <map> <name>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). Function or global name to find uses of.

### script reachable

List the functions reachable from main and the InitTrig entry points, and the dead functions nothing reaches, each with its line. Read-only.

Usage

```
wc3ctl script reachable <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### script api-usage

List the natives and functions the script uses that the target build does not declare. Read-only.

Usage

```
wc3ctl script api-usage <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--target <target>` (required). Target build name or schema folder path.
- `--schemas <schemas>`. Directory of schema folders. Default: search <base>/schemas then walk up to data/schemas.

### script semantic-diff

Compare two scripts line by line after normalizing whitespace and ignoring pure renames of local variables, reporting the real changes. Read-only.

Usage

```
wc3ctl script semantic-diff <map> <mapB>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<mapB>` (required). Path to the second map.

### script duplicate-bodies

Find functions whose normalized bodies are identical, up to comments, whitespace and local variable names. Read-only.

Usage

```
wc3ctl script duplicate-bodies <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### script modernize

Rewrite deprecated or renamed natives in the script to the target build's spelling where a mapping is known, leaving everything else untouched.

Usage

```
wc3ctl script modernize <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--target <target>` (required). Target build name or schema folder path.
- `--schemas <schemas>`. Directory of schema folders. Default: search <base>/schemas then walk up to data/schemas.
- `-o, --out <out>`. Output map path. Default: '<map>.modernized.<ext>' next to the input - the original is never overwritten.

### script loops

Find JASS loops that may never terminate: no exitwhen at all, or an exit condition naming only values the loop body never assigns.

Usage

```
wc3ctl script loops <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--all`. List every loop, not just the risky ones.

### script roots

List functions reachable only via a string literal, e.g. ExecuteFunc("Name"). A dead-code pass must pin these or it will delete live code.

Usage

```
wc3ctl script roots <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### script strip

Remove unreachable functions from war3map.j by call-graph closure from the engine's entry points plus every function named in a string literal. Globals are untouched.

Usage

```
wc3ctl script strip <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.stripped.<ext>' next to the input.

### script repair

Repair a map whose script cannot compile (restores declarations whose initializer was dropped by a port). An un-compilable war3map.j is why a hosted map shows no player slots.

Usage

```
wc3ctl script repair <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.repaired.<ext>' next to the input - the original is never overwritten.

### script check

Check the map script for errors that stop it loading. A Lua script is checked against Lua 5.3 exactly as luac would, a JASS script by the built-in JASS checks. Exits 1 on an error.

Usage

```
wc3ctl script check <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### script format

Reprint a Lua map script, pretty or canonical. The result compiles to the same Lua instructions as the original. JASS maps are refused.

Usage

```
wc3ctl script format <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--style <style>`. pretty (indented, comments kept) or canonical (one fixed form, for diffing). Default `pretty`.
- `-o, --out <out>`. Output map path. Default '<map>.formatted.<ext>' next to the input, the original is never overwritten.

### script to-lua

Convert a JASS map to a Lua map. The script becomes war3map.lua with the JASS integer, division and null string rules kept, and the w3i script language is set to Lua.

Usage

```
wc3ctl script to-lua <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. Default '<map>.lua.<ext>' next to the input. An existing file is never overwritten.

### script decompile

Turn Lua entries shipped as Lua 5.3 bytecode back into Lua source that runs the same. Nothing is written when the map holds no bytecode.

Usage

```
wc3ctl script decompile <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. Default '<map>.decompiled.<ext>' next to the input. An existing file is never overwritten.

### script lua-leaks

Find handle leaks in a Lua map script: a creator dropped as a statement, a creator on a period that is never released, and a handle kept in a table the script never clears. Read-only.

Usage

```
wc3ctl script lua-leaks <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--all`. List every finding, including code that runs only once.

### script lua-globals

List the globals a Lua map defines, where each is read and written, and any global that is written but never read. Read-only.

Usage

```
wc3ctl script lua-globals <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### script lua-check

Parse every Lua file in a map and report the syntax errors with their line and column, the way luac 5.3 refuses them. Exits 1 on an error.

Usage

```
wc3ctl script lua-check <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### script lua-api

List the natives and functions a Lua script calls that the target build does not declare. Read-only.

Usage

```
wc3ctl script lua-api <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--target <target>` (required). Target build name or schema folder path.
- `--schemas <schemas>`. Directory of schema folders. Default: search <base>/schemas then walk up to data/schemas.

### script lua-to-jass

Best effort conversion of a Lua map back to JASS. Only the constructs that map back without guessing are converted, and every construct left is reported.

Usage

```
wc3ctl script lua-to-jass <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. Default '<map>.jass.<ext>' next to the input. An existing file is never overwritten.

## bundle

Dependency bundles for porting between maps.

Usage

```
wc3ctl bundle
```

Subcommands

- `unit`. Resolve everything a unit depends on: objects, asset files and trigger strings.
- `object`. Resolve everything an object of any kind depends on ('bundle unit', generalized).
- `diff`. Compare two dependency bundles by their object and asset members, listing what each adds.
- `merge`. Merge two dependency bundles into one, refusing when the same rawcode is defined differently.

### bundle unit

Resolve everything a unit depends on: objects, asset files and trigger strings.

Usage

```
wc3ctl bundle unit <map> <rawcode> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character object rawcode.

Options

- `-o, --out <out>`. Write the resolved bundle to this JSON file, so 'bundle diff' and 'bundle merge' can read it.

### bundle object

Resolve everything an object of any kind depends on ('bundle unit', generalized).

Usage

```
wc3ctl bundle object <map> <rawcode> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character object rawcode.

Options

- `--kind <kind>`. Root object type: unit|item|ability|destructable|doodad|buff|upgrade. Default `unit`.
- `-o, --out <out>`. Write the resolved bundle to this JSON file, so 'bundle diff' and 'bundle merge' can read it.

### bundle diff

Compare two dependency bundles by their object and asset members, listing what each adds.

Usage

```
wc3ctl bundle diff <bundle-a> <bundle-b>
```

Arguments

- `<bundle-a>` (required). Path to a bundle JSON file (from 'bundle unit -o').
- `<bundle-b>` (required). Path to a second bundle JSON file.

### bundle merge

Merge two dependency bundles into one, refusing when the same rawcode is defined differently.

Usage

```
wc3ctl bundle merge <bundle-a> <bundle-b> [options]
```

Arguments

- `<bundle-a>` (required). Path to a bundle JSON file (from 'bundle unit -o').
- `<bundle-b>` (required). Path to a second bundle JSON file.

Options

- `-o, --out <out>`. Path to write the merged bundle JSON.

## port

Port content between maps.

Usage

```
wc3ctl port
```

Subcommands

- `unit`. Port a unit (its custom objects + assets + strings, and best-effort its trigger script) from one map into another, auto-remapping rawcode collisions.
- `deps`. Report the full dependency closure of a rawcode, its objects grouped by kind, its assets with sizes, its display strings and its script functions.
- `preview`. Dry run, listing exactly what a port of the rawcode into the target would add or change, writing nothing. Same code path as 'port unit', so the two cannot drift.
- `into-version`. Port a unit, then downgrade the result to a target build's schema. Object fields the build does not declare are removed, and script identifiers it lacks are flagged. Writes a copy, never in place.

### port unit

Port a unit (its custom objects + assets + strings, and best-effort its trigger script) from one map into another, auto-remapping rawcode collisions.

Usage

```
wc3ctl port unit <source-map> <rawcode> <target-map> [options]
```

Arguments

- `<source-map>` (required). Map to port FROM.
- `<rawcode>` (required). Four-character unit rawcode, or a comma-separated list (H000,H001,…) to port several units into the same target in one operation.
- `<target-map>` (required). Map to port INTO.

Options

- `-o, --out <out>`. Output directory (or output file for a single named extraction). Default: current directory.
- `--no-script`. Port object data + assets + strings only; skip the best-effort JASS script closure append.
- `--dry-run`. Preview the port: print the full report (remaps, objects, files, strings, script) without writing anything.
- `--synth-dispatch`. Single-unit port only. Instead of carrying the source's shared cast dispatcher (every gate it was written to satisfy on the source map, a placed-hero registration array, a map rect that reads null off the source map, a cooldown hashtable), read the unit's OWN branch of it and synthesize a fresh, minimal, self-contained dispatcher for exactly its abilities. Does not make the unit dependency-free (spell handlers still need their own carried state), only replaces the SHARED entry point into them. Default off: a plain port is unaffected.
- `--bootstrap-state`. Construct every carried global the ported code reads but nothing ever assigns (a region CreateRegions never carried, a timer only a hand-written Init reached through ExecuteFunc("Init") ever built), so it holds a real handle instead of silently sitting at its type default. Only timer, group, hashtable, trigger, rect and force are ever constructed this way (a rect is built empty, never guessed at). Anything else this cannot safely build, unit, item, destructable, effect, code, framehandle, an array, is left alone and reported instead. Default off, a plain port is unaffected.

### port deps

Report the full dependency closure of a rawcode, its objects grouped by kind, its assets with sizes, its display strings and its script functions.

Usage

```
wc3ctl port deps <map> <rawcode> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character object rawcode.

Options

- `--kind <kind>`. Root object type: unit|item|ability|destructable|doodad|buff|upgrade. Default `unit`.

### port preview

Dry run, listing exactly what a port of the rawcode into the target would add or change, writing nothing. Same code path as 'port unit', so the two cannot drift.

Usage

```
wc3ctl port preview <source-map> <rawcode> <target-map> [options]
```

Arguments

- `<source-map>` (required). Map to port FROM.
- `<rawcode>` (required). Four-character unit rawcode to preview.
- `<target-map>` (required). Map to port INTO.

Options

- `--no-script`. Port object data + assets + strings only; skip the best-effort JASS script closure append.

### port into-version

Port a unit, then downgrade the result to a target build's schema. Object fields the build does not declare are removed, and script identifiers it lacks are flagged. Writes a copy, never in place.

Usage

```
wc3ctl port into-version <source-map> <rawcode> <target-map> [options]
```

Arguments

- `<source-map>` (required). Map to port FROM.
- `<rawcode>` (required). Four-character unit rawcode to preview.
- `<target-map>` (required). Map to port INTO.

Options

- `--target <target>` (required). Target build name or schema folder path.
- `--schemas <schemas>`. Schemas directory, when --target is a build name rather than a folder.
- `-o, --out <out>`. Output directory (or output file for a single named extraction). Default: current directory.
- `--no-script`. Port object data + assets + strings only; skip the best-effort JASS script closure append.

## convert

Convert asset files between WC3 and standard formats (disk-to-disk).

Usage

```
wc3ctl convert <input> <output>
```

Arguments

- `<input>` (required). Source file: an image (.blp/.png/.jpg/.jpeg/.bmp/.tga/.gif) or a model (.mdx/.mdl).
- `<output>` (required). Destination file; its extension picks the format. Images: .png/.jpg/.jpeg/.bmp/.tga/.gif/.blp. Models: .obj (a .mtl is written beside it).

## validate

Check a map for problems (missing/empty files, loader errors, a script that cannot compile). Exits 2 if invalid.

Usage

```
wc3ctl validate <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--deep`. Also run pjass, the game's own JASS parser, over the map script (needs a Warcraft III install).
- `--strict`. Add the stricter checks on top of the default, a map with no (listfile), a script that declares no entry point function, and a placement whose type neither the map nor the installed game defines. The default validate is unchanged without it.

## audit

Check abilities against the map's own tooltips, plus inherited requirements, portrait-risk models, dangling object references and model paths that load no file. Read-only. Exits 2 if any error-level issue is found. The subcommands check a hero's wiring, one ability, object data fidelity and runtime readiness.

Usage

```
wc3ctl audit <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--check <check>`. Limit to named checks. Repeatable. Default runs all: level-gap, level-tooltip, tooltip-claim, orphan-ability, requirement, portrait-risk, dangling-reference, missing-model

Subcommands

- `hero`. Verify every ability of a placed hero is wired end to end (object present, dispatch carried, trigger attached, event covers the player, handler not empty, identity array registered, init called exactly once). Reports the exact missing link per ability. Exits 2 if any fail.
- `ability`. Walk each ability's own runtime chain, not just whether a cast reaches a live trigger: is the handler carried and not gutted, does its follow-up loop actually start, does anything in its closure deal damage, is every pause matched by an unpause, does a timer callback still point at a declared function, is every global it reads actually assigned, does a special-effect asset still exist. 'audit hero' only proves dispatch, a hero can read '8 of 8 wired' there and still do nothing in game. Exits 2 if any ability fails a check.
- `fidelity`. Compare an object and its whole custom closure between a source and target map, reporting fields, per-level values, levels, and objects the port failed to carry. Rawcode remaps and inlined trigger strings are accounted for and not counted as faults. Exits 2 if any real data loss is found.
- `readiness`. Check whether a placed hero's script would actually run, not just fire. Verifies InitGlobals and RunInitializationTriggers are both carried and called, and that every udg_ global the hero's own carried code reads is assigned somewhere in the script. A hero can audit clean on 'audit hero' and still do nothing in game if its damage, range or duration values were never initialized. Exits 2 if any problem is found.
- `trend`. Run the behavioural audit against two maps and report the findings the second added and removed, grouped by check. Read-only.

### audit hero

Verify every ability of a placed hero is wired end to end (object present, dispatch carried, trigger attached, event covers the player, handler not empty, identity array registered, init called exactly once). Reports the exact missing link per ability. Exits 2 if any fail.

Usage

```
wc3ctl audit hero <map> [<hero>]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `[<hero>]` (optional). Hero rawcode to audit. Omit to audit every hero placed on the map.

### audit ability

Walk each ability's own runtime chain, not just whether a cast reaches a live trigger: is the handler carried and not gutted, does its follow-up loop actually start, does anything in its closure deal damage, is every pause matched by an unpause, does a timer callback still point at a declared function, is every global it reads actually assigned, does a special-effect asset still exist. 'audit hero' only proves dispatch, a hero can read '8 of 8 wired' there and still do nothing in game. Exits 2 if any ability fails a check.

Usage

```
wc3ctl audit ability <map> [<hero>]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `[<hero>]` (optional). Hero rawcode to audit. Omit to audit every hero placed on the map.

### audit fidelity

Compare an object and its whole custom closure between a source and target map, reporting fields, per-level values, levels, and objects the port failed to carry. Rawcode remaps and inlined trigger strings are accounted for and not counted as faults. Exits 2 if any real data loss is found.

Usage

```
wc3ctl audit fidelity <source-map> <target-map> <rawcode>
```

Arguments

- `<source-map>` (required). Path to the SOURCE map the object was ported FROM.
- `<target-map>` (required). Path to the TARGET map the object was ported INTO.
- `<rawcode>` (required). Root object rawcode to compare (the same rawcode in both maps).

### audit readiness

Check whether a placed hero's script would actually run, not just fire. Verifies InitGlobals and RunInitializationTriggers are both carried and called, and that every udg_ global the hero's own carried code reads is assigned somewhere in the script. A hero can audit clean on 'audit hero' and still do nothing in game if its damage, range or duration values were never initialized. Exits 2 if any problem is found.

Usage

```
wc3ctl audit readiness <map> [<hero>]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `[<hero>]` (optional). Hero rawcode to check. Omit to check every hero placed on the map.

### audit trend

Run the behavioural audit against two maps and report the findings the second added and removed, grouped by check. Read-only.

Usage

```
wc3ctl audit trend <map> <other> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<other>` (required). The second map, the one treated as the newer state.

Options

- `--check <check>`. Limit to named checks. Repeatable. Default runs all: level-gap, level-tooltip, tooltip-claim, orphan-ability, requirement, portrait-risk, dangling-reference, missing-model

## deprotect

Recover the file names a protected map stripped, from its own data, its script, its models' texture declarations, and any listfiles supplied. Read-only without --apply.

Usage

```
wc3ctl deprotect <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--listfile <listfile>`. Extra name dictionary, one name per line. Repeatable. Community listfiles and the listfiles of sibling maps both work.
- `--apply`. Write the recovered names into the map as a real (listfile), so every later load names those entries. Changes no other byte.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## debug

Instrument a map so the running game reports what it is doing, for problems static analysis cannot see.

Usage

```
wc3ctl debug
```

Subcommands

- `wiring`. Instruments an already-ported map's cast-dispatch chain with BJDebugMsg calls, so the running game reports exactly where a hero's cast attempt stops. Use this when a hero audits clean on 'audit hero' and 'audit readiness' and still cannot cast in game, since neither static check can see whether the dispatcher's own gate passes, whether its own per-hero branch is ever entered, or whether a deferred trigger registration ran before the cast. Opt in, meant for a disposable copy of a map, never run as part of an ordinary port.
- `trace-load`. Instrument a map's initialisation with numbered markers, load it, and report the last step that ran and the first that did not. Turns 'stuck on loading' into a line number.
- `game-hang`. Sample a running Warcraft III process to tell whether it is SPINNING (looping in its own code) or WAITING (blocked on I/O or a lock). Run it while a map is stuck on the loading screen.

### debug wiring

Instruments an already-ported map's cast-dispatch chain with BJDebugMsg calls, so the running game reports exactly where a hero's cast attempt stops. Use this when a hero audits clean on 'audit hero' and 'audit readiness' and still cannot cast in game, since neither static check can see whether the dispatcher's own gate passes, whether its own per-hero branch is ever entered, or whether a deferred trigger registration ran before the cast. Opt in, meant for a disposable copy of a map, never run as part of an ordinary port.

Usage

```
wc3ctl debug wiring <map> [<hero>] [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `[<hero>]` (optional). Hero rawcode to add a per-hero branch checkpoint for. Omit to instrument only the checkpoints shared by every cast, with no per-hero branch print.

Options

- `-o, --out <out>`. Output map path. Default is '<map>.debug.<ext>' next to the input, the original is never overwritten.

### debug trace-load

Instrument a map's initialisation with numbered markers, load it, and report the last step that ran and the first that did not. Turns 'stuck on loading' into a line number.

Usage

```
wc3ctl debug trace-load <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--timeout <timeout>`. Seconds to wait. Default `180`.
- `--documents <documents>`. Warcraft III documents folder. Default: Documents\Warcraft III.

### debug game-hang

Sample a running Warcraft III process to tell whether it is SPINNING (looping in its own code) or WAITING (blocked on I/O or a lock). Run it while a map is stuck on the loading screen.

Usage

```
wc3ctl debug game-hang [options]
```

Options

- `--seconds <seconds>`. How long to sample. Longer is steadier; 5s is enough to tell spinning from waiting. Default `5`.
- `--threads <threads>`. How many busiest threads to list. Default `6`.
- `--locate <locate>`. Take this many live instruction-pointer samples of the busiest thread and report which module it is executing in. Needs to briefly suspend that thread, so it is opt-in. Use 40 or so; 0 disables it. Default `0`.
- `--process <process>`. Process name override, if the game runs under a name this does not know.

## place

Place objects on a map and save the edited copy.

Usage

```
wc3ctl place
```

Subcommands

- `doodad`. Place a doodad at (x, y) and save the edited map. Writes war3map.doo.
- `region`. Add a rectangular region and save the edited map. Writes war3map.w3r.
- `unit`. Place a unit at (x, y) and save the edited map. Writes war3mapUnits.doo, and the map's script when a spawn call has to be wired (a preplaced unit no script creates never appears in the game).
- `start-location`. Place or move a player's start location at (x, y) and save the edited map. One per player. An existing one for this player is moved. Writes war3mapUnits.doo, and the map's script when a spawn call has to be wired.
- `item`. Place a preplaced item at (x, y) and save the edited map. Writes war3mapUnits.doo (item slot), and the map's script when a spawn call has to be wired.
- `grid`. Place a regular rows x cols grid of copies of one type and save the edited map. The grid is centred on the map origin. Writes war3mapUnits.doo for units or war3map.doo for doodads.
- `scatter`. Place N copies of a unit type at deterministic random positions and save the edited map. Positions are uniform inside a named region or the whole playable area, and the same seed reproduces the same layout. Writes war3mapUnits.doo.
- `sync`. Regenerate the runtime creation script (CreateAllUnits/CreateAllItems in war3map.j) from war3mapUnits.doo so preplaced widgets actually spawn in game. Repairs maps whose units were placed without the spawning script. Placing through wc3ctl does this automatically; this repairs a map that predates it.

### place doodad

Place a doodad at (x, y) and save the edited map. Writes war3map.doo.

Usage

```
wc3ctl place doodad <map> <rawcode> <x> <y> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character doodad type rawcode.
- `<x>` (required). World X coordinate.
- `<y>` (required). World Y coordinate.

Options

- `--z <z>`. World Z (height offset). Default: 0. Default `0`.
- `--rotation <rotation>`. Facing angle in radians. Default: 0. Default `0`.
- `--scale <scale>`. Uniform scale. Default: 1. Default `1`.
- `--variation <variation>`. Doodad variation index. Default: 0. Default `0`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### place region

Add a rectangular region and save the edited map. Writes war3map.w3r.

Usage

```
wc3ctl place region <map> <name> <left> <bottom> <right> <top> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). Region name.
- `<left>` (required). West edge (min X).
- `<bottom>` (required). South edge (min Y).
- `<right>` (required). East edge (max X).
- `<top>` (required). North edge (max Y).

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### place unit

Place a unit at (x, y) and save the edited map. Writes war3mapUnits.doo, and the map's script when a spawn call has to be wired (a preplaced unit no script creates never appears in the game).

Usage

```
wc3ctl place unit <map> <rawcode> <owner> <x> <y> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character unit type rawcode.
- `<owner>` (required). Owning player id (0-based; 0 = red).
- `<x>` (required). World X coordinate.
- `<y>` (required). World Y coordinate.

Options

- `--z <z>`. World Z (height offset). Default: 0. Default `0`.
- `--rotation <rotation>`. Facing angle in radians. Default: 0. Default `0`.
- `--scale <scale>`. Uniform scale. Default: 1. Default `1`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### place start-location

Place or move a player's start location at (x, y) and save the edited map. One per player. An existing one for this player is moved. Writes war3mapUnits.doo, and the map's script when a spawn call has to be wired.

Usage

```
wc3ctl place start-location <map> <player> <x> <y> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<player>` (required). Player whose start location this is (0-based; 0 = red).
- `<x>` (required). World X coordinate.
- `<y>` (required). World Y coordinate.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### place item

Place a preplaced item at (x, y) and save the edited map. Writes war3mapUnits.doo (item slot), and the map's script when a spawn call has to be wired.

Usage

```
wc3ctl place item <map> <rawcode> <x> <y> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character item type rawcode.
- `<x>` (required). World X coordinate.
- `<y>` (required). World Y coordinate.

Options

- `--z <z>`. World Z (height offset). Default: 0. Default `0`.
- `--rotation <rotation>`. Facing angle in radians. Default: 0. Default `0`.
- `--scale <scale>`. Uniform scale. Default: 1. Default `1`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### place grid

Place a regular rows x cols grid of copies of one type and save the edited map. The grid is centred on the map origin. Writes war3mapUnits.doo for units or war3map.doo for doodads.

Usage

```
wc3ctl place grid <map> <rawcode> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character unit or doodad type rawcode.

Options

- `--rows <rows>` (required). Number of grid rows.
- `--cols <cols>` (required). Number of grid columns.
- `--spacing <spacing>`. Spacing between adjacent cells in world units. Defaults to 128, one tile. Default `128`.
- `--owner <owner>`. Owning player id for a unit grid (0-based, 0 = red). Defaults to 0. Default `0`.
- `--kind <kind>`. Placement kind, unit or doodad. Defaults to unit. Default `unit`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### place scatter

Place N copies of a unit type at deterministic random positions and save the edited map. Positions are uniform inside a named region or the whole playable area, and the same seed reproduces the same layout. Writes war3mapUnits.doo.

Usage

```
wc3ctl place scatter <map> <rawcode> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<rawcode>` (required). Four-character unit type rawcode.

Options

- `--count <count>` (required). Number of copies to place.
- `--region <region>`. Region name to scatter inside. Omit to scatter across the whole playable map.
- `--seed <seed>`. Deterministic random seed. Defaults to 0. Default `0`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### place sync

Regenerate the runtime creation script (CreateAllUnits/CreateAllItems in war3map.j) from war3mapUnits.doo so preplaced widgets actually spawn in game. Repairs maps whose units were placed without the spawning script. Placing through wc3ctl does this automatically; this repairs a map that predates it.

Usage

```
wc3ctl place sync <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## placed

Placed-object census and reference checks, stats, missing-refs and fix-refs.

Usage

```
wc3ctl placed
```

Subcommands

- `stats`. Count placed units and doodads by rawcode and kind, resolving each type's name from the map's object data and, when --game-dir is given, the base game data. Read-only.
- `missing-refs`. List placements whose rawcode is defined neither in the map's object data nor, when --game-dir is given, in the base game data. Read-only.
- `fix-refs`. Drop (or, with --to, rewrite) placements that reference a missing rawcode and save the edited map, reporting each change. Refuses when nothing is wrong. Writes war3mapUnits.doo and war3map.doo.

### placed stats

Count placed units and doodads by rawcode and kind, resolving each type's name from the map's object data and, when --game-dir is given, the base game data. Read-only.

Usage

```
wc3ctl placed stats <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### placed missing-refs

List placements whose rawcode is defined neither in the map's object data nor, when --game-dir is given, in the base game data. Read-only.

Usage

```
wc3ctl placed missing-refs <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### placed fix-refs

Drop (or, with --to, rewrite) placements that reference a missing rawcode and save the edited map, reporting each change. Refuses when nothing is wrong. Writes war3mapUnits.doo and war3map.doo.

Usage

```
wc3ctl placed fix-refs <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--to <to>`. Rewrite each missing placement's rawcode to this four-character code instead of dropping it.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## palette

List the doodad types placeable on a map (base-game catalog + the map's own object-data).

Usage

```
wc3ctl palette <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

## terrain

Terrain editing: height, cliffs, ramps, textures, water, blight. Brush commands work a radius at a time, 'fill' works a rectangle at a time.

Usage

```
wc3ctl terrain
```

Subcommands

- `fill`. Apply a tool over an inclusive rectangle of corners, instead of one brush dab at a time. Corners may be given in any order and an off-grid rectangle is clipped.
- `stats`. Summarize terrain: tile count, height range and cliff range.
- `audit`. Report the terrain's size and its measurable waste: grid and tile counts, distinct ground and cliff textures in use, cliff/ramp/water/blight totals, the war3map.w3e byte size, and any uniform, all-zero or out-of-range-tileset pattern found. Read-only.
- `optimize`. Shrink war3map.w3e by dropping the declared ground and cliff tile types the terrain never references and remapping every corner's texture index onto the compacted list. Every other file is written back byte for byte. Writes a new map (never the input).
- `deform`. Raise/lower/set/flatten/smooth ground height over a brush.
- `cliff`. Raise/lower/set the cliff (stepped-terrain) level over a brush.
- `ramp`. Toggle the ramp (sloped cliff transition) flag over a brush.
- `paint`. Paint a ground texture over a brush.
- `water`. Set/raise/lower/remove water over a brush.
- `export-heightmap`. Write the terrain heights as a 16-bit grayscale PNG, one pixel per corner. Read-only.
- `import-heightmap`. Set the tile heights from a grayscale PNG. The PNG must match the map's corner grid exactly, and a mismatch is refused.
- `smooth`. Average every corner height with its neighbours, N times, across the whole map.
- `info`. Grid extents in corners, plus the map's ground and cliff tile lists. Texture fields are indexes into those lists.
- `corner`. One terrain corner at a time: get, set. The brush commands cover areas.
- `blight`. Set or clear the blight (corrupted ground) flag over a brush.

### terrain fill

Apply a tool over an inclusive rectangle of corners, instead of one brush dab at a time. Corners may be given in any order and an off-grid rectangle is clipped.

Usage

```
wc3ctl terrain fill <map> <x0> <y0> <x1> <y1> <tool> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<x0>` (required). First corner column, 0-based.
- `<y0>` (required). First corner row, 0-based.
- `<x1>` (required). Second corner column, 0-based (inclusive, any order).
- `<y1>` (required). Second corner row, 0-based (inclusive, any order).
- `<tool>` (required). What to apply over the rectangle.

Options

- `--value <value>`. Height, cliff step count, or ground tile index, depending on the tool. Ignored by Flatten, Ramp, RampOff, WaterRemove, Blight and BlightOff. Default `1`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### terrain stats

Summarize terrain: tile count, height range and cliff range.

Usage

```
wc3ctl terrain stats <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### terrain audit

Report the terrain's size and its measurable waste: grid and tile counts, distinct ground and cliff textures in use, cliff/ramp/water/blight totals, the war3map.w3e byte size, and any uniform, all-zero or out-of-range-tileset pattern found. Read-only.

Usage

```
wc3ctl terrain audit <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### terrain optimize

Shrink war3map.w3e by dropping the declared ground and cliff tile types the terrain never references and remapping every corner's texture index onto the compacted list. Every other file is written back byte for byte. Writes a new map (never the input).

Usage

```
wc3ctl terrain optimize <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### terrain deform

Raise/lower/set/flatten/smooth ground height over a brush.

Usage

```
wc3ctl terrain deform <map> <cx> <cy> <radius> <op> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<cx>` (required). Brush centre tile X (column).
- `<cy>` (required). Brush centre tile Y (row).
- `<radius>` (required). Brush radius in tiles.
- `<op>` (required). raise|lower|set|flatten|smooth

Options

- `--amount <amount>`. Step for raise/lower; target for set. Default: 1. Default `1`.
- `--shape <shape>`. Brush footprint: Circle or Square. Default `Circle`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### terrain cliff

Raise/lower/set the cliff (stepped-terrain) level over a brush.

Usage

```
wc3ctl terrain cliff <map> <cx> <cy> <radius> <op> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<cx>` (required). Brush centre tile X (column).
- `<cy>` (required). Brush centre tile Y (row).
- `<radius>` (required). Brush radius in tiles.
- `<op>` (required). raise|lower|set

Options

- `--level <level>`. Step count for raise/lower; absolute level for set. Default: 1. Default `1`.
- `--shape <shape>`. Brush footprint: Circle or Square. Default `Circle`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### terrain ramp

Toggle the ramp (sloped cliff transition) flag over a brush.

Usage

```
wc3ctl terrain ramp <map> <cx> <cy> <radius> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<cx>` (required). Brush centre tile X (column).
- `<cy>` (required). Brush centre tile Y (row).
- `<radius>` (required). Brush radius in tiles.

Options

- `--off`. Clear the ramp flag instead of setting it.
- `--shape <shape>`. Brush footprint: Circle or Square. Default `Circle`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### terrain paint

Paint a ground texture over a brush.

Usage

```
wc3ctl terrain paint <map> <cx> <cy> <radius> <texture> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<cx>` (required). Brush centre tile X (column).
- `<cy>` (required). Brush centre tile Y (row).
- `<radius>` (required). Brush radius in tiles.
- `<texture>` (required). Ground texture slot index in the map's tileset table.

Options

- `--variation <variation>`. Tile variation; omit for default.
- `--shape <shape>`. Brush footprint: Circle or Square. Default `Circle`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### terrain water

Set/raise/lower/remove water over a brush.

Usage

```
wc3ctl terrain water <map> <cx> <cy> <radius> <op> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<cx>` (required). Brush centre tile X (column).
- `<cy>` (required). Brush centre tile Y (row).
- `<radius>` (required). Brush radius in tiles.
- `<op>` (required). set|raise|lower|remove

Options

- `--amount <amount>`. Absolute height for set; delta for raise/lower. Default: 0. Default `0`.
- `--shape <shape>`. Brush footprint: Circle or Square. Default `Circle`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### terrain export-heightmap

Write the terrain heights as a 16-bit grayscale PNG, one pixel per corner. Read-only.

Usage

```
wc3ctl terrain export-heightmap <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>` (required). Output PNG path.

### terrain import-heightmap

Set the tile heights from a grayscale PNG. The PNG must match the map's corner grid exactly, and a mismatch is refused.

Usage

```
wc3ctl terrain import-heightmap <map> <png> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<png>` (required). Path to the grayscale heightmap PNG.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### terrain smooth

Average every corner height with its neighbours, N times, across the whole map.

Usage

```
wc3ctl terrain smooth <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--passes <passes>`. Number of smoothing passes, default 1. Default `1`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### terrain info

Grid extents in corners, plus the map's ground and cliff tile lists. Texture fields are indexes into those lists.

Usage

```
wc3ctl terrain info <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### terrain corner

One terrain corner at a time: get, set. The brush commands cover areas.

Usage

```
wc3ctl terrain corner
```

Subcommands

- `get`. Show one terrain corner's full state.
- `set`. Set a field on ONE terrain corner and save the edited map. For area work use 'terrain deform', 'terrain paint' and the other brush commands.

#### terrain corner get

Show one terrain corner's full state.

Usage

```
wc3ctl terrain corner get <map> <col> <row>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<col>` (required). Corner column, 0-based.
- `<row>` (required). Corner row, 0-based.

#### terrain corner set

Set a field on ONE terrain corner and save the edited map. For area work use 'terrain deform', 'terrain paint' and the other brush commands.

Usage

```
wc3ctl terrain corner set <map> <col> <row> <field> <value> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<col>` (required). Corner column, 0-based.
- `<row>` (required). Corner row, 0-based.
- `<field>` (required). Field: GroundHeight|AddGroundHeight|WaterHeight|GroundTexture|CliffLevel.
- `<value>` (required). New value.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### terrain blight

Set or clear the blight (corrupted ground) flag over a brush.

Usage

```
wc3ctl terrain blight <map> <cx> <cy> <radius> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<cx>` (required). Brush centre tile X (column).
- `<cy>` (required). Brush centre tile Y (row).
- `<radius>` (required). Brush radius in tiles.

Options

- `--clear`. Clear blight instead of setting it.
- `--shape <shape>`. Brush footprint: Circle or Square. Default `Circle`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## sound

Sound editing: the map's catalog (war3map.w3s) via list, add, set, remove, and a standalone file optimiser via optimize.

Usage

```
wc3ctl sound
```

Subcommands

- `list`. List the map's sound definitions.
- `add`. Add a new sound definition and save the edited map.
- `set`. Set a field on a sound and save the edited map.
- `remove`. Remove a sound definition and save the edited map.
- `optimize`. Decode a sound, resample it to a target rate and optionally downmix to mono, and write a smaller 16-bit .wav. Reports the before and after sample rate, channels and byte size.

### sound list

List the map's sound definitions.

Usage

```
wc3ctl sound list <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### sound add

Add a new sound definition and save the edited map.

Usage

```
wc3ctl sound add <map> <name> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). Sound label (the key triggers/UI reference).

Options

- `--file <file>`. Audio file path inside the map (e.g. war3mapImported\snd.wav).
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### sound set

Set a field on a sound and save the edited map.

Usage

```
wc3ctl sound set <map> <name> <field> <value> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). Sound label (the key triggers/UI reference).
- `<field>` (required). Field: Name|File|Eax|Volume|Pitch|PitchVariance|FadeIn|FadeOut|Priority|Channel|Flags|MinDistance|MaxDistance|DistanceCutoff|ConeInside|ConeOutside|ConeOutsideVolume.
- `<value>` (required). New value for the field.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### sound remove

Remove a sound definition and save the edited map.

Usage

```
wc3ctl sound remove <map> <name> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). Sound label (the key triggers/UI reference).

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### sound optimize

Decode a sound, resample it to a target rate and optionally downmix to mono, and write a smaller 16-bit .wav. Reports the before and after sample rate, channels and byte size.

Usage

```
wc3ctl sound optimize <file> [options]
```

Arguments

- `<file>` (required). Path to a .wav or .mp3 sound file.

Options

- `-o, --out <out>` (required). Output .wav path. The input is never modified.
- `--rate <rate>`. Target sample rate in Hz. Default 22050. Default `22050`.
- `--mono`. Downmix to a single channel. Default keeps the source channel count.

## camera

Camera catalog editing (war3map.w3c). List, add, set, remove, tour.

Usage

```
wc3ctl camera
```

Subcommands

- `list`. List the map's cameras.
- `add`. Add a camera at a target position and save the edited map.
- `set`. Set a field on a camera and save the edited map.
- `remove`. Remove a camera and save the edited map.
- `tour`. Interpolate N cameras between two existing cameras as a smooth path and save the edited map. Every field is blended linearly and the generated cameras sit strictly between the endpoints, named <from>-tour-i.

### camera list

List the map's cameras.

Usage

```
wc3ctl camera list <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### camera add

Add a camera at a target position and save the edited map.

Usage

```
wc3ctl camera add <map> <name> <x> <y> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). Camera name.
- `<x>` (required). Camera target X (map coordinates).
- `<y>` (required). Camera target Y (map coordinates).

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### camera set

Set a field on a camera and save the edited map.

Usage

```
wc3ctl camera set <map> <name> <field> <value> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). Camera name.
- `<field>` (required). Field: Name|TargetX|TargetY|ZOffset|Rotation|AngleOfAttack|TargetDistance|Roll|FieldOfView|FarClippingPlane|NearClippingPlane|LocalPitch|LocalYaw|LocalRoll.
- `<value>` (required). New value for the field.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### camera remove

Remove a camera and save the edited map.

Usage

```
wc3ctl camera remove <map> <name> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). Camera name.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### camera tour

Interpolate N cameras between two existing cameras as a smooth path and save the edited map. Every field is blended linearly and the generated cameras sit strictly between the endpoints, named <from>-tour-i.

Usage

```
wc3ctl camera tour <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--from <from>` (required). Name of the camera the tour starts at.
- `--to <to>` (required). Name of the camera the tour ends at.
- `--count <count>`. How many cameras to interpolate between the two. Default `3`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## pathing

Pathing-map editing (war3map.wpm): paint walk/build/fly/water bits over a brush.

Usage

```
wc3ctl pathing
```

Subcommands

- `paint`. Paint pathing bits over a brush and save the edited map. Writes war3map.wpm.
- `export-png`. Render the pathing map to a colour-coded PNG with a legend of the pathing flags. Read-only.
- `import-png`. Set the pathing flags from a colour-coded PNG. An unknown colour is refused.
- `validate`. Flag enclosed unpathable pockets, start locations on unpathable tiles, and unreachable map corners. Read-only.

### pathing paint

Paint pathing bits over a brush and save the edited map. Writes war3map.wpm.

Usage

```
wc3ctl pathing paint <map> <cx> <cy> <radius> <flags> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<cx>` (required). Brush centre pathing-cell X.
- `<cy>` (required). Brush centre pathing-cell Y.
- `<radius>` (required). Brush radius in pathing cells.
- `<flags>` (required). Pathing bits to affect (comma-separated): Walk, Fly, Build, Blight, Water. A set bit RESTRICTS that capability (Walk set = units cannot walk there).

Options

- `--op <op>`. How the bits combine: Set, Clear or Toggle. Default `Set`.
- `--shape <shape>`. Brush footprint: Circle or Square. Default `Circle`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### pathing export-png

Render the pathing map to a colour-coded PNG with a legend of the pathing flags. Read-only.

Usage

```
wc3ctl pathing export-png <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>` (required). Output PNG path.

### pathing import-png

Set the pathing flags from a colour-coded PNG. An unknown colour is refused.

Usage

```
wc3ctl pathing import-png <map> <png> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<png>` (required). Path to the colour-coded pathing PNG.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### pathing validate

Flag enclosed unpathable pockets, start locations on unpathable tiles, and unreachable map corners. Read-only.

Usage

```
wc3ctl pathing validate <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

## map-info

Scenario fields (war3map.w3i): get, set (name, author, description, tileset, ...).

Usage

```
wc3ctl map-info
```

Subcommands

- `get`. Show the map's editable scenario fields.
- `set`. Set a scenario field and save the edited map.

### map-info get

Show the map's editable scenario fields.

Usage

```
wc3ctl map-info get <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### map-info set

Set a scenario field and save the edited map.

Usage

```
wc3ctl map-info set <map> <field> <value> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<field>` (required). Field: MapName|Author|Description|RecommendedPlayers|Tileset|LightEnvironment|GlobalWeather|SoundEnvironment|WaterTintColor|FogStyle|FogStartZ|FogEndZ|FogDensity|FogColor|LoadingScreenIndex|LoadingScreenPath|LoadingScreenTitle|LoadingScreenSubtitle|LoadingScreenText|PrologueTitle|PrologueSubtitle|PrologueText|MeleeMap|HideMinimapInPreview|ModifyAllyPriorities|MaskedAreasPartiallyVisible|FixedPlayerSettings|UseCustomForces|UseCustomTechtree|UseCustomAbilities|UseCustomUpgrades|WaterWavesOnCliffShores|WaterWavesOnRollingShores|HasTerrainFog|HasWaterTint|ItemClassification|AccurateProbabilities.
- `<value>` (required). New value for the field.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## player

Player slots (war3map.w3i): list, set-force.

Usage

```
wc3ctl player
```

Subcommands

- `list`. List the map's player slots.
- `set-force`. Move a player into a force and save the edited map.
- `set-name`. Set a player's display name in the map info and save the edited map.
- `set-color`. Set a player's team color in the map script and save the edited map.
- `set-race`. Set a player's race in the map info and save the edited map.

### player list

List the map's player slots.

Usage

```
wc3ctl player list <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### player set-force

Move a player into a force and save the edited map.

Usage

```
wc3ctl player set-force <map> <player> <force> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<player>` (required). Player id (0-based).
- `<force>` (required). Force index (0-based).

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### player set-name

Set a player's display name in the map info and save the edited map.

Usage

```
wc3ctl player set-name <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--player <player>` (required). Player id, 0-based.
- `--name <name>` (required). New name text.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### player set-color

Set a player's team color in the map script and save the edited map.

Usage

```
wc3ctl player set-color <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--player <player>` (required). Player id, 0-based.
- `--color <color>` (required). Team color name, for example red, lightblue or darkgreen.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### player set-race

Set a player's race in the map info and save the edited map.

Usage

```
wc3ctl player set-race <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--player <player>` (required). Player id, 0-based.
- `--race <race>` (required). human, orc, undead, nightelf or random.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## force

Forces / teams (war3map.w3i): list, set-flags.

Usage

```
wc3ctl force
```

Subcommands

- `list`. List the map's forces (teams).
- `set-flags`. Set a force's alliance/sharing flags and save the edited map.
- `rename`. Rename a force and save the edited map.
- `set-players`. Add or remove a player from a force and save the edited map.

### force list

List the map's forces (teams).

Usage

```
wc3ctl force list <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### force set-flags

Set a force's alliance/sharing flags and save the edited map.

Usage

```
wc3ctl force set-flags <map> <force> [<allied>] [<alliedVictory>] [<sharedVision>] [<sharedControl>] [<sharedAdvControl>] [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<force>` (required). Force index (0-based).
- `[<allied>]` (optional). Allied.
- `[<alliedVictory>]` (optional). Allied victory.
- `[<sharedVision>]` (optional). Shared vision.
- `[<sharedControl>]` (optional). Shared unit control.
- `[<sharedAdvControl>]` (optional). Shared advanced unit control.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### force rename

Rename a force and save the edited map.

Usage

```
wc3ctl force rename <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--force <force>` (required). Force index, 0-based.
- `--name <name>` (required). New name text.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### force set-players

Add or remove a player from a force and save the edited map.

Usage

```
wc3ctl force set-players <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--force <force>` (required). Force index, 0-based.
- `--add <add>`. Add this player id to the force.
- `--remove <remove>`. Remove this player id from the force.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## region

Region tooling for war3map.w3r. Create, list, remove, to-rect, validate.

Usage

```
wc3ctl region
```

Subcommands

- `create`. Create a rectangular region (south-west corner plus width and height) and save the edited map. Writes war3map.w3r.
- `list`. List the map's regions.
- `remove`. Remove a region by name and save the edited map.
- `to-rect`. Print one region's four corner values for a trigger Rect call. Read-only.
- `validate`. Flag overlapping regions, zero-area regions and regions outside the playable bounds. Read-only.

### region create

Create a rectangular region (south-west corner plus width and height) and save the edited map. Writes war3map.w3r.

Usage

```
wc3ctl region create <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--name <name>` (required). Region name.
- `--x <x>` (required). South-west corner X (world units).
- `--y <y>` (required). South-west corner Y (world units).
- `--w <w>` (required). Region width (world units).
- `--h <h>` (required). Region height (world units).
- `--weather`. Attach the default weather effect to the region.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### region list

List the map's regions.

Usage

```
wc3ctl region list <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### region remove

Remove a region by name and save the edited map.

Usage

```
wc3ctl region remove <map> <name> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). Region name to remove.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### region to-rect

Print one region's four corner values for a trigger Rect call. Read-only.

Usage

```
wc3ctl region to-rect <map> <region>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<region>` (required). Region name to convert.

### region validate

Flag overlapping regions, zero-area regions and regions outside the playable bounds. Read-only.

Usage

```
wc3ctl region validate <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

## new

Create a blank, World-Editor-openable map at the given path.

Usage

```
wc3ctl new <out> [options]
```

Arguments

- `<out>` (required). Path to write the new .w3x/.w3m map.

Options

- `--name <name>`. Map name (default: Blank Map).
- `--tiles <tiles>`. Playable size in tiles per edge (default: 32).

## repair-generated

Repair a map that wc3ctl generated, whose preplaced-hero helper block is too narrow for the heroes installed into it. Widens the player slots, rewrites the hero owners in the script, and brings war3map.w3i and the map header into line. Only applies to maps carrying wc3ctl's own generated helper, and refuses others.

Usage

```
wc3ctl repair-generated <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## repair

Repair generated/byproduct map issues and save the edited copy.

Usage

```
wc3ctl repair
```

Subcommands

- `generated-heroes`. Repair the generated preplaced-hero helper block so finalized heroes use distinct owner slots and owner-aware spell wiring.
- `reforged-3`. Detect or repair legacy SLK-map incompatibilities introduced by Reforged 3.0.0 (build 24268).
- `portraits`. Remove the camera chunk from models below version 900, which is what makes Reforged 3.0.0 render a black portrait pane. Models with no camera already render correctly.
- `data-pointers`. Correct a levelled field whose data pointer is 0 on some levels and a real DataA to DataF selector on others, which makes those levels name a different column than the author meant. Caused by a level ADDED by an earlier version of this tool.
- `model-paths`. Find model paths in the script and object data that resolve to no file, which the engine logs as 'model creation failed' and retries on every creation. Fixes the ones with a single obvious target, reports the rest.
- `uabi-runtime`. Move each unit type's normal ability list (uabi) out of the object data and add the same abilities by script the moment each unit is created. Hero abilities, morph targets and dummy units (Locust) stay whole, and Locust, Invulnerable and inventory abilities stay in every list. Reported fix for disconnects on maps with thousands of distinct uabi ids.
- `audit-errors`. Fix what audit reports as dangling-reference and requirement errors, with the same detection. Dead ids leave unit and item ability lists, a missing buff gets its base ability's value back, and an inherited requirement the map never defines is cleared.
- `preload`. Find the unit types and abilities a map touches in its first seconds of play, from triggers on an early single timer, and load them while the loading screen is still up. Fixes the freeze right after loading. The map's own code is not changed.

### repair generated-heroes

Repair the generated preplaced-hero helper block so finalized heroes use distinct owner slots and owner-aware spell wiring.

Usage

```
wc3ctl repair generated-heroes <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### repair reforged-3

Detect or repair legacy SLK-map incompatibilities introduced by Reforged 3.0.0 (build 24268).

Usage

```
wc3ctl repair reforged-3 <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--apply`. Apply the detected Warcraft III 3.0.0 compatibility repairs and save a new map.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### repair portraits

Remove the camera chunk from models below version 900, which is what makes Reforged 3.0.0 render a black portrait pane. Models with no camera already render correctly.

Usage

```
wc3ctl repair portraits <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--apply`. Rewrite the affected models. Without it, nothing is changed.
- `--stamp-version <stamp-version>`. After removing the camera, rewrite the model's VERS to this value, for example 1800. Removing the camera alone is not enough on its own.
- `--upgrade-geometry`. With --stamp-version, also insert the fields a version 900 or above model carries that an 800 era one does not, a u32 lod plus char[80] lod name per geoset and a char[80] shader name per material, so the declared version and the actual layout agree. Without it only the version number changes.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### repair data-pointers

Correct a levelled field whose data pointer is 0 on some levels and a real DataA to DataF selector on others, which makes those levels name a different column than the author meant. Caused by a level ADDED by an earlier version of this tool.

Usage

```
wc3ctl repair data-pointers <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--apply`. Correct the pointers. Without it, nothing is changed.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### repair model-paths

Find model paths in the script and object data that resolve to no file, which the engine logs as 'model creation failed' and retries on every creation. Fixes the ones with a single obvious target, reports the rest.

Usage

```
wc3ctl repair model-paths <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--apply`. Rewrite the paths that have exactly one intended file. Without it, nothing is changed.
- `--keep-placeholders`. Leave placeholder models such as '.mdl' untouched. By default they are pointed at a bundled model with no geometry, which still draws nothing but loads.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### repair uabi-runtime

Move each unit type's normal ability list (uabi) out of the object data and add the same abilities by script the moment each unit is created. Hero abilities, morph targets and dummy units (Locust) stay whole, and Locust, Invulnerable and inventory abilities stay in every list. Reported fix for disconnects on maps with thousands of distinct uabi ids.

Usage

```
wc3ctl repair uabi-runtime <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--ids <ids>`. Move only these ability ids, comma or space separated, and leave every other uabi entry in place.
- `--keep-shared <keep-shared>`. Also keep in the data every ability id that at least this many unit types list. Shared ids barely raise the distinct count, and moving them costs a runtime add on every unit. 0 keeps only the fixed list (Locust, Invulnerable, inventories).
- `--apply`. Move the lists and write the adder into the script. Without it, only the counts are shown.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### repair audit-errors

Fix what audit reports as dangling-reference and requirement errors, with the same detection. Dead ids leave unit and item ability lists, a missing buff gets its base ability's value back, and an inherited requirement the map never defines is cleared.

Usage

```
wc3ctl repair audit-errors <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--apply`. Make the edits. Without it, every edit is listed and nothing is changed.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### repair preload

Find the unit types and abilities a map touches in its first seconds of play, from triggers on an early single timer, and load them while the loading screen is still up. Fixes the freeze right after loading. The map's own code is not changed.

Usage

```
wc3ctl repair preload <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--apply`. Write the preload into the script. Without it, only what would be preloaded is shown.
- `--within <within>`. A trigger on a single timer at or below this many seconds counts as early. Default `5`.
- `--function <function>`. Also walk from these script functions, for a map that starts its heavy work another way.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.
- `--all-units`. Also preload every unit type the map defines that the script names anywhere, for a map that spawns new types all game, such as a tower defense's waves.

## trigger

GUI triggers, read the tree, edit a trigger's name and flags, browse the World-Editor catalog, recover a tree from the script, and analyse the tree read-only (to-script, diff, lint, export, dead).

Usage

```
wc3ctl trigger
```

Subcommands

- `catalog`. GUI-trigger catalog from UI\TriggerData.txt: list, describe.
- `recover-from-script`. Rebuild a browsable GUI trigger tree for a map that has NONE, by decompiling the compiled script. The script is not modified. Refuses when the map already has a tree, because a decompiled tree cannot be trusted to replace a real one.
- `to-script`. Render the GUI trigger tree to JASS-style text, the way the World Editor's 'convert to custom text' presents one trigger. Read-only. It is a rendering for reading, not a replacement for the compiled script, and it says so in its own header.
- `diff`. Compare two maps' GUI trigger trees, reporting triggers added, removed and renamed, and event, condition or action changes within a trigger shared by both maps.
- `lint`. Check a map's GUI trigger tree for triggers that can never fire (no event and not run on map initialization), triggers with a duplicate event, and triggers with no action.
- `export`. Export the full GUI trigger tree as structured output. Each trigger carries its name and enabled flags, and its event, condition and action lists with the catalog function name and arguments for every node.
- `dead`. Find triggers that cannot do anything. Those with no action, and those whose events never fire, for example a unit event holding a unit variable nothing assigns or a variable event watching a variable nothing sets.
- `read`. Read the GUI trigger tree (war3map.wtg) plus the custom-text bodies (war3map.wct). Use the sibling verbs to add, remove, rename and re-flag triggers.
- `rename`. Rename a trigger item (a category, a trigger or a deleted stub) and save the edited map.
- `set-enabled`. Enable or disable a trigger. A disabled trigger is not compiled into the map script.
- `set-initially-on`. Set whether a trigger starts switched on. One that is off at map start never fires until something turns it on.
- `set-run-on-map-init`. Set whether a trigger runs on map initialization.
- `add-category`. Add a category to the trigger tree and save the edited map.
- `add`. Add a trigger under a category and save the edited map. The new trigger is enabled and initially on, matching the World Editor. Custom-text triggers cannot be added, because their body lives in war3map.wct, which cannot be written back. This edits the World Editor's trigger source, not the compiled war3map.j the game runs, so the trigger takes effect only after a World Editor save.
- `remove`. Remove a trigger item and save the edited map. Refuses to orphan a category's children (pass --recursive to take the subtree) and refuses when it would shift a war3map.wct code body onto the wrong trigger.
- `add-eca`. Add an event, condition or action to a GUI trigger and save the edited map. Parameters you leave off are filled from the World-Editor table's own defaults, because a function written with the wrong number of parameters produces a map nothing can read. This edits the World Editor's trigger source (war3map.wtg), not the compiled script the game runs (war3map.j), so the trigger takes effect only after the map is opened and saved in the World Editor.
- `remove-eca`. Remove one event, condition or action from a GUI trigger by its position.
- `set-eca-enabled`. Enable or disable ONE event, condition or action within a trigger, which is the World Editor's per-line toggle rather than the whole-trigger one.

### trigger catalog

GUI-trigger catalog from UI\TriggerData.txt: list, describe.

Usage

```
wc3ctl trigger catalog
```

Subcommands

- `list`. List GUI-trigger functions from the World-Editor catalog.
- `describe`. Show full detail for one GUI-trigger function.

#### trigger catalog list

List GUI-trigger functions from the World-Editor catalog.

Usage

```
wc3ctl trigger catalog list [options]
```

Options

- `--file <file>`. Read the catalog from an explicit TriggerData.txt instead of the installed game.
- `--kind <kind>`. Filter by kind: event|condition|action|call.
- `--search <search>`. Case-insensitive substring filter over function name and display name.

#### trigger catalog describe

Show full detail for one GUI-trigger function.

Usage

```
wc3ctl trigger catalog describe <name> [options]
```

Arguments

- `<name>` (required). Function name (e.g. DoNothing).

Options

- `--file <file>`. Read the catalog from an explicit TriggerData.txt instead of the installed game.

### trigger recover-from-script

Rebuild a browsable GUI trigger tree for a map that has NONE, by decompiling the compiled script. The script is not modified. Refuses when the map already has a tree, because a decompiled tree cannot be trusted to replace a real one.

Usage

```
wc3ctl trigger recover-from-script <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### trigger to-script

Render the GUI trigger tree to JASS-style text, the way the World Editor's 'convert to custom text' presents one trigger. Read-only. It is a rendering for reading, not a replacement for the compiled script, and it says so in its own header.

Usage

```
wc3ctl trigger to-script <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### trigger diff

Compare two maps' GUI trigger trees, reporting triggers added, removed and renamed, and event, condition or action changes within a trigger shared by both maps.

Usage

```
wc3ctl trigger diff <mapA> <mapB>
```

Arguments

- `<mapA>` (required). Path to the first .w3x/.w3m map.
- `<mapB>` (required). Path to the second .w3x/.w3m map.

### trigger lint

Check a map's GUI trigger tree for triggers that can never fire (no event and not run on map initialization), triggers with a duplicate event, and triggers with no action.

Usage

```
wc3ctl trigger lint <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### trigger export

Export the full GUI trigger tree as structured output. Each trigger carries its name and enabled flags, and its event, condition and action lists with the catalog function name and arguments for every node.

Usage

```
wc3ctl trigger export <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### trigger dead

Find triggers that cannot do anything. Those with no action, and those whose events never fire, for example a unit event holding a unit variable nothing assigns or a variable event watching a variable nothing sets.

Usage

```
wc3ctl trigger dead <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### trigger read

Read the GUI trigger tree (war3map.wtg) plus the custom-text bodies (war3map.wct). Use the sibling verbs to add, remove, rename and re-flag triggers.

Usage

```
wc3ctl trigger read <map>
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

### trigger rename

Rename a trigger item (a category, a trigger or a deleted stub) and save the edited map.

Usage

```
wc3ctl trigger rename <map> <id> <name> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<id>` (required). Trigger item id, as shown by 'trigger read'.
- `<name>` (required). New name.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### trigger set-enabled

Enable or disable a trigger. A disabled trigger is not compiled into the map script.

Usage

```
wc3ctl trigger set-enabled <map> <id> [<on>] [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<id>` (required). Trigger item id, as shown by 'trigger read'.
- `[<on>]` (optional). true or false.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### trigger set-initially-on

Set whether a trigger starts switched on. One that is off at map start never fires until something turns it on.

Usage

```
wc3ctl trigger set-initially-on <map> <id> [<on>] [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<id>` (required). Trigger item id, as shown by 'trigger read'.
- `[<on>]` (optional). true or false.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### trigger set-run-on-map-init

Set whether a trigger runs on map initialization.

Usage

```
wc3ctl trigger set-run-on-map-init <map> <id> [<on>] [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<id>` (required). Trigger item id, as shown by 'trigger read'.
- `[<on>]` (optional). true or false.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### trigger add-category

Add a category to the trigger tree and save the edited map.

Usage

```
wc3ctl trigger add-category <map> <name> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). New name.

Options

- `-p, --parent <parent>`. Parent item id, or -1 for the top level. Default `-1`.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### trigger add

Add a trigger under a category and save the edited map. The new trigger is enabled and initially on, matching the World Editor. Custom-text triggers cannot be added, because their body lives in war3map.wct, which cannot be written back. This edits the World Editor's trigger source, not the compiled war3map.j the game runs, so the trigger takes effect only after a World Editor save.

Usage

```
wc3ctl trigger add <map> <name> <parent> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<name>` (required). New name.
- `<parent>` (required). Id of the category to add the trigger to, or -1 for the top level.

Options

- `--comment`. Create a comment rather than a GUI trigger.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### trigger remove

Remove a trigger item and save the edited map. Refuses to orphan a category's children (pass --recursive to take the subtree) and refuses when it would shift a war3map.wct code body onto the wrong trigger.

Usage

```
wc3ctl trigger remove <map> <id> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<id>` (required). Trigger item id, as shown by 'trigger read'.

Options

- `-r, --recursive`. Remove the item's descendants too, rather than refusing to orphan them.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### trigger add-eca

Add an event, condition or action to a GUI trigger and save the edited map. Parameters you leave off are filled from the World-Editor table's own defaults, because a function written with the wrong number of parameters produces a map nothing can read. This edits the World Editor's trigger source (war3map.wtg), not the compiled script the game runs (war3map.j), so the trigger takes effect only after the map is opened and saved in the World Editor.

Usage

```
wc3ctl trigger add-eca <map> <id> <kind> <name> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<id>` (required). Trigger item id, as shown by 'trigger read'.
- `<kind>` (required). event, condition or action.
- `<name>` (required). Function name from the World-Editor table, for example DisplayTextToForce. Use 'trigger catalog list' to find one.

Options

- `-p, --param <param>`. A parameter value, repeated once per parameter, in the order the function declares them. Any you leave off are filled from the World-Editor table's own defaults.
- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### trigger remove-eca

Remove one event, condition or action from a GUI trigger by its position.

Usage

```
wc3ctl trigger remove-eca <map> <id> <index> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<id>` (required). Trigger item id, as shown by 'trigger read'.
- `<index>` (required). Zero-based position of the function within the trigger, as 'trigger read' lists it.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

### trigger set-eca-enabled

Enable or disable ONE event, condition or action within a trigger, which is the World Editor's per-line toggle rather than the whole-trigger one.

Usage

```
wc3ctl trigger set-eca-enabled <map> <id> <index> [<on>] [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.
- `<id>` (required). Trigger item id, as shown by 'trigger read'.
- `<index>` (required). Zero-based position of the function within the trigger, as 'trigger read' lists it.
- `[<on>]` (optional). true or false.

Options

- `-o, --out <out>`. Output map path. Default: '<map>.edited.<ext>' next to the input - the original is never overwritten.

## editor

World Editor base data (TileSets, SkyModels, LoadingScreens, brushes and more).

Usage

```
wc3ctl editor
```

Subcommands

- `catalog`. The World Editor's catalogs from UI\WorldEditData.txt (list, get).

### editor catalog

The World Editor's catalogs from UI\WorldEditData.txt (list, get).

Usage

```
wc3ctl editor catalog
```

Subcommands

- `list`. List the catalog names the installed game defines, with entry counts.
- `get`. Show one catalog's entries, stored key, display name and payload fields.

#### editor catalog list

List the catalog names the installed game defines, with entry counts.

Usage

```
wc3ctl editor catalog list
```

#### editor catalog get

Show one catalog's entries, stored key, display name and payload fields.

Usage

```
wc3ctl editor catalog get <name>
```

Arguments

- `<name>` (required). Catalog name, e.g. TileSets (see 'editor catalog list').

## gamedata

Base game data, for tracking what a patch changes.

Usage

```
wc3ctl gamedata
```

Subcommands

- `snapshot`. Dump the base game's text/data files (plus client and editor strings) for diffing across patches.
- `schema`. Build and save a per-build schema (object fields, trigger functions, script APIs, enum option sets) from a game install or a folder of loose files. The schema describes what the editor for a specific build shows and what the compiler accepts.
- `schema-diff`. Compare two schema folders and report added, removed and changed fields, trigger functions, script types and natives, and enumerated option sets. Groups output by section then by file or category. Exits 0 when no changes, 1 when there are changes.
- `schema-index`. Read all schema folders in a directory, build an index of entries, and write index.json. Prints the entry count and the path.
- `history`. Report whether a name is an object field code, a native, a type, a global or a function in any recorded build, list every build it appears in, and name the first and last build.
- `changelog`. Diff two builds' object fields and report fields added, removed, retyped, or whose minimum or maximum bound changed, grouped by metadata file.
- `field-lookup`. Find object field codes whose display name contains the query, in one build or all builds.
- `bounds`. Report a field code's type, minimum and maximum bounds, whether it can be empty, and the metadata file that defines it, in one build or all builds where it exists.
- `enum`. Report the closed set of legal values for an enumerated object field type, from the build's UnitEditorData.txt, in one build or all builds. Omit the type to list the enumerated types a build defines. This is the authority a picker should offer, not the values a stock object happens to use.
- `coverage`. List one row per schema folder with the build, its era, and which sections it holds (objects, triggers, script, enums), so a reader can tell a build carrying real object data from one carrying only its script.
- `sources`. Write sources.json, a machine readable provenance index of the dataset. For every schema folder it records the build, era, sections, the SHA-256 of each section file and the input hashes from its manifest.
- `export-schemas`. Copy the schema dataset to a target directory, or pack it into one zip when the target ends in .zip, for attaching to a release.

### gamedata snapshot

Dump the base game's text/data files (plus client and editor strings) for diffing across patches.

Usage

```
wc3ctl gamedata snapshot <out-dir> [options]
```

Arguments

- `<out-dir>` (required). Directory to write the snapshot into.

Options

- `--locales`. Include the per-locale string tables (much larger, rarely what a diff needs).
- `--no-binary-strings`. Skip the client/editor string dump. That dump is the only place editor-only changes show up, since the World Editor's behaviour is not in the game data.

### gamedata schema

Build and save a per-build schema (object fields, trigger functions, script APIs, enum option sets) from a game install or a folder of loose files. The schema describes what the editor for a specific build shows and what the compiler accepts.

Usage

```
wc3ctl gamedata schema <out-dir> [options]
```

Arguments

- `<out-dir>` (required). Directory to write the snapshot into.

Options

- `--from <from>`. Build a schema from a folder of loose files instead of a game install.
- `--build <build>`. Override the build version string. Default: read from the install.

### gamedata schema-diff

Compare two schema folders and report added, removed and changed fields, trigger functions, script types and natives, and enumerated option sets. Groups output by section then by file or category. Exits 0 when no changes, 1 when there are changes.

Usage

```
wc3ctl gamedata schema-diff <a> <b> [options]
```

Arguments

- `<a>` (required). Path to the first schema folder.
- `<b>` (required). Path to the second schema folder.

Options

- `--section <section>`. Show changes for one section only: objects, triggers, script or enums. Omit for all.
- `--limit <limit>`. Maximum lines of human output to print. Zero means no limit. Default `0`.

### gamedata schema-index

Read all schema folders in a directory, build an index of entries, and write index.json. Prints the entry count and the path.

Usage

```
wc3ctl gamedata schema-index <schemas-dir>
```

Arguments

- `<schemas-dir>` (required). Path to the schemas directory.

### gamedata history

Report whether a name is an object field code, a native, a type, a global or a function in any recorded build, list every build it appears in, and name the first and last build.

Usage

```
wc3ctl gamedata history <name> [options]
```

Arguments

- `<name>` (required). Name to look for.

Options

- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

### gamedata changelog

Diff two builds' object fields and report fields added, removed, retyped, or whose minimum or maximum bound changed, grouped by metadata file.

Usage

```
wc3ctl gamedata changelog <buildA> <buildB> [options]
```

Arguments

- `<buildA>` (required). The older build name or schema folder.
- `<buildB>` (required). The newer build name or schema folder.

Options

- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

### gamedata field-lookup

Find object field codes whose display name contains the query, in one build or all builds.

Usage

```
wc3ctl gamedata field-lookup <query> [options]
```

Arguments

- `<query>` (required). Text to find inside a field display name.

Options

- `--target <target>`. Build name or schema folder path. Omit to search every build.
- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

### gamedata bounds

Report a field code's type, minimum and maximum bounds, whether it can be empty, and the metadata file that defines it, in one build or all builds where it exists.

Usage

```
wc3ctl gamedata bounds <code> [options]
```

Arguments

- `<code>` (required). The object field code to inspect.

Options

- `--target <target>`. Build name or schema folder path. Omit to search every build.
- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

### gamedata enum

Report the closed set of legal values for an enumerated object field type, from the build's UnitEditorData.txt, in one build or all builds. Omit the type to list the enumerated types a build defines. This is the authority a picker should offer, not the values a stock object happens to use.

Usage

```
wc3ctl gamedata enum [<type>] [options]
```

Arguments

- `[<type>]` (optional). The enumerated field type to inspect, the metadata SLK type column. Omit to list types.

Options

- `--target <target>`. Build name or schema folder path. Omit to search every build.
- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

### gamedata coverage

List one row per schema folder with the build, its era, and which sections it holds (objects, triggers, script, enums), so a reader can tell a build carrying real object data from one carrying only its script.

Usage

```
wc3ctl gamedata coverage [options]
```

Options

- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

### gamedata sources

Write sources.json, a machine readable provenance index of the dataset. For every schema folder it records the build, era, sections, the SHA-256 of each section file and the input hashes from its manifest.

Usage

```
wc3ctl gamedata sources [options]
```

Options

- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.
- `-o, --out <out>` (required). Path to write sources.json into.

### gamedata export-schemas

Copy the schema dataset to a target directory, or pack it into one zip when the target ends in .zip, for attaching to a release.

Usage

```
wc3ctl gamedata export-schemas [options]
```

Options

- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.
- `-o, --out <out>` (required). Target directory for the dataset, or a path ending in .zip to pack it into one archive.

## compat

Check a map against a target game build (a schema folder or build name). Reports compile-time incompatibilities (missing natives, globals, types, functions) and display-time incompatibilities (modified object fields the target build no longer defines). Exits 2 if any error findings. Omit --target to use the configured default.

Usage

```
wc3ctl compat <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--target <target>`. Target build name or schema folder path. Omit to use the configured default.
- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

Subcommands

- `matrix`. Check several maps against several target builds and print a table of pass or fail per cell, with the finding count. Exits 2 if any cell fails.
- `explain`. Explain one identifier against a target build. Reports whether the target declares it, and if not, the earliest build that added it and the builds that declare it.

### compat matrix

Check several maps against several target builds and print a table of pass or fail per cell, with the finding count. Exits 2 if any cell fails.

Usage

```
wc3ctl compat matrix <maps...> [options]
```

Arguments

- `<maps...>` (required). One or more .w3x/.w3m maps.

Options

- `--targets <targets>` (required). One or more target build names or schema folder paths.
- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

### compat explain

Explain one identifier against a target build. Reports whether the target declares it, and if not, the earliest build that added it and the builds that declare it.

Usage

```
wc3ctl compat explain <name> [options]
```

Arguments

- `<name>` (required). Identifier to explain, a native, global, type or function name.

Options

- `--target <target>`. Target build name or schema folder path. Omit to use the configured default.
- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

## bench

Measured benchmarks. Each command prints a number rather than a claim, over a folder of maps or a schemas corpus. Read-only unless noted.

Usage

```
wc3ctl bench
```

Subcommands

- `schema-coverage`. Measure object-data coverage of every schema folder. One row per build with its era and whether object data is real, era-derived or absent, and the real field count, plus totals. Read-only.
- `compat`. Check every map against every target build and report a pass or fail matrix with finding counts, plus the aggregate. Exits 2 if any cell fails. Read-only.
- `leaks`. Run the static leak detector over every map in a folder and report the counts per map and per leak kind, a sweep rather than one map. Read-only.
- `open`. Open every map in a folder through the loader and record the per-map open time, entry count and total bytes, reporting the slowest map and the median. Read-only.
- `perf-gate`. Compare two recorded benchmark results and exit non-zero when any metric regressed beyond the threshold, for use as a CI gate. Read-only.

### bench schema-coverage

Measure object-data coverage of every schema folder. One row per build with its era and whether object data is real, era-derived or absent, and the real field count, plus totals. Read-only.

Usage

```
wc3ctl bench schema-coverage [options]
```

Options

- `--schemas <schemas>`. Schemas directory containing schema folders with manifest.json.

### bench compat

Check every map against every target build and report a pass or fail matrix with finding counts, plus the aggregate. Exits 2 if any cell fails. Read-only.

Usage

```
wc3ctl bench compat <maps...> [options]
```

Arguments

- `<maps...>` (required). One or more .w3x/.w3m maps.

Options

- `--targets <targets>` (required). One or more target build names or schema folder paths.
- `--schemas <schemas>`. Schemas directory containing schema folders with manifest.json.

### bench leaks

Run the static leak detector over every map in a folder and report the counts per map and per leak kind, a sweep rather than one map. Read-only.

Usage

```
wc3ctl bench leaks <folder>
```

Arguments

- `<folder>` (required). Folder of .w3x/.w3m maps, searched recursively.

### bench open

Open every map in a folder through the loader and record the per-map open time, entry count and total bytes, reporting the slowest map and the median. Read-only.

Usage

```
wc3ctl bench open <folder>
```

Arguments

- `<folder>` (required). Folder of .w3x/.w3m maps, searched recursively.

### bench perf-gate

Compare two recorded benchmark results and exit non-zero when any metric regressed beyond the threshold, for use as a CI gate. Read-only.

Usage

```
wc3ctl bench perf-gate <baseline> <run> [options]
```

Arguments

- `<baseline>` (required). Path to the baseline benchmark JSON.
- `<run>` (required). Path to the benchmark JSON to compare against the baseline.

Options

- `--max-regression-pct <max-regression-pct>`. Fail when any metric regressed by more than this percent. Default `10`.

## ci

CI gates. Each runs a check on a map or the schema dataset, prints what it checked, and exits non-zero when it fails. Read-only.

Usage

```
wc3ctl ci
```

Subcommands

- `lint`. Run the lint as a gate. Fails on an error-level finding, or, with --baseline, only on a finding the baseline does not already hold. With --target it also runs the compat check and fails on an error-level finding.
- `validate`. Run the validation as a gate. Exits non-zero when the map cannot load or reports an error-level issue. With --strict the extra strict checks are included.
- `compat`. Run the compat check as a gate. Exits non-zero on an error-level finding.
- `schema`. Check the schema dataset for regressions. Fails when any build stops parsing or fewer than --min-real builds carry real object data.
- `all`. Run lint, validate and, when --target is given, compat in one pass. Prints a summary line per gate and the overall pass or fail with the exit code.

### ci lint

Run the lint as a gate. Fails on an error-level finding, or, with --baseline, only on a finding the baseline does not already hold. With --target it also runs the compat check and fails on an error-level finding.

Usage

```
wc3ctl ci lint <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--baseline <baseline>`. Baseline file written by 'lint baseline'. When given, the gate fails only on a finding the baseline does not already hold.
- `--target <target>`. Target build name or schema folder path. When given, a compat check runs and fails on an error-level finding.
- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.
- `--against <against>`. Optional path to the ORIGINAL map this one was built from, so the lint also reports what a rebuild lost or clobbered.

### ci validate

Run the validation as a gate. Exits non-zero when the map cannot load or reports an error-level issue. With --strict the extra strict checks are included.

Usage

```
wc3ctl ci validate <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--strict`. Run the stricter validation, whose extra checks are too strict to be a default.

### ci compat

Run the compat check as a gate. Exits non-zero on an error-level finding.

Usage

```
wc3ctl ci compat <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--target <target>` (required). Target build name or schema folder path.
- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

### ci schema

Check the schema dataset for regressions. Fails when any build stops parsing or fewer than --min-real builds carry real object data.

Usage

```
wc3ctl ci schema [options]
```

Options

- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.
- `--min-real <min-real>`. Fail when fewer than this many builds carry real object data. Default `0`.

### ci all

Run lint, validate and, when --target is given, compat in one pass. Prints a summary line per gate and the overall pass or fail with the exit code.

Usage

```
wc3ctl ci all <map> [options]
```

Arguments

- `<map>` (required). Path to a .w3x/.w3m map.

Options

- `--target <target>`. Target build name or schema folder path. When given, a compat check runs and fails on an error-level finding.
- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

## library

Sweep a folder of maps instead of one map, the way a maintainer checks a whole library. Every subcommand is read-only and opens one map at a time.

Usage

```
wc3ctl library
```

Subcommands

- `scan`. Open every map in a folder and report per map its size, the build it targets if known, whether it loads, its entry count and its diagnostic count. Read-only.
- `audit`. Run the behavioural audit over every map in a folder and report the findings per map and the totals per check, so a whole library is ranked by how much it needs fixing. Read-only.
- `validate`. Run the load validation over every map in a folder and list the ones that fail, with the reason. Read-only.
- `compare`. Compare two folders by map file name and content hash, listing the maps only in the first, only in the second, and changed between them. Read-only.
- `report`. Write one Markdown report over a folder, a summary line, a per-map table and the aggregated findings, an artifact a maintainer can attach to a review.

### library scan

Open every map in a folder and report per map its size, the build it targets if known, whether it loads, its entry count and its diagnostic count. Read-only.

Usage

```
wc3ctl library scan <folder>
```

Arguments

- `<folder>` (required). Folder of .w3x/.w3m maps, searched recursively.

### library audit

Run the behavioural audit over every map in a folder and report the findings per map and the totals per check, so a whole library is ranked by how much it needs fixing. Read-only.

Usage

```
wc3ctl library audit <folder>
```

Arguments

- `<folder>` (required). Folder of .w3x/.w3m maps, searched recursively.

### library validate

Run the load validation over every map in a folder and list the ones that fail, with the reason. Read-only.

Usage

```
wc3ctl library validate <folder>
```

Arguments

- `<folder>` (required). Folder of .w3x/.w3m maps, searched recursively.

### library compare

Compare two folders by map file name and content hash, listing the maps only in the first, only in the second, and changed between them. Read-only.

Usage

```
wc3ctl library compare <folderA> <folderB>
```

Arguments

- `<folderA>` (required). The first folder of maps.
- `<folderB>` (required). The second folder of maps.

### library report

Write one Markdown report over a folder, a summary line, a per-map table and the aggregated findings, an artifact a maintainer can attach to a review.

Usage

```
wc3ctl library report <folder> [options]
```

Arguments

- `<folder>` (required). Folder of .w3x/.w3m maps, searched recursively.

Options

- `-o, --out <out>` (required). Path to write the Markdown report to.

## mcp

The MCP server for AI apps, and registering it with them.

Usage

```
wc3ctl mcp
```

Subcommands

- `serve`. Run the MCP server on stdio. AI apps start this, you normally do not.
- `tools`. Dump the MCP tool surface the server builds. Name, description and the four annotations, read-only.
- `audit-annotations`. Report MCP tools whose annotations disagree with their name or that declare no annotations.
- `install`. Register the MCP server with AI apps (install --all, or name them, e.g. install cursor vscode).
- `uninstall`. Remove the MCP server from AI apps and the exe folder from the user PATH.
- `config`. Print the settings to paste into an AI app by hand.
- `clients`. List the supported AI apps and whether each is set up.
- `doctor`. Check the install, the game folder and the AI apps.
- `version`. Print the version.
- `help`. Show the setup help, with every option and app.

### mcp serve

Run the MCP server on stdio. AI apps start this, you normally do not.

Usage

```
wc3ctl mcp serve
```

### mcp tools

Dump the MCP tool surface the server builds. Name, description and the four annotations, read-only.

Usage

```
wc3ctl mcp tools
```

### mcp audit-annotations

Report MCP tools whose annotations disagree with their name or that declare no annotations.

Usage

```
wc3ctl mcp audit-annotations
```

### mcp install

Register the MCP server with AI apps (install --all, or name them, e.g. install cursor vscode).

Usage

```
wc3ctl mcp install
```

### mcp uninstall

Remove the MCP server from AI apps and the exe folder from the user PATH.

Usage

```
wc3ctl mcp uninstall
```

### mcp config

Print the settings to paste into an AI app by hand.

Usage

```
wc3ctl mcp config
```

### mcp clients

List the supported AI apps and whether each is set up.

Usage

```
wc3ctl mcp clients
```

### mcp doctor

Check the install, the game folder and the AI apps.

Usage

```
wc3ctl mcp doctor
```

### mcp version

Print the version.

Usage

```
wc3ctl mcp version
```

### mcp help

Show the setup help, with every option and app.

Usage

```
wc3ctl mcp help
```

## target

View or set the target game build used by compat and lint when --target is omitted.

Usage

```
wc3ctl target [options]
```

Options

- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

Subcommands

- `set`. Set the target build.
- `list`. List all available builds.
- `clear`. Clear the target setting.

### target set

Set the target build.

Usage

```
wc3ctl target set <build> [options]
```

Arguments

- `<build>` (required). Target build name or schema folder path.

Options

- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.

### target list

List all available builds.

Usage

```
wc3ctl target list [options]
```

Options

- `--schemas <schemas>`. Path to schemas directory containing schema folders with manifest.json.
- `--full-only`. Skip scripts-only builds.

### target clear

Clear the target setting.

Usage

```
wc3ctl target clear
```

## version

Print the product version, the git short hash and build time stamped into this build, and the MCP server name and version. With --set, write a new version into Directory.Build.props. Read-only unless --set is given.

Usage

```
wc3ctl version [options]
```

Options

- `--set <set>`. Write this x.y.z version into Directory.Build.props instead of reporting the current one.

## changelog

Generate release notes from git log between two refs, grouped by Conventional Commit type (feat, fix, perf, docs, chore and the rest), a heading per group with its subjects under it. Read-only over git.

Usage

```
wc3ctl changelog [options]
```

Options

- `--from <from>`. Git ref to start after. Default, the most recent tag reachable from the end ref.
- `--to <to>`. Git ref to end at. Default, HEAD.
- `-o, --out <out>` (required). Path to write the release notes markdown to.

## export

Export tooling for the public wc3-mcp repository.

Usage

```
wc3ctl export
```

Subcommands

- `verify`. Verify a wc3-mcp checkout holds the same server source this repository would export, reporting every file that differs. Writes nothing.

### export verify

Verify a wc3-mcp checkout holds the same server source this repository would export, reporting every file that differs. Writes nothing.

Usage

```
wc3ctl export verify <path> [options]
```

Arguments

- `<path>` (required). Path to the wc3-mcp checkout to check.

Options

- `--source <source>`. wc3ctl repository directory. Default, the located repository.

## docs

Generate the command reference and the MCP tool reference from the live command tree and tool surface.

Usage

```
wc3ctl docs <target> [options]
```

Arguments

- `<target>` (required). Which reference to write, cli, mcp or all.

Options

- `--force`. Overwrite an existing file with a different render. Without it a changed file is reported and left alone.
- `--out-dir <out-dir>`. Directory to write into. Default, the repository docs folder.

