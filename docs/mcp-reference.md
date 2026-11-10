# wc3ctl MCP tool reference

Generated from the live tool surface by `wc3ctl docs mcp`. Do not edit by hand. Regenerate with `wc3ctl docs mcp --force`, and the CI guard fails when this file is stale.

The server exposes 277 tool(s) in 11 families.

Each entry carries the tool's family, its parameters with their type and default, the shape of its result and the error codes it can return, then the four annotation hints. A parameter line reads `name` and its type, then `required` or `optional`, then `default value` where the schema declares one. The Errors line lists the codes the tool's own description or a parameter description names, and the two codes every call passes through, `POLICY_DENIED` from the permission file's deny list and `COMMAND_REJECTED` when a shared command refuses and states its own reason. A tool whose method is typed `object` reports any JSON value, because no tool publishes an output schema and the shape is chosen by the arguments.

## asset_audit

Parameters.

- `map` string, required.
- `max_bytes` integer, optional, default 4194304.

Family. assets

Flag suspicious imported assets. It reports entries larger than max_bytes, BLP textures whose width or height is not a power of two, a binary .mdx with no .mdl source beside it, and zero-byte entries. Read-only.

Result. one JSON object with the fields findings (array), scanned (integer), maxBytes (integer), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## asset_convert_batch

Parameters.

- `folder` string, required.
- `to` string, required.
- `out_dir` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Convert every texture in a folder to one target format, reusing the shared converter, and write the copies under out_dir with their relative paths preserved. The source folder is never modified. Returns the output directory, the count and the file names.

Result. one JSON object with the fields outDir (string), converted (integer), to (string), files (array), diagnostics (array), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer), failed (array), partial (boolean)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## asset_duplicates

Parameters.

- `map` string, required.

Family. assets

Group the map's imported files whose bytes are identical (by sha256) and report the space each duplicate group wastes. Zero-byte entries are excluded. Read-only.

