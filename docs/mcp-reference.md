# wc3ctl MCP tool reference

Generated from the live tool surface by `wc3ctl docs mcp`. Do not edit by hand. Regenerate with `wc3ctl docs mcp --force`, and the CI guard fails when this file is stale.

The server exposes 240 tool(s).

## asset_audit

Flag suspicious imported assets. It reports entries larger than max_bytes, BLP textures whose width or height is not a power of two, a binary .mdx with no .mdl source beside it, and zero-byte entries. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## asset_convert_batch

Convert every texture in a folder to one target format, reusing the shared converter, and write the copies under out_dir with their relative paths preserved. Read-only over the source folder. Returns the output directory, the count and the file names.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## asset_duplicates

Group the map's imported files whose bytes are identical (by sha256) and report the space each duplicate group wastes. Zero-byte entries are excluded. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## asset_import_wire

Add disk files to a map as imports and, when a file is a model or an icon and object names an object, point its matching model or icon field at the new import. Saves the edited copy to out_path. The input map is NEVER modified. Returns the imported paths and the fields set.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## asset_list

List the asset paths an icon or model object field can actually be set to, from the map's own imported files and from the base game. Ask this BEFORE setting a model or icon field, because a field naming a path that does not resolve renders as nothing and reports no error anywhere.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## asset_model_lint

Scan every imported model (.mdx/.mdl) in the map and report the structural problems a static check can find, a geoset with no material, a material naming a texture the map does not hold, a sequence with a zero duration, and an out of range bounding box. Read-only, it parses bytes in memory and changes nothing.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## asset_replace

Replace an asset path everywhere it is referenced (object-data field values, script string literals and the war3map.imp import table) and save the edited copy to out_path. Refuses when the old path is referenced nowhere. The input map is NEVER modified, and the archive entry itself is NOT renamed, move the file if the new path must resolve.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## asset_size_report

Break the map's imported bytes down by parent folder, largest total first, so the folders that dominate the map's weight are obvious. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## asset_thumbnails

Render a thumbnail PNG for every model in a folder, or for every imported model inside every map in the folder, and write them under out_dir. Reuses the model renderer. Read-only over the source folder and every map it holds. Returns the output directory and the file names.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## asset_unused

List the map's imported files that no object-data field value and no script string literal references, each with the size it holds. A lead to review before deleting an import, since the base game can also load a file a field does not name. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## audit_ability

Tells you, per ability, PASS or the exact broken link across a placed hero's whole runtime chain, far deeper than audit_hero, which only proves a cast reaches a live trigger. Checks whether the dispatch is reached exactly once (two independently live triggers for the same ability is the double-registration bug that makes every affected spell fire twice), whether its handler is carried and not gutted by porting, whether a follow-up ability's TimerStart loop actually starts and its callback is declared, whether anything in its closure deals damage and names the call, whether every StartSpellUnit pause is matched by a live StopSpellUnit (an imbalance can leave the caster paused forever), whether a timer or trigger callback still points at a declared, live function, whether a global it reads is ever really assigned, and whether a special-effect asset it names still exists in the map. A hero can read '8 of 8 wired' on audit_hero and still be unplayable, this is built to catch exactly that gap. Omit hero to audit every hero placed on the map.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## audit_fidelity

Tells you whether a ported object's data came across faithfully, by comparing it and its whole custom-object dependency closure between the SOURCE map it was ported from and the TARGET map it was ported into. Reports fields the source set that the target dropped, per-level or per-variation values missing, whole levels lost, and objects present in the source closure but absent from the target, each labeled a real loss or merely informational. A rawcode remapped on a porting collision and a trigger string inlined to literal text are recognized as legitimate port transformations, not faults.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## audit_hero

Tells you whether a placed hero's abilities are actually wired up in the map's script, not merely present as object data. For every ability it verifies the whole chain end to end, the ability object exists, something dispatches a cast on its id, that handler is attached to a trigger, the trigger's event reaches the hero's player, the handler body still has live statements (not commented out by porting), any per-player identity array it gates on lists this hero, and the init that builds the trigger runs exactly once. When a link is missing the result names exactly which one, so a broken port can be fixed rather than only detected. Omit hero to audit every hero type placed on the map.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## audit_map

Behavioural audit of a map's object data, read-only. Where 'validate' answers whether a map can LOAD, this answers whether its abilities do what they CLAIM, by comparing the author's own tooltips against the data and by resolving every object reference. Checks: level-gap, level-tooltip, tooltip-claim, orphan-ability, requirement, portrait-risk, dangling-reference, missing-model (model paths in the script or object data that load no file, which the engine logs and retries on every creation). 'requirement' and 'dangling-reference' need an installed game and say so in a diagnostic rather than reporting a false clean. It CANNOT see a trigger's damage formula, whether a status effect lands, or timing, so a clean result means no claim in the map contradicts its data, not that the map is correct.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## audit_readiness

Tells you whether a placed hero's script would actually RUN correctly, not just fire, the gap audit_hero cannot see since it only proves a cast reaches a handler, never what that handler computes. Checks that InitGlobals and RunInitializationTriggers, the two World Editor entry points that set every custom starting value (damage, range, duration, a dummy id) and run any Map Initialization trigger, are both carried and actually called, and that every udg_ global this hero's own carried code reads is assigned somewhere in the script rather than stuck at its type default. A hero can audit clean on audit_hero and still do nothing worth playing if these were dropped by a port. Omit hero to check every hero placed on the map.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## audit_trend

Run the behavioural audit against two maps and report the findings the second added and removed, grouped by check. Where audit_map answers what is wrong now, this answers what an edit or a port changed. Issues are matched by check, rawcode and message.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## bench_compat

Check every map against every target build and return a pass or fail matrix with the finding counts in each cell, plus the aggregate over the whole grid. Pass means the target build raised no error-level finding. The measured sweep behind compat_matrix, with the totals a benchmark report needs.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## bench_leaks

Run the static leak detector over every map in a folder and report the counts per map and per leak kind, with the hot and repeat totals. The folder sweep the single script_leaks call cannot give, so a whole map set can be ranked by how much leaking code it carries.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## bench_open

Open every map in a folder through the loader and record the per-map open time in milliseconds, entry count and total decompressed bytes, reporting the slowest map and the median open time. The load is read-only and never writes a map back.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## bench_perf_gate

Compare two recorded benchmark result JSON files and report whether any metric regressed by more than a threshold, for use as a CI gate. Each file is an object with a name, an optional generated stamp, a metrics map of name to value, and an optional higherIsBetter list naming metrics where an increase is an improvement. A metric in only one file is reported rather than counted. The comparison is pure data, it runs no benchmark.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## bench_schema_coverage

Measure what object data a schemas corpus carries. One row per schema folder names the build, its era and whether the object data is real (the folder holds its own fields), era-derived (no fields here but a sibling build in the same era carries them) or absent, plus the real field count, and the totals. Reuses the compat schema resolution and the gamedata era labels.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## bundle_diff

Compare two dependency bundles (the JSON written by 'bundle unit -o') by their object and asset members, listing what each adds, removes or renames.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## bundle_merge

Merge two dependency bundles into one, written as JSON at out_path. Refuses when the same rawcode is defined differently in the two bundles (a different kind, custom-vs-base, or a contradicting name).

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## bundle_unit

