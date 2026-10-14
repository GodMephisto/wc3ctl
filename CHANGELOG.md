# Changelog

All notable changes to wc3ctl. One file per release lives under
[`changelog/`](changelog/), and this file shows the latest one. The format
follows Keep a Changelog, and the project follows Conventional Commits.

## 0.4.7 - 2026-10-15

Two threads in one release. The suite was run on macOS for the first time, which found three real
cross-platform bugs and a test category that was running when it should not. Then a second
performance pass, on the schema load, the doctor and the Studio's terrain and files panels.

### Fixed

- Archive paths broke off Windows. `Path.GetFileName` treats only the forward slash as a separator
  when it is not running on Windows, so `Path.GetFileName` on
  `ReplaceableTextures\CommandButtons\BTNFoo.blp` returns the whole string on macOS and Linux and
  every operation built on it quietly did nothing. An icon sibling was never generated, a sound
  label kept the whole path, a sibling `.mdl` was never looked for, a model export name was wrong.
  `Wc3Path` does those operations with both separators, and real filesystem paths still use
  `System.IO.Path`. Six of the fifteen macOS failures traced here.
- `replay_find_map` matched the recorded map path against the candidate directly, and the recorded
  path keeps a `Maps/` segment the candidate does not, so the exact match never hit and resolution
  fell through to the first enumerated candidate. That was the right one on Windows by directory
  order and the wrong one on macOS. The recorded path is matched on its tail now, longest first.
- `sound optimize` checked the mp3 bitrate after the no-encoder fallback and only when the resolved
  format was still mp3, so on a machine with no encoder a bad bitrate became a WAV instead of
  being rejected. It is checked against the format that was asked for, before the fallback.
- `ContentTypeSniffer.Sniff` read an entry's leading bytes before consulting its cache, so the
  cache only avoided the comparison and never the read. Every pass re-read all 64,738 nameless
  entries on `ORDR_S2_2.323[R]_english.w3x`. Reopening the Studio Files panel on that map went
  from 42,385 ms to 0, and the first pass from 39,370 ms to 22,620.

### Changed

- `compat_explain` 5.5 s to 3.1 on the Release CLI, `gamedata_sources` 2.1 s to 0.5,
  `gamedata_coverage` 3.6 s to 3.1. `TargetBuildSettings.List` loaded every build's four sections
  to read its name and section list, 400 MB, and a fresh CLI process paid it once.
  `LoadSchema`, `BuildNames` and the default-build loop loaded every full schema to read a name.
  The union scan is written to a cache file, and it holds name sets rather than declarations,
  which nothing read. In process the scan is 2,782 ms cold, 918 ms from the cache, 12 ms warm.
- `doctor_check`, `doctor_inspect` and `doctor_compare` remember a read-only answer for the life of
  the process, keyed by the command, its arguments and the timestamp and size of any file among
  them. The doctor is a separate process at about five seconds a call and was 16 seconds of the
  MCP tool sweep.
- The Studio Terrain panel renders its minimap off the UI thread. It blocked for 740 to 855 ms on
  the heaviest map against a 1,000 ms budget and blocks for 16 ms now, so it carries the same
  500 ms budget as every other panel.

### Tests

- The hermetic filter excluded Corpus and GameData but not Doctor, though CI and the release
  workflow exclude all three and the Doctor tests are documented as opt-in. On a machine without
  the pinned doctor binary they ran and failed.
- Three `SetupTests` assert the Windows user PATH and skip off Windows. The terrain probe needs the
  game install as well as the maps, so it gates on the install. `MapDoctorCommand.Locate` no longer
  throws `ArgumentNullException` when `GetFolderPath` returns null, which it does for
  `LocalApplicationData` on macOS.
- `AddOrReplace_SurvivesSaveAndReload` failed once and did not reproduce across thirty targeted and
  five full runs. It asserts the in-memory read before the save now and names what the reloaded map
  holds, so the next failure localises itself.
- The nameless-entry typing budget is two minutes, not forty-five seconds. At 45 s it policed the
  machine's load rather than an order of magnitude.

### Known

- The first typing pass over a protected map is still 22.6 seconds for 64,738 entries, one bounded
  read each, serialized on the document's archive lock. Parallelizing it needs a per-thread archive
  handle.
