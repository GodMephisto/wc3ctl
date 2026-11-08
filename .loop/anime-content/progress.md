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