Resolve everything a unit depends on - the porting preview: referenced objects (with custom-to-map flags), asset files, trigger strings, dependency edges and the JASS trigger-function closure.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## camera_add

Add a camera at a target position and save the edited map to out_path. The input map is NEVER modified in place. Rejects a blank or duplicate name.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## camera_list

List the map's cameras (war3map.w3c): each camera's name, target position, rotation, angle of attack, distance and field of view.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## camera_remove

Remove a camera and save the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## camera_set

Set a field on a camera and save the edited map to out_path. The input map is NEVER modified in place. Editable fields: Name|TargetX|TargetY|ZOffset|Rotation|AngleOfAttack|TargetDistance|Roll|FieldOfView|FarClippingPlane|NearClippingPlane|LocalPitch|LocalYaw|LocalRoll.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## camera_tour

Add N cameras interpolated between two existing cameras as a smooth tour path, and save the edited map to out_path. Every float field is blended linearly and the generated cameras sit strictly between the endpoints, named <from>-tour-i. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## changelog

Generate release notes from git log between two refs, grouped by Conventional Commit type (feat, fix, perf, docs, chore and the rest), a heading per group with its subjects under it. Read-only over git.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## ci_all

Run lint, validate and, when target is given, compat in one pass. Returns one summary line per gate and the overall pass or fail with an exit code (0 pass, 1 fail), so a pipeline gets the whole map picture from one call. Reuses the lint and validate gates, so a gate means here exactly what ci_lint and ci_validate mean on their own. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## ci_compat

Run the compat check as a CI gate. Returns one pass or fail with what was examined and an exit code (0 pass, 1 fail). Fails on an error-level finding, a native, global, type or function the map uses that the target build does not declare. Warnings about object fields the target no longer defines do not fail the gate. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## ci_lint

Run the lint as a CI gate. Returns one pass or fail per check with what was examined and an exit code (0 pass, 1 fail). Fails on an error-level finding, or, with baseline, only on a finding the baseline does not already hold, so a map with known and accepted warnings can still be gated on regressions. With target it also runs the compat check and fails on an error-level finding. Read-only, never writes the map or the baseline.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## ci_schema

Check the schema dataset for regressions as a CI gate. Returns one pass or fail per check with what was examined and an exit code (0 pass, 1 fail). Fails when any build stops parsing or fewer than min_real builds carry real object data. Each folder is loaded on its own so a broken build is named rather than aborting the whole check. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## ci_validate

Run the validation as a CI gate. Returns one pass or fail with what was examined and an exit code (0 pass, 1 fail). Fails when the map cannot load or reports an error-level issue. With strict the extra strict checks (missing listfile, a script with no entry point, an undefined placement type) are included. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## compat_explain

Explain one identifier against a target build. Reports whether the target declares it as a native, global, type or function, and if not, the earliest build that added it plus the test build it was first seen in, and every build that declares it. Reuses the compat union index, so its answer about when a name appeared matches a compat finding.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## compat_matrix

Check several maps against several target builds and return a grid of pass or fail per cell, with the finding count in each cell. Pass means the target build raised no error-level finding, so a map that only warns still passes. The whole-map by whole-build view the single map_compat call cannot give.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## deprotect_map

Recover the file names a protected map stripped. A protected map ships no (listfile), so most entries have no name and cannot be looked up by name at all. Measured on one real map, 793 of 851 entries were nameless and recovery reached 832. Names come from the map's own object data, its script literals, the texture paths its models declare, any listfiles supplied, and an asset-name sweep of every block. Read-only. Pass out_path to write a copy whose listfile carries the recovered names.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## diff

Compare two maps and report which internal files were added, removed or changed. The direct way to see what an edit actually did to a map, and to check that it touched only what was intended.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## doctor_check

Will this map run on Reforged 3.0, and if not, why. A verdict plus findings, each with a code, a severity (blocker, warning, info), the affected files or models, and whether the doctor can fix it. Catches what stops a map loading that wc3ctl's own checks do not, such as half written Buttonpos in SLK profile tables and models with more than 255 matrix groups. Never starts the game. Runs Devo's Map Doctor (MIT, github.com/devoltzz/devos-map-doctor), found through WC3_DOCTOR or %LOCALAPPDATA%\wc3ctl\doctor\<version>\, and only a pinned build. Returns the doctor's own JSON unchanged.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## doctor_compare

What differs between two versions of a map, file by file (added, removed, changed) with names recovered on protected archives, plus object, script and string summaries. The fastest way to see what someone else's fix actually changed. Runs Devo's Map Doctor (MIT, github.com/devoltzz/devos-map-doctor), found through WC3_DOCTOR or %LOCALAPPDATA%\wc3ctl\doctor\<version>\, and only a pinned build. Returns the doctor's own JSON unchanged.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## doctor_fix

Repair a COPY of a map for Reforged 3.0 with the doctor's Fix map: protection, fake file tables, scrambled ids, data the 3.0 engine refuses, broken SLK tables, crashing models, portrait cameras, data pointers, ability lists. The original is never written and an existing output is never overwritten. Run doctor_inspect (diag) first to see which steps apply. Runs Devo's Map Doctor (MIT, github.com/devoltzz/devos-map-doctor), found through WC3_DOCTOR or %LOCALAPPDATA%\wc3ctl\doctor\<version>\, and only a pinned build. Returns the doctor's own JSON unchanged.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## doctor_inspect

Read-only doctor reports on one map. diag is what protects the map and what the World Editor needs, including whether it runs in SLK mode and the fix steps that apply. files lists the archive's files with recovered names, card the name, author, players and loading screen, rawcodes the object ids with names, recipes the item recipes the script makes. Runs Devo's Map Doctor (MIT, github.com/devoltzz/devos-map-doctor), found through WC3_DOCTOR or %LOCALAPPDATA%\wc3ctl\doctor\<version>\, and only a pinned build. Returns the doctor's own JSON unchanged.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## editor_catalog_get

Show one World Editor catalog's entries, each with its stored key (the token a map file actually carries), its resolved display name and every payload field. TileSets, for example, maps the letter A to Ashenvale plus its blight texture path.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## editor_catalog_list

List the World Editor's catalog names from UI\WorldEditData.txt with entry counts (TileSets, SkyModels, LoadingScreens, SoundChannels, MapSizes, the brush palettes and more). These are the catalogs the editor drives its own pickers from, ask here instead of inventing a token.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## file_get_text

Read one internal file decoded as UTF-8, with any leading BOM stripped. Use it for the override text files, war3mapSkin.txt and friends, that carry a map's string and art overrides.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## file_list

List the archive's internal entries with their CURRENT size, which reflects a pending replacement rather than the stale original. Name is null for an unnamed entry, which is what a protected map produces.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## file_set

Replace an internal file's bytes verbatim from a disk file and save the edited map to out_path. The input map is NEVER modified in place. Bytes are staged unchanged, so a file carrying a BOM or CRLF round-trips exactly, which is what keeps the archive byte-faithful. Content comes from a disk path rather than an argument because these payloads are multi-line and full of pipes and colour codes.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## force_list

List the map's forces/teams (war3map.w3i): each force's index, name, member player ids and alliance/sharing flags.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## force_rename

