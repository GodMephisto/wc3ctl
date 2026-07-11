# MapDocument Core — Design

**Date:** 2026-07-12
**Status:** Approved (pending written-spec review)
**Slice:** 1 of N — the foundation of the `wc3ctl` / Warcraft III Next-Generation Editor.

---

## 1. Context & scope

The [architecture vision](../../../Warcraft_III_Next_Generation_Editor_Architecture.txt) describes a full product — GUI + CLI + MCP over terrain, units, object data, triggers, runtime profiles, a plugin system, and GPU rendering. That is a dozen independent subsystems and years of work. It cannot and will not be specced in one document.

Every one of those subsystems operates on the same thing: a **`MapDocument`** — a parsed, editable, losslessly-preserved representation of a `.w3x`/`.w3m` map. Until that model can be opened and re-saved without corrupting anything, nothing else has a foundation. This slice builds exactly that and nothing more.

### In scope
- Open a `.w3x`/`.w3m` (MPQ archive + Warcraft III pre-archive header).
- Parse **all** known `war3map.*` formats into typed, queryable models (**read/query only**).
- Preserve unknown and unnamed (protected-map) files raw — never drop them.
- Save back **byte-faithfully per file** (round-trip).
- A CLI (`wc3ctl`) exposing read/query commands + a `roundtrip` verification command.
- The **shared command layer** as a real library, so MCP/GUI become thin adapters later.

### Explicitly out of scope (later slices)
- Editing / mutation commands (`object set`, `terrain paint`, `build`, `validate`, `test`).
- Faithful **write-back** for each format (only needed once editing exists).
- The MCP server implementation (the *seam* is designed now; the adapter is built next).
- GUI, runtime profiles, native registry, trigger compilation, plugin host, rendering.

---

## 2. Key decisions (with rationale)

| Decision | Choice | Why |
|---|---|---|
| First slice | MapDocument round-trip | Bedrock; every other subsystem depends on it; isolates the hardest correctness problem. |
| Stack | C# / .NET 8 (LTS) | War3Net provides MPQ + parsers/serializers for nearly all `war3map.*` files, cross-platform, strong CLI + MCP SDKs. Lets us spend effort on the model, not binary reverse-engineering. |
| Fidelity target | **Per-file preserve + dirty re-serialize** | Untouched files write back original bytes (byte-identical); only edited files re-serialize. Catches parser bugs; matches the doc's `RawFiles`/`DirtyFiles`. |
| Byte-identical whole archive? | **No** | MPQ hash/block tables are encrypted, power-of-two-sized, linear-probe-ordered; combined with compression + file ordering the blob is not reproducible. No serious tool attempts it; the game does not require it. |
| Parse depth | **All known formats, read/query only** | Round-trip fidelity is preserved regardless (untouched files emit original bytes), so exposing rich typed models never threatens fidelity. Editing/write-back deferred. |
| Internal architecture | **Layered: model + shared command library + thin front-ends** | Directly satisfies Principle #3 ("GUI, CLI, MCP share one command layer"). Command layer is the single source of truth; front-ends are renderers. |
| MCP this slice | **Seam-only** | Build the `Commands` library so MCP is a thin adapter later; don't implement the server yet. Keeps slice 1 on the bedrock. |

### Foundational library finding (evidence)
War3Net's `MpqArchiveBuilder` already implements the per-file-preserve model. Constructed from an opened `MpqArchive`, it keeps `_originalFiles`, `_modifiedFiles`, `_removedFiles`; on save it concatenates modified + original minus removed, so **unmodified and unknown files retain their original stored bytes** and only explicitly-replaced files are re-serialized. `SaveWithPreArchiveData()` preserves the Warcraft III map header (`HM3W` / `0x200` block) that precedes the MPQ. This means a no-edit round-trip is faithful almost for free.

---

## 3. Architecture

Root namespace `Wc3`. Solution projects:

```
Wc3.MapDocument   canonical model: sections + raw/unknown/dirty tracking; load & save
Wc3.Commands      shared command layer — handlers that take a MapDocument and return plain result POCOs
wc3ctl            CLI front-end (System.CommandLine); renders results as human text or --json
Wc3.Mcp          MCP server — SEAM ONLY this slice (references Wc3.Commands; not implemented)
Wc3.Tests         xUnit — round-trip fidelity + parse coverage
```

External dependencies: `War3Net.Build`, `War3Net.IO.Mpq`.

**Boundary contract for each unit**
- `Wc3.MapDocument` — *what:* load a map into a typed model, save it back faithfully. *how used:* `MapDocument.Load(path)` → query sections → `doc.Save(path)`. *depends on:* War3Net.
- `Wc3.Commands` — *what:* one handler per user operation, returning serializable result objects. *how used:* front-ends call `handler.Execute(args)`. *depends on:* `Wc3.MapDocument` only (no CLI/MCP types leak in).
- `wc3ctl` — *what:* parse argv, invoke a command, render the result. *depends on:* `Wc3.Commands`.

If a result POCO ever needs a CLI-specific or MCP-specific field, the boundary has leaked and must be fixed.

---

## 4. The MapDocument model

