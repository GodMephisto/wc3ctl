# Changelog

All notable changes to wc3ctl. One file per release lives under
[`changelog/`](changelog/), and this file shows the latest one. The format
follows Keep a Changelog, and the project follows Conventional Commits.

## 0.4.2 - 2026-10-14

Seven more tool fixes, found by re-running every MCP tool against the fixed
build and checking each output against its own description.

### Fixed

- `terrain import-heightmap` wrapped the very top of the height range. The top
  16-bit sample decodes to 112.0, a hair past the 111.998 the raw field holds,
  and the tile setter wrapped it to the -16 minimum, so the highest exported
  height came back inverted. It clamps and quantises now.
- `terrain corner set` and the other mutating tools refused an omitted
  `out_path` even though it is documented optional with a sibling `.edited`
  default. The default is honoured now.
- `hero roster`, and the hero contract scan behind it, missed a roster whose
  rawcodes are the Wurst hex form `$4830334C` rather than a quoted `'H03L'`, so
  it found no roster at all on a Wurst-compiled map.
- `model export-obj` wrote an OBJ whose `mtllib` named the model's basename
  while the MTL on disk took the output path's name, so the two did not line up
  and no renderer could find the materials.
- `target get` promised the target's schema sections and never carried them.
- `replay players` described team and race as reported when unknown, but the
  parser does not read them, so both are omitted and the description says so.
- The wc3-mcp build had no assembly stamp, so its `version info` reported
  `nogit` and `unknown`. The mirror stamps the git hash and build time now.

## All releases

- [0.4.2 - 2026-10-14](changelog/0.4.2.md)
- [0.4.1 - 2026-10-14](changelog/0.4.1.md)
- [0.4.0 - 2026-10-13](changelog/0.4.0.md)