Rename a force (team) in war3map.w3i and save the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## force_set_flags

Set a force's alliance/sharing flags and save the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## force_set_players

Add or remove a player from a force (team) in war3map.w3i and save the edited map to out_path. Pass exactly one of add or remove. Adding clears the player from every other force so the masks stay consistent. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## gamedata_bounds

Report a field code's type, minimum and maximum bounds, whether it can be empty, and the object metadata file that defines it, in one build or every build where it exists.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## gamedata_changelog

Diff two builds' object data field by field. Reports fields added, removed, retyped, or whose minimum or maximum bound changed, grouped by object metadata file. A file only one build carries is listed as not compared rather than every field added or removed.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## gamedata_coverage

List one row per schema folder with the build, its era, and which sections it holds (objects, triggers, script, enums), plus field and native counts, so a reader can tell a build carrying real object data from one carrying only its script.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## gamedata_enum

Report the closed set of legal values for an enumerated object field type, from the build's UnitEditorData.txt, in one build or every build where it exists. The type is the metadata SLK type column, the one gamedata_bounds reports for a field code. This is the authority a picker should offer, not the values a stock object happens to use.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## gamedata_export_schemas

Export the schema dataset. Copies every schema folder into a target directory, or packs the whole dataset into a single zip when out_path ends in .zip, for attaching to a release. Writes only under out_path. Returns the build folder names, file count and byte total.

Annotations. read-only no, destructive unset, idempotent yes, open-world no.

## gamedata_field_lookup

Find object field codes whose display name contains the query, in one build or all builds. Reports the code, display name, type and build for each match.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## gamedata_history

Report whether a name is an object field code, a native, a type, a global or a function across every recorded build. Lists every build each kind appears in, plus the first and last build that carry the name, and the object metadata files that define it as a field.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## gamedata_schema_diff

Compare two schema folders (produced by gamedata schema) and report added, removed and changed object fields, trigger functions, script types and natives, and enumerated field option sets. Groups output by section (objects, triggers, script, enums) then by file or category. A section only one build has is listed as not compared rather than everything added or removed. Use this to track what a patch changes across builds, or to check whether two maps targeting different patches are compatible.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## gamedata_sources

Return the dataset's provenance index. For every schema folder it reports the build, its era, the sections present, the SHA-256 of each section file, and the input hashes from the folder's manifest, so the whole dataset can be traced back to the game files it was derived from.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## hero_install

Install a hero definition into a map and save the result to out_path. Remaps rawcodes that would collide, writes the hero's assets, and reports what the target map must still be wired with before the hero actually works, since a definition carries objects and script but not the target's own integration (roster registration, spell dispatch, per-player hero arrays). Refuses asset collisions unless force is set. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## hero_lint

Validate a hero definition folder before installing it: assets present and hash-matched, objects installable, declared script functions present, no dangling references. Reads a definition and returns findings, it writes nothing. Run this before hero_install, because an install that fails halfway leaves a map holding some of a hero.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## hero_roster

List the playable heroes a map registers, with each hero's rawcode, display name, the string tags from its registration call (typically a role and a portrait path), and which trigger's custom-text body holds its code. A HERO IS NOT A TRIGGER and counting one does not count the other: measured on a real map, 170 registered heroes are spread across 39 Character triggers, because a few grouped triggers each bundle many heroes while a minority get a trigger of their own. Use this rather than counting triggers when asked how many heroes a map has. Returns Ok=false for maps that create heroes inline instead of registering them, since those have no roster to read.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## imports_list

List the map's import table (war3map.imp) against what the archive actually holds, so a table entry with no file behind it and a file absent from the table both show up.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## leak_ab

Diff two wc3watch sessions and report whether the handle and memory slope fell, the usual shape being one session with a trigger live and one with it disabled. With a map, name the periodic candidate whose predicted rate is nearest the drop, labelled an inference. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## leak_benchmark

Run the leak detector over a set of synthetic maps, each with a known injected leak of one creator kind plus a clean control, and report true positive and false positive counts so the detector's accuracy is a measured number. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## leak_harness

Emit a small in-map harness in the map's own language (jass or lua) that drives the named hot leak candidates on a short timer so the runtime handle and memory slope is measurable in seconds rather than an hour, plus a handle count sampler. Writes the harness to out_path as valid syntax for the language. Does not start the game.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## leak_join

Join a map's static leak list to a wc3watch session folder. Reads samples.csv, fits the handle slope and the memory slope, and aligns a rising slope with the periodic candidates from the static list. Read-only. A folder with no samples.csv is reported clearly.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## leak_list

The ranked static handle-leak list for a map's script plus the periodic timers and triggers that feed it, and every hot leak target by function. Read-only, a presentation over script_leaks for the leak workflow.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## leak_model

Report the model-level leaks and waste across a map's imported models and textures, the join between the model audit and the import table. Finds an imported model no field or script names, a model naming a texture the map does not hold, a model imported more than once under different names with identical bytes, and an imported texture no imported model names. Reuses the existing model parser and import primitives. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## library_audit

Run the behavioural audit over every map in a folder and report the findings per map and the totals per check, so a whole library is ranked by how much it needs fixing. Opens one map at a time. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## library_compare

Compare two folders of maps by file name and content hash, listing the maps only in the first folder, only in the second, and changed between them, plus the identical count and any file name that is not unique inside a folder. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## library_report

Write one Markdown report over a folder of maps, a summary line, a per-map table and the aggregated findings, to out_path. The report is built from the same probes the scan, audit and validate sweeps use. Writes only out_path and never a map.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## library_scan

Open every map in a folder through the loader and report per map its size in bytes, the build it targets if known, whether it loads, its archive entry count and its diagnostic count, plus the loaded, failed and byte totals. Opens one map at a time. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## library_validate

Run the load validation over every map in a folder and list the ones that fail, with the reason. A map that cannot be opened counts as invalid. Opens one map at a time. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## lint

Pre-flight the map: does its script compile, does war3map.imp agree with what the archive holds, do referenced asset paths resolve, and is every entry a loadable type. This is the 'is this map going to work' check and is the right thing to run before handing a map to anyone. A failure here usually means the map will not host or will hang on load with no error of its own.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## lint_baseline

Write the map's current lint findings to a baseline JSON file. A later gate, the CLI 'lint <map> --baseline <file>', then fails only on a finding this file does not already hold, so known and accepted warnings do not block CI. Writes a local file, never the map.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## list_files

List the map archive's internal files, each with its name (null = unnamed/protected entry), size in bytes, whether wc3ctl knows/parses the format, and for unnamed entries the content type sniffed from their leading bytes ('BLP texture', 'MDX model', 'empty', ...). Pass brief true to get the total and only the first 50 files when the full list would be too large for the context.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## map_compat

Check a Warcraft III map against a target build (a schema folder path or build name). Reports compile-time incompatibilities (missing natives, globals, types, functions used by the map but absent from the target's scripts/common.j and scripts/blizzard.j) and display-time incompatibilities (object fields modified in the map but not defined in the target's object data). Returns IdentifiersChecked and FieldsChecked so you can tell 'checked and fine' from 'no script section to check'. Omit target to use the configured default from settings, environment, or the newest available schema. Pass schemas_dir to override the schemas location.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## map_info