- One **entry per internal file**, each retaining its **original raw (decompressed) bytes** plus MPQ flags and name (or block index if unnamed).
- Known `war3map.*` files are parsed into typed models (wrapping War3Net's parsed types), grouped into the canonical sections from the vision doc:
  `Terrain (w3e)`, `Pathing (wpm)`, `Units/Items (war3mapUnits.doo)`, `Doodads (war3map.doo)`, `Regions (w3r)`, `Cameras (w3c)`, `Sounds (w3s)`, `Info (w3i)`, `Strings (wts)`, `Imports (imp)`, `Objects (w3u/w3a/w3t/w3b/w3d/w3h/w3q)`, `Triggers (wtg/wct)`, `Scripts (j/lua)`.
- `RawFiles` — every entry's original bytes (source of truth for round-trip).
- `UnknownFiles` — unrecognized or unnamed files; preserved, never dropped.
- `DirtyFiles` — set of mutated entries. Empty this slice (no editors), but the plumbing exists so `Save` already behaves correctly when editing lands.
- `Diagnostics` — structured list of parse warnings (see §6).

Parsing may be eager or lazy per section; either is acceptable as long as raw bytes are always captured at load.

---

## 5. Data flow

**Load** `.w3x` →
1. `MpqArchive.Open` (auto-detects pre-archive header offset).
2. Enumerate entries; for each, read decompressed bytes + flags + name/block index.
3. Classify known vs unknown/unnamed.
4. Parse known formats into typed models, retaining raw bytes. Unknown → `UnknownFiles`.
5. Return a `MapDocument`.

**Save** `MapDocument` → path →
1. `new MpqArchiveBuilder(originalArchive)`.
2. For each file in `DirtyFiles`: `AddFile(reserialized bytes)`.
3. All other files keep original bytes automatically.
4. `SaveWithPreArchiveData(path)` to preserve the header.

A no-edit save is therefore faithful by construction.

---

## 6. Error handling (Principle #5 is non-negotiable)

- Unknown / unnamed files → preserved raw; never an error, never dropped.
- A parse failure on a **known** file does **not** fail the load: keep its raw bytes, mark that section `raw-only / unparsed`, append a structured `Diagnostic`. A corrupt `w3i` must never prevent opening — or faithfully re-saving — a map.
- `Save` is always possible even with unparsed sections; they round-trip as raw.
- Diagnostics are surfaced to the user (CLI prints a summary; `--json` includes the full list).

---

## 7. CLI surface (this slice)

Read/query + verification. Every command supports a global `--json` flag for AI/MCP-native structured output.

| Command | Purpose |
|---|---|
| `wc3ctl info <map>` | Map metadata from `w3i`: name, author, players, dimensions, tileset. |
| `wc3ctl ls <map>` | List internal files: name, size, compressed?, flags, known/unknown, parsed?. |
| `wc3ctl cat <map> <internal>` / `extract` | Dump/extract an internal file's bytes. |
| `wc3ctl object get <map> <rawcode> [field]` | Query object data. |
| `wc3ctl search <map> <query>` | Search across models (units, strings, imports, …). |
| `wc3ctl diff <mapA> <mapB>` | Per-file + semantic diff. |
| `wc3ctl roundtrip <map> [--out <path>]` | Open→save→reopen→verify per-file fidelity. The correctness gate. |

Deferred: `object set`, `terrain paint`, `trigger compile`, `build`, `validate`, `test`.

---

## 8. Testing

- **Round-trip fidelity** (xUnit): for each corpus map, open→save→reopen and assert:
  1. every internal file's decompressed bytes are byte-identical,
  2. the set of internal files is identical,
  3. the pre-archive header matches.
- **Parse coverage**: every known format present in the corpus either parses cleanly or degrades to raw with a recorded diagnostic — no silent data loss, no crash.
- **Corpus:**
  - `ggg_en_1.11r_slk.w3x` (63 MB, SLK-optimized) — real-world stress case, already on disk.
  - A handful of small maps for fast unit tests (to source or generate). The 63 MB map is too slow to be the only fixture.
  - If available: a *protected* map (stripped `(listfile)`, unnamed files) to exercise the unnamed-entry preservation path.

---

## 9. Prerequisites

- Install **.NET 8 SDK** (not currently on the machine).
- Add NuGet packages: `War3Net.Build`, `War3Net.IO.Mpq`.

---

## 10. Delegation note ("use fable")

Correctness-critical work (MPQ handling, format parsing, round-trip verification) stays on the Opus model. Open-ended/creative work — human-readable CLI output formatting, command ergonomics exploration, generating varied synthetic test maps — is delegated to **Fable 5** subagents.

---

## 11. Success criteria

1. `wc3ctl roundtrip ggg_en_1.11r_slk.w3x` reports **0 byte differences** across all internal files and an identical file set + header.
2. Every known `war3map.*` format in the corpus parses into a typed model or is recorded as a diagnostic — zero crashes, zero silent drops.
3. `wc3ctl info` / `ls` / `object get` / `search` / `diff` return correct results in both human and `--json` form.
4. `Wc3.Commands` has no dependency on CLI or MCP types (the shared-layer seam holds).
