# Progress, wc3-mcp hardening

Branch `loop/wc3-mcp-hardening`. Spec `SPEC.md`. Queue `queue.json`.

## q01, write gate and object_set, done 2026-11-08

- Added `src/Wc3.Mcp/WriteGate.cs`, `ToolError`, `WriteOutcome`.
- Routed `object_set` through it and added `overwrite` and `expect_input_sha256`.
- Added `tests/Wc3.Tests/McpWriteGateTests.cs`, 5 facts.
- Evidence, 55 MCP and setup tests pass, dist and dist-mcp republished, smoke reports 269 tools.
- Spec records partial truth, G-12 and G-13 say only object_set.

## q02, SaveEdit through the gate, done 2026-11-08

- Both SaveEdit overloads now call WriteGate.Save, overwrite true to preserve legacy behaviour.
- EditToolResult carries the digests, rewritten count and bytes.
- Covers the placed, camera, map-info, player, force and trigger edit tools.
- Evidence, 2697 hermetic tests pass.

## q03, SaveTerrain through the gate, done 2026-11-08

- SaveTerrain now calls WriteGate.Save, overwrite true.
- TerrainToolResult carries the digests.
- A folder map writes as a folder, no output digest, bytes summed across the folder.
- Added a folder-map fact to McpWriteGateTests.
- Evidence, 10 gate and folder tests pass, 2697 hermetic tests pass.
- Note, RoundtripTests failed once under load, passed alone and on the full rerun. Watch it.

## q08, publish, done 2026-11-08

- dist and dist-mcp published into fresh folders and swapped, both exe timestamps 05:34.
- Smoke over stdio reports 269 tools.

## q09, specification audit, done 2026-11-08

- Re-derived every measured claim from the published `dist-mcp/Wc3.Mcp.exe` over stdio, not from the document.
- Twenty claims confirmed. The two that moved are mine, 81,615 to 81,772 characters is exactly the 157 character growth of the `object_set` description, and the one extra string and one extra boolean are `expect_input_sha256` and `overwrite`.
- One claim looked wrong and was not. A top level count gave 483 strings against 619, because the document counts a nullable type as its base type and calls it optional. The definition was unstated and is now in Appendix E.
- Found what the document lacked, not what it got wrong. No external source was cited anywhere.
- Added section 3.13 with the re-measurement, Appendix D with the protocol and community sources, Appendix E with the verification log and its one honest miss, DOC-06, G-46 to G-49, and a related work comparison with wc3-forge from the vault.
- Measured that npm latest is 0.5.19 while the mirror tags stop at v0.5.9 and its tree says 0.5.16, so the published artifact is newer than any committed value. G-25 now says so.
- The spec README said 149 requirements, 19 groups, 32 checks and 36 gaps. Corrected to 160, 20, 33 and 49. That is the drift G-47 names.
- PDF rebuilt, 55 pages, 117 bookmarks, zero em dash, en dash, semicolon or prose colon.

## Publish after the loaded-digest fix, 2026-11-08

- Wired `LoadedShaOf(doc)` into `object_set`, both `SaveEdit` overloads and `SaveTerrain`, so a file replaced between load and save is refused as `STALE_INPUT` instead of saving a stale snapshot.
- Added three gate facts, an existing output folder is refused, an input replaced after load is refused, and an untouched input reports the loaded digest. The gate suite is 9 facts.
- Hermetic suite 2700 passed, 0 failed.
- `dist` and `dist-mcp` published into fresh folders and swapped, both exe timestamps 05:53, smoke reports 269 tools.

## Review triage and destination commit, 2026-11-08

- Triage of the second external review. Two claims were wrong. Appendix C holds 269 unique names and the group totals sum to 269, not 271. AC-05 was already consistent, because an alias is not a canonical name.
- The rest were right. The PDF cover and footer were hardcoded at 159 requirements, 45 gaps and version 1, while the body said 160, 49 and 1.1. The builder now counts them from the markdown and prints the result, 269 tools, 160 requirements, 33 checks, 49 gaps, version 1.1.
- Seven requirements had no acceptance row, IDN-06, TRN-06, ERR-07, RSC-06, DSC-05, DSC-06 and PKG-12. Each is now named in AC-01, AC-03, AC-18, AC-20, AC-22 and AC-25.
- Corrected 35 statements. SAF-07 and flow 5.3 now say input revision verification, not chaining proof. The race row says the losing writer gets OUTPUT_EXISTS from the commit. RES-10 names returnedCount, totalCount, hasMore and nextOffset. RES-03 excludes image blocks from the 256 KB bound. RES-04 and AC-16 key on invalid decoding rather than non-ASCII. TST-04 was a typo, TST-10, DSC-06, CMP-06 and SUR-03 were ambiguous, the naming grammar now puts the action last, and 15.7 bumps the version before it builds and tests.
- Appendix D now attributes outputSchema, structuredContent and title to 2025-06-18, verified from the vendor docs. 2024-11-05 has none of them.
- Code, `WriteGate.Commit` maps a rename that loses a race to OUTPUT_EXISTS instead of a raw IOException, and the folder path does the same. Two facts bring the gate to 11. Hermetic suite 2702 passed. dist and dist-mcp republished at 06:06, smoke 269 tools.
- Committed in wc3-mcp as 5bf2557 with the markdown and the PDF only. The builder, the requirement data and the README stay untracked by request. Nothing pushed.