Show a Warcraft III map's metadata: name, author, player count, playable dimensions and load diagnostics.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## map_info_get

Read the map's editable scenario fields (war3map.w3i): name, author, description, recommended players, tileset, environment, fog and the map-option flags. Richer than map_info; this is the field set map_info_set edits.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## map_info_set

Set a scenario field and save the edited map to out_path. The input map is NEVER modified in place. Editable fields: MapName|Author|Description|RecommendedPlayers|Tileset|LightEnvironment|GlobalWeather|SoundEnvironment|WaterTintColor|FogStyle|FogStartZ|FogEndZ|FogDensity|FogColor (and the map-option booleans).

Annotations. read-only no, destructive no, idempotent no, open-world no.

## map_overview

One call per map: metadata (name, author, player count, dimensions), every archive file, the custom or modified object counts by Object Editor kind, and the load diagnostics. Use this when you need a map's overall shape instead of calling map_info, list_files and object_list separately.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## mcp_audit_annotations

Audit the MCP tool surface itself. Reports every tool whose annotations disagree with its name (a mutating name marked read-only) and every tool that declares no annotations, as a list of problems plus a count. An empty list means the surface is clean. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## mcp_export_verify

Verify that a wc3-mcp checkout holds the same server source scripts/export-wc3-mcp.py would export from this repository, reporting every file that differs without writing. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## model_audit

Audit a .mdx or .mdl model for the resource waste and draw-nothing problems an optimiser can remove, and report what it holds. Finds duplicate textures, geosets with no texture or no triangles, zero-area faces, unused skeleton nodes and non-finite vertices. Also counts the effects an MDX carries, particle emitters, particle emitters 2, ribbons and popcorns, from the binary chunk list, without parsing their payloads. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## model_export_obj

Export a map-imported model to Wavefront OBJ at out_path, writing an MTL and any map-resolvable textures converted to PNG beside it. The map itself is never modified. Returns the .obj path, the .mtl path and the texture file names.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## model_import

Import a foreign model file into a Warcraft III MDX at out_path. Accepts Wavefront OBJ (.obj), glTF 2.0 (.gltf and .glb), STL (.stl, binary and ASCII) and PLY (.ply, ASCII and binary little-endian). The format is detected from the file content, so a correct extension is not required. Builds one v800 geoset with normals and UVs, triangulating an n-gon face as a fan, computing a per-face normal when the source carries none and zeroing UVs when it has none, plus one texture and one material. The input is never modified. Returns the output path and the vertex, face and texture counts.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## model_optimize

Decimate a .mdx or .mdl model's geometry with the quadric error metric, drop zero-area faces and duplicate textures, and write a smaller MDX at out_path, leaving the other chunks untouched. ratio is the target fraction of faces to keep. Returns the before and after vertex and face counts. The input is never modified.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## mpq_add

Add or replace one internal archive file from a file on disk, byte-faithfully. Reports whether the name was added or replaced and whether the payload round-trips. Writes a copy, the input map is never modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_bytes_diff

Compare two archives entry by entry by name. For each name present in both, report whether the payload SHA256 matches, and list names only one side carries. The byte-level counterpart of the structural mpq-diff. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## mpq_extract_all

Extract every entry of a map archive to a directory, including unnamed protected blocks, through the same path-safety rule 'extract' uses. Read-only over the map. Returns the output directory, the counts and the written file names.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_listfile

Report an archive's listfile coverage in three parts, the entries named by the listfile it ships, the entries recovered by the standard-name probe, and the entries with no name at all. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## mpq_merge

Merge two map archives into one, taking every readable named entry from both. A name in both is resolved by the preferred side and reported as a conflict. Writes a copy of the first map plus both maps' entries, the input maps are never modified.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_name_recover

Resolve a dictionary of candidate names against the archive's hash table and report the names that hit unnamed blocks. Narrower than deprotect_map, which harvests its own candidates from the map. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## mpq_rebuild

Rebuild a map archive from scratch into a healthy one, re-placing every readable entry and dropping the blocks protection made unreadable or left nameless. Reports the entry counts before and after and the names dropped. Writes a copy, the input map is never modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_remove

Remove one internal file by rebuilding the archive without it, and report whether the rebuild actually dropped the name. Writes a copy, the input map is never modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_strip_bookkeeping

Save a copy of a map with the (listfile) and (attributes) bookkeeping entries dropped, then confirm the copy opens again. The input map is never overwritten. out_path defaults to a sibling '<map>.edited.<ext>'.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_verify

Open every entry of a map archive and read it to its end, reporting any that fail with the error raised, plus a summary count. Use this to prove an archive is fully usable rather than merely addressable. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## new_map

Create a blank, World-Editor-openable map (.w3x) at out_path. No input map. Returns where it was written.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_bulk_set

Set one field on every object of a kind that matches every where clause, and save the edited copy to out_path. The input map is NEVER modified in place. A where clause is code=value and tests the object's merged value (base game data overlaid by the map). Reports how many objects changed.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_clone

Deep-clone a map object under a new rawcode and save the edited map to out_path. The input map is NEVER modified in place. The clone copies the source's field deltas and, for a leveled kind, its per-level data too, keeping the source's base. Refuses a rawcode already taken by the map.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_deprecated

List object fields the target build's schema does not define but the map still sets, meaning the edit is ignored on that build. Read-only. The target is a schema folder path or a build name under the schemas directory. A target with no object data section is reported as such rather than as a clean result.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## object_export

Export the objects of one kind (base game data overlaid by the map's deltas) to a CSV file at out_path, one row per object, one column per field code, with a header. Read-only on the map. Feed the CSV back with object_import.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## object_field_options

The legal values for one object-data field, taken from what the base game data actually uses for it. Ask this BEFORE object_set on an enumerated field, so the value written is one the game already accepts rather than a guess.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## object_form

Get an object as an editable FORM rather than a flat field dump: fields grouped and ordered the way the World Editor groups them (Art, Combat, Stats, ...), fields that do not apply to this object hidden, each field's legal range and whether a blank is allowed, and which object-data layer it is written to (war3map.* or the war3mapSkin.* twin). Prefer this over object_get when deciding what to edit.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## object_get

Get an object's merged fields (base game data overlaid by the map's deltas), each labeled 'base' or 'map', with field names resolved. Omit kind to auto-detect it from the rawcode.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## object_import

Import a CSV of one kind back into the map, adding objects the map does not define and updating those it does, then save the edited copy to out_path. The input map is NEVER modified in place. An unknown column refuses the whole import. A blank cell leaves that field untouched.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_list

List the map's custom/modified objects of one Object Editor kind: rawcode, base rawcode (null = created from scratch) and resolved name. Pass brief true to get the total and only the first 50 objects when the full list would be too large for the context.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## object_migrate

Rewrite a map's object data to a target build's field set, dropping every field the target does not define, and save the edited copy to out_path. The input map is NEVER modified in place. Every drop is reported, and a target with no object data section refuses rather than stripping everything.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_new

Create a new custom object derived from a base rawcode (a fresh unused rawcode is allocated) and save the edited map to out_path. The input map is NEVER modified in place. Returns the new rawcode.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_noop_fields

List object fields whose map value equals the base game value, so the edit changes nothing. Read-only. Needs a game_dir for the base values; without one the answer is map-only and says so.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## object_rawcode_suggest

