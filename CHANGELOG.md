# Changelog

All notable changes to wc3ctl. One file per release lives under
[`changelog/`](changelog/), and this file shows the latest one. The format
follows Keep a Changelog, and the project follows Conventional Commits.

## 0.4.9 - 2026-10-21

A static verifier and a cost bench, a way to organise the trigger tree, and two repairs that were
proven on a real map. The verifier is the first check here that models reachability rather than one
function at a time, and it is the one that tells the truth about pause, timer and handle defects
that a grep cannot see.

### Added

- `script verify`, a reachability-aware static verifier. It parses every function, builds a call
  graph from direct calls and `TimerStart(..., function X)` callback edges, closes it under
  reachability, and flags a state change only when its restore is absent from that whole set. Eleven
  rules: pause without a reachable restore, `EnableUserControl`, `SetUnitTimeScale(0)`, a periodic
  timer never stopped, group and location leaks, ability-strip, ability-hide, `GetLocalPlayer` on a
  line with a game-state call, a sleep or `GetTriggerUnit` inside a timer callback, `bj_lastCreatedUnit`,
  and a constant-exit loop. Each finding is graded `high` or `medium`, and the ability rules read the
  map's own `uabi` lists so a real hero ability and a dummy ability are not graded the same. Exits 1
  as a CI gate. Also `script_verify` in the MCP.
- `script bench`, ranks every function by static engine cost per second, per-tick `CreateUnit`,
  special effects, enumeration, condition allocation and damage, plus its leak shape. Comments are
  stripped, so a commented-out `CreateUnit` is not counted. `script_bench` in the MCP.
- `trigger move`, re-parent a trigger item under a category. Only `ParentId` changes, so document
  order and every `war3map.wct` code body stay put, and a category cannot be moved inside its own
  subtree. `trigger_move` in the MCP.
- `repair portraits` and `repair audit-errors` in the MCP, so the two repairs reachable from the CLI
  are reachable from a client too.
- `asset import-wire` gained `--base`, which keeps each file's path relative to a directory instead
  of flattening to `war3mapImported`. A sound or asset pack lands on its original subfolders, and
  `war3mapimported` is normalised to `war3mapImported`.

### Fixed

- `asset audit` reads PNG IHDR dimensions when a file named `.blp` is actually PNG data, which three
  textures on a real map were. They no longer report as unreadable.
- `repair portraits` removes the sub-900 camera chunk that renders a black portrait. The version
  re-stamp stays refused, because it was measured to crash the map on load.
- `repair audit-errors` fills ability level gaps from the nearest valid level as well as clearing
  dangling references and impossible requirements.
- The MPQ in-place patch gained a compacting save and an overwrite-when-it-fits path, and the
  salvage patch no longer grows a map by appending a payload that would fit in its original slot.

### Known

- A script edit beyond the trivial can destabilise a whole map even when `script check`, compat,
  validate and mpq verify are all clean. The only proof is a host test, one change at a time.
- Object-data edits, the level-gap fill in particular, can make a map kick on host. On one map it did.
