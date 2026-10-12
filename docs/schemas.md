# The schema dataset

The `data/schemas` directory holds one folder per Warcraft III build. Each folder describes what that
build's World Editor shows and what its JASS compiler accepts, derived from the build's own files and
never copying them. A schema is what lets `wc3ctl` tell whether a map made for one patch opens and
compiles on another, which fields the editor for a target build does not know, and which natives and
types a build lacks.

The dataset is derived metadata (see the licence note below). It is built on demand, committed to git,
and also shipped as a release asset so an installed toolkit needs no checkout.

## What a schema folder holds

Every folder has `manifest.json`, which names the build, its source, and the input files it was derived
from, each with a SHA-256. The manifest also lists the inputs that were missing at build time.

A folder carries up to four sections, each in its own file.

- `objects.json`. The object editor's fields, one entry per metadata SLK (units, abilities, buffs,
  destructables, doodads, upgrades, upgrade effects, misc and skin).
- `triggers.json`. The GUI trigger types, functions and categories from `TriggerData.txt`.
- `script.json`. The types, natives, globals and functions from `common.j`, `blizzard.j` and
  `common.ai`.
- `enums.json`. The closed set of legal values for each enumerated field type, and the name the
  editor shows, from `UnitEditorData.txt`. This is the authority a dropdown should offer, not the
  values a stock object happens to use, so a picker can be built from the schema alone rather than
  from a live install.

## Which builds carry object data

Not every build can be built the same way, so the dataset mixes two kinds of folder.

- A build built from a full game data dump carries `objects.json` and often `triggers.json` and
  `script.json`. These are the real-object builds, the ones that can answer what the object editor
  shows.
- A build built from scripts alone carries `script.json` and nothing else. These are the script-only
  builds, common for old patches where only the extracted JASS survived. They can answer what the
  compiler accepts but not what the editor shows.

The `coverage` command lists one row per build with the sections it holds, so a script-only build is
never mistaken for a real-object one. The `sources` command writes the same knowledge as a machine
readable `sources.json` provenance index, with a hash of every section file and every manifest input.

## Regenerating the dataset

`scripts/build-schemas.ps1` walks a `jass-history` checkout and runs `wc3ctl gamedata schema` for each
build it finds, writing or rebuilding that build's folder under `data/schemas`. It ends by writing
`index.json`. Builds whose input hashes are unchanged are skipped unless `-Force` is passed, so a re-run
is cheap.

Because a build can be produced from more than one source, `scripts/merge-schemas.ps1 -From <dir> -Into
data\schemas` folds a directory of fresh folders into the dataset. An incoming folder replaces the
existing one only when it carries a section the existing one lacks, so a real-object build upgrades a
script-only one and a script-only build never downgrades a real-object one. The script reports what it
added, upgraded and left.

## How it ships

The dataset is committed to git, so a checkout already has it. It is also a release asset, so an
installed toolkit needs no checkout. The release workflow packs `data\schemas` into
`wc3ctl-schemas-<tag>.zip` and attaches it with a SHA-256 sidecar, and `install.ps1` unpacks that asset
into `<install>\schemas`. `scripts/pack-schemas.ps1 -Source data\schemas -Out <zip>` does the same pack
by hand, refreshing `sources.json` through the CLI when a published binary is present and writing a
`schemas.version` entry inside the archive.

`wc3ctl gamedata export-schemas -o <dir>` copies the dataset to a directory, and
`wc3ctl gamedata export-schemas -o <target>.zip` packs it into one archive, either way for attaching to
a release. The same two steps are on the agent surface as the `gamedata_sources` and
`gamedata_export_schemas` tools.

## How a build finds the dataset

Every command and MCP tool that needs a schema resolves the dataset directory, in this order.

1. An explicit `--schemas <dir>` on the CLI, or the `schemas_dir` argument on a tool call.
2. The `WC3_SCHEMAS_DIR` environment variable, when it names an existing directory.
3. A `schemas` folder beside the executable.
4. A `data/schemas` folder found by walking up from the executable.

Installing places the dataset at `<install>\schemas`, so step 3 finds it with no configuration. Set
`WC3_SCHEMAS_DIR` to point at a dataset elsewhere, for example one unpacked from the
`wc3ctl-schemas-<version>.zip` release asset or kept on another drive. A value that names no directory
is ignored, the same as an explicit path that does not exist.

## Schema commands and the coverage limit

The dataset is reached through the `gamedata` group. These verbs read it.

- `gamedata coverage` lists one row per build, with its era and the sections it holds, so a build
  with real object data is told apart from a script-only one. This is the view
  `GamedataCoverageCommand` builds.
- `gamedata sources` writes `sources.json`, the provenance index with a hash of every section file
  and every manifest input.
- `gamedata field-lookup` and `gamedata bounds` answer where a field code comes from and what values
  it accepts.
- `gamedata schema`, `gamedata schema-index` and `gamedata schema-diff` build, index and compare a
  schema folder.
- `gamedata changelog` and `gamedata history` track what a patch changed.
- `gamedata export-schemas` copies the dataset to a directory or packs it into one zip.
- `gamedata snapshot` writes the base game data to a directory for diffing across patches.

The coverage limit is the honest boundary of the dataset. Object data is real for a subset of builds,
the ones derived from a full game data dump, and every one of those carries `objects.json`. For the
rest the object data is era-derived or absent. A build that carries only `script.json` has no object
data of its own, so its fields are taken from a sibling build in the same era when one carries them,
and are unknown when none does. The `bench schema-coverage` command states the same three states as
`real`, `era-derived` and `absent` with corpus totals, so a script-only build is never reported as
though it held the object editor.

A command that needs an object field therefore works only where the coverage says real or era-derived,
and a map whose target build is absent cannot be checked for the fields the editor would show.

## Licence note

A schema is derived metadata. It records field codes, types and bounds, native signatures and trigger
function names that the World Editor and the compiler themselves expose, and it never copies, bundles
or redistributes a Blizzard file. The dataset is generated locally from files the user already owns and
is provided under the same Apache License 2.0 as the rest of this project. It is not a Blizzard product
and it is not endorsed by Blizzard.