Propose free four-character rawcodes for one Object Editor kind that collide with neither the map's objects nor the base game data. Read-only. Without a game_dir the suggestions are checked against the map only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## object_rename

Rename an object rawcode and save the edited copy to out_path. The input map is NEVER modified in place. With rewrite_refs true, every object-data field value and script string literal that names the old rawcode is rewritten too, so references do not dangle. Refuses a new rawcode already taken by the map.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_search

Full-text search across every custom or modified object's field values, in both the base game data and the map's own deltas. Read-only. Reports the kind, rawcode, resolved name, field code, and the base and map values, so a hit is traceable. Pass kind to search one Object Editor type or field to restrict to one field code.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## object_set

Set a field on an object of one Object Editor kind and save the edited map to out_path (only that kind's war3map.* file is re-serialized). The input map is NEVER modified in place. Field syntax: a bare 4-char field code, or 'code:N' to select level N (ability/upgrade) or variation N (doodad).

Annotations. read-only no, destructive no, idempotent no, open-world no.

## palette_doodad

List the doodad types placeable on a map - the palette to consult before place_doodad. Unions the base-game doodad catalog (installed GameData) with the map's own object-data: custom New* doodads and modified Base* doodads. Each entry carries its four-char rawcode, a resolved display name (null when unresolvable), its source (base|map-custom|map-modified) and the base it derives from. Without a WC3 install the palette is map-only. The map is never modified.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## pathing_export_png

Render a map's war3map.wpm pathing grid to a colour-coded PNG at out_path, one pixel per pathing cell with a legend strip of the pathing flags. Read-only, the map is not modified. The same file re-imports through pathing_import_png.

Annotations. read-only no, destructive unset, idempotent yes, open-world no.

## pathing_import_png

Set the pathing flags from a colour-coded PNG and save the edited map to out_path. Each cell's flags are replaced by the value its pixel decodes to. The image must be at least the map's Width by Height cell grid (the top-left region is read, so a legend to the right is ignored) and any unknown colour is refused. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## pathing_paint

Paint pathing bits over a circular or square brush and save the edited map to out_path. The input map is NEVER modified in place. Flags (comma-separated): Walk|Fly|Build|Blight|Water - a SET bit RESTRICTS that capability (Walk set = ground units cannot walk there). op: set|clear|toggle.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## pathing_validate

Flag pathing problems in a map's war3map.wpm. It reports a pocket of unpathable tiles fully enclosed by pathable ones, a start location sitting on an unpathable tile, and a map corner that is unpathable or sealed off from the main walkable area. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## place_doodad

Place a doodad instance at (x, y) on the map's doodad layer (war3map.doo) and save the edited map to out_path. The input map is NEVER modified in place - out_path must differ from it. Returns the assigned creation number.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_grid

Place a regular rows x cols grid of copies of one unit or doodad type and save the edited copy to out_path. The grid is centred on the map origin. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_item

Place a preplaced item of the given type at world (x, y) and save the edited map to out_path. Items share war3mapUnits.doo with units (WC3 spawns a ground item because the rawcode is an item), so the item has no owning player. The input map is NEVER modified in place. Returns the assigned creation number.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_region

Define a rectangular region on the map's region layer (war3map.w3r) and save the edited map to out_path. The input map is NEVER modified in place - out_path must differ from it. Bounds need right>left and top>bottom. Returns the assigned creation number.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_scatter

Place N copies of a unit type at deterministic random positions and save the edited copy to out_path. Positions are uniform inside a named region or across the whole playable area, and the same seed reproduces the same layout. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_start_location

Place or move a player's start location at world (x, y) and save the edited map to out_path. The input map is NEVER modified in place. A start location is stored as a preplaced 'sloc' unit owned by the player; the World Editor permits exactly one per player, so if this player already has one it is moved (its creation number preserved) rather than duplicated. Returns the start location's creation number.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_unit

Place a unit of the given type, owned by a player, at world (x, y) and save the edited map to out_path. The input map is NEVER modified in place. Returns the assigned creation number (the id triggers use to reference the unit).

Annotations. read-only no, destructive no, idempotent no, open-world no.

## placed_doodad_get

Show one placed doodad's full state by creation number: type, position, rotation, scale, variation and life percentage.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## placed_doodad_remove

Remove a doodad already placed on the map and save the edited copy to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive yes, idempotent yes, open-world no.

## placed_doodad_set

Set one field on a doodad already placed on the map and save the edited copy to out_path. The input map is NEVER modified in place. A position field takes 'x,y' or 'x,y,z'.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## placed_doodads_list

List every doodad and destructable already PLACED on the map (war3map.doo), each with the creation number needed to edit or remove it, plus its type rawcode, position, rotation, scale and variation.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## placed_fix_refs

Drop (or, with to_rawcode, rewrite) placements that reference a missing rawcode and save the edited copy to out_path, reporting each change. Refuses when nothing is wrong. The input map is NEVER modified in place.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## placed_missing_refs

List placements whose rawcode is defined neither in the map's object data nor in the base game data. Without a game_dir only the map's object data can be consulted, so stock base-game types read as unresolved too and the message says so. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## placed_stats

Count the units and doodads already placed on the map, grouped by rawcode and by kind, with each type's resolved name where one is available. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## placed_unit_get

Show one placed unit's full state by creation number: type, owner, position, facing, scale, hero level and attributes, hit points and mana percentages, gold and target acquisition range.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## placed_unit_remove

Remove a unit already placed on the map and save the edited copy to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive yes, idempotent yes, open-world no.

## placed_unit_set

Set one field on a unit already placed on the map and save the edited copy to out_path. The input map is NEVER modified in place. A position field takes 'x,y', a scale field takes one number (uniform) or 'sx,sy,sz', and Facing is in RADIANS (pi/2 faces north), the unit the map file stores.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## placed_units_list

List every unit already PLACED on the map (war3mapUnits.doo), each with the creation number needed to edit or remove it, plus its type rawcode, owner, position and hero stats. Start locations appear here too. Call this before placed_unit_set or placed_unit_remove. Pass brief true to get the total and only the first 50 units when the full list would be too large for the context.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## player_list

List the map's player slots (war3map.w3i): each player's id, name, color, race, controller and whether its start position is fixed.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## player_set_color

Set a player's team color and save the edited map to out_path. war3map.w3i has no per-player color field, so this rewrites the SetPlayerColor call the map script already carries. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## player_set_force

Move a player into a force (team) and save the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## player_set_name

Set a player's display name (war3map.w3i) and save the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## player_set_race

Set a player's race (war3map.w3i) and save the edited map to out_path. Accepted values are human, orc, undead, nightelf and random. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## port_deps

Report the full dependency closure of a rawcode, its objects grouped by kind, its assets with their byte sizes, its display strings and its script functions. The readable shape of a dependency bundle, before porting it.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## port_into_version

Port a unit, then downgrade the result to a target build's schema. Object fields the build does not declare are removed and script identifiers it lacks are flagged. Writes a NEW file at out_path - the source and target maps are NEVER modified.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## port_preview

Dry run, listing exactly what a port of the rawcode into the target map would add or change, writing nothing. Same code path as port_unit, so the preview and the port cannot drift.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## port_unit

Port a unit (its custom-object closure + imported assets + strings, and best-effort its JASS trigger closure) from a source map into a target map, auto-remapping rawcode collisions. WRITES A NEW FILE '<target>.ported.<ext>' next to the target map (overwritten if it exists from a previous run) - the source and target maps themselves are NEVER modified. Returns the full port report: remaps, ported objects, copied files, inlined strings, script info and warnings.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## region_create

Create a rectangular region from its south-west corner (x, y) plus a width w and height h, and save the edited map to out_path. The input map is NEVER modified in place. Rejects a blank or duplicate name and a non-positive size. Set weather to attach the default weather effect. Returns the assigned creation number.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## region_list

List the map's rectangular regions (war3map.w3r), each with its creation number, name, and bounds. Add regions with place_region.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## region_remove

Remove a region by name and save the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## region_to_rect

Resolve one named region to the four corner values a JASS trigger's Rect call needs (left, bottom, right, top), with a ready-to-copy Rect(...) line. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## region_validate

Flag overlapping regions, zero-area regions and regions that fall outside the playable bounds in a map's war3map.w3r. Regions sharing only an edge do not overlap. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## render_model

Render an object's model (map-imported .mdx/.mdl) to a PNG written at out_path. Returns the saved path and byte size; small images also come back inline as MCP image content. The map itself is never modified.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## repair_generated

Repair a map that wc3ctl itself generated, whose preplaced-hero helper block is too narrow for the heroes installed into it, and save the repaired map to out_path. Widens the player slots, rewrites hero owners in the script, and brings war3map.w3i and the map header into line. This is NOT a general map repair: it looks for wc3ctl's own generated helper function and refuses any map that does not carry one. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## replay_diff

Compare two recorded games (.w3g). Reports whether they played the same map, the length difference, and each player's leave time difference, keyed by slot. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## replay_find_map

The map path a replay (.w3g) records, and whether that map file exists in the Maps folder, resolving the .w3x or .w3m. Default folder is the user's Documents\Warcraft III\Maps. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## replay_players

Per-player table for one recorded game (.w3g). Reports name, slot, team, race, result, leave time and leave reason. Team and race are reported as unknown when the record does not carry them. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## replay_stats

Aggregate stats for one recorded game (.w3g). Reports duration, player count, actions per minute per player when the record carries them, and the disconnect count. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## replay_summary

Read recorded games (.w3g) and report each one's map, length, players, and how and when every player left. A game where any player left with result 0x01 is flagged as a disconnect, which is how a dropped or desynced game is scored without anyone writing results down. Reforged autosaves every game under Documents\Warcraft III\BattleNet\<account>\Replays\Autosaved.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## replay_timeline

Ordered event list for one recorded game (.w3g). Reports the game start, each chat line and player leave with its reason, and the game end time. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## roundtrip

Verify the map survives a load and save byte for byte. Any file that differs is one this toolkit does not reproduce faithfully, which is worth knowing BEFORE editing that map, since a save would then rewrite it. Archive bookkeeping the container regenerates is excluded by design.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_api_usage

List the natives and functions the map script uses that a target game build does not declare, the compile-time side of a port. Uses the same schema membership test as the compat check. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_callgraph

The call edges between the functions a map's war3map.j declares, caller to callee. Only edges between functions this script declares are included, so a native call or a call through a local code value is not an edge. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_check

Check the map script for errors that stop it loading, war3map.j or war3map.lua. Lua is checked against Lua 5.3 exactly as luac 5.3.6 would, with its line and message, JASS by the built-in JASS checks. A script that fails here cannot run, and the game gives no reason, it just shows no slots.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_complexity

Measure each function's cyclomatic complexity as the number of if, loop, and and or branch keywords in its body, ranked worst first with the line. Answers where the complexity is on a large script. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_decompile

Turn every Lua entry of a map that ships as precompiled Lua 5.3 bytecode (war3map.lua or a module it requires, common in YDWE maps) back into Lua source that runs the same, and save the map to out_path. The source is a transcription of the bytecode, one statement per instruction with gotos, not the author's original text. Both standard Lua 5.3 and the KK YDWE engine's opcode numbering are read. Nothing is written when the map holds no bytecode. The input map is NEVER modified.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## script_duplicate_bodies

Find functions whose normalized bodies are identical, up to comments, whitespace and local variable names. A copied function is a maintenance trap, a fix in one copy silently misses the others. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_format

Reprint a Lua map script and save the map to out_path. pretty is indented with comments kept, canonical is one fixed form for diffing two builds. The result compiles to the same Lua instructions as the original. JASS maps are refused. The input map is NEVER modified, and nothing is written when the script is already in that form.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## script_functions

List every function declared in the map's compiled script, with where it starts and ends. Indentation-tolerant, so it finds declarations a naive line scan misses. Use it to locate a handler before reading or editing the script.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_leaks

Find handle leaks in a map's war3map.j (locations, groups, forces, effects, timers, text tags, lightning and triggers created and never destroyed), each ranked by how often its code runs, hot (periodic), repeat (any callback) or once (init), plus every periodic timer and trigger with its period. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_lua_api

List the natives and functions a Lua script calls that a target game build does not declare. Uses the same schema membership test as the compat check, and reads the calls from the parsed tree. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_lua_check

Parse every Lua file in a map and report the syntax errors with their line and column, the way luac 5.3 refuses them. Wider than script_check, which parses only the entry the map runs. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_lua_globals

List the globals a Lua map defines, where each is read and written, and any global that is written but never read. The Lua twin of script_unused_globals, parser based rather than a line scan. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_lua_leaks

Find handle leaks in a Lua map script. Lua collects the wrapper but the engine still owns the handle, so a creator dropped as a statement, a creator on a period that is never released, and a handle kept in a table the script never clears are the shapes that grow memory. Each finding is ranked hot, repeat or once. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_lua_to_jass

Best effort conversion of a Lua map back to JASS, saved to out_path. The JASS to Lua conversion is not reversible in general, so only the constructs that map back without guessing are converted and every construct left is reported. Nothing is written when the script does not convert. The input map is NEVER modified.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## script_modernize

Rewrite deprecated or renamed natives in the map script to the target build's spelling where a known mapping exists, and save the map to out_path. A rewrite is only applied when the target build declares the new name, and names inside strings or comments are never touched. Nothing is written when there is nothing to rewrite. The input map is NEVER modified.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## script_null_locals

Find the classic JASS handle leak: a local of a handle type (location, group, force, effect, timer, trigger, text tag, lightning) that the function destroys with the matching Remove or Destroy but never sets to null, which keeps the handle id reserved for the rest of the function. A local that is never destroyed is a different leak and is not reported here. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_reachable

List the functions reachable from the map script's entry points, main and the InitTrig functions, and the dead functions nothing reaches, each with its declaration line. A function reached only through a string (ExecuteFunc) is reported dead here, which is the honest answer to what main reaches. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_references

Find every use of a name in the map's compiled script: direct calls, and the places it is passed as a code value (Condition, Filter, TriggerAddAction). The second kind is how most trigger handlers are actually reached and a text search for 'name(' cannot see it, so use this rather than search when asking what reaches a function. Each result carries its line, the function containing it, and the line text.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_semantic_diff

Compare two map scripts line by line after normalizing whitespace, dropping comments and ignoring pure renames of local variables, and report the real changes. Use this rather than diff for a before/after of a port or an edit where a variable rename or a reindent should not drown the intended change. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_to_lua

Convert a JASS map to a Lua map and save it to out_path. The script becomes war3map.lua, which keeps the JASS rules Lua lacks (32 bit integer wrap, integer division toward zero, division by zero gives zero, a null string concatenates as empty, unset array slots read their type default), and the w3i script language is set to Lua so the game runs it. war3map.j is left as a one line stub. Warnings name untyped calls, and custom text triggers the World Editor would regenerate as JASS. Lua maps are refused. The input map is NEVER modified.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## script_unused_functions

Find the functions a map's war3map.j declares that no other function calls and nothing else reaches, excluding the engine's entry points (main, config, InitCustomTriggers and the map initialization functions) and anything an InitTrig_* function or an ExecuteFunc("...") string names. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## script_unused_globals

Find the globals a map's war3map.j declares and never reads, the variable half of dead code. A global that is only ever assigned and never appears on the right of anything is reported too, since its value is never used. The World Editor's generated globals are excluded (every gg_ handle, every bj_ name, and the literal worldedit). Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## search

Search the map's contents for a string, ignoring case: archive file names, script lines (with the line number), string-table entries, and object data (custom object rawcodes and every field value). Each hit names the file and where in it. Capped at 500 hits, with truncated set when the cap was reached. Use this to find where a name, an ability id or an asset path is actually referenced, rather than guessing which file holds it.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## sound_add

Add a new sound definition (keyed by name) to the map's sound catalog and save the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## sound_list

List the map's sound catalog (war3map.w3s): each definition's name, file path, channel, flags, volume, pitch, priority and min/max distance.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## sound_optimize

Shrink a sound file by decoding it, resampling to a target rate (default 22050 Hz) and optionally folding it to mono, and write a smaller 16-bit PCM .wav at out_path. The input is never modified. Reads .wav natively and .mp3 through the Windows codecs. Returns the before and after sample rate, channel count and byte size.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## sound_remove

Remove a sound definition from the map's sound catalog and save the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## sound_set

Set a field on a sound definition and save the edited map to out_path. The input map is NEVER modified in place. Editable fields: Name|File|Eax|Volume|Pitch|PitchVariance|FadeIn|FadeOut|Priority|Channel|Flags|MinDistance|MaxDistance|DistanceCutoff|ConeInside|ConeOutside|ConeOutsideVolume.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## strings_diff

Compare two maps' string tables (war3map.wts) and report entries added, removed and changed. Entries are matched by id, so a text change on the same key reads as a change rather than a remove plus an add. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## strings_find

Find war3map.wts entries whose text contains the given text, case-insensitive, each with its id. Read-only. An empty query matches every entry.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## strings_list

List the map's string table (war3map.wts). Every TRIGSTR_ reference in object data and in scripts resolves through here, so this is how to read what a tooltip or a unit name actually says.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## strings_missing

List the TRIGSTR_ keys the map script and the GUI trigger tree reference that have no war3map.wts entry behind them. A missing key shows the literal token TRIGSTR_nnn in game where a name or a message was meant. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## strings_set

Set one war3map.wts entry's text, or add the entry when the id is absent, and save the edited map to out_path. The file's own encoding and line endings are preserved and the input map is NEVER modified. out_path defaults to a sibling '<map>.edited.<ext>' and must differ from the input.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## strings_unused

List the war3map.wts entries no object-data field, map-info field, GUI trigger or script line references, each with its text. Read-only. An unreferenced entry is dead weight rather than a fault, so treat the list as a lead to review and not an instruction to delete.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## target_get

View the current target build setting: which build maps are checked against when compat or lint omit --target, where it comes from (option, environment, settings, default), and which schema sections are available. Also shows the installed game build version.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## target_list

List all available builds in the schemas directory, with their sections. Use --full_only to skip scripts-only builds.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## target_set

Set the target build that compat and lint use when --target is omitted. Validates the build exists in the schemas folder.

Annotations. read-only no, destructive unset, idempotent yes, open-world no.

## terrain_audit

Audit a map's terrain (war3map.w3e) for its size and its measurable waste: grid and tile counts, distinct ground and cliff textures in use, cliff/ramp/water/blight totals, the war3map.w3e byte size, and any uniform, all-zero or out-of-range-tileset pattern found. Reports which checks it ran. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## terrain_blight

Set or clear the blight (corrupted ground) flag over a brush, saving the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_cliff

Raise/lower/set the cliff (stepped-terrain) level over a brush, saving the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_corner_get

Show one terrain corner's full state: ground height, cliff level, water, ground and cliff texture indexes with variations, and the ramp, blight, boundary and edge flags.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## terrain_corner_set

Set one field on ONE terrain corner and save the edited copy to out_path. The input map is NEVER modified in place. For area work prefer terrain_deform, terrain_paint and the other brush tools.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## terrain_deform

Raise/lower/set/flatten/smooth ground height over a circular or square brush, saving the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_export_heightmap

Export a map's terrain heights as a 16-bit grayscale PNG of the (Width+1) by (Height+1) corner grid, one pixel per corner, to out_path. Read-only, the map is not modified, and the same file re-imports through terrain_import_heightmap.

Annotations. read-only no, destructive unset, idempotent yes, open-world no.

## terrain_fill

Apply a terrain tool over an inclusive RECTANGLE of corners and save the edited map to out_path, instead of one brush dab at a time. Corners may be given in any order and a rectangle that runs off the grid is clipped rather than refused. tool is one of Raise, Lower, SetHeight, Flatten, Paint, CliffRaise, CliffLower, CliffSet, Ramp, RampOff, Water, WaterRemove, Blight, BlightOff. value means height for Raise/Lower/SetHeight/Water, a ground tile index for Paint (see terrain_info for the map's tile list), and a step count for the Cliff tools; it is ignored by Flatten, Ramp, RampOff, WaterRemove, Blight and BlightOff. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_import_heightmap

Set the terrain tile heights from a grayscale PNG and save the edited map to out_path. The PNG dimensions must match the map's corner grid exactly or the call is refused. Only ground height is written, and cliff level, texture, water and flags are untouched. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_info

The terrain grid's extents in corners, plus the map's ground and cliff tile lists. Texture fields on a corner are indexes into those lists, so read this first to know what an index means.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## terrain_paint

Paint a ground texture over a brush, saving the edited map to out_path. texture_index selects a slot in the map's tileset table. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_ramp

Toggle the ramp (sloped cliff transition) flag over a brush, saving the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_smooth

Smooth the whole map: each pass replaces every corner height with the mean of itself and its in-grid four neighbours, saving the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_stats

Summarize a map's terrain (war3map.w3e): tile count, min/mean/max ground height and min/max cliff level. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## terrain_water

Set/raise/lower/remove water over a brush, saving the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## texture_optimize

Shrink a .blp, .dds or standard image texture to fit a maximum side, preserving aspect ratio, and write it at out_path in the same format. The input is never modified. Returns the before and after dimensions and byte sizes.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_add

Add a trigger to a category in the map's GUI trigger tree and save the edited map to out_path. The new trigger is created enabled and initially on, matching the World Editor, and has no events, conditions or actions yet. IMPORTANT: this edits war3map.wtg, the World Editor's trigger SOURCE, not the compiled war3map.j that Warcraft III runs, so the trigger is inert in game until the map is opened and saved in the World Editor. Custom-text (script) triggers cannot be added, because their body lives in war3map.wct, which has a lossy decoder and so is never rewritten. The input map is NEVER modified in place. Writes war3map.wtg only.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## trigger_add_category

Add a category to the map's GUI trigger tree and save the edited map to out_path. A category is a folder in the trigger tree, so this is always safe: categories hold no script and adding one moves nothing. The input map is NEVER modified in place. Writes war3map.wtg only.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## trigger_add_eca

Add an event, condition or action to a GUI trigger and save the edited map to out_path. Parameters are given in the order the function declares them; any left off are filled from the World-Editor table's own defaults. This matters more than it looks: war3map.wtg stores a function's parameters but NOT how many there are, so a function written with the wrong number of parameters yields a map that nothing, including this tool, can read afterwards. Use trigger_catalog_list and trigger_catalog_describe to find a function name and its arguments. Custom-text (script) triggers are refused, since their body is JASS rather than a list of functions. IMPORTANT: this edits war3map.wtg, the World Editor's trigger SOURCE, not war3map.j, the compiled script Warcraft III actually runs. wc3ctl does not regenerate the compiled script, so a trigger authored this way is inert in game until the map is opened and saved in the World Editor. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## trigger_catalog_describe

Show full detail for one GUI-trigger function by name (e.g. 'DoNothing'): kind, game version, return type, argument types, display name, parameters layout and defaults. The catalog is read from an explicit file when given, otherwise from the installed game.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## trigger_catalog_list

List GUI-trigger functions from the World-Editor catalog (UI\TriggerData.txt), optionally filtered by kind and/or a name/display-name search. The catalog is read from an explicit file when given, otherwise from the installed game.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## trigger_dead

Find triggers that cannot do anything, listing each reason. Triggers with no action, and triggers whose events never fire, which is an event depending on a variable nothing sets (a unit event holding an unassigned unit variable, or a variable event watching a variable that never changes). A variable counts as set when the tree initialises it or, when the compiled script is present, when the script assigns it. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## trigger_diff

Compare two maps' GUI trigger trees and report triggers added, removed and renamed, plus event, condition or action changes within a trigger shared by both maps. Triggers are matched first by a unique id (so a rename reads as a rename) and then by name. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## trigger_export

Export a map's full GUI trigger tree as structured output. Each trigger carries its name, its enabled, initially-on and run-on-map-init flags, and its event, condition and action lists with the catalog function name, enabled flag, arguments and nested blocks for every node. Read-only. A map with no trigger tree returns empty lists.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## trigger_lint

Check a map's GUI trigger tree for three structural faults, a trigger that can never fire (no event and not run on map initialization), a trigger that registers the same event twice, and a trigger with no action. Disabled and custom-text triggers are skipped and the counts say so. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## trigger_recover_from_script

Rebuild a browsable GUI trigger tree (war3map.wtg) for a map that has NONE, by decompiling the compiled script the game runs, and save the result to out_path. The script itself is never modified, so this only ever ADDS a tree. It REFUSES any map that already carries a trigger tree, because a decompiled tree cannot be trusted to replace a real one: measured across the map library the decompiler reported success on a map holding 626 trigger items while producing 1, at zero percent name recall. Use this on protected or stripped maps whose trigger tree was removed but whose compiled script survives. The recovered tree DESCRIBES the script, it does not drive it, so editing the tree afterwards changes what the World Editor shows and not what the game runs. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_remove

Remove a trigger item (a category, a trigger or a comment) from the GUI trigger tree and save the edited map to out_path. Refuses rather than risk two silent corruptions: removing a category that still holds children would orphan them (pass recursive to remove the subtree instead), and removing a trigger whose script body sits ahead of other bodies in war3map.wct would shift every later body onto the wrong trigger, since that file cannot be rewritten. The input map is NEVER modified in place.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## trigger_remove_eca

Remove one event, condition or action from a GUI trigger by its zero-based position, and save the edited map to out_path. Positions come from triggers_read, which lists a trigger's functions in order. The input map is NEVER modified in place.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## trigger_rename

Rename a trigger item (a category, a trigger or a deleted stub) in the GUI trigger tree and save the edited map to out_path. The input map is NEVER modified in place. Ids come from triggers_read. Writes war3map.wtg only.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_set_eca_enabled

Enable or disable ONE event, condition or action within a trigger, the World Editor's per-line toggle rather than the whole-trigger one, and save the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_set_enabled

Enable or disable a GUI trigger and save the edited map to out_path. A disabled trigger is not compiled into the map script at all, so this is how you take one out of play without deleting it. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_set_initially_on

Set whether a GUI trigger starts switched on, and save the edited map to out_path. A trigger that is off at map start never fires until something turns it on, which is a common reason a trigger looks correct and does nothing. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_set_run_on_map_init

Set whether a GUI trigger runs on map initialization, and save the edited map to out_path. The input map is NEVER modified in place.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_to_script

Render the map's GUI trigger tree (war3map.wtg) to JASS-style text, the way the World Editor's 'convert to custom text' presents one trigger. Read-only, it never edits the map. Preset parameter values are resolved to their script form through the World-Editor catalog when the installed game provides one, and otherwise keep their raw names. This is a rendering for reading rather than a replacement for the compiled script (war3map.j), so it is not guaranteed to compile.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## triggers_read

Read the map's GUI trigger tree (war3map.wtg) plus the custom-text bodies (war3map.wct) and the script language: every category, every trigger with its event/condition/action tree, and every GUI variable. Read-only, because this format cannot yet be written back. A map with no GUI triggers returns empty lists rather than failing.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## uabi_profile

Measure unit ability lists (uabi) across many maps at once, one row per map: unit types, references, distinct ids, hero and item abilities in normal lists, ids neither the map nor the game defines, duplicates, and a per-race breakdown. Built to compare the variants of a disconnect bisection without starting the game.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## unit_abilities

Every ability a unit has, each tagged with where it comes from. ObjectNormal and ObjectHero are the unit type's own lists (uabi, uhab). Spellbook is inside a spellbook the unit has (passives often hide there). PlacedUnit is set on one placed unit, with its level. MorphForm comes from a form the unit turns into. Script is given by war3map.j at runtime, found where a statement names the unit type and the ability, or where an add-ability call sits inside an if that tests for the unit type. Script entries are inferred and carry their line number. Use this rather than object_get when asking what abilities a unit really has.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## validate

Check the map for structural problems: missing or empty files, entries that fail to load, and a script that cannot compile. Narrower than lint and a good first question when a map behaves as though something is absent.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## validate_strict

A stricter validate. Runs the default checks and adds three that are too strict to be a default, a map with no (listfile), a script that declares no entry point function, and a placement whose type neither the map nor the installed game defines. The undefined-type check needs the installed game and reports a diagnostic rather than a clean result when it cannot run. The default validate is unchanged.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

## version_info

Report the product version, the git short hash and UTC build time stamped into this build, and the name and version of the MCP server it carries. Read-only.

Annotations. read-only yes, destructive unset, idempotent yes, open-world no.

