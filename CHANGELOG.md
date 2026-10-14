# Changelog

All notable changes to wc3ctl. One file per release lives under
[`changelog/`](changelog/), and this file shows the latest one. The format
follows Keep a Changelog, and the project follows Conventional Commits.

## 0.4.4 - 2026-10-14

Eleven fixes from a parallel audit of every tool group against real maps, plus a punctuation
sweep. Each was reproduced on a map or in a hermetic test before and after.

### Fixed

- `deprotect_map` wrote a byte-identical copy and dropped every recovered name. The name lives in
  `(listfile)`, not in the hash table, and the in-place save wrote neither. Measured on PumpkinTD,
  1529 names recovered and an output carrying the same 26 as the input. The save writes a listfile
  now, and PumpkinTD reports 1556 names and Naruto 1394.
- Saving a protected map could re-emit its block-table stuffing. The guard compared named rows to
  unnamed ones, which a deprotect pass defeats by naming the real files, so the rebuild ran and
  grew the table (Naruto 1623 rows to 1853). A second check measures the rebuilt table against the
  original plus the rows the edit itself can add. Both maps hold their row count now.
- `terrain audit` counted the `0xF` "no cliff" sentinel as a distinct cliff texture, so a map
  declaring two cliff tiles reported three.
- `terrain cliff`, `terrain water` and `terrain deform` op set echoed the requested value while the
  tile held the clamped one. All three report the stored value now.
- `hero roster` invented a roster on a map with no custom units, returning item codes on 12331 and
  an ability code on Otaku Defense as `ok:true`. A registry must now have a rawcode that resolves
  to a unit the map defines.
- `object new` reported a silent success when the base rawcode was unknown to both the map and the
  game data, leaving an object that inherits nothing. It appends a note now.
- `object rename` with rewrite-refs rewrote the object fields and the script but left the
  `war3map.wct` custom-text bodies naming the old rawcode, so trigger attribution dangled. The wct
  is rewritten at the byte level, since its decoder is lossy.
- `object import` called the set path once per CSV cell and re-walked the object files each time.
  The full 2265-object GGGA export went from over fifteen minutes to 3 seconds, one session per
  import.
- `file list` and `list files` omitted the `name` key for an unnamed entry rather than writing
  null, so a client reading it got undefined.
- `map optimize` reported the per-file byte sum as the total saving, which read positive on a run
  whose file grew. `bytesSaved` is the on-disk delta now and `contentBytesSaved` the sum.
- `deprotect_map` was annotated read-only while a call with `out_path` writes a map.

### Changed

- The em dash, en dash and spaced dash are gone from comments, message strings and docs, 451
  occurrences across 116 code files and 8 docs, replaced by a comma.

## 0.4.3 - 2026-10-14

Seven more tool fixes, from a third full pass that ran every MCP tool against
a real map and read the output against the tool's own description.

### Fixed

- `terrain optimize` refused every real map. The corner cliff-texture field
  uses `0xF` to mean "no cliff", and every uncliffed corner carries it
  (12429 of 25921 corners on the Gem TD map tested), so the range check read
  it as a dangling index and the remap loop indexed past the end of the cliff
  remap array. The sentinel is skipped now, and `terrain audit` no longer
  reports those corners as out-of-range cliffs.
- `script semantic-diff` reported a pure rename of a Lua chunk-level `local`
  as a change. The canonicalizer only knew a function's own locals, so a use
  inside a function read as a global reference. It normalizes chunk-level
  locals now, with a function-local of the same name shadowing.
- `script leaks` matched a periodic timer callback only in the inline
  `function Name` form. Wurst compiles the callback as a code variable first,
  `set GC = function Name`, so a Wurst map produced no periodic driver and the
  hot-leak join attributed nothing. A code-variable argument resolves through
  its binding now.
- `port into-version` and the compat check reported call tokens and language
  keywords as undeclared identifiers. The scanner split on whitespace and
  stripped only trailing punctuation, so `call SetUnitX(u,` yielded
  `SetUnitX(u`, and the keyword list carried no `set`, `elseif`, `and`, `or`,
  `not` or Lua keyword. It takes the leading identifier run and skips the
  keywords now.
- `trigger lint` silently skipped custom-text triggers, so its summary
  understated how many it had not examined. It reports the count beside the
  disabled count.
- `version info` baked git's `fatal: not a git repository` message as the
  build hash on a source tree with no `.git`. The stamp falls back to `nogit`
  unless the captured value is a real hash.

## All releases

- [0.4.4 - 2026-10-14](changelog/0.4.4.md)
- [0.4.3 - 2026-10-14](changelog/0.4.3.md)
- [0.4.2 - 2026-10-14](changelog/0.4.2.md)
- [0.4.1 - 2026-10-14](changelog/0.4.1.md)
- [0.4.0 - 2026-10-13](changelog/0.4.0.md)
