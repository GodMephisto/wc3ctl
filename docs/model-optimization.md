# Model Optimization and Analysis — Design

**Date:** 2026-10-13
**Status:** Draft, self-reviewed. Awaiting the user's confirmation of the decisions in section 3.
**Slice:** 1 of N — model make-small and model audit.

---

## 1. Context

wc3ctl reads and writes Warcraft III maps. `Wc3.Modeling` already parses MDX and MDL models, their
skeleton, geosets, materials, sequences and textures. Commands already lint a model, render it to a
PNG and export it to OBJ. What wc3ctl cannot do is make a model smaller, or say whether a model is
safe for the engine and cheap at runtime.

A WC3 map has a hard size budget, and the engine has known limits on textures, bones, geosets and
emitters. A model taken from a modelling site is usually far heavier than the engine wants, and the
World Editor offers no decimation at all. The ask is a model optimizer and analyser, reachable from
the CLI, the MCP and Studio, that makes a model as small as it can be without changing how it looks,
then reports what could still exceed a limit or leak.

## 2. Research grounding

Simplification is a settled family. The standard is greedy edge collapse under a quadric error metric
(QEM), from Garland and Heckbert onward. The recent work, found through OpenAlex, is:

- `2512.19959`, a comprehensive implementation guide to edge collapse, with QEM, Lindstrom-Turk and
  attribute-aware variants, and the full operation sequence.
- `2409.15458`, simplifying textured meshes in the wild, which handles non-manifold and
  multi-component input and preserves appearance through mesh correspondence rather than naive UV
  preservation.
- `2104.03989`, appearance-driven simplification, which drives reduction by image-space difference
  to a reference render rather than by geometry alone.
- `2305.06410`, intrinsic error metrics, adding hard element-quality guarantees.
- `2404.15661`, consolidating weak features.

WC3 models are small, non-manifold and split into many geosets, which is exactly the in-the-wild
case. The design therefore follows attribute-aware QEM, with image-space verification as the
acceptance test rather than geometry error alone.

## 3. Decisions

These are the design-tree decisions, each with the answer taken. The user is asked to confirm or
correct them.

1. **Input format.** WC3 MDX and MDL only for slice 1. Foreign formats (OBJ, glTF, FBX) are a
   separate track, because the project has no importer and the engine wants MDX.
2. **What optimize means.** A set of independent, composable passes, applied in a fixed order, each
   optional. Slice 1 ships three. Decimate (attribute-aware QEM edge collapse preserving UVs,
   normals, vertex colours and skin weights). Prune (drop unreferenced geosets, materials, textures,
   nodes and sequences). Compact (drop unused MDX chunks and zero-area faces, and renumber). Later
   passes, texture downscale and LOD generation, are out of slice 1.
3. **Output.** A new file, never in place, matching every other editing command, plus a machine
   readable report. `--json` for the report.
4. **Fidelity.** Structures no pass touches are written byte-identical. The report carries a
   before and after count per structure (vertices, faces, geosets, textures, bytes) and an
   appearance delta.
5. **Appearance verification.** Render the model before and after to a PNG through the existing
   renderer, and compare with a silhouette intersection-over-union and a mean pixel difference.
   A decimation that drops the silhouette below a threshold fails. This is the "without sacrificing
   how it looks" guard, and it is measurable, not a claim.
6. **WC3 limits.** A checker for the engine's known constraints, reported as warnings, not errors,
   because the tool does not know the map's intent. Texture dimensions and power-of-two, bone
   count, geoset count, sequence count, team colour and team glow presence, particle emitter count,
   and total model bytes against a map size budget.
7. **Memory leak.** Two readings, and slice 1 ships the first. The **static** reading is a model
   resource audit, duplicate textures, orphaned geosets, zero-area faces, unused nodes and
   emitters that emit forever. The **runtime** reading, does a map that uses the model leak handles
   at play time, is the existing `leak` workflow pointed at a map, and is a later slice. The user is
   asked which they meant.
8. **Placement.** Logic in `Wc3.Commands` (`ModelOptimizeCommand`, `ModelAuditCommand`,
   `ModelLimitsCommand`), CLI under a new `model` group (`model optimize`, `model audit`,
   `model limits`), MCP tools (`model_optimize`, `model_audit`, `model_limits`), and a Studio panel
   that reuses the same commands. No parsing or optimisation in the front ends.
9. **Determinism.** Decimation is deterministic, no random tie-breaking, so a test can pin a count.
10. **First slice.** Audit, limits, and a conservative decimate that refuses when the appearance
    delta would exceed the threshold. Texture downscale, LOD, foreign import and runtime leak join
    are later slices.

## 4. In scope (slice 1)

- `model audit` and `model_audit`. Resource audit and WC3 limit warnings for one model or a folder.
- `model optimize` and `model_optimize`. Decimate, prune and compact, with a before and after
  report and an appearance check.
- `model limits` and `model_limits`. The engine limits alone, for a quick check.
- Deterministic QEM decimation preserving UVs, normals, vertex colours and skin weights.
- Appearance verification by rendered silhouette and pixel difference.
- Hermetic tests on a small synthetic MDX, with byte tests for untouched chunks.

## 5. Out of scope (slice 1)

- Foreign model import (OBJ, glTF, FBX) and conversion to MDX.
- Texture downscale, recompression and atlas packing.
- LOD chain generation.
- Runtime leak join against a live map session.
- Studio panel, deferred to keep the first slice small.
- In-place editing and animation retargeting.

## 6. Command surface (slice 1)

```
wc3ctl model audit   <model|folder> [--json]
wc3ctl model limits  <model|folder> [--json]
wc3ctl model optimize <model> -o <out.mdx> [--ratio 0.5] [--keep-animations] [--json]
```

`--ratio` is the target fraction of faces to keep. `--keep-animations` refuses to prune sequences.

## 7. Verification

- `dotnet test` hermetic suite stays green, with new tests for the decimator, the audit and the
  limits checker.
- A round-trip test, optimize with ratio 1.0 changes nothing but the report.
- A silhouette test, decimate a known model to half the faces and assert the silhouette IoU stays
  above the threshold.
- `ci` and the MCP reference guards updated for the new tools.

## 8. Open questions for the user

- Did "memory leak" mean the static resource audit, the runtime map leak, or both?
- Is a foreign model importer in scope at all, or only WC3 MDX and MDL?
- Should optimize ever edit in place, or always write a new file?