## Not done from the review, and why

- Batch edit or project head, the only real fix for the chaining mistake. It is Q6 and a new tool, out of this slice.
- Multi-file atomicity for exports, a manifest or a directory publish. Needs a result envelope change.
- Lifecycle contract for progress, cancellation and response loss. Needs the SDK callback work.
- Resource limits before parsing, cache identity, provenance of the npm 0.5.19 artifact, semantic write assertions per family. All real, all separate queue items.

## q04 partial, the object family, 2026-11-08

- Added `object_batch_set` with `ObjectBatchSetCommand` in the shared layer, the CLI verb `object batch-set`, the parity map entry, five command facts and two protocol facts. Up to 500 edits in one load and one save, all or nothing.
- Routed `object_bulk_set` through the gate and gave it `overwrite` and `expect_input_sha256`. It also now refuses when the command fails, where before it saved an unchanged map and reported nothing wrong.
- Three object writes carry the gate. `object_new`, `object_clone`, `object_rename`, `object_import` and `object_migrate` remain.
- Surface 270 tools. Both references regenerated. Hermetic suite 2709 passed. dist and dist-mcp swapped, smoke 270 tools.
- Spec 1.2 adds SAF-13 to SAF-15, RES-11, PRF-08, PRF-09, RSC-07, PKG-13, TST-14, CMP-07, SEC-08, AUT-11 and DOC-07, gaps G-50 to G-60, checks AC-34 to AC-38 and section 20. Committed in wc3-mcp as e5fba1f, markdown and PDF only.
- The remaining review items are stated as requirements and gaps, not implemented. The shared gate, multi-file publish, lifecycle, limits, cache identity, provenance, semantic sweep, revision matrix and print edition are open.

## Ability order audit, 2026-11-08

- Grounded the anime-map proposal in the two real maps. ACA V0.33A is 10p, 11,540 files, 677 MB, 8,303 BLP, 3,091 MDX, 2,728 abilities. WoS2 v0.34h is 15p, 7,099 files, 356 MB, 4,134 BLP, 1,207 MDX, 1,374 MP3, 763 abilities.
- Added `AbilityOrderAuditCommand`, `audit orders` and `audit_ability_orders`. It groups Channel abilities by `Ncl6` and reports shared ids, levels that disagree and empty ids. Five command facts and one protocol fact.
- Measured: ACA 1,105 Channel abilities, 172 orders, 105 shared, 151 levels disagree. WoS2 389, 97, 53, 125. Largest groups are acidbomb x92 in ACA and acolyteharvest x92 in WoS2.
- The hero scope works but WoS2 keeps ability lists in the script, so `uabi` is absent for H000 and the audit says so rather than guessing.
- 271 tools, 2715 hermetic tests, both references regenerated, dist and dist-mcp republished, smoke 271.
- Spec 1.3 adds SAF-16 and AC-39, marks SAF-16 met, and adds section 21 with the map measurements and the workstreams. Committed in wc3-mcp as 7912db7, markdown and PDF only.
- Killed 368 leaked headless Chrome processes from earlier PDF builds that had exhausted the 9337 to 9376 port range. Scoped to the wc3spec and pdfbuild profile markers, not the user's browser.

## Audio ingest, first slice, 2026-11-08

- Added `SoundAnalyzeCommand`, `wc3ctl sound analyze <file>`, and two facts. Measures duration, format, peak, RMS, clipped samples and leading/trailing silence at a dBFS threshold.
- Clipping is counted at 0.999 because NAudio maps 16 bit full scale to 0.99997, which the first test caught.
- 2717 hermetic tests pass, docs regenerated, dist and dist-mcp swapped, smoke 271 tools.
- Next in this workstream, trim and normalise, then `sound import --events` writing the catalog and the dispatcher with concurrency and cooldown.
