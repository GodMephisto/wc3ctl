# Changelog

All notable changes to wc3ctl. One file per release lives under
[`changelog/`](changelog/), and this file shows the latest one. The format
follows Keep a Changelog, and the project follows Conventional Commits.

## 0.4.1 - 2026-10-14

Twelve bug fixes, found by sweeping every MCP tool with real inputs and then
reviewing for the classes those bugs exposed.

### Fixed

- `model optimize` and `model lod` dropped every UV. The rewritten UVAS chunk
  omitted the per-set UVBS tag, so a reader stopped at the count and the model
  came back with no texture coordinates, and a textured render then tripped on
  the empty array. `map optimize` inherited the same defect for a model inside a
  map.
- `terrain deform` and `terrain water` wrote the caller amount straight into a
  16-bit (and 14-bit) field, so a value past the maximum wrapped and stored a
  wrong height while still reporting the tiles changed. Both clamp and quantize
  now.
- `terrain cliff` floored the level at 0 but never capped it at the 4-bit
  maximum, and `terrain paint` capped neither the texture index at 15 nor the
  variation at all. Each out-of-range value made the writer throw on save. All
  three are bounded now.
- `object clone` and `object rename` read and wrote only `war3map.*` and ignored
  the `war3mapSkin.*` twin, so a clone lost the skin half (name, model and icon)
  and a rename left the old rawcode resolvable and moved only half the object.
- `trigger remove --recursive` walked children by id, and ids are numbered per
  type, so a trigger id that happened to equal a category id pulled in an
  unrelated subtree and emptied the whole tree. It now recurses on category ids
  only.
- `script lua globals` threw on any Lua map with a file-scope local, because the
  walker had no top-level scope.
- `hero lint` and `hero install` threw a raw exception on a camelCase
  `hero.json`, because System.Text.Json matched case-sensitively and left the
  collections null, then enumerated them before any validation.
- `replay summary` emitted a map path carrying the short non-text prefix the
  settings blob stores, while every sibling report trimmed it.
- `player list` serialized the team color as an empty object, because a
  ValueTuple's components are fields and fields are excluded by default. It is a
  record with R, G and B properties now.
- `mcp export verify` fell back to the process working directory when it could
  not locate the wc3ctl checkout, so it compared against nothing and reported
  every target file as removed. It refuses a source root with no wc3ctl source
  now, and it skips the gitignored `CLAUDE.md` so a clean export reads as in
  step.
- `asset unused` ignored `war3mapSkin.txt` and `war3mapMisc.txt`, so an import
  named only by the skin read as unused.

## All releases

- [0.4.1 - 2026-10-14](changelog/0.4.1.md)
- [0.4.0 - 2026-10-13](changelog/0.4.0.md)
