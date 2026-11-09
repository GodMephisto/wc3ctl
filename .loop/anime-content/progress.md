# Progress, anime content generation

## a01, sound trim, done 2026-11-08

- `SoundTrimCommand`, `wc3ctl sound trim <in> <out> --margin --peak`, two facts.
- Cuts silence at a threshold, keeps a margin, normalises the peak, writes 16 bit PCM WAV.
- The refusals are tested, a silent clip throws and writes nothing.
- 2719 hermetic tests pass, docs regenerated, dist and dist-mcp swapped, smoke 271 tools.
- `sound analyze` shipped just before it, same workstream.

## a02 next

- `sound import --events`, resolve events to clips, write war3map.w3s and a dispatcher with cooldown and max voices.
- Then the emitter writer, which is the gate for VFX from scratch.

## a04, sequence writer, done

- `MdxSequenceWriter` and six facts. SEQS is a flat array of 132 byte records with no count prefix, not the count plus 88 byte records the brief stated. The worker checked `MdxParser`, `MdxMeshBuilder` and `SyntheticModel`, and the output is byte identical to the block `SyntheticModel` writes.
- Names truncate at 80 bytes, frames at 80 and 84, the 44 byte animation tail is zeroed. Negative and backwards intervals are refused.
- This is a brief error I made, not a worker error. The corpus reader is the authority.

## a05, material writer, done

- `MdxMaterialWriter` and six facts, including a cross check through `ModelParser.Parse` so the layout is pinned to the shipped reader.
- Second brief error of mine. TEXS has no count field, the material record is 48 bytes, the priority plane is a uint32, and blend mode is the layer filter mode. The corpus reader was right and the brief was wrong.

## Shared build hazard, not a file hazard

- One writer per file was not enough. a02 mid-write left `SoundImportCommand.cs` uncompilable, so a04 and a05 could not be independently re-run even though their own files are correct.
- Rule for the next window, either give each worker its own worktree, or serialize verification behind a green build. A per-file lock does not protect the compiler.
- a03 held until the tree compiles. Four in flight is a limit, not a target.

## a02, a04, a05, a06 all done and independently verified

- 22 facts green in one run on the real tree, all four suites.
- a02 sound import, catalog entries survive a save and reload, a missing clip refuses the event, the generated JASS passes JassScriptCheck.
- a04 sequence, a05 material, a06 four primitives, each parsed back by a shipped reader.
- a03 emitter launched with the real 424 byte PREM payload as its layout authority.

## a03, emitter writer, done

- `MdxEmitterWriter`, 10 facts, full suite 2751 green.
- PREM and PRE2 encoders. The real 424 byte fireblast PREM reproduced byte for byte apart from the track it does not write. PRE2 texture id at offset 155, cross checked against six real records in freezingring.mdx.
- PREM has no texture id, its path field is a spawn model, and the writer says so instead of pretending. Blend space is separate from FilterMode.
- Not proven, in game load, and a classic 1.26 engine tail. Read back and an independent decoder only.

## a07 package generator launched

- Body, effect, icon, sound, manifest, all real files, using the new writers.

## a07, package generator, done

- Body from the capsule primitive, effect from a PRE2 emitter, icon TGA, impact WAV, manifest with roles and a rebind that changes one key.
- Fixed the scaffold I wrote earlier, it had no effect model, an OBJ body, no effect role, and duplicated role ids.
- 8 facts, full suite 2759 green, verified by me.
- a08 launched, the two form hero generator.

## a08, hero generator, done

- `HeroGenerateCommand`, 7 facts. Two forms, three abilities, distinct Ncl6 per ability, uabi scoped per form, JASS compiles, reload keeps objects, unknown base refused before any write.
- Learned from the object layer, sessions must be built after the last creation, real fields need a decimal point, per level order ids must agree.
- Real map probe on ACA V0.33A, temp output, all fields Source=map, no collisions.
- Unverified, transform keeps level, items and experience, JASS not spliced into war3map.j, damage stub range fixed at 300.

