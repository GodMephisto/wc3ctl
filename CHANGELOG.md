# Changelog

All notable changes to wc3ctl. The format follows Keep a Changelog, and the
project follows Conventional Commits.

## 0.4.0 - 2026-10-13

The model and asset optimiser, a per-build schema dataset with enum option
sets, and a model and runtime leak workflow.

### Added

Models and assets, all reachable from the CLI, the MCP and Studio.

- `model audit`, a read-only report of what a model holds and what wastes space
  or draws nothing. It finds duplicate texture paths, geosets with no texture or
  no triangles, zero-area faces, skeleton nodes no geoset skins to, non-finite
  vertices, and particle emitters or ribbons that point at a node or a texture
  the model does not define.
- `model optimize`, which shrinks an MDX while every other chunk stays byte for
  byte. It decimates the geometry with a quadric error metric, drops zero-area
  faces, removes duplicate textures from TEXS and remaps the material ids, and
  can strip particle and ribbon effects.
- `model lod`, a chain of level-of-detail MDX files at descending face counts.
- `model import`, a foreign model to MDX. It reads Wavefront OBJ, glTF both
  `.gltf` and `.glb`, STL both binary and ASCII, PLY both ASCII and binary, and
  ASCII FBX, and it writes one geoset and material per primitive, each glTF
  base-colour texture as a BLP beside the MDX, and each glTF animation as an
  MDX sequence.
- `texture optimize`, which shrinks a texture to a maximum side, preserving
  aspect ratio, in its own format.
- `texture atlas`, which packs two or more textures into one power-of-two atlas
  and reports each input's rectangle.
- `sound optimize`, which resamples, folds to mono or stereo, and encodes to WAV
  or MP3 by the output extension, falling back to WAV when no MP3 encoder is
  present.
- `terrain audit`, a read-only report of a map's terrain size and waste.
- `terrain optimize`, which drops the declared ground and cliff tiles no corner
  uses and remaps every cell, changing only `war3map.w3e`.
- `map optimize`, which shrinks every imported model and texture in a map in one
  pass and writes a new map with only the changed files replaced.
- `leak model`, a model resource leak report for a map, naming an import nothing
  references, a model whose texture the map does not hold, a byte-identical
  duplicate import, and a texture no model names.
- The runtime leak join reads the session `events.csv` and the `gc_probe`
  baseline, so a rise is joined to the event and log line nearest it, and the
  A/B mode measures each session from its own baseline.
- A Studio **Optimise** panel drives every optimise and audit command through
  the shared command layer, with a shared `ReportRowCard` control.

Schema dataset.

- A per-build schema dataset, one folder per build, describing what that build's
  editor shows and what its compiler accepts. 157 builds, 31 with real object
  data.
- An `enums` section, the closed set of legal values for each enumerated field
  type and the name the editor shows, read from `UI\UnitEditorData.txt`, so a
  picker can be built from the schema alone.
- `gamedata enum`, which reports those option sets, in one build or all.
- Real object data for ten more builds, from ROC 1.00 to TFT 1.31.1.12164, so
  the classic range is no longer script-only.
- The dataset resolves from an explicit `--schemas`, the `WC3_SCHEMAS_DIR`
  environment variable, a `schemas` folder beside the executable, or a walk up
  to `data/schemas`, and the installer unpacks the release asset into
  `<install>\schemas`.

### Changed

- The MCP server exposes 244 tools.
- The model parser reads a material texture id unchecked, so the no-texture
  sentinel no longer overflows.

### Fixed

- The TFT 1.24.4 schema, which lacked `SkinMetaData.slk` and `common.ai` and so
  held fewer fields than the older 1.21a. It now matches its neighbours.
- A Console writer race and a CASC open race, the two tests that failed only
  under a full parallel run.
- The MDX PREM emitter node offset is confirmed against a real record, not only
  the format source.

### Notes

- The classic object metadata, `TriggerData.txt` and `common.j` are
  byte-identical from 1.21a to 1.28.5, verified across independent clients, so
  those builds share one schema payload.
- The per-build schema dataset is derived metadata. No game file is
  redistributed, only field codes, types, bounds and names read out of files the
  user owns.
- glTF bone tracks, binary FBX, and glTF multi-material beyond the base-colour
  texture are not imported yet.
