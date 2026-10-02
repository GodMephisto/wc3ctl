# wc3ctl (Warcraft III map toolkit)

CLI + Avalonia GUI + shared command layer for **byte-faithful** WC3 `.w3x`/`.w3m` editing & inspection. Replaces the World Editor's jobs, CLI/MCP/AI-native.

## Writing style (prose, UI text, comments, docs)
No em-dashes, no en-dashes, no semicolons in prose. Use commas, periods, or parentheses. Only literal code (bash, C#, paths) may contain semicolons. Hyphens in compound words (byte-faithful, round-trip) are fine.

## Architecture (layered, keep the boundaries)
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

## Reusable components, DO NOT hardcode or duplicate
**Every piece of generated code must be reusable, or reuse something that already exists.** Before writing UI or logic, look for an existing component/control/helper and use it. If a pattern appears more than once (or obviously will), extract it into a shared, parameterized unit instead of copy-pasting or inlining values.
- **Studio UI**, an Avalonia `UserControl` in `src/Wc3.Studio/Controls/` with bindable `StyledProperty`/`DirectProperty` inputs (for example `LabeledField`, `OwnerTeamPicker`, `PlayerChip`, `SectionHeader`, `Card`). Panels compose these controls, they do not re-declare the same label-row / dropdown / swatch / card markup. No magic numbers baked into a single panel, promote shared sizes/brushes/templates to the control or a resource.
- **Logic**, a method/type in the correct layer (`Wc3.Commands` for map logic, a shared static/helper otherwise), never re-implemented per front-end. This is Principle #3 applied to the front-ends themselves.
Rule of thumb: if you are about to write the second copy of something, stop and make it a component.

## Dependency pins (do NOT bump)
- .NET **8** (SDK 8.0.422). `War3Net.Build`/`War3Net.IO.Mpq` **6.x**. `CascLib.NET` (bundles native x64 dll). `SixLabors.ImageSharp` **3.1.x** (4.x is a build-time license gate). Avalonia **11.2.x** (12 needs newer Roslyn than the SDK ships). `System.CommandLine` 2.0.0-beta4.* (beta5 renamed the handler API).

## Critical gotchas
- **Namespace is `Wc3.Model`**, not `Wc3.MapDocument` (a `MapDocument` class inside a `Wc3.MapDocument` namespace collides, CA1724).
- **Fidelity**: untouched files write original bytes, only dirty entries re-serialize. Round-trip comparisons EXCLUDE MPQ bookkeeping `(listfile)/(attributes)/(signature)` (regenerated or dropped, no map content lost).
- **MpqArchiveBuilder**: `RemoveFile(name)` + `AddFile(same name)` silently DROPS the file (the removal set filters the re-add). Use `AddFile` alone, it shadows the original (first-add-wins dedup by hashed name).
- **`MpqArchiveBuilder.SaveTo(stream)` disposes the stream** unless `leaveOpen: true`.

## Build & test
```
dotnet build
dotnet test --filter "Category!=Corpus&Category!=GameData"   # hermetic (CI-safe)
dotnet test --filter "Category=GameData"                     # needs WC3 install at C:\Warcraft III
dotnet test --filter "Category=Corpus"                       # needs a real .w3x on disk
```
Publish: `wc3ctl` to `dist/` (plus `CascLib.dll` beside it). `Wc3.Studio` to `dist-studio/` (close the running app first, the DLL locks). `Wc3.Mcp` to `dist-mcp/` (MCP registration snippet in `src/Wc3.Mcp/README.md`).

## ⚠️ When you change code, REPUBLISH before the user tests
`dotnet run` / `dotnet build` / `dotnet test` all run from **source**. The user runs the **published** binaries: `dist-studio\Wc3.Studio.exe`, `dist\wc3ctl.exe`, `dist-mcp\`. **Source edits do NOT reach those exes until you republish.** A green build or passing test only proves the *source* is fixed, it does **not** mean the user's app changed. Skip this and the user keeps hitting the old bug on a stale binary (this has happened, do not repeat it).

After any code change the user will exercise through the app, you MUST:
1. **Close the running app** before publishing Studio. `dist-studio\Wc3.Studio.exe` locks its DLLs, so the publish fails while it is open. Ask the user to close it, do not kill it yourself.
2. **Republish the affected target(s)** (self-contained, win-x64) into its dist folder:
   - Studio: `dotnet publish src\Wc3.Studio\Wc3.Studio.csproj -c Release --self-contained -r win-x64 -o dist-studio`
   - CLI:    `dotnet publish src\Wc3.CLI\Wc3.CLI.csproj -c Release --self-contained -r win-x64 -o dist`  (keep `CascLib.dll` beside `wc3ctl.exe`)
   - MCP:    `dotnet publish src\Wc3.Mcp\Wc3.Mcp.csproj -c Release --self-contained -r win-x64 -o dist-mcp`
   (Match the existing dist layout, for example `PublishSingleFile`, if the current folder differs.)
3. **Verify freshness**, confirm the exe timestamp is *now*, for example PowerShell `Get-Item dist-studio\Wc3.Studio.exe | Select LastWriteTime`, so you know the user's binary actually carries the fix.

Rule of thumb: **a user-visible change is not "done" until its dist target is republished and its timestamp is fresh.**

## History / decisions
`docs/superpowers/specs/`, `docs/superpowers/plans/`, and `.superpowers/sdd/*progress.md` (git-ignored ledgers).