## Integration, model primitive, 2026-11-08

- `wc3ctl model primitive` and `model_primitive` MCP tool, card, ring, cone, capsule, one smoke fact.
- 272 tools, 2767 hermetic green, dist and dist-mcp republished, smoke 272.
- a09 spec 1.4 and a10 wiring for sound import are ready. Budget stopped before them.

## Window a12 to a15, done

- a13 splice with pjass, 8 facts, two generator defects fixed at the source.
- a14 hero generate and content scaffold wired, 275 tools.
- a15 surface guard, build refuses a PDF whose numbers disagree with the exe.
- a12 spec 1.5 committed d923b17, md and pdf only.
- 2774 hermetic green, 275 tools published and smoked.

## Left

- a11 permission file and skills as resources.
- The P2 and P3 items, shared gate, multi file atomicity, provenance, semantic writes, revision matrix, fuzz, light print edition.

## Window, spec gaps closed from section 10, 2026-11-08 09:26

- ENV-05 closed. `McpLogLevel` reads `WC3_MCP_LOG`, maps error, warn, info and debug, and falls back to warn on an unknown value. Proven end to end on the published exe, 0 stderr lines at the default, 16 at `info`, 24 at `debug`, all on stderr and none on stdout.
- `McpContractTests`, 11 new facts, all green, 34 green in the filter with the MCP server, annotation audit and parity suites.
- TRN-05 investigated and not closed. `CreateOptions` sets `Capabilities` with `ListChanged` false and the pinned SDK 1.4.1 still answers true, because it derives the capabilities from the registered collections. A handshake test proved the reply ignores the options, so the test was removed and the finding recorded in the spec. G-24 now names the SDK pin as the blocker, not this repository.
- dist-mcp republished, 275 tools, surface guard clean, spec 1.8, PDF 63 pages.

## ENV-04 closed, PRF-01 measured, 2026-11-08 09:40

- Start line on stderr, `wc3ctl 0.4.13  protocol 2025-11-25  tools 275  target Beta-ROC-v1.00 (settings)  game 3.0.1.24371  profile full`, written at 0.31 seconds.
- `ToolCount` counts the surface by reflection without building a handler per tool, because counting the built collection cost 0.35 seconds of cold start. Measured with the line suppressed, the line itself costs 0.03 seconds.
- The protocol revision is negotiated, not fixed. The pin answers 2025-11-25 to a client that offers it and 2024-11-05 to a 2025-06-18 offer. `ProtocolRevision` states the newest and a handshake test pins it.
- PRF-01 fails. Cold start to the initialize reply is 1.2 seconds on a warm disk, four runs between 1.15 and 1.27. Recorded as G-64, spec 1.9.
- 36 green in the contract, server, annotation audit and parity filter. dist-mcp republished, guard clean.

## Spec gaps from section 10, batch two, 2026-11-08 11:07

- SAF-01, SAF-02, SAF-03 closed. All 275 tools declare all four hints, including `Destructive = false` on the 157 read-only tools, because an unset hint defaults to true and the audit now fails on it.
- Five writing tools claimed read-only in their description, not two, `mpq_extract_all`, `terrain_export_heightmap`, `pathing_export_png`, `asset_thumbnails`, `asset_convert_batch`. All five reworded, and the audit now fails a writing tool whose description contains the word.
- PAR-01 closed. `LoadMap` refuses a folder and a wrong extension before opening an archive.
- DSC-01 closed. Instructions carry the product, target, game build, three rules and the quickstart hint in 405 characters.
- DOC-07 closed. `--light` renders a light printing edition, 73 pages against 63, page luminance 189 against 13.
- 68 green in the MCP filter. Surface guard clean at 275 tools, 84,319 description characters, 122,218 catalog bytes. Spec 1.12, two PDFs.
- Section 10 is now 58 rows and the loop queue for this workstream, since section 10 already carries dependencies and verifiers.

