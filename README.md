# wc3ctl — Warcraft III map tool (slice 1: MapDocument Core)

Open a `.w3x`/`.w3m` map, inspect it, and round-trip it byte-faithfully.

Slice 1 delivers the read/query core: `MapDocument.Load` opens an MPQ-based map
(including maps with a 512-byte pre-archive header), parses every *present,
known* `war3map.*` file into a typed model via [War3Net](https://github.com/Drake53/War3Net),
preserves everything else (unknown or unnamed files) as raw bytes, and can save
the archive back out such that every map-data file compares byte-identical to
the original. Parse failures never crash a load — they are recorded as
diagnostics and the file stays available raw.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Wc3.MapDocument` | Core model: `MapDocument`, `MapFileEntry`, format registry, War3Net parser wiring |
| `src/Wc3.Commands` | Shared command layer (front-end-agnostic result records) |
| `src/wc3ctl` | CLI front-end (System.CommandLine) |
| `src/Wc3.Mcp` | MCP server seam (stub — proves the shared-command boundary only) |
| `tests/Wc3.Tests` | xUnit suite: fast synthetic-fixture tests + opt-in corpus tests |

## Commands

```
wc3ctl ls <map>                             List internal files
wc3ctl info <map>                           Map metadata (name, author, players, ...)
wc3ctl roundtrip <map>                      Verify byte-faithful save
wc3ctl search <map> <query>                 Search map contents (filename-only for now)
wc3ctl diff <mapA> <mapB>                   Compare two maps (added/removed/modified files)
wc3ctl object get <map> <rawcode> [--field F]   Object data query (stub this slice)
```

Add `--json` to any command for machine-readable output.

## Build & test

Requires the .NET 8 SDK.

```bash
dotnet build
dotnet test --filter "Category!=Corpus"   # fast suite (synthetic fixtures)
dotnet test --filter "Category=Corpus"    # against a real map (opt-in)
```

The corpus tests exercise parse coverage and byte-faithful round-trip against a
real ~60 MB map. The map path is currently hardcoded in the corpus test files
(`tests/Wc3.Tests/ParseCoverageTests.cs` and friends); if the file is absent
the corpus tests self-skip (pass without asserting).

## Format coverage

Parsed into typed models: `war3map.w3i` (info), `.w3e` (terrain), `.wpm`
(pathing), `.doo` / `war3mapUnits.doo` (doodads/units), `.w3r` (regions),
`.w3c` (cameras), `.w3s` (sounds), `.wtg` / `.wct` (triggers), `.wts` (trigger
strings), `.j` / `.lua` (script), `.imp` (imports), and the object-data set
`.w3u .w3a .w3t .w3b .w3d .w3h .w3q`.

Everything not in that list is preserved as raw bytes and round-trips
unchanged.

## Known limitations / deferred (later slices)

- **Read-only.** Editing/write-back is deferred: `object get` is a stub that
  always returns not-found, and `MapDocument.SerializeEntry` throws by design.
  Saving re-emits the original raw bytes of each file.
- **`search` matches filenames only** — no content search yet.
- **MPQ bookkeeping files**: `(listfile)` and `(attributes)` are regenerated
  by the archive builder on save; `(signature)` is **not** carried over or
  re-signed (the rebuilt map is unsigned — unsigned custom maps are
  unaffected). They are archive metadata, not map data, and are excluded from
  the round-trip fidelity comparison; `wc3ctl roundtrip` reports any that were
  dropped or rewritten as notes.
- **`war3map.mmp` and `war3map.shd`** (minimap positions, shadow map) are not
  yet in the format registry — they are preserved raw, not parsed.
- **MCP server is a seam-only stub** — it proves the shared command layer is
  front-end-agnostic but exposes no real MCP transport yet.
