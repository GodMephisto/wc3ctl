# Changelog

All notable changes to wc3ctl. One file per release lives under
[`changelog/`](changelog/), and this file shows the latest one. The format
follows Keep a Changelog, and the project follows Conventional Commits.

## 0.4.6 - 2026-10-14

A performance pass over the schema-backed tools. Every command re-read and re-parsed the whole
schema set on each call, 159 folders and 203 MB of JSON, which made one tool take twenty seconds.
Measured on the MCP tool sweep against a real map, 181 read-only tools, the total went from
73,248 ms to 41,507 ms.

### Changed

- `compat_explain` 19,923 ms to 3,800 ms, and 9 ms warm. It parses the whole schema set, and now
  the result is cached under a key of the build folders and their manifest timestamps, so it
  cannot serve a stale set. `map_compat`, `lua_api` and `map_min_build` share the same scan, and
  `map_compat` built it a second time without it, 5,703 ms to 2,451 ms.
- The union scan parses each build's `script.json` only, not the objects, triggers and enums it
  never reads, and the folders parse in parallel. Cold, 16,511 ms to 2,753 ms.
- The build names are parsed once. The version comparer called `Gbn.Parse`, which runs a regex, on
  both sides of every comparison, millions of regex passes over 157 builds and 5,393 identifiers.
- A schema load takes a section mask now, and each command asks for what it reads. `gamedata_sources`
  3,694 ms to 387 ms, `ci_schema` 3,588 ms to 513 ms, `gamedata_field_lookup` 2,468 ms and
  `bench_schema_coverage` 2,146 ms off the slow list.

### Tests

- The MCP tool sweep times each call and prints the twenty slowest with the total and median,
  which is what turned the 19.9 seconds inside `compat_explain` from invisible into a finding.
- The nameless-entry typing budget is two minutes, not forty-five seconds. At 45 s it policed the
  machine's load rather than an order of magnitude, and failed twice, 33.4 s alone and 116 s after
  a corpus sweep.

### Known

- The three `doctor_*` tools are 16 seconds of the remainder and are not ours. They spawn the
  pinned third party doctor binary, so the cost is that process starting.
- Caching the loaded schemas themselves would take every `gamedata_*` tool to milliseconds and
  hold 276 MB resident, measured. It is left out for the memory, not the difficulty.