## SAF-10 closed, 2026-11-08 11:24

- Nine destructive tools take `confirm`, refused as `CONFIRM_REQUIRED` naming the item before any map load, so the refusal costs nothing.
- `RunConfirmed` wraps the guard inside `Run` so the code reaches the client as the `[CODE]` message the other ToolErrors use. The first cut threw before `Run` and leaked a raw exception, caught by the new test.
- 44 green in the MCP filter. 843 parameters now, 51 booleans, 272 optional. Catalog unchanged at 122,218 bytes because it carries no schemas.
- Spec 1.15, dark 63 pages, light 71.

## Drift sweep, 2026-11-08 11:33

- PAR-01 was marked met in 1.13 on the loader half alone. Measured, 7 of 202 `map` descriptions say a folder is accepted and 195 do not, so it is back to gap and G-65 carries the count. A requirement is not met because half of it is.
- G-58 named CMP-07, which 1.14 closed, so it was removed. Every gap row now names only a gap requirement, 56 rows naming 61.
- SAF-10 stays closed. Spec 1.16, dark 63 pages, light 71.

## Agent batch, SAF-11, DOC-03, TST-08, 2026-11-09 02:05

- Three subagents, disjoint files, one C# writer so the build never raced.
- SAF-11, `MapDocument.Save` stages as `.<name>.<8 hex>.tmp` beside the destination, flushes, renames, deletes on failure. Test throws from an internal thread-static seam between flush and rename and proves the old file is byte for byte. Subagent also mutated the change back to `File.WriteAllBytes` and watched the test go red.
- DOC-03, `tools/check_counts.py` parses the counts out of the prose and compares them with the exe and the JSON. Its first run caught the README claiming 58 gap rows when there were 56, and later two gap rows still naming DOC-03 after it closed.
- TST-08, nine launcher tests on node --test with fetch stubbed per child. The instructed `node --test test/` does not run on Node 24, so the script is the glob form.
- Both guards pass, 41 green in the map, smoke, safety, contract and server filter, exe republished. Spec 1.17, dark 63 pages, light 71, 15 met, 58 gap, 54 rows.

## SET-06 closed, export lag found, 2026-11-09 02:30

- doctor checks the context folder item by item and the latest release, capped at ten seconds, warning only. Verified on the published exe, all five context items present, `0.5.19 is available, you are on 0.4.13`, exit 0. 66 tests green.
- TST-12 could not be closed. The CI job was written and correct, then removed, because the mirror checkout builds 267 tools and 3 resources against the documented 275 and 6, so the job fails on a correct tree. Eight tools missing, audit_ability_orders, content_scaffold, hero_generate, model_primitive, model_squish, object_batch_set, object_fix_model_ext, sound_import_events. Recorded as G-66 under PKG-07 and G-36 narrowed to the CI step.
- Contamination caught. The CI agent left WC3_MCP_EXE pointing at its scratch publish in the shared shell, so one guard run measured the mirror instead of the canonical exe. Unset it, re-ran both guards green, deleted the scratch folder.
- Spec 1.18, dark 64 pages, light 71, 16 met, 58 gap, 54 rows.

## SAF-05 closed, write default flipped, 2026-11-09 02:52

- The gate was always right and 43 tools called it with `overwrite: true` hardcoded, so a rerun replaced a good build silently. Now 61 writing tools take `overwrite` and `expect_input_sha256` and pass them through.
- The placement family wrote around the gate entirely, `doc.Save(full)` after its own path check, so six tools had no digests and no refusal. They route through the gate now and PlacementToolResult carries the write fields.
- One scripting mistake caught by the compiler, offsets computed before a length-changing rename put 43 parameter blocks in the wrong places. Stripped and redone back to front, 0 errors.
- 91 green in the map, smoke, safety, gate, setup, contract and server filter. Parameters 957, booleans 108, optional 386. Guards green. Spec 1.19, 17 met, 58 gap, 54 rows.

