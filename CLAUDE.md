# wc3ctl — Warcraft III map toolkit

CLI + Avalonia GUI + shared command layer for **byte-faithful** WC3 `.w3x`/`.w3m` editing & inspection. Replaces the World Editor's jobs, CLI/MCP/AI-native.

## Architecture (layered — keep the boundaries)
```
Wc3.MapDocument  (namespace Wc3.Model)  model: load/save, per-file raw+dirty
Wc3.GameData     Reforged base data via CASC (units/abilities/items/... + strings)
Wc3.Modeling     MDX/MDL geometry parser + BLP texture decode
Wc3.Render       terrain heightmap → PNG (model→PNG renderer coming)
Wc3.Commands     handlers → plain result POCOs   (DEPENDS ON Model/GameData/etc. ONLY)
wc3ctl           CLI (System.CommandLine, --json)  ─┐ thin front-ends over Wc3.Commands
Wc3.Studio       Avalonia GUI                       ─┤ (never parse/re-implement here)
Wc3.Mcp          MCP server over stdio (official C# SDK) ─┘
```
**`Wc3.Commands` must not reference CLI/GUI/MCP types.** Front-ends only render.

## Dependency pins (do NOT bump)
- .NET **8** (SDK 8.0.422). `War3Net.Build`/`War3Net.IO.Mpq` **6.x**. `CascLib.NET` (bundles native x64 dll). `SixLabors.ImageSharp` **3.1.x** (4.x = build-time license gate). Avalonia **11.2.x** (12 needs newer Roslyn than the SDK ships). `System.CommandLine` 2.0.0-beta4.* (beta5 renamed the handler API).

## Critical gotchas
- **Namespace is `Wc3.Model`**, not `Wc3.MapDocument` (a `MapDocument` class inside a `Wc3.MapDocument` namespace collides — CA1724).
- **Fidelity**: untouched files write original bytes; only dirty entries re-serialize. Round-trip comparisons EXCLUDE MPQ bookkeeping `(listfile)/(attributes)/(signature)` (regenerated/dropped; no map content lost).
- **MpqArchiveBuilder**: `RemoveFile(name)` + `AddFile(same name)` silently DROPS the file (removal set filters the re-add). Use `AddFile` alone — it shadows the original (first-add-wins dedup by hashed name).
- **`MpqArchiveBuilder.SaveTo(stream)` disposes the stream** unless `leaveOpen: true`.

## Build & test
```
dotnet build
dotnet test --filter "Category!=Corpus&Category!=GameData"   # hermetic (CI-safe)
dotnet test --filter "Category=GameData"                     # needs WC3 install at D:\Warcraft III
dotnet test --filter "Category=Corpus"                       # needs a real .w3x on disk
```
Publish: `wc3ctl` → `dist/` (+ `CascLib.dll` beside it); `Wc3.Studio` → `dist-studio/` (close the running app first — the DLL locks); `Wc3.Mcp` → `dist-mcp/` (MCP registration snippet in `src/Wc3.Mcp/README.md`).

## History / decisions
`docs/superpowers/specs/`, `docs/superpowers/plans/`, and `.superpowers/sdd/*progress.md` (git-ignored ledgers).
