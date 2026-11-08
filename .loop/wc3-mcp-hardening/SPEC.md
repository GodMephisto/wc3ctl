# Spec, wc3-mcp hardening slice

Goal, make the write path of the MCP server safe to chain, provable and hard to misuse, one write family at a time, without changing what a map contains.

Source of truth, `C:\playground\Programming\wc3-mcp\docs\specification\wc3-mcp-specification-v1.md`, requirements SAF-05 to SAF-11 and ERR-02/03, gaps G-11 to G-15. The requirement data is `requirements.json` in the same folder.

## Definition of done

A write family is done when all of these hold.

1. Every tool in the family writes through `WriteGate`.
2. Its result carries `inputSha256`, `outputSha256`, `filesRewritten` and `bytesWritten`.
3. A stale `expect_input_sha256` is refused as `STALE_INPUT`.
4. An existing output is refused without `overwrite`.
5. The write is atomic, a temp file in the destination folder renamed into place.
6. A test named after the failure proves each refusal.
7. The spec records what is true, not what is intended.

The slice is done when every map-writing tool routes through the gate and the spec marks SAF-05, SAF-06, SAF-07, SAF-10 and SAF-11 as met.

## Scope

In.

- The gate, `src/Wc3.Mcp/WriteGate.cs`.
- The tool families that write a map, via `SaveEdit`, `SaveTerrain` and the direct saves in `Wc3Tools`.
- The result records they return.
- A `confirm` parameter on destructive tools.
- Tests and the published dist.

Out.

- Model, texture and sound file writes that never touch a map. They keep their own out_path rules for now.
- Structured `structuredContent` on the wire, which needs the output schema work (RES-02).
- Any new tool, any rename, any surface change.
- The mirror export and npm release, which need an explicit push.

## Interfaces

`WriteGate.Save(MapDocument doc, string map, string? outPath, bool overwrite, string? expectInputSha256)` returns `WriteOutcome(SavedTo, InputSha256, OutputSha256, FilesRewritten, BytesWritten)`.

Refusal codes, `OUT_PATH_IS_INPUT`, `OUT_PATH_INSIDE_INPUT`, `OUTPUT_EXISTS`, `STALE_INPUT`.

A tool error is a `ToolError` caught by `Run` and sent as an MCP error whose message starts with `[CODE]`.

## Failure modes

| Failure | Behaviour |
|---|---|
| Input equals output | Refuse, write nothing |
| Output exists, no overwrite | Refuse, leave the existing file untouched |
| Digest does not match | Refuse, leave both files untouched |
| Process dies mid-save | Temp file only, the destination is unchanged |
| Folder map | Write directly, no digest, stated in the result |

## Acceptance, exact commands

- `dotnet test tests/Wc3.Tests/Wc3.Tests.csproj -c Release --filter "FullyQualifiedName~McpWriteGateTests"` passes.
- `dotnet test tests/Wc3.Tests/Wc3.Tests.csproj -c Release --filter "Category!=Corpus&Category!=GameData&Category!=Doctor"` passes.
- `powershell scripts/smoke-mcp.ps1 -Exe dist-mcp/Wc3.Mcp.exe -MinTools 260` reports the tool count.
- The published exe timestamp is after the change.

## Rollout and rollback

Publish into `dist.new` and `dist-mcp.new`, swap the folders, keep the old pair as `.old`. Rollback is the reverse rename. No schema or data migration exists.

## Budget and autonomy

One working session. Four workers at most, one writer per file. No push, no release, no npm, no mirror export without an explicit request. Local branch `loop/wc3-mcp-hardening`.

## Assumptions

- The existing `.edited` default stays the default where a tool already documents it.
- Legacy tools that do not expose `overwrite` keep replacing an existing output until their family is wired, so the gate is called with overwrite true there. This is recorded, not hidden.
- `object_set` is the reference implementation.