## ENV-02 closed, 2026-11-09 03:05

- Seven tests over the precedence chain, game dir argument over environment, environment when no argument, target argument over environment over settings file, the reported source per tier, and the placeholder in both the argument and the environment counting as unset.
- The counts guard caught the README again after the gap total moved, 54 to 53, third time it has paid for itself.
- Spec 1.20, 18 met, 57 gap, 53 rows, dark 64 pages, light 71.

## SUR-06 closed, 2026-11-09 03:45

- `McpToolFamilies` generated out of Appendix C, 275 tools across the eleven curated families, so the grouping the catalogue reports and the grouping the document draws come from one source.
- The catalogue record gained `family` and the resource grew to 127,247 bytes. `Every_tool_belongs_to_exactly_one_family` fails on a tool with no family, which is what stops a new tool landing ungrouped.
- One generator slip, wrapping the initialiser split entries across `=` and the compiler rejected 6 of them; regenerated one entry per line.
- RSC-03 narrowed to aliases and the input schema. 21 met, 54 gap, 51 rows. Spec 1.23, dark 64 pages, light 71.

## PRM-02 closed, prompt count now measured, 2026-11-09 04:10

- Six prompts added, one per quickstart workflow that had none, so every one of the ten workflows returns a plan.
- `WorkflowPrompts` maps workflow id to prompt name and `Every_quickstart_workflow_has_a_prompt` checks both directions, a workflow with no prompt and a prompt mapped to a method that does not exist.
- measure_surface.py gained a thirteenth measure, the prompt count, because five stale statements of it were sitting in the document and no guard covered it.
- 22 met, 53 gap, 50 rows. Spec 1.24, dark 64 pages, light 72.

## DSC-02 closed, 2026-11-09 04:35

- Two workflows added, trigger wiring and learning the surface, so all eleven families are reachable from `quickstart`.
- `QuickstartWorkflow` gained `Prompt`, so each workflow returns the name of the prompt that repeats its plan, and `Wc3Prompts.WorkflowPrompts` is derived from the workflow list instead of written beside it.
- One smoke test caught the count, 10 to 12, and now also asserts the prompt field on every returned workflow.
- 23 met, 52 gap, 49 rows. Spec 1.25, dark 64 pages, light 72.

## ERR and RSC assessed, 2026-11-09 05:20

- Three closed on measurement. A missing map answers isError true in one line, no stack, server still serving. An unknown tool is answered `Unknown tool: '...'` at the protocol layer. With `W3C_RAG_HOME` empty, `rag_status` reports nothing installed and `docs_search` falls back to lexical with 8 matches and downloads nothing.
- Five became gaps with named rows. Resources carry no size or hash (RSC-01, RSC-05). Nothing reports a partial (ERR-05). No test guards error text (ERR-06). `gamedata_enum` returns `entries: []` with no note when no schemas were found, and nothing warns before reading when the game is absent (ERR-04).
- Two probe mistakes nearly became false defect reports, a wrong argument name and a map path passed where a folder was expected. Both were my error, not the product's, so neither was recorded.
- 29 met, 58 gap, 92 unassessed, 55 rows. Spec 1.27, dark 64 pages, light 73.

## TRN and OBS assessed, 2026-11-09 05:45

- Six closed. Two instances start at once and neither locks. Every stdout line in every capture parsed as JSON while 89 diagnostics sat on stderr with levels. The tree holds no listener at all. Two calls in one process return each map's own answer.
- Four became gaps. An in-flight call is abandoned when stdin closes, exit 0 in 0.4 s with no result. The only per-call log line names the tool and isError, with no duration or size. A failing call is logged by the generic handler as an unhandled exception at debug, without its code.
- The per-call line is the SDK's, not ours, so OBS-02 and OBS-04 need a tool invocation filter rather than a one-line edit.
- 35 met, 62 gap, 82 unassessed, 59 rows. Spec 1.28, dark 65 pages, light 74.
