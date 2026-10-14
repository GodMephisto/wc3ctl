# Changelog

All notable changes to wc3ctl. One file per release lives under
[`changelog/`](changelog/), and this file shows the latest one. The format
follows Keep a Changelog, and the project follows Conventional Commits.

## 0.4.5 - 2026-10-14

The GameData and Corpus suites were run against the real Warcraft III install and the map
library, which is where the first three fixes came from, and the Lua conversion gained its
reverse direction. Every fix was reproduced on a map before and after.

### Fixed

- A map whose `(listfile)` is unreadable failed to load at all. `MapDocument.Load` now retries
  without the listfile, and a block-table index past the archive end is swallowed rather than
  thrown, so `ORDR_S2_2.323[R]_english.w3x` (119 MB) loads with 64,748 entries.
- `target_get` reported a version source in its on-disk case, so a caller matching on the value
  had to guess. It is lowercased now, the form the rest of the tool already uses.
- `replay` listed the game client's live scratch file, `TempReplay.w3g`, as a replay. It is
  skipped, since it is a partial write of the game in progress and not a finished game.

### Added

- `script lua-to-jass` inverts the strict-real and hashtable helpers, so a script that has been
  through the converter comes back rather than leaving `__fadd` and `__hs_save` as plain calls.
  A rewritten hashtable's empty-table local becomes a `CreateHashtable` declaration again, which
  closes the round trip for a hashtable script.

### Changed

- The `script conversion-notes` hashtable rule is opt-in now, and names the typed helpers, since
  passing `--hashtables` rewrites a local hashtable into a Lua table and the Lua to JASS
  direction reads the type back from the helper's name.
- The Studio Objects panel builds its schema list once per target instead of per request, 5,256 ms
  to 85 ms on a heavy target.

### Tests

- The fidelity sweep names its two known drifts by map prefix, `GGGA_V0.07c_slk` and
  `FateGA_R0.2A.w3x`, rather than failing the sweep. Both are documented in the test.
- The Studio panel budgets say they need the project alone and an idle machine, and the files
  typing budget is 45 s against a measured 33.4 s.
- The MCP tool sweep has a ten minute budget, not five. It answers 109 of 181 read-only tools in
  about five minutes, which passed alone and timed out as the last test of a corpus run.
