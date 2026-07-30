# Agent Handoff

## Current Goal

Stabilize the byproduct map `ShikiArena.w3x`, which currently has broken or inconsistent hero behavior because the generated preplaced-hero script block is not universal enough.

## Shared Context

- Direct live Codex <-> Claude Code communication does not exist in this environment.
- Use this file as the shared state handoff inside the repo.
- The user-facing source map under investigation is:
  - `C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\1\ShikiArena.w3x`

## Confirmed Facts

- The checked-in repo does **not** contain the old emitter that generated the bad helper block in `war3map.j`.
- The problematic generated block is inside the map script itself:
  - `wc3ctl_WirePlacedHeroSpells`
  - `CreateAllUnits`
- The core runtime problem is a mismatch between:
  - generated multiple heroes sharing one owner
  - imported systems that assume exactly one canonical hero per player slot
- The generated block also had a concrete indexing bug:
  - `udg_Hero2[GetPlayerId(GetOwningPlayer(u))]`
  - imported systems expect `udg_Hero2[1 + GetPlayerId(...)]`

## Concrete Findings From `ShikiArena`

- Finalized source heroes in the generated block:
  - `H0DA`
  - `H000`
  - `H001`
- Original generated ownership for those finalized heroes was all `Player(0)`.
- The generated helper only finalized heroes found under `Player(0)`.
- The map originally declared only `2` player slots in script/config, even though the generated content effectively needed more separation for finalized heroes.

## Implemented Source Fix

A reusable repair command was added to the checked-in source.

### New command

```text
wc3ctl repair generated-heroes <map> [-o <out>]
```

### What it does

- rewrites the generated `wc3ctl_WirePlacedHeroSpells` function to:
  - register spell-effect triggers for every declared repaired player slot
  - finalize heroes with `GetOwningPlayer(hu)` instead of hardcoded `Player(0)`
- rewrites the generated `CreateAllUnits` function to:
  - move duplicated finalized heroes onto distinct regular player slots
  - fix `udg_Hero2` to 1-based indexing
- rewrites script-side player declarations:
  - `InitCustomPlayerSlots`
  - `InitCustomTeams`
  - `config`
- updates `war3map.w3i`
- updates the outer `HM3W` pre-archive max-player count

## Source Files Changed

- `src/Wc3.Commands/GeneratedMapRepairCommand.cs`
- `src/wc3ctl/Program.cs`
- `src/wc3ctl/Render.cs`
- `tests/Wc3.Tests/GeneratedMapRepairCommandTests.cs`

## Verification Already Done

### Targeted tests

Passed:

```text
dotnet test tests\Wc3.Tests\Wc3.Tests.csproj --filter GeneratedMapRepairCommandTests
```

### Source build

Passed:

```text
dotnet build src\wc3ctl\wc3ctl.csproj
```

### CLI publish

Passed:

```text
dotnet publish src\wc3ctl\wc3ctl.csproj -c Release --self-contained -r win-x64 -o dist
```

Freshness check:

- `dist\wc3ctl.exe` was republished and its timestamp was verified fresh after publish.

## Real Map Output Already Produced

A repaired copy was generated from source:

- `tmp\ShikiArena.repaired.w3x`

A repaired copy was also generated from the published CLI:

- `tmp\ShikiArena.repaired.dist.w3x`
- real workspace path:
  - `D:\playground\Programming\Wc3_CLI\tmp\ShikiArena.repaired.dist.w3x`

A direct user-test copy was written beside the original map:

- `C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\1\ShikiArena.repaired.w3x`

The repair reported:

- player slots: `2 -> 3`
- hero owner reassignment:
  - `H0DA: 0 -> 0`
  - `H000: 0 -> 1`
  - `H001: 0 -> 2`

### Sanity checks on repaired copy

Using the built source binary:

- `validate` passed
- `info` reports `Players: 3`

Using the published `dist\wc3ctl.exe`:

- `repair generated-heroes` passed on the real source map
- `validate` passed on `tmp\ShikiArena.repaired.dist.w3x`
- `validate` passed on `C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\1\ShikiArena.repaired.w3x`
- `info` reports `Players: 3` on `C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\1\ShikiArena.repaired.w3x`

## Current State

- The CLI fix is now in source **and** published to `dist\wc3ctl.exe`.
- The published binary has already been used successfully against the real `ShikiArena.w3x`.
- No Studio or MCP publish was needed for this repair wave.
- GitHub push/PR/merge is currently blocked:
  - local branch: `studio-completion-checkpoint`
  - local `master` branch exists
  - no git remote is configured in `.git\config`
  - `gh auth status` shows invalid tokens for the configured GitHub accounts
  - result: no push, no PR, no merge to `master` has been done

## Suggested Next Step For Claude Code

If Claude Code needs to continue from here, start with:

1. Read this handoff file.
2. Inspect `src/Wc3.Commands/GeneratedMapRepairCommand.cs`.
3. Compare `tmp\ShikiArena.repaired.w3x` against the original map if needed.
4. If gameplay still fails, inspect which imported systems still assume one hero per player and whether more than the generated helper block must be normalized.
5. Use `tmp\ShikiArena.repaired.dist.w3x` as the first published-binary output to compare against the original.

## Suggested Next Step For Codex

1. If the user wants direct map help, have them test `tmp\ShikiArena.repaired.dist.w3x` first.
2. The easiest real-game test path is now:
   - `C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\1\ShikiArena.repaired.w3x`
3. If the repaired map still has hero-specific failures, inspect the exact hero family and patch only the remaining failing assumption.
4. If more hero systems fail, add another repair pass based on the exact failing hero family instead of guessing globally.

## Useful Commands

Repair from built source:

```powershell
dotnet src\wc3ctl\bin\Debug\net8.0\wc3ctl.dll repair generated-heroes "C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\1\ShikiArena.w3x" -o tmp\ShikiArena.repaired.w3x
```

Repair from project:

```powershell
dotnet run --project src\wc3ctl -- repair generated-heroes "C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\1\ShikiArena.w3x" -o tmp\ShikiArena.repaired.w3x
```

Repair from published CLI:

```powershell
dist\wc3ctl.exe repair generated-heroes "C:\Users\GodMephisto\Documents\Warcraft III\Maps\Download\1\ShikiArena.w3x" -o tmp\ShikiArena.repaired.dist.w3x
```

Validate repaired map:

```powershell
dotnet src\wc3ctl\bin\Debug\net8.0\wc3ctl.dll validate tmp\ShikiArena.repaired.w3x --json
```

Validate published-CLI repaired map:

```powershell
dist\wc3ctl.exe validate tmp\ShikiArena.repaired.dist.w3x --json
```

Validated directly through the real workspace path too:

```powershell
dist\wc3ctl.exe validate D:\playground\Programming\Wc3_CLI\tmp\ShikiArena.repaired.dist.w3x --json
```