Result. one JSON object with the fields groups (array), importedEntries (integer), recoverableBytes (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## asset_import_wire

Parameters.

- `map` string, required.
- `out_path` string, required.
- `files` array, required.
- `obj` string|null, optional, default null.
- `kind` string|null, optional, default null.
- `base_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Add disk files to a map as imports and, when a file is a model or an icon and object names an object, point its matching model or icon field at the new import. Saves the edited copy to out_path. The input map is NEVER modified. Returns the imported paths and the fields set.

Result. one JSON object with the fields savedTo (string), imported (array), fieldsSet (array), diagnostics (array), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## asset_list

Parameters.

- `family` string, required.
- `map` string|null, optional, default null.
- `game_dir` string|null, optional, default null.

Family. assets

List the asset paths an icon or model object field can actually be set to, from the map's own imported files and from the base game. Ask this BEFORE setting a model or icon field, because a field naming a path that does not resolve renders as nothing and reports no error anywhere.

Result. one JSON object with the fields family (string), mapPaths (array), gamePaths (array), diagnostic (string), all (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## asset_model_lint

Parameters.

- `map` string, required.

Family. assets

Scan every imported model (.mdx/.mdl) in the map and report the structural problems a static check can find, a geoset with no material, a material naming a texture the map does not hold, a sequence with a zero duration, and an out of range bounding box. Read-only, it parses bytes in memory and changes nothing.

Result. one JSON object with the fields modelsScanned (integer), findings (array), diagnostics (array), findingCount (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## asset_replace

Parameters.

- `map` string, required.
- `out_path` string, required.
- `old_path` string, required.
- `new_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Replace an asset path everywhere it is referenced (object-data field values, script string literals and the war3map.imp import table) and save the edited copy to out_path. Refuses when the old path is referenced nowhere. The input map is NEVER modified, and the archive entry itself is NOT renamed, move the file if the new path must resolve.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## asset_report

Parameters.

- `map` string, required.
- `model_lint` boolean, optional, default true.
- `log` boolean, optional, default false.
- `log_path` string|null, optional, default null.

Family. assets

One report over every imported file in a map. Lists each entry with its size and whether anything references it, plus every fault the asset checks and the model scan find, an empty entry, an oversized or non power of two texture, a model without source, an import referenced nowhere, byte identical duplicate groups, and model lint faults (geoset without material, missing texture, zero duration sequence, bounding box out of range). The engine logs nothing about what it loads, so this is the full answer a map gives. ModelWithoutSource fires for nearly every compiled .mdx, so read it as a lead.

Result. one JSON object with the fields map (string), entries (integer), totalBytes (integer), importedEntries (integer), byType (array), entryList (array), findings (array), engineFaults (array), summary (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## asset_size_report

Parameters.

- `map` string, required.

Family. assets

Break the map's imported bytes down by parent folder, largest total first, so the folders that dominate the map's weight are obvious. Read-only.

Result. one JSON object with the fields folders (array), totalBytes (integer), totalEntries (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## asset_thumbnails

Parameters.

- `folder` string, required.
- `out_dir` string, required.
- `size` integer, optional, default 256.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Render a thumbnail PNG for every model in a folder, or for every imported model inside every map in the folder, and write them under out_dir. Reuses the model renderer. The source folder and every map it holds are never modified. Returns the output directory and the file names.

Result. one JSON object with the fields outDir (string), rendered (integer), files (array), diagnostics (array), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## asset_unused

Parameters.

- `map` string, required.

Family. assets

List the map's imported files that nothing references, each with its size. A file counts as referenced when an object field, a war3mapSkin.txt or war3mapMisc.txt value, a script literal, an imported model's texture chunk, a path embedded in a model's bytes, an imported .fdf or .toc, or a map format file names it. The map format scan matters, since the w3i names the loading screen, the w3e every terrain tile and the w3s every sound, so without it those read as unused. Treat the list as leads, as the base game can load a file none of these name.

Result. one JSON object with the fields unused (array), importedEntries (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## audit_ability

Parameters.

- `map` string, required.
- `hero` string|null, optional, default null.

Family. triggers

Report PASS or the exact broken link, per ability, across a placed hero's runtime chain, deeper than audit_hero, which only proves a cast reaches a live trigger. Checks that the dispatch is reached exactly once (two live triggers make every affected spell fire twice), that handlers survive a port, that a follow-up TimerStart loop starts and its callback is declared, that every StartSpellUnit pause is matched by a live StopSpellUnit, that callbacks name a declared function, that globals read are assigned, and that named special effects still exist. Omit hero to audit every placed hero.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## audit_ability_orders

Parameters.

- `map` string, required.
- `hero` string|null, optional, default null.

Family. repair

Find Channel-based abilities that share a Base Order ID (Ncl6). Channel dispatches by order string, so two abilities on one unit with the same string can both fire from a single key press. Read-only, no game install, no script run. With a hero rawcode it checks only that hero's uabi list, which is the precise case. Also reports levels that disagree and empty order ids.

Result. one JSON object with the fields channelAbilities (integer), distinctOrders (integer), collisions (array), problems (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## audit_fidelity

Parameters.

- `source_map` string, required.
- `target_map` string, required.
- `rawcode` string, required.
- `game_dir` string|null, optional, default null.

Family. triggers

Tells you whether a ported object's data came across faithfully, by comparing it and its whole custom-object dependency closure between the SOURCE map it was ported from and the TARGET map it was ported into. Reports fields the source set that the target dropped, per-level or per-variation values missing, whole levels lost, and objects present in the source closure but absent from the target, each labeled a real loss or merely informational. A rawcode remapped on a porting collision and a trigger string inlined to literal text are recognized as legitimate port transformations, not faults.

Result. one JSON object with the fields root (string), rootKind (string), name (string), objectsCompared (integer), findings (array), diagnostics (array), errors (integer), infos (integer), faithful (boolean), objectsWithLosses (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## audit_hero

Parameters.

- `map` string, required.
- `hero` string|null, optional, default null.

Family. triggers

Say whether a placed hero's abilities are wired up in the script, not merely present as object data. For every ability it walks the chain end to end, the ability object exists, something dispatches a cast on its id, that handler is attached to a trigger, the trigger's event reaches the hero's player, the handler body still has live statements, any identity array it gates on lists this hero, and the init that builds the trigger runs exactly once. A missing link is named, so a broken port can be fixed rather than only detected. Omit hero to audit every placed hero.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## audit_map

Parameters.

- `map` string, required.
- `check` string|null, optional, default null.
- `game_dir` string|null, optional, default null.

Family. triggers

Behavioural audit of a map's object data, read-only. Where validate asks whether a map loads, this asks whether its abilities do what they claim, by comparing the author's tooltips against the data and resolving every object reference. Checks level gap, level tooltip, tooltip claim, orphan ability, requirement, portrait risk, dangling reference and missing model, the last being a path that loads no file, which the engine logs and retries on every creation. Requirement and dangling reference need an installed game and report a diagnostic rather than a false clean.

Result. one JSON object with the fields ok (boolean), objectsChecked (integer), issues (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## audit_readiness

Parameters.

- `map` string, required.
- `hero` string|null, optional, default null.

Family. triggers

Say whether a placed hero's script would run correctly, not merely fire, the gap audit_hero cannot see since it only proves a cast reaches a handler. Checks that InitGlobals and RunInitializationTriggers, the two World Editor entry points that set every custom starting value and run any Map Initialization trigger, are carried and actually called, and that every udg_ global the hero's carried code reads is assigned somewhere rather than left at its type default. A hero can audit clean on audit_hero and still do nothing worth playing. Omit hero to check every hero placed.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## audit_trend

Parameters.

- `map` string, required.
- `other` string, required.
- `check` string|null, optional, default null.
- `game_dir` string|null, optional, default null.

Family. triggers

Run the behavioural audit against two maps and report the findings the second added and removed, grouped by check. Where audit_map answers what is wrong now, this answers what an edit or a port changed. Issues are matched by check, rawcode and message.

Result. one JSON object with the fields mapA (string), mapB (string), rows (array), added (integer), removed (integer), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## bench_compat

Parameters.

- `maps` array, required.
- `targets` array, required.
- `schemas_dir` string|null, optional, default null.

Family. leaks

Check every map against every target build and return a pass or fail matrix with the finding counts in each cell, plus the aggregate over the whole grid. Pass means the target build raised no error-level finding. The measured sweep behind compat_matrix, with the totals a benchmark report needs.

Result. one JSON object with the fields maps (array), targets (array), cells (array), aggregate (object)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## bench_leaks

Parameters.

- `folder` string, required.

Family. leaks

Run the static leak detector over every map in a folder and report the counts per map and per leak kind, with the hot and repeat totals. The folder sweep the single script_leaks call cannot give, so a whole map set can be ranked by how much leaking code it carries.

Result. one JSON object with the fields maps (array), kinds (array), totalLeaks (integer), hot (integer), repeat (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## bench_open

Parameters.

- `folder` string, required.

Family. leaks

Open every map in a folder through the loader and record the per-map open time in milliseconds, entry count and total decompressed bytes, reporting the slowest map and the median open time. The load is read-only and never writes a map back.

Result. one JSON object with the fields maps (array), slowest (object), medianMs (number), totalBytes (integer), failures (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## bench_perf_gate

Parameters.

- `baseline` string, required.
- `run` string, required.
- `max_regression_pct` number, optional, default 10.

Family. leaks

Compare two recorded benchmark result JSON files and report whether any metric regressed by more than a threshold, for use as a CI gate. Each file is an object with a name, an optional generated stamp, a metrics map of name to value, and an optional higherIsBetter list naming metrics where an increase is an improvement. A metric in only one file is reported rather than counted. The comparison is pure data, it runs no benchmark.

Result. one JSON object with the fields pass (boolean), maxRegressionPct (number), metricsCompared (integer), regressed (integer), metrics (array), missingInRun (array), addedInRun (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## bench_schema_coverage

Parameters.

- `schemas_dir` string|null, optional, default null.

Family. leaks

Measure what object data a schemas corpus carries. One row per schema folder names the build, its era and whether the object data is real (the folder holds its own fields), era-derived (no fields here but a sibling build in the same era carries them) or absent, plus the real field count, and the totals. Reuses the compat schema resolution and the gamedata era labels.

Result. one JSON object with the fields rows (array), totals (object)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## bundle_diff

Parameters.

- `bundle_a` string, required.
- `bundle_b` string, required.

Family. porting

Compare two dependency bundles (the JSON written by 'bundle unit -o') by their object and asset members, listing what each adds, removes or renames.

Result. one JSON object with the fields rootA (string), rootB (string), added (array), removed (array), changed (array), shared (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## bundle_merge

Parameters.

- `bundle_a` string, required.
- `bundle_b` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. porting

Merge two dependency bundles into one, written as JSON at out_path. Refuses when the same rawcode is defined differently in the two bundles (a different kind, custom-vs-base, or a contradicting name).

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## bundle_unit

Parameters.

- `map` string, required.
- `rawcode` string, required.
- `game_dir` string|null, optional, default null.

Family. porting

Resolve everything a unit depends on - the porting preview: referenced objects (with custom-to-map flags), asset files, trigger strings, dependency edges and the JASS trigger-function closure.

Result. one JSON object with the fields rootRawcode (string), rootName (string), objects (array), files (array), strings (array), edges (array), diagnostics (array), functions (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## camera_add

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `target_x` number, required.
- `target_y` number, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Add a camera at a target position and save the edited map to out_path. The input map is NEVER modified in place. Rejects a blank or duplicate name.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## camera_list

Parameters.

- `map` string, required.

Family. placed

List the map's cameras (war3map.w3c): each camera's name, target position, rotation, angle of attack, distance and field of view.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## camera_remove

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `confirm` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Remove a camera and save the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. CONFIRM_REQUIRED, OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## camera_set

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `field` string, required.
- `value` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Set a field on a camera and save the edited map to out_path. The input map is NEVER modified in place. Editable fields: Name|TargetX|TargetY|ZOffset|Rotation|AngleOfAttack|TargetDistance|Roll|FieldOfView|FarClippingPlane|NearClippingPlane|LocalPitch|LocalYaw|LocalRoll.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## camera_tour

Parameters.

- `map` string, required.
- `out_path` string, required.
- `from` string, required.
- `to` string, required.
- `count` integer, optional, default 3.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Add N cameras interpolated between two existing cameras as a smooth tour path, and save the edited map to out_path. Every float field is blended linearly and the generated cameras sit strictly between the endpoints, named <from>-tour-i. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), names (array), count (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## changelog

Parameters.

- `from_ref` string|null, optional, default null.
- `to_ref` string|null, optional, default null.
- `repo_dir` string|null, optional, default null.

Family. product

Generate release notes from git log between two refs, grouped by Conventional Commit type (feat, fix, perf, docs, chore and the rest), a heading per group with its subjects under it. Read-only over git.

Result. one JSON object with the fields ok (boolean), from (string), to (string), groups (array), markdown (string), message (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## ci_all

Parameters.

- `map` string, required.
- `target` string|null, optional, default null.
- `schemas_dir` string|null, optional, default null.

Family. leaks

Run lint, validate and, when target is given, compat in one pass. Returns one summary line per gate and the overall pass or fail with an exit code (0 pass, 1 fail), so a pipeline gets the whole map picture from one call. Reuses the lint and validate gates, so a gate means here exactly what ci_lint and ci_validate mean on their own. Read-only.

Result. one JSON object with the fields passed (boolean), exitCode (integer), gates (array), lint (object), validate (object), compat (object)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## ci_compat

Parameters.

- `map` string, required.
- `target` string, required.
- `schemas_dir` string|null, optional, default null.

Family. leaks

Run the compat check as a CI gate. Returns one pass or fail with what was examined and an exit code (0 pass, 1 fail). Fails on an error-level finding, a native, global, type or function the map uses that the target build does not declare. Warnings about object fields the target no longer defines do not fail the gate. Read-only.

Result. one JSON object with the fields passed (boolean), exitCode (integer), gates (array), compat (object)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## ci_lint

Parameters.

- `map` string, required.
- `baseline` string|null, optional, default null.
- `target` string|null, optional, default null.
- `schemas_dir` string|null, optional, default null.
- `against` string|null, optional, default null.

Family. leaks

Run the lint as a CI gate. Returns one pass or fail per check with what was examined and an exit code (0 pass, 1 fail). Fails on an error-level finding, or, with baseline, only on a finding the baseline does not already hold, so a map with known and accepted warnings can still be gated on regressions. With target it also runs the compat check and fails on an error-level finding. Read-only, never writes the map or the baseline.

Result. one JSON object with the fields passed (boolean), exitCode (integer), gates (array), lint (object), baseline (string), added (array), resolved (array), compat (object)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## ci_schema

Parameters.

- `schemas_dir` string|null, optional, default null.
- `min_real` integer, optional, default 0.

Family. leaks

Check the schema dataset for regressions as a CI gate. Returns one pass or fail per check with what was examined and an exit code (0 pass, 1 fail). Fails when any build stops parsing or fewer than min_real builds carry real object data. Each folder is loaded on its own so a broken build is named rather than aborting the whole check. Read-only.

Result. one JSON object with the fields passed (boolean), exitCode (integer), gates (array), builds (array), coverage (object), schemasDir (string), minReal (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## ci_validate

Parameters.

- `map` string, required.
- `strict` boolean, optional, default false.
- `game_dir` string|null, optional, default null.

Family. leaks

Run the validation as a CI gate. Returns one pass or fail with what was examined and an exit code (0 pass, 1 fail). Fails when the map cannot load or reports an error-level issue. With strict the extra strict checks (missing listfile, a script with no entry point, an undefined placement type) are included. Read-only.

Result. one JSON object with the fields passed (boolean), exitCode (integer), gates (array), validate (object), strict (object)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## compat_explain

Parameters.

- `name` string, required.
- `target` string|null, optional, default null.
- `schemas_dir` string|null, optional, default null.

Family. repair

Explain one identifier against a target build. Reports whether the target declares it as a native, global, type or function, and if not, the earliest build that added it plus the test build it was first seen in, and every build that declares it. Reuses the compat union index, so its answer about when a name appeared matches a compat finding.

Result. one JSON object with the fields name (string), isIdentifier (boolean), kind (string), declaredInTarget (boolean), targetBuild (string), builds (array), addedIn (string), firstSeen (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## compat_matrix

Parameters.

- `maps` array, required.
- `targets` array, required.
- `schemas_dir` string|null, optional, default null.

Family. repair

Check several maps against several target builds and return a grid of pass or fail per cell, with the finding count in each cell. Pass means the target build raised no error-level finding, so a map that only warns still passes. The whole-map by whole-build view the single map_compat call cannot give.

Result. one JSON object with the fields maps (array), targets (array), cells (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## content_scaffold

Parameters.

- `name` string, required.
- `out_dir` string, required.
- `texture_path` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. product

Generate a playable content package into out_dir from a name alone: a body capsule model, a particle effect model, a 64 by 64 icon, a short 16 bit PCM impact sound, and a role manifest (package.json). Every file is loadable rather than a stub, and both models name the package's own icon, so the package has no dependency on art outside itself. Gameplay binds a role such as hero.alpha.body, never a filename, so replacing the placeholder with final art is one binding change. Returns the root, the name, every file with its role and size, and the manifest path.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## deprotect_map

Parameters.

- `map` string, required.
- `out_path` string|null, optional, default null.
- `listfiles` array|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. repair

Recover the file names a protected map stripped. A protected map ships no (listfile), so most entries have no name and cannot be looked up by name at all. Measured on one real map, 793 of 851 entries were nameless and recovery reached 832. Names come from the map's own object data, its script literals, the texture paths its models declare, any listfiles supplied, and an asset-name sweep of every block. Without out_path nothing is written. With it, the recovered names go into a copy at out_path and the input map is never modified in place.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## diff

Parameters.

- `map` string, required.
- `other` string, required.

Family. scripts

Compare two maps and report which internal files were added, removed or changed. The direct way to see what an edit actually did to a map, and to check that it touched only what was intended.

Result. one JSON object with the fields entries (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## docs_search

Parameters.

- `query` string, required.
- `doc` string|null, optional, default null.
- `max` integer, optional, default 8.

Family. product

Search the bundled Warcraft III reference documents and return the matching passages with their document and nearest heading. Use this to find the exact rule, field or warning in the UI-Frame tutorial or the Ability Insight reference without loading the whole file. Terms are matched on word boundaries and ranked by how many the passage carries, with a bonus when the section heading names a term, so an ability name finds that ability's section.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## doctor_check

Parameters.

- `map` string, required.
- `game` string|null, optional, default null.

Family. repair

Will this map run on Reforged 3.0, and if not, why. A verdict plus findings, each with a code, a severity (blocker, warning, info), the affected files or models, and whether the doctor can fix it. Catches what stops a map loading that wc3ctl's own checks do not, such as half written Buttonpos in SLK profile tables and models with more than 255 matrix groups. Never starts the game. Runs Devo's Map Doctor (MIT, github.com/devoltzz/devos-map-doctor), found through WC3_DOCTOR or %LOCALAPPDATA%\wc3ctl\doctor\<version>\, and only a pinned build. Returns the doctor's own JSON unchanged.

Result. one JSON object with the fields command (string), exitCode (integer), result (any), output (string), doctor (string), stderr (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## doctor_compare

Parameters.

- `map` string, required.
- `other` string, required.

Family. repair

What differs between two versions of a map, file by file (added, removed, changed) with names recovered on protected archives, plus object, script and string summaries. The fastest way to see what someone else's fix actually changed. Runs Devo's Map Doctor (MIT, github.com/devoltzz/devos-map-doctor), found through WC3_DOCTOR or %LOCALAPPDATA%\wc3ctl\doctor\<version>\, and only a pinned build. Returns the doctor's own JSON unchanged.

Result. one JSON object with the fields command (string), exitCode (integer), result (any), output (string), doctor (string), stderr (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## doctor_fix

Parameters.

- `map` string, required.
- `out_path` string|null, optional, default null.
- `options` array|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. repair

Repair a COPY of a map for Reforged 3.0 with the doctor's Fix map: protection, fake file tables, scrambled ids, data the 3.0 engine refuses, broken SLK tables, crashing models, portrait cameras, data pointers, ability lists. The original is never written and an existing output is never overwritten. Run doctor_inspect (diag) first to see which steps apply. Runs Devo's Map Doctor (MIT, github.com/devoltzz/devos-map-doctor), found through WC3_DOCTOR or %LOCALAPPDATA%\wc3ctl\doctor\<version>\, and only a pinned build. Returns the doctor's own JSON unchanged.

Result. one JSON object with the fields command (string), exitCode (integer), result (any), output (string), doctor (string), stderr (string)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## doctor_inspect

Parameters.

- `map` string, required.
- `report` string, optional, default "diag".

Family. repair

Read-only doctor reports on one map. diag is what protects the map and what the World Editor needs, including whether it runs in SLK mode and the fix steps that apply. files lists the archive's files with recovered names, card the name, author, players and loading screen, rawcodes the object ids with names, recipes the item recipes the script makes. Runs Devo's Map Doctor (MIT, github.com/devoltzz/devos-map-doctor), found through WC3_DOCTOR or %LOCALAPPDATA%\wc3ctl\doctor\<version>\, and only a pinned build. Returns the doctor's own JSON unchanged.

Result. one JSON object with the fields command (string), exitCode (integer), result (any), output (string), doctor (string), stderr (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## editor_catalog_get

Parameters.

- `name` string, required.
- `game_dir` string|null, optional, default null.

Family. product

Show one World Editor catalog's entries, each with its stored key (the token a map file actually carries), its resolved display name and every payload field. TileSets, for example, maps the letter A to Ashenvale plus its blight texture path.

Result. one JSON object with the fields catalog (string), total (integer), entries (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## editor_catalog_list

Parameters.

- `game_dir` string|null, optional, default null.

Family. product

List the World Editor's catalog names from UI\WorldEditData.txt with entry counts (TileSets, SkyModels, LoadingScreens, SoundChannels, MapSizes, the brush palettes and more). These are the catalogs the editor drives its own pickers from, ask here instead of inventing a token.

Result. one JSON object with the fields total (integer), catalogs (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## error_codes

Parameters. none.

Family. product

List every stable error code this server can return, each with the one line that says what it means. A refusal carries one of these codes as the first bracketed word of its message and as the code field of the error result, so a caller can branch on the code instead of reading prose. Read-only, it touches no map.

Result. one JSON object with the fields count (integer), codes (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## file_extract

Parameters.

- `map` string, required.
- `internal_path` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. map

Write one internal archive entry to out_path exactly as the archive holds it, so a binary entry, or one whose text decode would be lossy, can be read without corruption. The map is never modified. Refuses an out_path equal to the input map, and an out_path that already exists without overwrite. Returns where it landed, the entry name, its size and its digest.

Result. one JSON object with the fields savedTo (string), name (string), sizeBytes (integer), sha256 (string), inputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## file_get_text

Parameters.

- `map` string, required.
- `internal_path` string, required.
- `allow_lossy` boolean, optional, default false.

Family. map

Read one internal file as text, naming the encoding it used and whether a byte order mark declared that encoding or it was assumed. Reports how many bytes the decoder had to replace and refuses a lossy decode with LOSSY_DECODE unless allow_lossy is true, so a binary entry is never handed back as corrupt text. Use file_extract to write the raw bytes instead.

Result. one JSON object with the fields name (string), text (string), encoding (string), encodingDeclared (boolean), bytesReplaced (integer), lossy (boolean)

Errors. LOSSY_DECODE. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## file_list

Parameters.

- `map` string, required.

Family. map

List the archive's internal entries with their CURRENT size, which reflects a pending replacement rather than the stale original. Name is null for an unnamed entry, which is what a protected map produces.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## file_set

Parameters.

- `map` string, required.
- `out_path` string, required.
- `internal_path` string, required.
- `from` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. map

Replace an internal file's bytes verbatim from a disk file and save the edited map to out_path. The input map is NEVER modified in place. Bytes are staged unchanged, so a file carrying a BOM or CRLF round-trips exactly, which is what keeps the archive byte-faithful. Content comes from a disk path rather than an argument because these payloads are multi-line and full of pipes and colour codes.

Result. one JSON object with the fields savedTo (string), name (string), replaced (boolean), sizeBytes (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## force_list

Parameters.

- `map` string, required.

Family. placed

List the map's forces/teams (war3map.w3i): each force's index, name, member player ids and alliance/sharing flags.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## force_rename

Parameters.

- `map` string, required.
- `out_path` string, required.
- `force_index` integer, required.
- `name` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Rename a force (team) in war3map.w3i and save the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## force_set_flags

Parameters.

- `map` string, required.
- `out_path` string, required.
- `force_index` integer, required.
- `allied` boolean, required.
- `allied_victory` boolean, required.
- `shared_vision` boolean, required.
- `shared_unit_control` boolean, required.
- `shared_adv_unit_control` boolean, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Set a force's alliance/sharing flags and save the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## force_set_players

Parameters.

- `map` string, required.
- `out_path` string, required.
- `force_index` integer, required.
- `add` integer|null, optional, default null.
- `remove` integer|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Add or remove a player from a force (team) in war3map.w3i and save the edited map to out_path. Pass exactly one of add or remove. Adding clears the player from every other force so the masks stay consistent. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## gamedata_bounds

Parameters.

- `code` string, required.
- `target` string|null, optional, default null.
- `schemas_dir` string|null, optional, default null.

Family. objects

Report a field code's type, minimum and maximum bounds, whether it can be empty, and the object metadata file that defines it, in one build or every build where it exists.

Result. one JSON object with the fields code (string), target (string), entries (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## gamedata_changelog

Parameters.

- `build_a` string, required.
- `build_b` string, required.
- `schemas_dir` string|null, optional, default null.

Family. objects

Diff two builds' object data field by field. Reports fields added, removed, retyped, or whose minimum or maximum bound changed, grouped by object metadata file. A file only one build carries is listed as not compared rather than every field added or removed.

Result. one JSON object with the fields buildA (string), buildB (string), groups (array), notCompared (array), added (integer), removed (integer), retyped (integer), boundsChanged (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## gamedata_coverage

Parameters.

- `schemas_dir` string|null, optional, default null.

Family. objects

List one row per schema folder with the build, its era, and which sections it holds (objects, triggers, script, enums), plus field and native counts, so a reader can tell a build carrying real object data from one carrying only its script.

Result. one JSON object with the fields rows (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## gamedata_enum

Parameters.

- `type` string, required.
- `target` string|null, optional, default null.
- `schemas_dir` string|null, optional, default null.

Family. objects

Report the closed set of legal values for an enumerated object field type, from the build's UnitEditorData.txt, in one build or every build where it exists. The type is the metadata SLK type column, the one gamedata_bounds reports for a field code. This is the authority a picker should offer, not the values a stock object happens to use.

Result. one JSON object with the fields type (string), target (string), entries (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## gamedata_export_schemas

Parameters.

- `out_path` string, required.
- `schemas_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. objects

Export the schema dataset. Copies every schema folder into a target directory, or packs the whole dataset into a single zip when out_path ends in .zip, for attaching to a release. Writes only under out_path. Returns the build folder names, file count and byte total.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## gamedata_field_lookup

Parameters.

- `query` string, required.
- `target` string|null, optional, default null.
- `schemas_dir` string|null, optional, default null.

Family. objects

Find object field codes whose display name contains the query, in one build or all builds. Reports the code, display name, type and build for each match.

Result. one JSON object with the fields query (string), target (string), matches (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## gamedata_history

Parameters.

- `name` string, required.
- `schemas_dir` string|null, optional, default null.

Family. objects

Report whether a name is an object field code, a native, a type, a global or a function across every recorded build. Lists every build each kind appears in, plus the first and last build that carry the name, and the object metadata files that define it as a field.

Result. one JSON object with the fields name (string), found (boolean), kinds (array), objectFiles (array), firstBuild (string), lastBuild (string), buildsSearched (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## gamedata_schema_diff

Parameters.

- `a` string, required.
- `b` string, required.
- `section` string|null, optional, default null.

Family. objects

Compare two schema folders (produced by gamedata schema) and report added, removed and changed object fields, trigger functions, script types and natives, and enumerated field option sets. Groups output by section (objects, triggers, script, enums) then by file or category. A section only one build has is listed as not compared rather than everything added or removed. Use this to track what a patch changes across builds, or to check whether two maps targeting different patches are compatible.

Result. one JSON object with the fields buildA (string), buildB (string), changes (array), notCompared (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## gamedata_sources

Parameters.

- `schemas_dir` string|null, optional, default null.

Family. objects

Return the dataset's provenance index. For every schema folder it reports the build, its era, the sections present, the SHA-256 of each section file, and the input hashes from the folder's manifest, so the whole dataset can be traced back to the game files it was derived from.

Result. one JSON object with the fields schemaVersion (integer), sources (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## graph_hubs

Parameters.

- `top` integer, optional, default 20.
- `type` string|null, optional, default null.

Family. product

The most connected nodes in the corpus graph, the documents and sections the corpus centres on. Use it to see what the corpus is really about.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## graph_neighbors

Parameters.

- `node` string, required.
- `depth` integer, optional, default 1.
- `edge` string|null, optional, default null.
- `limit` integer, optional, default 25.

Family. product

List the nodes within a depth of a start node in the corpus graph, over link edges and embedding-similarity edges. Use it to find the documents related to one you already have. The graph is built by rag install, beside the index.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## graph_path

Parameters.

- `from` string, required.
- `to` string, required.

Family. product

The shortest chain of edges between two nodes in the corpus graph, so you can see how two documents or subjects connect.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## hero_generate

Parameters.

- `map` string, required.
- `out_path` string, required.
- `spec_json` string, required.
- `game_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. porting

Generate a multi-form hero from a JSON brief and save the result to out_path, with one unit object per form derived from one base unit, one ability object per slot carrying its own Base Order ID, cooldown, cast time and per level tooltip, each form's ability list in uabi, and a self contained JASS block with one transform function per form and one timer driven damage stub per damaging ability. The whole brief is validated before the first object is created, so a brief naming an unknown base is refused with nothing written.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## hero_install

Parameters.

- `definition` string, required.
- `map` string, required.
- `out_path` string, required.
- `force` boolean, optional, default false.
- `game_dir` string|null, optional, default null.
- `role` string|null, optional, default null.
- `keep_source_stats` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Install a hero definition into a map and save the result to out_path. Remaps rawcodes that would collide, writes the hero's assets, and reports what the target map must still be wired with before the hero actually works, since a definition carries objects and script but not the target's own integration (roster registration, spell dispatch, per-player hero arrays). Refuses asset collisions unless force is set. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## hero_lint

Parameters.

- `definition` string, required.

Family. triggers

Validate a hero definition folder before installing it: assets present and hash-matched, objects installable, declared script functions present, no dangling references. Reads a definition and returns findings, it writes nothing. Run this before hero_install, because an install that fails halfway leaves a map holding some of a hero.

Result. one JSON object with the fields directory (string), id (string), checks (array), errors (integer), warnings (integer), ok (boolean)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## hero_roster

Parameters.

- `map` string, required.

Family. triggers

List the playable heroes a map registers, each with its rawcode, display name, the string tags from its registration call (typically a role and a portrait path), and which trigger's custom-text body holds its code. A hero is not a trigger, and counting one does not count the other, a measured map held 170 registered heroes across 39 Character triggers because a few grouped triggers bundle many heroes. Use this rather than counting triggers when asked how many heroes a map has. A map that creates heroes inline returns Ok=false.

Result. one JSON object with the fields ok (boolean), message (string), registryFunction (string), heroes (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## imports_list

Parameters.

- `map` string, required.

Family. map

List the map's import table (war3map.imp) against what the archive actually holds, so a table entry with no file behind it and a file absent from the table both show up.

Result. one JSON object with the fields hasManifest (boolean), entries (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## leak_ab

Parameters.

- `session_a` string, required.
- `session_b` string, required.
- `map` string|null, optional, default null.

Family. leaks

Diff two wc3watch sessions and report whether the handle and memory slope fell, each measured from its own forced garbage collection. The usual shape is one session with a trigger live and one with it disabled. With a map, name the periodic candidate whose predicted rate is nearest the drop, labelled an inference. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), a (object), b (object), handleDropPerMin (number), memoryDropPerMin (number), handlesFell (boolean), memoryFell (boolean), candidates (array), likelyDisabled (string), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## leak_benchmark

Parameters. none.

Family. leaks

Run the leak detector over a set of synthetic maps, each with a known injected leak of one creator kind plus a clean control, and report true positive and false positive counts so the detector's accuracy is a measured number. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), maps (integer), truePositives (integer), falsePositives (integer), falseNegatives (integer), precision (number), recall (number), cases (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## leak_harness

Parameters.

- `map` string, required.
- `lang` string, required.
- `target` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. leaks

Emit a small in-map harness in the map's own language (jass or lua) that drives the named hot leak candidates on a short timer so the runtime handle and memory slope is measurable in seconds rather than an hour, plus a handle count sampler. Writes the harness to out_path as valid syntax for the language. Does not start the game.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## leak_join

Parameters.

- `map` string, required.
- `session_folder` string, required.

Family. leaks

Join a map's static leak list to a wc3watch session folder. Reads samples.csv and events.csv, fits the handle and memory slopes from the session's forced garbage collection when one was marked, and joins each rising window to the periodic candidates from the static list and the session events and game log lines near it. Names the likely leaking trigger. Read-only. A folder with no samples.csv is reported clearly.

Result. one JSON object with the fields ok (boolean), message (string), session (string), sampleCount (integer), span (string), gcProbe (string), eventsJoined (integer), handleSlopePerMin (number), handleCorrelation (number), memorySlopePerMin (number), memoryCorrelation (number), drivers (array), and 2 more

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## leak_list

Parameters.

- `map` string, required.

Family. leaks

The ranked static handle-leak list for a map's script plus the periodic timers and triggers that feed it, and every hot leak target by function. Read-only, a presentation over script_leaks for the leak workflow.

Result. one JSON object with the fields ok (boolean), message (string), leaks (integer), hot (integer), repeat (integer), once (integer), targets (array), findings (array), periodic (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## leak_model

Parameters.

- `map` string, required.

Family. leaks

Report the model-level leaks and waste across a map's imported models and textures, the join between the model audit and the import table. Finds an imported model no field or script names, a model naming a texture the map does not hold, a model imported more than once under different names with identical bytes, and an imported texture no imported model names. Reuses the existing model parser and import primitives. Read-only.

Result. one JSON object with the fields modelsScanned (integer), texturesScanned (integer), findings (array), diagnostics (array), findingCount (integer), leakedImports (integer), missingTextures (integer), duplicateImports (integer), unusedTextures (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## library_audit

Parameters.

- `folder` string, required.
- `game_dir` string|null, optional, default null.

Family. leaks

Run the behavioural audit over every map in a folder and report the findings per map and the totals per check, so a whole library is ranked by how much it needs fixing. Opens one map at a time. Read-only.

Result. one JSON object with the fields maps (array), checks (array), totalFindings (integer), mapsWithFindings (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## library_compare

Parameters.

- `folder_a` string, required.
- `folder_b` string, required.

Family. leaks

Compare two folders of maps by file name and content hash, listing the maps only in the first folder, only in the second, and changed between them, plus the identical count and any file name that is not unique inside a folder. Read-only.

Result. one JSON object with the fields onlyInA (array), onlyInB (array), changed (array), ambiguousNames (array), same (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## library_report

Parameters.

- `folder` string, required.
- `out_path` string, required.
- `game_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. leaks

Write one Markdown report over a folder of maps, a summary line, a per-map table and the aggregated findings, to out_path. The report is built from the same probes the scan, audit and validate sweeps use. Writes only out_path and never a map.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## library_scan

Parameters.

- `folder` string, required.

Family. leaks

Open every map in a folder through the loader and report per map its size in bytes, the build it targets if known, whether it loads, its archive entry count and its diagnostic count, plus the loaded, failed and byte totals. Opens one map at a time. Read-only.

Result. one JSON object with the fields maps (array), loaded (integer), failed (integer), totalBytes (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## library_validate

Parameters.

- `folder` string, required.

Family. leaks

Run the load validation over every map in a folder and list the ones that fail, with the reason. A map that cannot be opened counts as invalid. Opens one map at a time. Read-only.

Result. one JSON object with the fields maps (array), valid (integer), invalid (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## lint

Parameters.

- `map` string, required.
- `against` string|null, optional, default null.

Family. scripts

Pre-flight the map: does its script compile, does war3map.imp agree with what the archive holds, do referenced asset paths resolve, and is every entry a loadable type. This is the 'is this map going to work' check and is the right thing to run before handing a map to anyone. A failure here usually means the map will not host or will hang on load with no error of its own.

Result. one JSON object with the fields checks (array), errors (integer), warnings (integer), ok (boolean)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## lint_baseline

Parameters.

- `map` string, required.
- `out_path` string, required.
- `against` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. scripts

Write the map's current lint findings to a baseline JSON file. A later gate, the CLI 'lint <map> --baseline <file>', then fails only on a finding this file does not already hold, so known and accepted warnings do not block CI. Writes a local file, never the map.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## list_files

Parameters.

- `map` string, required.
- `harvest` boolean, optional, default false.
- `brief` boolean, optional, default false.

Family. map

List the map archive's internal files, each with its name (null = unnamed/protected entry), size in bytes, whether wc3ctl knows/parses the format, and for unnamed entries the content type sniffed from their leading bytes ('BLP texture', 'MDX model', 'empty', ...). Pass brief true to get the total and only the first 50 files when the full list would be too large for the context.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), files (array), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## map_compat

Parameters.

- `map` string, required.
- `target` string|null, optional, default null.
- `schemas_dir` string|null, optional, default null.

Family. map

Check a map against a target build, given as a schema folder path or a build name. Reports compile time incompatibilities (natives, globals, types or functions the map uses and the target scripts do not declare) and display time ones (object fields the map modifies and the target does not define). IdentifiersChecked and FieldsChecked tell checked and fine apart from nothing to check. Omit target to use the configured default from settings, then the environment, then the newest schema. Never writes.

Result. one JSON object with the fields map (string), targetBuild (string), findings (array), notChecked (array), identifiersChecked (integer), fieldsChecked (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## map_info

Parameters.

- `map` string, required.

Family. map

Show a Warcraft III map's metadata: name, author, player count, playable dimensions and load diagnostics.

Result. one JSON object with the fields name (string), author (string), players (integer), width (integer), height (integer), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## map_info_get

Parameters.

- `map` string, required.

Family. map

Read the map's editable scenario fields (war3map.w3i): name, author, description, recommended players, tileset, environment, fog and the map-option flags. Richer than map_info; this is the field set map_info_set edits.

Result. one JSON object with the fields mapName (string), author (string), description (string), recommendedPlayers (string), playableWidth (integer), playableHeight (integer), players (integer), cameraBounds (string), tileset (string), lightEnvironment (string), globalWeather (string), soundEnvironment (string), and 29 more

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## map_info_set

Parameters.

- `map` string, required.
- `out_path` string, required.
- `field` string, required.
- `value` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. map

Set a scenario field and save the edited map to out_path. The input map is NEVER modified in place. Editable fields: MapName|Author|Description|RecommendedPlayers|Tileset|LightEnvironment|GlobalWeather|SoundEnvironment|WaterTintColor|FogStyle|FogStartZ|FogEndZ|FogDensity|FogColor (and the map-option booleans).

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## map_optimize

Parameters.

- `map` string, required.
- `out_path` string, required.
- `ratio` number, optional, default 1.
- `max_texture` integer, optional, default 256.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. map

Shrink every imported texture in a map in one pass and write a new map to out_path, replacing only those files and keeping the rest byte for byte. Models are left untouched by default and each texture is scaled to fit max_texture on its longest side. Pass ratio below 1 to decimate models too, which is unsafe by default because a decimated model once crashed the game on load with an ACCESS_VIOLATION, so load-test the result. A file whose optimised bytes are not smaller is left as it was. The input map is never modified.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## map_overview

Parameters.

- `map` string, required.

Family. map

One call per map: metadata (name, author, player count, dimensions), every archive file, the custom or modified object counts by Object Editor kind, and the load diagnostics. Use this when you need a map's overall shape instead of calling map_info, list_files and object_list separately.

Result. one JSON object with the fields info (object), files (array), objectCounts (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## mcp_audit_annotations

Parameters. none.

Family. product

Audit the MCP tool surface itself. Reports every tool whose annotations disagree with its name (a mutating name marked read-only) and every tool that declares no annotations, as a list of problems plus a count. An empty list means the surface is clean. Read-only.

Result. one JSON object with the fields problems (array), count (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## mcp_export_verify

Parameters.

- `target_dir` string, required.
- `source_dir` string|null, optional, default null.

Family. product

Verify that a wc3-mcp checkout holds the same server source scripts/export-wc3-mcp.py would export from this repository, reporting every file that differs without writing. Read-only.

Result. one JSON object with the fields inStep (boolean), sourceRoot (string), targetRoot (string), differences (array), checkedFiles (integer), message (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## model_audit

Parameters.

- `model` string, required.

Family. assets

Audit a .mdx or .mdl model for the resource waste and draw-nothing problems an optimiser can remove, and report what it holds. Finds duplicate textures, geosets with no texture or no triangles, zero-area faces, unused skeleton nodes and non-finite vertices. Counts the effects an MDX carries, particle emitters, particle emitters 2, ribbons and popcorns, from the binary chunk list, and reads each particle emitter's payload to report one that references a node the model does not define or a texture outside its texture list. Read-only.

Result. one JSON object with the fields path (string), format (string), geosets (integer), vertices (integer), faces (integer), textures (integer), sequences (integer), sequenceList (array), nodes (integer), particleEmitters (integer), particleEmitters2 (integer), ribbons (integer), and 6 more

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## model_compare

Parameters.

- `a` string, required.
- `b` string, required.
- `width` integer, optional, default 256.
- `height` integer, optional, default 256.
- `textures` string|null, optional, default null.

Family. assets

Render two models from four angles and score how close they look, by SSIM on the shaded frame and silhouette intersection-over-union, per angle and as a mean and worst case. The gate a lossy model step has to pass. Read-only.

Result. one JSON object with the fields a (string), b (string), width (integer), height (integer), angles (array), meanSsim (number), minSilhouetteIou (number), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## model_export_obj

Parameters.

- `map` string, required.
- `model` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Export a map-imported model to Wavefront OBJ at out_path, writing an MTL and any map-resolvable textures converted to PNG beside it. The map itself is never modified. Every file is staged beside the target and published only when all of them are complete, so a failure leaves no partial export. An existing .obj, .mtl or texture is refused and left unchanged unless overwrite is true. Returns the .obj path, the .mtl path, the texture names and any publish note.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## model_import

Parameters.

- `obj` string, required.
- `out_path` string, required.
- `texture` string|null, optional, default null.
- `fps` number, optional, default 30.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Import a foreign model into a Warcraft III MDX at out_path. Reads OBJ, glTF and GLB, STL, PLY and ASCII FBX, detecting the format from content so the extension does not matter, and refuses binary FBX. One geoset is built per source primitive with normals and UVs, each glTF base colour texture becomes a .blp beside the MDX with one entry per distinct texture, and each glTF animation becomes a named sequence. Bone keyframes are not written. The input is never modified, and an existing output is refused without overwrite.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## model_lint

Parameters.

- `model` string, required.

Family. assets

Lint one .mdx or .mdl for the structural problems the game renders wrongly or drops in silence: non-finite vertices, normals or UVs, zero-length normals, indices past the vertex array, zero-area triangles, skin bones the skeleton does not define, weight sets that do not sum to one, non-finite animation keys and textures no geoset references. Read-only.

Result. one JSON object with the fields path (string), geosets (integer), textures (integer), vertices (integer), findings (array), findingCount (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## model_lod

Parameters.

- `model` string, required.
- `ratios` string, required.
- `out_dir` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Generate a level-of-detail chain for a .mdx or .mdl model, writing one decimated MDX per ratio into out_dir. ratios is a comma separated list of face fractions to keep, each between 0 and 1, processed in order, so a descending list gives a descending chain. Every level keeps the model's materials, textures, sequences, bones, emitters and cameras byte for byte and shrinks only geometry. Ratio 1 is the original and its file is byte identical to the input. The input is never modified. Returns each level's counts and byte size.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## model_optimize

Parameters.

- `model` string, required.
- `out_path` string, required.
- `ratio` number, optional, default 0.5.
- `strip_effects` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Decimate a .mdx or .mdl model's geometry with the quadric error metric, drop zero-area faces and duplicate textures, and write a smaller MDX at out_path, leaving the other chunks untouched. ratio is the target fraction of faces to keep. Set strip_effects to also drop the model's effect chunks, particle emitters (PREM), particle emitters 2 (PRE2), ribbons (RIBB) and corns (CORN), which changes how the model looks, so it defaults to false. Returns the before and after vertex and face counts and the number of effect chunks removed. The input is never modified.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## model_primitive

Parameters.

- `kind` string, required.
- `out_path` string, required.
- `size` number, optional, default 32.
- `length` number, optional, default 0.
- `inner_radius` number, optional, default 0.
- `segments` integer, optional, default 16.
- `texture_path` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Generate a primitive mesh and write a real MDX at out_path. Kinds are card, ring, cone and capsule. The output has a geoset, a material and a texture reference, so it loads like any imported model. Use it for effects and placeholders without an external asset. A curved kind with fewer than three segments is refused.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## model_squish

Parameters.

- `model` string, required.
- `out_path` string, required.
- `ratio` number, optional, default 0.5.
- `texture_side` integer, optional, default 0.
- `texture_out` string|null, optional, default null.
- `min_ssim` number, optional, default 0.9.
- `min_silhouette` number, optional, default 0.85.
- `no_lossless` boolean, optional, default false.
- `no_decimate` boolean, optional, default false.
- `strip_effects` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

A lossy model run with a render gate. Applies the lossless key drop, then quadric decimation, renders the original and each candidate from four angles after every pass, and keeps a pass only while SSIM and silhouette stay above the floor. Writes a receipt per pass. Optionally shrinks the model's own textures into a side folder. The input is never modified.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_add

Parameters.

- `map` string, required.
- `name` string, required.
- `from` string, required.
- `out_path` string|null, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. map

Add or replace one internal archive file from a file on disk, byte-faithfully. Reports whether the name was added or replaced and whether the payload round-trips. Writes a copy, the input map is never modified in place.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_bytes_diff

Parameters.

- `map` string, required.
- `other` string, required.

Family. map

Compare two archives entry by entry by name. For each name present in both, report whether the payload SHA256 matches, and list names only one side carries. The byte-level counterpart of the structural mpq-diff. Read-only.

Result. one JSON object with the fields pathA (string), pathB (string), inBoth (integer), identicalCount (integer), differ (integer), differences (array), onlyInA (array), onlyInB (array), identical (boolean)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## mpq_extract_all

Parameters.

- `map` string, required.
- `out_dir` string|null, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. map

Extract every entry of a map archive to a directory, including unnamed protected blocks, through the same path-safety rule 'extract' uses. The map is never modified. Returns the output directory, the counts and the written file names.

Result. one JSON object with the fields outDir (string), totalEntries (integer), namedEntries (integer), unnamedEntries (integer), totalBytes (integer), files (array), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_listfile

Parameters.

- `map` string, required.

Family. map

Report an archive's listfile coverage in three parts, the entries named by the listfile it ships, the entries recovered by the standard-name probe, and the entries with no name at all. Read-only.

Result. one JSON object with the fields path (string), totalEntries (integer), namedByListfile (integer), recoveredByStandardProbe (integer), withoutName (integer), withoutNameSample (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## mpq_merge

Parameters.

- `map` string, required.
- `other` string, required.
- `out_path` string|null, required.
- `prefer` string, optional, default "a".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. map

Merge two map archives into one, taking every readable named entry from both. A name in both is resolved by the preferred side and reported as a conflict. Writes a copy of the first map plus both maps' entries, the input maps are never modified.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_name_recover

Parameters.

- `map` string, required.
- `dictionary` string, required.

Family. map

Resolve a dictionary of candidate names against the archive's hash table and report the names that hit unnamed blocks. Narrower than deprotect_map, which harvests its own candidates from the map. Read-only.

Result. one JSON object with the fields dictionaryPath (string), candidatesTried (integer), unnamedBefore (integer), recovered (integer), names (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## mpq_rebuild

Parameters.

- `map` string, required.
- `out_path` string|null, required.
- `grow_hash` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. map

Rebuild a map archive from scratch into a healthy one, re-placing every readable entry and dropping the blocks protection made unreadable or left nameless. Reports the entry counts before and after and the names dropped. Writes a copy, the input map is never modified in place.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_remove

Parameters.

- `map` string, required.
- `name` string, required.
- `out_path` string|null, required.
- `confirm` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. map

Remove one internal file by rebuilding the archive without it, and report whether the rebuild actually dropped the name. Writes a copy, the input map is never modified in place.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. CONFIRM_REQUIRED, OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## mpq_remove_many

Parameters.

- `map` string, required.
- `names` array, required.
- `out_path` string, required.
- `confirm` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. map

Remove many internal files in one rebuild and report how many were dropped. Writes a copy, the input map is never modified in place. The one-rebuild form of mpq_remove, for pruning the files asset_unused reports as unreferenced.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. CONFIRM_REQUIRED, OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## mpq_strip_bookkeeping

Parameters.

- `map` string, required.
- `out_path` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. map

Save a copy of a map with the (listfile) and (attributes) bookkeeping entries dropped, then confirm the copy opens again. The input map is never overwritten. out_path defaults to a sibling '<map>.edited.<ext>'.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## mpq_verify

Parameters.

- `map` string, required.

Family. map

Open every entry of a map archive and read it to its end, reporting any that fail with the error raised, plus a summary count. Use this to prove an archive is fully usable rather than merely addressable. Read-only.

Result. one JSON object with the fields path (string), totalEntries (integer), readableEntries (integer), unreadableEntries (integer), unreadable (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## new_map

Parameters.

- `out_path` string, required.
- `name` string|null, optional, default null.
- `tiles` integer|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. repair

Create a blank, World-Editor-openable map (.w3x) at out_path. No input map. Returns where it was written.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_batch_set

Parameters.

- `map` string, required.
- `out_path` string, required.
- `edits` array, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. objects

Apply several object edits in one load and save once to out_path. The input map is NEVER modified in place. Edits are all or nothing, so the first failure stops the batch and nothing is written. Use this instead of two object_set calls from one base, which loses the first edit. Returns both digests, the rewritten count and the number applied.

Result. one JSON object with the fields savedTo (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer), applied (integer), message (string), warnings (array)

Errors. STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_bulk_set

Parameters.

- `map` string, required.
- `out_path` string, required.
- `set` string, required.
- `kind` string, optional, default "unit".
- `where` array|null, optional, default null.
- `game_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. objects

Set one field on every object of a kind that matches every where clause, and save the edited copy to out_path. The input map is NEVER modified in place. A where clause is code=value and tests the object's merged value (base game data overlaid by the map). Reports how many objects changed.

Result. one JSON object with the fields savedTo (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer), message (string), kind (string), matched (integer), changed (integer), skipped (integer), failed (array), partial (boolean)

Errors. STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_clone

Parameters.

- `map` string, required.
- `out_path` string, required.
- `rawcode` string, required.
- `new_rawcode` string, required.
- `kind` string, optional, default "unit".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. objects

Deep-clone a map object under a new rawcode and save the edited map to out_path. The input map is NEVER modified in place. The clone copies the source's field deltas and, for a leveled kind, its per-level data too, keeping the source's base. Refuses a rawcode already taken by the map.

Result. one JSON object with the fields savedTo (string), message (string), newRawcode (string), fieldsCopied (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_deprecated

Parameters.

- `map` string, required.
- `target` string, required.
- `schemas_dir` string|null, optional, default null.

Family. objects

List object fields the target build's schema does not define but the map still sets, meaning the edit is ignored on that build. Read-only. The target is a schema folder path or a build name under the schemas directory. A target with no object data section is reported as such rather than as a clean result.

Result. one JSON object with the fields targetBuild (string), fieldsChecked (integer), fields (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## object_export

Parameters.

- `map` string, required.
- `out_path` string, required.
- `kind` string, optional, default "unit".
- `game_dir` string|null, optional, default null.

Family. objects

Export the objects of one kind (base game data overlaid by the map's deltas) to a CSV file at out_path, one row per object, one column per field code, with a header. Read-only on the map. Feed the CSV back with object_import.

Result. one JSON object with the fields csvPath (string), message (string), kind (string), objectCount (integer), columns (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## object_field_options

Parameters.

- `kind` string, required.
- `field` string, required.
- `game_dir` string|null, optional, default null.

Family. objects

The legal values for one object-data field, taken from what the base game data actually uses for it. Ask this BEFORE object_set on an enumerated field, so the value written is one the game already accepts rather than a guess.

Result. one JSON object with the fields type (string), isList (boolean), options (array), diagnostic (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## object_fix_model_ext

Parameters.

- `map` string, required.
- `out_path` string, required.
- `redirect_missing` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. objects

Repair object-data model paths in one pass and save to out_path. A .mdl is rewritten to the .mdx that exists, a doubled war3mapImported prefix is collapsed, and any path still unresolved is redirected to redirect_missing when that file resolves. Base-game roots are left alone. The input map is NEVER modified in place.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_form

Parameters.

- `map` string, required.
- `rawcode` string, required.
- `kind` string, optional, default "unit".
- `game_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. objects

Get an object as an editable FORM rather than a flat field dump: fields grouped and ordered the way the World Editor groups them (Art, Combat, Stats, ...), fields that do not apply to this object hidden, each field's legal range and whether a blank is allowed, and which object-data layer it is written to (war3map.* or the war3mapSkin.* twin). Prefer this over object_get when deciding what to edit.

Result. one JSON object with the fields rawcode (string), name (string), baseRawcode (string), kind (string), groups (array), fieldCount (integer), hiddenFieldCount (integer), diagnostics (array)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## object_get

Parameters.

- `map` string, required.
- `rawcode` string, required.
- `kind` string|null, optional, default null.
- `game_dir` string|null, optional, default null.

Family. objects

Get an object's merged fields (base game data overlaid by the map's deltas), each labeled 'base' or 'map', with field names resolved. Omit kind to auto-detect it from the rawcode.

Result. one JSON object with the fields rawcode (string), found (boolean), baseRawcode (string), name (string), fields (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## object_import

Parameters.

- `map` string, required.
- `out_path` string, required.
- `csv_path` string, required.
- `kind` string, optional, default "unit".
- `game_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. objects

Import a CSV of one kind back into the map, adding objects the map does not define and updating those it does, then save the edited copy to out_path. The input map is NEVER modified in place. An unknown column refuses the whole import. A blank cell leaves that field untouched.

Result. one JSON object with the fields savedTo (string), message (string), kind (string), added (integer), updated (integer), fieldsSet (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_list

Parameters.

- `map` string, required.
- `kind` string, optional, default "unit".
- `game_dir` string|null, optional, default null.
- `brief` boolean, optional, default false.

Family. objects

List the map's custom/modified objects of one Object Editor kind: rawcode, base rawcode (null = created from scratch) and resolved name. Pass brief true to get the total and only the first 50 objects when the full list would be too large for the context.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## object_migrate

Parameters.

- `map` string, required.
- `out_path` string, required.
- `to` string, required.
- `schemas_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. objects

Rewrite a map's object data to a target build's field set, dropping every field the target does not define, and save the edited copy to out_path. The input map is NEVER modified in place. Every drop is reported, and a target with no object data section refuses rather than stripping everything.

Result. one JSON object with the fields savedTo (string), message (string), targetBuild (string), fieldsKept (integer), fieldsDropped (integer), drops (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_new

Parameters.

- `map` string, required.
- `out_path` string, required.
- `base_rawcode` string, required.
- `kind` string, optional, default "unit".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. objects

Create a new custom object derived from a base rawcode (a fresh unused rawcode is allocated) and save the edited map to out_path. The input map is NEVER modified in place. Returns the new rawcode.

Result. one JSON object with the fields savedTo (string), message (string), newRawcode (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_noop_fields

Parameters.

- `map` string, required.
- `kind` string|null, optional, default null.
- `game_dir` string|null, optional, default null.

Family. objects

List object fields whose map value equals the base game value, so the edit changes nothing. Read-only. Needs a game_dir for the base values; without one the answer is map-only and says so.

Result. one JSON object with the fields count (integer), fields (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## object_rawcode_suggest

Parameters.

- `map` string, required.
- `kind` string, required.
- `count` integer, optional, default 10.
- `game_dir` string|null, optional, default null.

Family. objects

Propose free four-character rawcodes for one Object Editor kind that collide with neither the map's objects nor the base game data. Read-only. Without a game_dir the suggestions are checked against the map only.

Result. one JSON object with the fields kind (string), count (integer), rawcodes (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## object_rename

Parameters.

- `map` string, required.
- `out_path` string, required.
- `old_rawcode` string, required.
- `new_rawcode` string, required.
- `kind` string, optional, default "unit".
- `rewrite_refs` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. objects

Rename an object rawcode and save the edited copy to out_path. The input map is NEVER modified in place. With rewrite_refs true, every object-data field value and script string literal that names the old rawcode is rewritten too, so references do not dangle. Refuses a new rawcode already taken by the map.

Result. one JSON object with the fields savedTo (string), message (string), kind (string), oldRawcode (string), newRawcode (string), fieldsRewritten (integer), scriptLiteralsRewritten (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## object_search

Parameters.

- `map` string, required.
- `query` string, required.
- `kind` string|null, optional, default null.
- `field` string|null, optional, default null.
- `game_dir` string|null, optional, default null.

Family. objects

Full-text search across every custom or modified object's field values, in both the base game data and the map's own deltas. Read-only. Reports the kind, rawcode, resolved name, field code, and the base and map values, so a hit is traceable. Pass kind to search one Object Editor type or field to restrict to one field code.

Result. one JSON object with the fields query (string), count (integer), hits (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## object_set

Parameters.

- `map` string, required.
- `out_path` string, required.
- `rawcode` string, required.
- `field` string, required.
- `value` string, required.
- `kind` string, optional, default "unit".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. objects

Set a field on an object of one Object Editor kind and save the edited map to out_path (only that kind's war3map.* file is re-serialized). The input map is NEVER modified in place. Field syntax: a bare 4-char field code, or 'code:N' to select level N (ability/upgrade) or variation N (doodad). Returns both SHA-256 digests and the count of files rewritten, refuses an existing output unless overwrite is true, and refuses a stale expect_input_sha256.

Result. one JSON object with the fields savedTo (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer), message (string), warning (string)

Errors. STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## palette_doodad

Parameters.

- `map` string, required.
- `game_dir` string|null, optional, default null.

Family. objects

List the doodad types placeable on a map - the palette to consult before place_doodad. Unions the base-game doodad catalog (installed GameData) with the map's own object-data: custom New* doodads and modified Base* doodads. Each entry carries its four-char rawcode, a resolved display name (null when unresolvable), its source (base|map-custom|map-modified) and the base it derives from. Without a WC3 install the palette is map-only. The map is never modified.

Result. one JSON object with the fields ok (boolean), message (string), entries (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## pathing_export_png

Parameters.

- `map` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Render a map's war3map.wpm pathing grid to a colour-coded PNG at out_path, one pixel per pathing cell with a legend strip of the pathing flags. The input map is never modified. The same file re-imports through pathing_import_png.

Result. one JSON object with the fields savedTo (string), message (string), width (integer), height (integer), sizeBytes (integer), legend (array), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## pathing_import_png

Parameters.

- `map` string, required.
- `out_path` string, required.
- `png` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Set the pathing flags from a colour-coded PNG and save the edited map to out_path. Each cell's flags are replaced by the value its pixel decodes to. The image must be at least the map's Width by Height cell grid (the top-left region is read, so a legend to the right is ignored) and any unknown colour is refused. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## pathing_paint

Parameters.

- `map` string, required.
- `out_path` string, required.
- `center_x` integer, required.
- `center_y` integer, required.
- `radius` integer, required.
- `flags` string, required.
- `op` string, optional, default "set".
- `shape` string, optional, default "circle".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Paint pathing bits over a circular or square brush and save the edited map to out_path. The input map is NEVER modified in place. Flags (comma-separated): Walk|Fly|Build|Blight|Water - a SET bit RESTRICTS that capability (Walk set = ground units cannot walk there). op: set|clear|toggle.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## pathing_validate

Parameters.

- `map` string, required.

Family. terrain

Flag pathing problems in a map's war3map.wpm. It reports a pocket of unpathable tiles fully enclosed by pathable ones, a start location sitting on an unpathable tile, and a map corner that is unpathable or sealed off from the main walkable area. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), width (integer), height (integer), issues (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## place_doodad

Parameters.

- `map` string, required.
- `out_path` string, required.
- `placement` object|null, optional, default null.
- `type_rawcode` string|null, optional, default null.
- `x` number|null, optional, default null.
- `y` number|null, optional, default null.
- `z` number|null, optional, default null.
- `rotation` number|null, optional, default null.
- `scale` number|null, optional, default null.
- `variation` integer|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Place a doodad instance at (x, y) on the map's doodad layer (war3map.doo) and save the edited map to out_path. The input map is NEVER modified in place, so out_path must differ from it. Returns the assigned creation number. Pass the placement as one object, or the flattened type_rawcode, x, y, z, rotation, scale and variation arguments this tool shipped with, which stay for one version (SUR-08).

Result. one JSON object with the fields savedTo (string), message (string), creationNumber (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_grid

Parameters.

- `map` string, required.
- `type_rawcode` string, required.
- `rows` integer, required.
- `cols` integer, required.
- `spacing` number, optional, default 128.
- `owner_id` integer, optional, default 0.
- `kind` string, optional, default "unit".
- `out_path` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Place a regular rows x cols grid of copies of one unit or doodad type and save the edited copy to out_path. The grid is centred on the map origin. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), placed (integer), creationNumbers (array), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_item

Parameters.

- `map` string, required.
- `out_path` string, required.
- `type_rawcode` string, required.
- `x` number, required.
- `y` number, required.
- `z` number, optional, default 0.
- `rotation` number, optional, default 0.
- `scale` number, optional, default 1.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Place a preplaced item of the given type at world (x, y) and save the edited map to out_path. Items share war3mapUnits.doo with units (WC3 spawns a ground item because the rawcode is an item), so the item has no owning player. The input map is NEVER modified in place. Returns the assigned creation number.

Result. one JSON object with the fields savedTo (string), message (string), creationNumber (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_region

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `left` number, required.
- `bottom` number, required.
- `right` number, required.
- `top` number, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Define a rectangular region on the map's region layer (war3map.w3r) and save the edited map to out_path. The input map is NEVER modified in place - out_path must differ from it. Bounds need right>left and top>bottom. Returns the assigned creation number.

Result. one JSON object with the fields savedTo (string), message (string), creationNumber (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_scatter

Parameters.

- `map` string, required.
- `type_rawcode` string, required.
- `count` integer, required.
- `region` string|null, optional, default null.
- `seed` integer, optional, default 0.
- `out_path` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Place N copies of a unit type at deterministic random positions and save the edited copy to out_path. Positions are uniform inside a named region or across the whole playable area, and the same seed reproduces the same layout. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), placed (integer), creationNumbers (array), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_start_location

Parameters.

- `map` string, required.
- `out_path` string, required.
- `player` integer, required.
- `x` number, required.
- `y` number, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Place or move a player's start location at world (x, y) and save the edited map to out_path. The input map is NEVER modified in place. A start location is stored as a preplaced 'sloc' unit owned by the player; the World Editor permits exactly one per player, so if this player already has one it is moved (its creation number preserved) rather than duplicated. Returns the start location's creation number.

Result. one JSON object with the fields savedTo (string), message (string), creationNumber (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## place_unit

Parameters.

- `map` string, required.
- `out_path` string, required.
- `placement` object|null, optional, default null.
- `type_rawcode` string|null, optional, default null.
- `owner_id` integer|null, optional, default null.
- `x` number|null, optional, default null.
- `y` number|null, optional, default null.
- `z` number|null, optional, default null.
- `rotation` number|null, optional, default null.
- `scale` number|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Place a unit of the given type, owned by a player, at world (x, y) and save the edited map to out_path. The input map is NEVER modified in place, so out_path must differ from it. Returns the assigned creation number (the id triggers use to reference the unit). Pass the placement as one object, or the flattened type_rawcode, owner_id, x, y, z, rotation and scale arguments this tool shipped with, which stay for one version (SUR-08).

Result. one JSON object with the fields savedTo (string), message (string), creationNumber (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## placed_doodad_get

Parameters.

- `map` string, required.
- `creation_number` integer, required.

Family. placed

Show one placed doodad's full state by creation number: type, position, rotation, scale, variation and life percentage.

Result. one JSON object with the fields creationNumber (integer), typeRawcode (string), name (string), x (number), y (number), z (number), rotation (number), scale (object), variation (integer), lifePercent (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## placed_doodad_remove

Parameters.

- `map` string, required.
- `creation_number` integer, required.
- `out_path` string|null, optional, default null.
- `confirm` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Remove a doodad already placed on the map and save the edited copy to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. CONFIRM_REQUIRED, OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive yes, idempotent yes, open-world no.

## placed_doodad_set

Parameters.

- `map` string, required.
- `creation_number` integer, required.
- `field` string, required.
- `value` string, required.
- `out_path` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Set one field on a doodad already placed on the map and save the edited copy to out_path. The input map is NEVER modified in place. A position field takes 'x,y' or 'x,y,z'.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## placed_doodads_list

Parameters.

- `map` string, required.

Family. placed

List every doodad and destructable already PLACED on the map (war3map.doo), each with the creation number needed to edit or remove it, plus its type rawcode, position, rotation, scale and variation.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## placed_fix_refs

Parameters.

- `map` string, required.
- `out_path` string|null, optional, default null.
- `to_rawcode` string|null, optional, default null.
- `game_dir` string|null, optional, default null.
- `confirm` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Drop (or, with to_rawcode, rewrite) placements that reference a missing rawcode and save the edited copy to out_path, reporting each change. Refuses when nothing is wrong. The input map is NEVER modified in place.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. CONFIRM_REQUIRED, OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## placed_missing_refs

Parameters.

- `map` string, required.
- `game_dir` string|null, optional, default null.

Family. placed

List placements whose rawcode is defined neither in the map's object data nor in the base game data. Without a game_dir only the map's object data can be consulted, so stock base-game types read as unresolved too and the message says so. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), count (integer), missing (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## placed_stats

Parameters.

- `map` string, required.
- `game_dir` string|null, optional, default null.

Family. placed

Count the units and doodads already placed on the map, grouped by rawcode and by kind, with each type's resolved name where one is available. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), unitTotal (integer), doodadTotal (integer), types (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## placed_unit_get

Parameters.

- `map` string, required.
- `creation_number` integer, required.

Family. placed

Show one placed unit's full state by creation number: type, owner, position, facing, scale, hero level and attributes, hit points and mana percentages, gold and target acquisition range.

Result. one JSON object with the fields creationNumber (integer), typeRawcode (string), name (string), ownerId (integer), x (number), y (number), rotation (number), scale (object), heroLevel (integer), heroStrength (integer), heroAgility (integer), heroIntelligence (integer), and 4 more

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## placed_unit_remove

Parameters.

- `map` string, required.
- `creation_number` integer, required.
- `out_path` string|null, optional, default null.
- `confirm` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Remove a unit already placed on the map and save the edited copy to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. CONFIRM_REQUIRED, OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive yes, idempotent yes, open-world no.

## placed_unit_set

Parameters.

- `map` string, required.
- `creation_number` integer, required.
- `field` string, required.
- `value` string, required.
- `out_path` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Set one field on a unit already placed on the map and save the edited copy to out_path. The input map is NEVER modified in place. A position field takes 'x,y', a scale field takes one number (uniform) or 'sx,sy,sz', and Facing is in RADIANS (pi/2 faces north), the unit the map file stores.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## placed_units_list

Parameters.

- `map` string, required.
- `brief` boolean, optional, default false.

Family. placed

List every unit already PLACED on the map (war3mapUnits.doo), each with the creation number needed to edit or remove it, plus its type rawcode, owner, position and hero stats. Start locations appear here too. Call this before placed_unit_set or placed_unit_remove. Pass brief true to get the total and only the first 50 units when the full list would be too large for the context.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), units (array), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## player_list

Parameters.

- `map` string, required.

Family. placed

List the map's player slots (war3map.w3i): each player's id, name, color, race, controller and whether its start position is fixed.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## player_set_color

Parameters.

- `map` string, required.
- `out_path` string, required.
- `player_id` integer, required.
- `color` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Set a player's team color and save the edited map to out_path. war3map.w3i has no per-player color field, so this rewrites the SetPlayerColor call the map script already carries. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## player_set_force

Parameters.

- `map` string, required.
- `out_path` string, required.
- `player_id` integer, required.
- `force_index` integer, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Move a player into a force (team) and save the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## player_set_name

Parameters.

- `map` string, required.
- `out_path` string, required.
- `player_id` integer, required.
- `name` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Set a player's display name (war3map.w3i) and save the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## player_set_race

Parameters.

- `map` string, required.
- `out_path` string, required.
- `player_id` integer, required.
- `race` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Set a player's race (war3map.w3i) and save the edited map to out_path. Accepted values are human, orc, undead, nightelf and random. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## port_deps

Parameters.

- `map` string, required.
- `rawcode` string, required.
- `kind` string|null, optional, default null.
- `game_dir` string|null, optional, default null.

Family. porting

Report the full dependency closure of a rawcode, its objects grouped by kind, its assets with their byte sizes, its display strings and its script functions. The readable shape of a dependency bundle, before porting it.

Result. one JSON object with the fields rootRawcode (string), rootName (string), rootKind (string), objectGroups (array), objectCount (integer), assets (array), assetsInMap (integer), assetsMissing (integer), bytesInMap (integer), strings (array), functions (integer), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## port_into_version

Parameters.

- `source_map` string, required.
- `rawcode` string, required.
- `target_map` string, required.
- `target` string, required.
- `out_path` string, required.
- `schemas_dir` string|null, optional, default null.
- `include_script` boolean, optional, default true.
- `game_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. porting

Port a unit, then downgrade the result to a target build's schema. Object fields the build does not declare are removed and script identifiers it lacks are flagged. Writes a NEW file at out_path - the source and target maps are NEVER modified.

Result. one JSON object with the fields savedTo (string), report (object), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer), files (array)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## port_preview

Parameters.

- `source_map` string, required.
- `rawcode` string, required.
- `target_map` string, required.
- `include_script` boolean, optional, default true.
- `game_dir` string|null, optional, default null.

Family. porting

Dry run, listing exactly what a port of the rawcode into the target map would add or change, writing nothing. Same code path as port_unit, so the preview and the port cannot drift.

Result. one JSON object with the fields rootRawcode (string), rootPortedTo (string), rootName (string), remaps (array), objects (array), copiedFiles (array), skippedFiles (array), inlinedStrings (integer), warnings (array), diagnostics (array), script (object), carriedFiles (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## port_unit

Parameters.

- `source_map` string, required.
- `rawcode` string, required.
- `target_map` string, required.
- `include_script` boolean, optional, default true.
- `game_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. porting

Port a unit (its custom-object closure + imported assets + strings, and best-effort its JASS trigger closure) from a source map into a target map, auto-remapping rawcode collisions. WRITES A NEW FILE '<target>.ported.<ext>' next to the target map (overwritten if it exists from a previous run) - the source and target maps themselves are NEVER modified. Returns the full port report: remaps, ported objects, copied files, inlined strings, script info and warnings.

Result. one JSON object with the fields savedTo (string), report (object), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer), files (array)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## quickstart

Parameters.

- `goal` string|null, optional, default null.

Family. product

Route a broad request to the right workflow. Returns the ten core workflows a map task usually falls into (inspect, port-hero, reforged-3-fix, black-portraits, shrink-map, hunt-leaks, edit-objects, terrain-and-placement, lua-conversion, patch-compat), each naming the MCP tools and the wc3ctl commands that carry it out. Pass a goal (a substring such as 'portrait', 'leak' or 'shrink') to narrow the list. With more than two hundred tools on this server, call this first when the caller states an outcome rather than a tool. Read-only, it touches no map.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## rag_install

Parameters.

- `threads` integer|null, optional, default null.
- `batch` integer|null, optional, default null.

Family. product

Install the local semantic index and graph over the Warcraft III corpus. Fetches the embedding model and the corpus dataset, verifies each, and builds the index and the graph, after which docs_search is semantic and the graph tools work. Needs network on the first run. It writes many files under the user's local app data, in the rag root the answer names, and never into a map folder, so it is not gated like a single artifact and reports the root and how many files it wrote instead. The corpus is fixed to the bundled dataset, never a folder named by the caller, so this cannot read arbitrary local files.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only no, destructive no, idempotent yes, open-world yes.

## rag_status

Parameters. none.

Family. product

Report whether the local semantic retrieval index is installed, with its chunk count, corpus and the embedding model. docs_search uses the index automatically once it is installed.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## region_create

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `x` number, required.
- `y` number, required.
- `w` number, required.
- `h` number, required.
- `weather` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Create a rectangular region from its south-west corner (x, y) plus a width w and height h, and save the edited map to out_path. The input map is NEVER modified in place. Rejects a blank or duplicate name and a non-positive size. Set weather to attach the default weather effect. Returns the assigned creation number.

Result. one JSON object with the fields savedTo (string), message (string), creationNumber (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## region_list

Parameters.

- `map` string, required.

Family. placed

List the map's rectangular regions (war3map.w3r), each with its creation number, name, and bounds. Add regions with place_region.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## region_remove

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `confirm` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. placed

Remove a region by name and save the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. CONFIRM_REQUIRED, OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## region_to_rect

Parameters.

- `map` string, required.
- `region` string, required.

Family. placed

Resolve one named region to the four corner values a JASS trigger's Rect call needs (left, bottom, right, top), with a ready-to-copy Rect(...) line. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), creationNumber (integer), name (string), left (number), bottom (number), right (number), top (number), jass (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## region_validate

Parameters.

- `map` string, required.

Family. placed

Flag overlapping regions, zero-area regions and regions that fall outside the playable bounds in a map's war3map.w3r. Regions sharing only an edge do not overlap. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), regionCount (integer), issues (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## render_model

Parameters.

- `map` string, required.
- `rawcode` string, required.
- `out_path` string, required.
- `kind` string|null, optional, default null.
- `game_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Render an object's model (map-imported .mdx/.mdl) to a PNG written at out_path. Returns the saved path and byte size; small images also come back inline as MCP image content. The map itself is never modified.

Result. one JSON object with the fields content (array), structuredContent (any), isError (boolean), _meta (array)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## repair_audit_errors

Parameters.

- `map` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. repair

Repair the object-data defects audit reports. Fills ability level gaps (a field with no value at a declared level, copied from the nearest valid level), clears dangling references and impossible requirements, and saves to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## repair_generated

Parameters.

- `map` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. repair

Repair a map that wc3ctl itself generated, whose preplaced-hero helper block is too narrow for the heroes installed into it, and save the repaired map to out_path. Widens the player slots, rewrites hero owners in the script, and brings war3map.w3i and the map header into line. This is NOT a general map repair: it looks for wc3ctl's own generated helper function and refuses any map that does not carry one. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## repair_portraits

Parameters.

- `map` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. repair

Remove the pre-900 camera chunk from every at-risk model so the portrait pane renders under 3.0.0 and later, and save the repaired map to out_path. Camera removal only. The version re-stamp is refused because it was measured to crash the map on load. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## replay_diff

Parameters.

- `a` string, required.
- `b` string, required.

Family. leaks

Compare two recorded games (.w3g). Reports whether they played the same map, the length difference, and each player's leave time difference, keyed by slot. Read-only.

Result. one JSON object with the fields fileA (string), fileB (string), mapA (string), mapB (string), sameMap (boolean), lengthA (string), lengthB (string), lengthDelta (string), leaveDeltas (array), problemA (string), problemB (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## replay_find_map

Parameters.

- `replay` string, required.
- `maps` string|null, optional, default null.

Family. leaks

The map path a replay (.w3g) records, and whether that map file exists in the Maps folder, resolving the .w3x or .w3m. Default folder is the user's Documents\Warcraft III\Maps. Read-only.

Result. one JSON object with the fields file (string), recordedMap (string), cleanMapPath (string), mapsFolder (string), resolvedPath (string), found (boolean), candidates (array), problem (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## replay_players

Parameters.

- `replay` string, required.

Family. leaks

Per-player table for one recorded game (.w3g). Reports name, slot, result, leave time and leave reason. Team and race are not carried by this replay parser, so both are omitted rather than guessed. Read-only.

Result. one JSON object with the fields file (string), map (string), length (string), players (array), problem (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## replay_stats

Parameters.

- `replay` string, required.

Family. leaks

Aggregate stats for one recorded game (.w3g). Reports duration, player count, actions per minute per player when the record carries them, and the disconnect count. Read-only.

Result. one JSON object with the fields file (string), duration (string), playerCount (integer), disconnects (integer), players (array), actionsAvailable (boolean), problem (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## replay_summary

Parameters.

- `path` string|null, optional, default null.
- `map` string|null, optional, default null.

Family. leaks

Read recorded games (.w3g) and report each one's map, length, players, and how and when every player left. A game where any player left with result 0x01 is flagged as a disconnect, which is how a dropped or desynced game is scored without anyone writing results down. Reforged autosaves every game under Documents\Warcraft III\BattleNet\<account>\Replays\Autosaved.

Result. one JSON object with the fields games (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## replay_timeline

Parameters.

- `replay` string, required.

Family. leaks

Ordered event list for one recorded game (.w3g). Reports the game start, each chat line and player leave with its reason, and the game end time. Read-only.

Result. one JSON object with the fields file (string), length (string), events (array), problem (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## roundtrip

Parameters.

- `map` string, required.

Family. scripts

Verify the map survives a load and save byte for byte. Any file that differs is one this toolkit does not reproduce faithfully, which is worth knowing BEFORE editing that map, since a save would then rewrite it. Archive bookkeeping the container regenerates is excluded by design.

Result. one JSON object with the fields faithful (boolean), mismatches (array), excludedNotes (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_api_usage

Parameters.

- `map` string, required.
- `target` string, required.
- `schemas_dir` string|null, optional, default null.

Family. scripts

List the natives and functions the map script uses that a target game build does not declare, the compile-time side of a port. Uses the same schema membership test as the compat check. Read-only.

Result. one JSON object with the fields scriptFile (string), targetBuild (string), used (integer), missing (integer), usages (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_bench

Parameters.

- `map` string, required.

Family. scripts

Rank every script function by static engine cost per second, per-tick CreateUnit, special effects, group enumeration, condition allocation and damage, plus the leak shape each one carries. Headless and needs no game client, so an ability can be triaged before a play test. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), file (string), functions (integer), periodic (integer), rows (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_callgraph

Parameters.

- `map` string, required.

Family. scripts

The call edges between the functions a map's war3map.j declares, caller to callee. Only edges between functions this script declares are included, so a native call or a call through a local code value is not an edge. Read-only.

Result. one JSON object with the fields scriptFile (string), functions (integer), edges (integer), calls (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_check

Parameters.

- `map` string, required.

Family. scripts

Check the map script for errors that stop it loading, war3map.j or war3map.lua. Lua is checked against Lua 5.3 exactly as luac 5.3.6 would, with its line and message, JASS by the built-in JASS checks. A script that fails here cannot run, and the game gives no reason, it just shows no slots. A Lua script that carries the Wurst package runtime is reported as a warning, because a JASS-to-Lua conversion of a Wurst map does not preserve package initialization and a spell can then fail in game with "Could not initialize package".

Result. one JSON object with the fields scriptFile (string), language (string), ok (boolean), issues (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_complexity

Parameters.

- `map` string, required.

Family. scripts

Measure each function's cyclomatic complexity as the number of if, loop, and and or branch keywords in its body, ranked worst first with the line. Answers where the complexity is on a large script. Read-only.

Result. one JSON object with the fields scriptFile (string), functionCount (integer), totalDecisions (integer), functions (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_conversion_notes

Parameters. none.

Family. scripts

The rules the JASS to Lua converter applies and where it diverges, one row per construct, with the JASS form, the Lua it emits and whether that runs the same. Read it before editing a converted script or trusting a conversion, since two rules are approximated, real arithmetic precision and hashtables, and the first is fixed by passing strict_reals to script_to_lua. No map needed. Read-only.

Result. one JSON object with the fields rules (array), summary (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_decompile

Parameters.

- `map` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. scripts

Turn every Lua entry of a map that ships as precompiled Lua 5.3 bytecode (war3map.lua or a module it requires, common in YDWE maps) back into Lua source that runs the same, and save the map to out_path. The source is a transcription of the bytecode, one statement per instruction with gotos, not the author's original text. Both standard Lua 5.3 and the KK YDWE engine's opcode numbering are read. Nothing is written when the map holds no bytecode. The input map is NEVER modified.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## script_duplicate_bodies

Parameters.

- `map` string, required.

Family. scripts

Find functions whose normalized bodies are identical, up to comments, whitespace and local variable names. A copied function is a maintenance trap, a fix in one copy silently misses the others. Read-only.

Result. one JSON object with the fields scriptFile (string), language (string), functions (integer), duplicateGroups (integer), duplicateFunctions (integer), groups (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_format

Parameters.

- `map` string, required.
- `out_path` string, required.
- `style` string, optional, default "pretty".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. scripts

Reprint a Lua map script and save the map to out_path. pretty is indented with comments kept, canonical is one fixed form for diffing two builds. The result compiles to the same Lua instructions as the original. JASS maps are refused. The input map is NEVER modified, and nothing is written when the script is already in that form.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## script_functions

Parameters.

- `map` string, required.

Family. scripts

List every function declared in the map's compiled script, with where it starts and ends. Indentation-tolerant, so it finds declarations a naive line scan misses. Use it to locate a handler before reading or editing the script.

Result. one JSON object with the fields scriptFile (string), functions (array), syntaxError (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_leaks

Parameters.

- `map` string, required.

Family. scripts

Find handle leaks in a map's war3map.j (locations, groups, forces, effects, timers, text tags, lightning and triggers created and never destroyed), each ranked by how often its code runs, hot (periodic), repeat (any callback) or once (init), plus every periodic timer and trigger with its period. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), functions (integer), hotFunctions (integer), repeatFunctions (integer), leaks (array), periodic (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_lua_api

Parameters.

- `map` string, required.
- `target` string, required.
- `schemas_dir` string|null, optional, default null.

Family. scripts

List the natives and functions a Lua script calls that a target game build does not declare. Uses the same schema membership test as the compat check, and reads the calls from the parsed tree. Read-only.

Result. one JSON object with the fields scriptFile (string), targetBuild (string), mapping (string), used (integer), missing (integer), usages (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_lua_check

Parameters.

- `map` string, required.

Family. scripts

Parse every Lua file in a map and report the syntax errors with their line and column, the way luac 5.3 refuses them. Wider than script_check, which parses only the entry the map runs. Read-only.

Result. one JSON object with the fields ok (boolean), files (integer), errors (integer), checks (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_lua_globals

Parameters.

- `map` string, required.

Family. scripts

List the globals a Lua map defines, where each is read and written, and any global that is written but never read. The Lua twin of script_unused_globals, parser based rather than a line scan. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), declared (integer), neverRead (integer), globals (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_lua_leaks

Parameters.

- `map` string, required.

Family. scripts

Find handle leaks in a Lua map script. Lua collects the wrapper but the engine still owns the handle, so a creator dropped as a statement, a creator on a period that is never released, and a handle kept in a table the script never clears are the shapes that grow memory. Each finding is ranked hot, repeat or once. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), functions (integer), hotFunctions (integer), repeatFunctions (integer), leaks (array), periodic (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_lua_to_jass

Parameters.

- `map` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. scripts

Best effort conversion of a Lua map back to JASS, saved to out_path. The JASS to Lua conversion is not reversible in general, so only the constructs that map back without guessing are converted and every construct left is reported. Nothing is written when the script does not convert. The input map is NEVER modified.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## script_modernize

Parameters.

- `map` string, required.
- `out_path` string, required.
- `target` string, required.
- `schemas_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. scripts

Rewrite deprecated or renamed natives in the map script to the target build's spelling where a known mapping exists, and save the map to out_path. A rewrite is only applied when the target build declares the new name, and names inside strings or comments are never touched. Nothing is written when there is nothing to rewrite. The input map is NEVER modified.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## script_null_locals

Parameters.

- `map` string, required.

Family. scripts

Find the classic JASS handle leak: a local of a handle type (location, group, force, effect, timer, trigger, text tag, lightning) that the function destroys with the matching Remove or Destroy but never sets to null, which keeps the handle id reserved for the rest of the function. A local that is never destroyed is a different leak and is not reported here. Read-only.

Result. one JSON object with the fields scriptFile (string), checked (integer), findings (integer), locals (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_reachable

Parameters.

- `map` string, required.

Family. scripts

List the functions reachable from the map script's entry points, main and the InitTrig functions, and the dead functions nothing reaches, each with its declaration line. A function reached only through a string (ExecuteFunc) is reported dead here, which is the honest answer to what main reaches. Read-only.

Result. one JSON object with the fields scriptFile (string), language (string), reachable (integer), dead (integer), reachableFunctions (array), deadFunctions (array), deadPercent (number)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_references

Parameters.

- `map` string, required.
- `name` string, required.

Family. scripts

Find every use of a name in the map's compiled script: direct calls, and the places it is passed as a code value (Condition, Filter, TriggerAddAction). The second kind is how most trigger handlers are actually reached and a text search for 'name(' cannot see it, so use this rather than search when asking what reaches a function. Each result carries its line, the function containing it, and the line text.

Result. one JSON object with the fields scriptFile (string), name (string), asCode (integer), references (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_semantic_diff

Parameters.

- `map` string, required.
- `other` string, required.

Family. scripts

Compare two map scripts line by line after normalizing whitespace, dropping comments and ignoring pure renames of local variables, and report the real changes. Use this rather than diff for a before/after of a port or an edit where a variable rename or a reindent should not drown the intended change. Read-only.

Result. one JSON object with the fields scriptA (string), scriptB (string), language (string), changes (integer), lines (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_to_lua

Parameters.

- `map` string, required.
- `out_path` string, required.
- `strict_reals` boolean, optional, default false.
- `hashtables` boolean, optional, default false.
- `allow_wurst` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. scripts

Convert a JASS map to a Lua map and save it to out_path. The script becomes war3map.lua and keeps the JASS rules Lua lacks (32 bit integer wrap, integer division toward zero, divide by zero gives zero, a null string concatenates as empty, unset array slots read their type default), war3map.j becomes a stub, and the w3i language is set to Lua so the game runs it. Lua maps are refused, as is a WurstScript script, whose package runtime does not survive conversion, unless allow_wurst is true. Pass strict_reals to round real arithmetic back to 32 bit.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## script_unused_functions

Parameters.

- `map` string, required.

Family. scripts

Find the functions a map's war3map.j declares that no other function calls and nothing else reaches, excluding the engine's entry points (main, config, InitCustomTriggers and the map initialization functions) and anything an InitTrig_* function or an ExecuteFunc("...") string names. Read-only.

Result. one JSON object with the fields scriptFile (string), declared (integer), unused (integer), functions (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_unused_globals

Parameters.

- `map` string, required.

Family. scripts

Find the globals a map's war3map.j declares and never reads, the variable half of dead code. A global that is only ever assigned and never appears on the right of anything is reported too, since its value is never used. The World Editor's generated globals are excluded (every gg_ handle, every bj_ name, and the literal worldedit). Read-only.

Result. one JSON object with the fields scriptFile (string), declared (integer), framework (integer), unused (integer), globals (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## script_verify

Parameters.

- `map` string, required.

Family. scripts

Static verifier for a map script. Reachability-aware checks for pause without a reachable restore, SetUnitTimeScale(0), periodic timers never stopped, group and location leaks, ability-strip, ability-hide, GetLocalPlayer desync, sleeps and GetTriggerUnit in a timer callback, and constant-exit loops. It follows direct calls and TimerStart callback edges, so a correct channel ability with the pause in _Start and resume in _Loop is not flagged. Headless, no game client. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), file (string), functions (integer), periodFunctions (integer), findings (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## search

Parameters.

- `map` string, required.
- `query` string, required.

Family. scripts

Search the map's contents for a string, ignoring case: archive file names, script lines (with the line number), string-table entries, and object data (custom object rawcodes and every field value). Each hit names the file and where in it. Capped at 500 hits, with truncated set when the cap was reached. Use this to find where a name, an ability id or an asset path is actually referenced, rather than guessing which file holds it.

Result. one JSON object with the fields hits (array), truncated (boolean)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## sound_add

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `file` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Add a new sound definition (keyed by name) to the map's sound catalog and save the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## sound_import_events

Parameters.

- `map` string, required.
- `out_path` string|null, required.
- `events_json` string, required.
- `clips_dir` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Import a sound event set into a map and save the edited copy. Each event maps to clips with a cooldown, a voice cap and a priority. Writes one sound catalog entry per clip, refuses an event whose clip is missing, and returns a generated JASS dispatcher that should be appended to the map's script. The input map is never modified.

Result. one JSON object with the fields savedTo (string), inputSha256 (string), outputSha256 (string), message (string), events (integer), catalogEntries (integer), missingClips (array), refused (array), jass (string)

Errors. STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## sound_list

Parameters.

- `map` string, required.

Family. assets

List the map's sound catalog (war3map.w3s): each definition's name, file path, channel, flags, volume, pitch, priority and min/max distance.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## sound_optimize

Parameters.

- `sound` string, required.
- `out_path` string, required.
- `rate` integer, optional, default 22050.
- `channels` string, optional, default "source".
- `bitrate` integer, optional, default 128.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Shrink a sound file by decoding it, resampling to a target rate (default 22050 Hz), optionally setting the channel count and encoding it. The format comes from out_path's extension, .mp3 selects MP3 and anything else WAV. The input is never modified. Reads .wav natively and .mp3 through the Windows codecs. When no MP3 encoder is present the command writes WAV and reports a note rather than failing. Returns the before and after format, sample rate, channel count and byte size.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## sound_remove

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `confirm` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Remove a sound definition from the map's sound catalog and save the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. CONFIRM_REQUIRED, OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## sound_set

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `field` string, required.
- `value` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Set a field on a sound definition and save the edited map to out_path. The input map is NEVER modified in place. Editable fields: Name|File|Eax|Volume|Pitch|PitchVariance|FadeIn|FadeOut|Priority|Channel|Flags|MinDistance|MaxDistance|DistanceCutoff|ConeInside|ConeOutside|ConeOutsideVolume.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## strings_diff

Parameters.

- `map_a` string, required.
- `map_b` string, required.

Family. assets

Compare two maps' string tables (war3map.wts) and report entries added, removed and changed. Entries are matched by id, so a text change on the same key reads as a change rather than a remove plus an add. Read-only.

Result. one JSON object with the fields added (array), removed (array), changed (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## strings_find

Parameters.

- `map` string, required.
- `text` string, required.

Family. assets

Find war3map.wts entries whose text contains the given text, case-insensitive, each with its id. Read-only. An empty query matches every entry.

Result. one JSON object with the fields query (string), matchCount (integer), matches (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## strings_list

Parameters.

- `map` string, required.

Family. assets

List the map's string table (war3map.wts). Every TRIGSTR_ reference in object data and in scripts resolves through here, so this is how to read what a tooltip or a unit name actually says.

Result. one JSON object with the fields fileName (string), entries (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## strings_missing

Parameters.

- `map` string, required.

Family. assets

List the TRIGSTR_ keys the map script and the GUI trigger tree reference that have no war3map.wts entry behind them. A missing key shows the literal token TRIGSTR_nnn in game where a name or a message was meant. Read-only.

Result. one JSON object with the fields referencedIds (integer), definedEntries (integer), missingIds (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## strings_set

Parameters.

- `map` string, required.
- `id` integer, required.
- `text` string, required.
- `out_path` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Set one war3map.wts entry's text, or add the entry when the id is absent, and save the edited map to out_path. The file's own encoding and line endings are preserved and the input map is NEVER modified. out_path defaults to a sibling '<map>.edited.<ext>' and must differ from the input.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## strings_unused

Parameters.

- `map` string, required.

Family. assets

List the war3map.wts entries no object-data field, map-info field, GUI trigger or script line references, each with its text. Read-only. An unreferenced entry is dead weight rather than a fault, so treat the list as a lead to review and not an instruction to delete.

Result. one JSON object with the fields definedEntries (integer), referencedIds (integer), unused (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## target_get

Parameters.

- `schemas_dir` string|null, optional, default null.

Family. repair

View the current target build setting: which build maps are checked against when compat or lint omit --target, where it comes from (option, environment, settings, default), and which schema sections are available. Also shows the installed game build version.

Result. one JSON object with the fields build (string), source (string), installBuild (string), sections (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## target_list

Parameters.

- `schemas_dir` string|null, optional, default null.
- `full_only` boolean, optional, default false.

Family. repair

List all available builds in the schemas directory, with their sections. Use --full_only to skip scripts-only builds.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## target_set

Parameters.

- `build` string, required.
- `schemas_dir` string|null, optional, default null.
- `overwrite` boolean, optional, default false.

Family. repair

Set the target build that compat and lint use when --target is omitted. Validates the build exists in the schemas folder, then writes the settings file through the write gate, which refuses an existing settings file without overwrite and refuses a path that crosses a symbolic link. The write is staged beside the settings file and renamed into place, and the answer carries savedTo, inputSha256, outputSha256, filesRewritten and bytesWritten.

Result. one JSON object with the fields ok (boolean), message (string), savedTo (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## terrain_audit

Parameters.

- `map` string, required.

Family. terrain

Audit a map's terrain (war3map.w3e) for its size and its measurable waste: grid and tile counts, distinct ground and cliff textures in use, cliff/ramp/water/blight totals, the war3map.w3e byte size, and any uniform, all-zero or out-of-range-tileset pattern found. Reports which checks it ran. Read-only.

Result. one JSON object with the fields path (string), gridWidth (integer), gridHeight (integer), tileCount (integer), groundTileTypes (integer), cliffTileTypes (integer), distinctGroundTextures (integer), distinctCliffTextures (integer), cliffCells (integer), rampCells (integer), waterCells (integer), blightCells (integer), and 12 more

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## terrain_blight

Parameters.

- `map` string, required.
- `out_path` string, required.
- `center_x` integer, required.
- `center_y` integer, required.
- `radius` integer, required.
- `on` boolean, optional, default true.
- `shape` string, optional, default "circle".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Set or clear the blight (corrupted ground) flag over a brush, saving the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), tilesChanged (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_cliff

Parameters.

- `map` string, required.
- `out_path` string, required.
- `center_x` integer, required.
- `center_y` integer, required.
- `radius` integer, required.
- `op` string, optional, default "raise".
- `level` integer, optional, default 1.
- `shape` string, optional, default "circle".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Raise/lower/set the cliff (stepped-terrain) level over a brush, saving the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), tilesChanged (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_corner_get

Parameters.

- `map` string, required.
- `col` integer, required.
- `row` integer, required.

Family. terrain

Show one terrain corner's full state: ground height, cliff level, water, ground and cliff texture indexes with variations, and the ramp, blight, boundary and edge flags.

Result. one JSON object with the fields col (integer), row (integer), groundHeight (number), waterHeight (number), groundTexture (integer), textureVariation (integer), cliffLevel (integer), cliffTexture (integer), cliffVariation (integer), water (boolean), ramp (boolean), blighted (boolean), and 2 more

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## terrain_corner_set

Parameters.

- `map` string, required.
- `col` integer, required.
- `row` integer, required.
- `field` string, required.
- `value` string, required.
- `out_path` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Set one field on ONE terrain corner and save the edited copy to out_path. The input map is NEVER modified in place. For area work prefer terrain_deform, terrain_paint and the other brush tools.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## terrain_deform

Parameters.

- `map` string, required.
- `out_path` string, required.
- `center_x` integer, required.
- `center_y` integer, required.
- `radius` integer, required.
- `op` string, optional, default "raise".
- `amount` number, optional, default 1.
- `shape` string, optional, default "circle".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Raise/lower/set/flatten/smooth ground height over a circular or square brush, saving the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), tilesChanged (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_export_heightmap

Parameters.

- `map` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Export a map's terrain heights as a 16-bit grayscale PNG of the (Width+1) by (Height+1) corner grid, one pixel per corner, to out_path. The input map is never modified, and the same file re-imports through terrain_import_heightmap.

Result. one JSON object with the fields savedTo (string), message (string), width (integer), height (integer), sizeBytes (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## terrain_fill

Parameters.

- `map` string, required.
- `out_path` string, required.
- `x0` integer, required.
- `y0` integer, required.
- `x1` integer, required.
- `y1` integer, required.
- `tool` string, required.
- `value` number, optional, default 1.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Apply a terrain tool over an inclusive rectangle of corners and save the edited map to out_path, instead of one brush dab at a time. Corners may be given in any order and a rectangle that runs off the grid is clipped rather than refused. tool is one of Raise, Lower, SetHeight, Flatten, Paint, CliffRaise, CliffLower, CliffSet, Ramp, RampOff, Water, WaterRemove, Blight, BlightOff. value is a height for Raise, Lower, SetHeight and Water, a tile index for Paint (see terrain_info), and a step count for the cliff tools, and is ignored by Flatten, Ramp, RampOff, WaterRemove, Blight and BlightOff.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_import_heightmap

Parameters.

- `map` string, required.
- `out_path` string, required.
- `png` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Set the terrain tile heights from a grayscale PNG and save the edited map to out_path. The PNG dimensions must match the map's corner grid exactly or the call is refused. Only ground height is written, and cliff level, texture, water and flags are untouched. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), tilesChanged (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_info

Parameters.

- `map` string, required.

Family. terrain

The terrain grid's extents in corners, plus the map's ground and cliff tile lists. Texture fields on a corner are indexes into those lists, so read this first to know what an index means.

Result. one JSON object with the fields width (integer), height (integer), groundTiles (array), cliffTiles (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## terrain_optimize

Parameters.

- `map` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Shrink a map's war3map.w3e by dropping the declared ground and cliff tile types the terrain never references and remapping every corner's ground and cliff texture index onto the compacted list, saving the edited copy to out_path. Every other file is written back byte for byte. Reports the before and after declared tile counts and the war3map.w3e byte saving. The input map is never modified.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## terrain_paint

Parameters.

- `map` string, required.
- `out_path` string, required.
- `center_x` integer, required.
- `center_y` integer, required.
- `radius` integer, required.
- `texture_index` integer, required.
- `variation` integer|null, optional, default null.
- `shape` string, optional, default "circle".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Paint a ground texture over a brush, saving the edited map to out_path. texture_index selects a slot in the map's tileset table. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), tilesChanged (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_ramp

Parameters.

- `map` string, required.
- `out_path` string, required.
- `center_x` integer, required.
- `center_y` integer, required.
- `radius` integer, required.
- `on` boolean, optional, default true.
- `shape` string, optional, default "circle".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Toggle the ramp (sloped cliff transition) flag over a brush, saving the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), tilesChanged (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_smooth

Parameters.

- `map` string, required.
- `out_path` string, required.
- `passes` integer, optional, default 1.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Smooth the whole map: each pass replaces every corner height with the mean of itself and its in-grid four neighbours, saving the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), tilesChanged (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## terrain_stats

Parameters.

- `map` string, required.

Family. terrain

Summarize a map's terrain (war3map.w3e): tile count, min/mean/max ground height and min/max cliff level. Read-only.

Result. one JSON object with the fields ok (boolean), message (string), tileCount (integer), minHeight (number), maxHeight (number), meanHeight (number), minCliff (integer), maxCliff (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## terrain_water

Parameters.

- `map` string, required.
- `out_path` string, required.
- `center_x` integer, required.
- `center_y` integer, required.
- `radius` integer, required.
- `op` string, optional, default "set".
- `amount` number, optional, default 0.
- `shape` string, optional, default "circle".
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. terrain

Set/raise/lower/remove water over a brush, saving the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), tilesChanged (integer), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## texture_atlas

Parameters.

- `images` array, required.
- `out_path` string, required.
- `max` integer, optional, default 512.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Pack two or more .blp, .dds or standard image textures into one power-of-two atlas and write it at out_path in the first input's format. Every input is decoded, placed deterministically by a shelf packer, and left unmodified. Returns the atlas size, its byte count and each input's pixel rectangle, so a caller can build the UV mapping.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## texture_optimize

Parameters.

- `texture` string, required.
- `out_path` string, required.
- `max` integer, optional, default 256.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. assets

Shrink a .blp, .dds or standard image texture to fit a maximum side, preserving aspect ratio, and write it at out_path in the same format. The input is never modified. Returns the before and after dimensions and byte sizes.

Result. any JSON value, the method is typed object so the shape is chosen by the arguments

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_add

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `parent_id` integer, required.
- `comment` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Add a trigger to a category in the map's GUI trigger tree and save the edited map to out_path. The new trigger is created enabled and initially on, matching the World Editor, with no events, conditions or actions yet. This edits war3map.wtg, the World Editor's trigger source, not the compiled war3map.j the game runs, so the trigger is inert until the map is opened and saved in the World Editor. A custom-text trigger cannot be added, since its body lives in war3map.wct. The input map is never modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## trigger_add_category

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `parent_id` integer, optional, default -1.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Add a category to the map's GUI trigger tree and save the edited map to out_path. A category is a folder in the trigger tree, so this is always safe: categories hold no script and adding one moves nothing. The input map is NEVER modified in place. Writes war3map.wtg only.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## trigger_add_eca

Parameters.

- `map` string, required.
- `out_path` string, required.
- `id` integer, required.
- `kind` string, required.
- `name` string, required.
- `parameters` array|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Add an event, condition or action to a GUI trigger and save the edited map to out_path. Parameters follow the order the function declares, and any left off are filled from the World Editor table's own defaults. war3map.wtg stores a function's parameters but not how many there are, so a line written with the wrong count yields a map nothing can read afterwards. Custom-text triggers are refused. This edits the World Editor's trigger source, not the compiled script the game runs, so the line is inert until the map is opened and saved there.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## trigger_catalog_describe

Parameters.

- `name` string, required.
- `file` string|null, optional, default null.
- `game_dir` string|null, optional, default null.

Family. triggers

Show full detail for one GUI-trigger function by name (e.g. 'DoNothing'): kind, game version, return type, argument types, display name, parameters layout and defaults. The catalog is read from an explicit file when given, otherwise from the installed game.

Result. one JSON object with the fields name (string), kind (string), gameVersion (integer), usableInEvents (boolean), returnType (string), argumentTypes (array), displayName (string), parametersLayout (string), defaults (string), category (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## trigger_catalog_list

Parameters.

- `file` string|null, optional, default null.
- `game_dir` string|null, optional, default null.
- `kind` string|null, optional, default null.
- `search` string|null, optional, default null.

Family. triggers

List GUI-trigger functions from the World-Editor catalog (UI\TriggerData.txt), optionally filtered by kind and/or a name/display-name search. The catalog is read from an explicit file when given, otherwise from the installed game.

Result. one JSON object with the fields source (string), total (integer), functions (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## trigger_dead

Parameters.

- `map` string, required.

Family. triggers

Find triggers that cannot do anything, listing each reason. Triggers with no action, and triggers whose events never fire, which is an event depending on a variable nothing sets (a unit event holding an unassigned unit variable, or a variable event watching a variable that never changes). A variable counts as set when the tree initialises it or, when the compiled script is present, when the script assigns it. Read-only.

Result. one JSON object with the fields dead (array), triggersChecked (integer), scriptScanned (boolean), summary (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## trigger_diff

Parameters.

- `map_a` string, required.
- `map_b` string, required.

Family. triggers

Compare two maps' GUI trigger trees and report triggers added, removed and renamed, plus event, condition or action changes within a trigger shared by both maps. Triggers are matched first by a unique id (so a rename reads as a rename) and then by name. Read-only.

Result. one JSON object with the fields added (array), removed (array), renamed (array), changed (array), triggersInA (integer), triggersInB (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## trigger_export

Parameters.

- `map` string, required.

Family. triggers

Export a map's full GUI trigger tree as structured output. Each trigger carries its name, its enabled, initially-on and run-on-map-init flags, and its event, condition and action lists with the catalog function name, enabled flag, arguments and nested blocks for every node. Read-only. A map with no trigger tree returns empty lists.

Result. one JSON object with the fields triggers (array), categories (array), variables (array), scriptLanguage (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## trigger_lint

Parameters.

- `map` string, required.

Family. triggers

Check a map's GUI trigger tree for three structural faults, a trigger that can never fire (no event and not run on map initialization), a trigger that registers the same event twice, and a trigger with no action. Disabled and custom-text triggers are skipped and the counts say so. Read-only.

Result. one JSON object with the fields findings (array), triggersChecked (integer), disabledSkipped (integer), customTextSkipped (integer), summary (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## trigger_move

Parameters.

- `map` string, required.
- `out_path` string, required.
- `id` integer, required.
- `parent` integer, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Move a trigger item under a parent category in the GUI trigger tree and save the edited map to out_path. This is how you organise the tree into folders. Only ParentId changes, so document order and every war3map.wct code body stay put, and a category cannot be moved inside its own subtree. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_recover_from_script

Parameters.

- `map` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Rebuild a browsable GUI trigger tree (war3map.wtg) for a map that has none, by decompiling the script the game runs, and save it to out_path. It only ever adds a tree and leaves the script alone. A map that already carries a tree is refused, because a decompiled tree cannot replace a real one, the decompiler reported success on a map holding 626 trigger items while producing 1 at zero name recall. Use it on protected or stripped maps whose tree was removed but whose script survives. The tree describes the script and does not drive it.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_remove

Parameters.

- `map` string, required.
- `out_path` string, required.
- `id` integer, required.
- `recursive` boolean, optional, default false.
- `confirm` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Remove a trigger item (a category, a trigger or a comment) from the GUI trigger tree and save the edited map to out_path. Refuses rather than risk two silent corruptions: removing a category that still holds children would orphan them (pass recursive to remove the subtree instead), and removing a trigger whose script body sits ahead of other bodies in war3map.wct would shift every later body onto the wrong trigger, since that file cannot be rewritten. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. CONFIRM_REQUIRED, OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## trigger_remove_eca

Parameters.

- `map` string, required.
- `out_path` string, required.
- `id` integer, required.
- `index` integer, required.
- `confirm` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Remove one event, condition or action from a GUI trigger by its zero-based position, and save the edited map to out_path. Positions come from triggers_read, which lists a trigger's functions in order. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. CONFIRM_REQUIRED, OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## trigger_rename

Parameters.

- `map` string, required.
- `out_path` string, required.
- `id` integer, required.
- `name` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Rename a trigger item (a category, a trigger or a deleted stub) in the GUI trigger tree and save the edited map to out_path. The input map is NEVER modified in place. Ids come from triggers_read. Writes war3map.wtg only.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_set_code

Parameters.

- `map` string, required.
- `out_path` string, required.
- `id` integer, required.
- `code` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Set the custom-text JASS body of a custom-text trigger and save the edited map to out_path. Only that trigger's slot in war3map.wct is spliced, so every other slot's bytes survive verbatim, which is why this one write into war3map.wct is safe where the rest of the trigger surface refuses it. A GUI trigger is refused. This edits the World Editor's trigger source, not the compiled war3map.j the game runs, so the body is inert until the map is opened and saved in the World Editor.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_set_eca_enabled

Parameters.

- `map` string, required.
- `out_path` string, required.
- `id` integer, required.
- `index` integer, required.
- `on` boolean, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Enable or disable ONE event, condition or action within a trigger, the World Editor's per-line toggle rather than the whole-trigger one, and save the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_set_enabled

Parameters.

- `map` string, required.
- `out_path` string, required.
- `id` integer, required.
- `on` boolean, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Enable or disable a GUI trigger and save the edited map to out_path. A disabled trigger is not compiled into the map script at all, so this is how you take one out of play without deleting it. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_set_initially_on

Parameters.

- `map` string, required.
- `out_path` string, required.
- `id` integer, required.
- `on` boolean, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Set whether a GUI trigger starts switched on, and save the edited map to out_path. A trigger that is off at map start never fires until something turns it on, which is a common reason a trigger looks correct and does nothing. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_set_run_on_map_init

Parameters.

- `map` string, required.
- `out_path` string, required.
- `id` integer, required.
- `on` boolean, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Set whether a GUI trigger runs on map initialization, and save the edited map to out_path. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_sync_bodies

Parameters.

- `map` string, required.
- `out_path` string, required.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Reconcile war3map.wct with the war3map.wtg trigger tree and save the edited map to out_path. A map can arrive with more code bodies than the tree accounts for (a protected or ported build keeps orphaned bodies), or fewer, and the World Editor then cannot trust which body belongs to which trigger and refuses some trigger removals. This drops the orphaned tail or appends empty slots, copying every surviving byte verbatim. It is the repair for the trigger-bodies lint warning. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## trigger_to_script

Parameters.

- `map` string, required.
- `game_dir` string|null, optional, default null.

Family. triggers

Render the map's GUI trigger tree (war3map.wtg) to JASS-style text, the way the World Editor's 'convert to custom text' presents one trigger. Read-only, it never edits the map. Preset parameter values are resolved to their script form through the World-Editor catalog when the installed game provides one, and otherwise keep their raw names. This is a rendering for reading rather than a replacement for the compiled script (war3map.j), so it is not guaranteed to compile.

Result. one JSON object with the fields script (string), triggers (integer), customTextTriggers (integer), functions (integer), catalogApplied (boolean), notes (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## trigger_var_add

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `type` string, required.
- `is_array` boolean, optional, default false.
- `array_size` integer, optional, default 1.
- `initial_value` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Add a global variable to the map's wtg and save the edited map to out_path. Provides a name, a type (for example integer, real, string, boolean, unit, group, trigger) and, for an array, the array flag and size. An initial value marks the variable initialized. Refuses a blank name or type and a name that already exists, because the wtg keys variables by name. IMPORTANT: this edits war3map.wtg, the World Editor's trigger SOURCE, not war3map.j, so a variable it defines is visible in the World Editor and inert in game until that editor saves the map. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent no, open-world no.

## trigger_var_list

Parameters.

- `map` string, required.

Family. triggers

List the map's global variables (the .wtg Variables list), each with its name, type, array flag, declared size and initial value. Read-only.

Result. one JSON object with the fields returnedCount (integer), totalCount (integer), hasMore (boolean), nextOffset (integer), items (array), cap (integer), truncated (boolean), total (integer)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## trigger_var_remove

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `confirm` boolean, optional, default false.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Remove a global variable from the map's wtg by name and save the edited map to out_path. Any trigger that referenced the variable is left pointing at nothing, so check references afterwards. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. CONFIRM_REQUIRED, OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive yes, idempotent no, open-world no.

## trigger_var_set

Parameters.

- `map` string, required.
- `out_path` string, required.
- `name` string, required.
- `type` string|null, optional, default null.
- `is_array` boolean|null, optional, default null.
- `array_size` integer|null, optional, default null.
- `initial_value` string|null, optional, default null.
- `overwrite` boolean, optional, default false.
- `expect_input_sha256` string|null, optional, default null.

Family. triggers

Update an existing global variable in the map's wtg by name and save the edited map to out_path. Any argument left off keeps its current value, so one field can change without restating the rest. This does not rewrite references to the variable inside trigger bodies. The input map is NEVER modified in place.

Result. one JSON object with the fields savedTo (string), message (string), inputSha256 (string), outputSha256 (string), filesRewritten (integer), bytesWritten (integer)

Errors. OUTPUT_EXISTS, STALE_INPUT. Every call can also return POLICY_DENIED and COMMAND_REJECTED.

Annotations. read-only no, destructive no, idempotent yes, open-world no.

## triggers_read

Parameters.

- `map` string, required.

Family. triggers

Read the map's GUI trigger tree (war3map.wtg) plus the custom-text bodies (war3map.wct) and the script language: every category, every trigger with its event/condition/action tree, and every GUI variable. Read-only, because this format cannot yet be written back. A map with no GUI triggers returns empty lists rather than failing.

Result. one JSON object with the fields categories (array), triggers (array), variables (array), scriptLanguage (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## uabi_profile

Parameters.

- `paths` array, required.
- `game_dir` string|null, optional, default null.

Family. objects

Measure unit ability lists (uabi) across many maps at once, one row per map: unit types, references, distinct ids, hero and item abilities in normal lists, ids neither the map nor the game defines, duplicates, and a per-race breakdown. Built to compare the variants of a disconnect bisection without starting the game.

Result. one JSON object with the fields maps (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## unit_abilities

Parameters.

- `map` string, required.
- `rawcode` string, required.
- `creation_number` integer|null, optional, default null.
- `game_dir` string|null, optional, default null.

Family. objects

Every ability a unit has, each tagged with where it comes from. ObjectNormal and ObjectHero are the unit type's own uabi and uhab lists. Spellbook is inside a spellbook the unit holds, where passives often hide. PlacedUnit is set on one placed unit, with its level. MorphForm comes from a form the unit turns into. Script is given by the script at runtime, found where a statement names both the unit type and the ability, and those entries are inferred and carry a line number. Use this rather than object_get when asking what a unit really has.

Result. one JSON object with the fields unit (string), found (boolean), name (string), placedCreationNumber (integer), abilities (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## validate

Parameters.

- `map` string, required.

Family. scripts

Check the map for structural problems: missing or empty files, entries that fail to load, and a script that cannot compile. Narrower than lint and a good first question when a map behaves as though something is absent.

Result. one JSON object with the fields valid (boolean), errors (integer), warnings (integer), issues (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## validate_strict

Parameters.

- `map` string, required.
- `game_dir` string|null, optional, default null.

Family. scripts

A stricter validate. Runs the default checks and adds three that are too strict to be a default, a map with no (listfile), a script that declares no entry point function, and a placement whose type neither the map nor the installed game defines. The undefined-type check needs the installed game and reports a diagnostic rather than a clean result when it cannot run. The default validate is unchanged.

Result. one JSON object with the fields base (object), strictIssues (array), valid (boolean), errors (integer), warnings (integer), issues (array), diagnostics (array)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

## version_info

Parameters. none.

Family. product

Report the product version, the git short hash and UTC build time stamped into this build, the name and version of the MCP server it carries, the product id, the newest MCP protocol revision the build speaks and the SDK version it was compiled against. Read-only.

Result. one JSON object with the fields productVersion (string), gitHash (string), buildTimeUtc (string), mcpServerName (string), mcpVersion (string), productId (string), protocolVersion (string), sdkVersion (string)

Errors. Every call can return POLICY_DENIED and COMMAND_REJECTED, and this tool's own text names no other code.

Annotations. read-only yes, destructive no, idempotent yes, open-world no.

