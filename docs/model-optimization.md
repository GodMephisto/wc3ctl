# Model, texture, sound and terrain optimization

**Date.** 2026-10-13.
**Status.** Shipped. The remaining items are named in section 7.

## 1. What this covers

wc3ctl can make a Warcraft III map smaller without changing what the map is, and it can
say what is safe to remove. The work spans five surfaces, each on the command line and
on the MCP (AI app) surface, all driven by the same logic in `Wc3.Commands`.

| Surface | Commands |
|---|---|
| Model | `model audit`, `model optimize`, `model import` |
| Texture | `texture optimize` |
| Sound | `sound optimize` |
| Terrain | `terrain audit`, `terrain optimize` |
| Model leak | `leak model` |

Nothing edits a file in place. Every command writes a new file and leaves the input
alone, the same rule the rest of the toolkit follows. The Studio does not yet have a
panel for these, so the command line and the MCP surface are how they are reached today.

## 2. Model audit

`wc3ctl model audit <model>` reads an MDX or MDL and reports what it holds and what is
wasteful. It never throws on a bad file, it reports the parse failure instead, so a bad
file never reads as a clean one.

It counts geosets, vertices, faces, textures, sequences, nodes and the effect chunks
(particle emitters, particle emitters 2, ribbons and corns), then reports each of these
problems it finds.

- A texture path listed more than once in the texture list.
- A geoset with no texture, which the game draws as nothing with no message.
- A geoset with no triangles.
- A triangle whose three corners are collinear, so it has no area.
- A skeleton node no geoset skins to, a leftover bone or helper.
- A vertex coordinate that is not finite, the tell for a corrupt or badly transformed file.
- A particle emitter whose node id is outside the model's node list.
- A particle emitter 2 whose texture id is outside the model's texture list.

The two emitter checks read each emitter's payload. A model with no pivot list has no
node list to check against, so the node check is skipped there rather than flagging every
emitter. The audit is read-only and changes nothing.

## 3. Model optimize

`wc3ctl model optimize <model> -o <out.mdx>` runs the model's chunks through four passes.

- **Decimate.** Greedy edge collapse under a quadric error metric (Garland and Heckbert),
  following the implementation guide in arXiv 2512.19959. `--ratio` is the target
  fraction of faces to keep and defaults to 0.5. Output is rounded and clamped, so a
  ratio of 1 or above returns the input unchanged.
- **Compact.** Drop zero-area triangles the game draws as nothing.
- **Prune.** Remove duplicate texture paths and remap the material texture ids onto the
  compacted list.
- **Strip effects.** `--strip-effects` drops the effect chunks, particle emitters (PREM),
  particle emitters 2 (PRE2), ribbons (RIBB) and corns (CORN). This changes how the model
  looks, so it is off by default.

The decimator is deterministic. Vertices are welded by exact position and edges are
ordered by cost then by index, with no random tie-breaking, so the same input always
yields the same output and a test can pin a count. Chunks no pass touches are written back
byte for byte, so materials, sequences, bones, cameras and the untouched vertex arrays
survive exactly. A geoset with no vertex or index chunk, or one that does not reduce, is
left untouched. The report carries the before and after vertex and face counts, the number
of effect chunks removed and the byte sizes.

### Measured result

On a 9 by 9 vertex grid, that is 81 vertices and 128 faces, a decimation at ratio 0.5
produced 41 vertices and 63 faces. The output still parses and every index stays in range.
The figure came from a measured run of the same grid `QemDecimatorTests` and
`ModelOptimizeTests` build. Those tests assert the direction (the face and vertex counts
fall, the output parses, the indices stay in range), not the exact 41 and 63, so the exact
numbers are a measurement rather than a pinned assertion.

## 4. Texture and sound

`wc3ctl texture optimize <texture> --max 256 -o <out>` shrinks a texture to fit a maximum
side, preserving aspect ratio, and writes it back in the same format. A `.blp` and a
`.dds` are read through the same decoder the renderer uses, alongside the standard image
formats. This is the other half of making a model compact, since a model only references
its texture. The input is never modified.

`wc3ctl sound optimize <file> -o <out.wav>` decodes a `.wav` or `.mp3` through NAudio,
resamples it to a target rate (`--rate`, default 22050) and optionally folds it to mono
(`--mono`), then writes a smaller 16-bit PCM `.wav`. It reports the before and after
sample rate, channel count and byte size.

### Measured result

A 64 by 64 BLP shrunk to a maximum side of 32 produced a 32 by 32 BLP, 8,725 bytes down
to 7,432 in the BLP format. The dimension figure is the one the `texture optimize` path
reports. The byte figure is the measured file size of that run and depends on the
encoder, so treat the dimension as the durable number and the bytes as illustrative.

## 5. Terrain

`wc3ctl terrain audit <map>` reports the terrain's size and its measurable waste. It
reports the grid and tile counts, how many distinct ground and cliff textures are actually
in use, the cliff, ramp, water, blight, boundary and edge totals, the height and cliff
ranges, and the `war3map.w3e` byte size. It flags a grid where every corner is identical
(a placeholder or flattened map), an all-zero grid, and a corner whose texture index
reaches at or past the map's declared tile count. Read-only.

`wc3ctl terrain optimize <map> -o <out>` drops the declared ground and cliff tile types
the terrain never references and remaps every corner's texture index onto the compacted
list. A corner addresses a tile by its ordinal index, so dropping an unreferenced entry
and shifting the greater indices down leaves every corner pointing at the same tile it did
before, with no rendering or pathing change. The two declared tile lists and the per-corner
index fields are the only bytes touched, and every other file is written back byte for
byte. A dropped entry is exactly four bytes, so the decompressed `war3map.w3e` shrinks by
four bytes per dropped entry and no more. The pass refuses a map whose corners already
reference an index outside the declared list, because remapping a dangling index is
guesswork.

### Measured result

A blank map declares five ground tiles and references one. On a 16 by 16 tile blank map
the `war3map.w3e` entry shrank from 2,080 to 2,064 bytes, sixteen bytes, one dropped
four-byte entry each for the four unused tiles. The default 32 by 32 blank map shrank from
7,680 to 7,664 bytes. Both figures came from a measured run of `TerrainOptimizeCommand`
against `BlankMap.Create`.

## 6. Model leak

`wc3ctl leak model <map>` joins the map's imported models and textures to what the map and
those models reference. It reports four findings.

- An imported model no object field and no script literal names, a leaked import the map
  ships but never loads.
- A model naming a concrete texture the map does not hold.
- A model imported again under a different name with byte-identical content.
- An imported texture no imported model names.

It reuses the existing model parser and the map's own import and reference primitives, so
it re-implements no chunk reader and no MPQ reader. A parse failure on an imported model
is reported as a diagnostic, never as a clean run. Read-only.

## 7. Still planned

These were designed and are not shipped.

- **Engine limit warnings as a `model limits` command.** The design proposed a standalone
  checker for the engine's texture, bone, geoset, sequence and emitter ceilings. It does
  not exist on this branch. The audit reports resource waste, not those limits.
- **Appearance verification by rendered silhouette.** The design proposed rendering the
  model before and after and comparing a silhouette intersection over union and a mean
  pixel difference, so a decimation that changed the look too much would fail. It is not
  implemented. The optimiser does not currently measure how much a decimation changes the
  look, so `--ratio` is the only control and there is no automatic guard.
- **LOD chain generation.**
- **In-place editing and animation retargeting.**
- **A dedicated Studio panel.** The commands are on the command line and the MCP surface
  only.

## 8. Design decisions, and how they shipped

| Decision | Shipped as |
|---|---|
| Independent passes in a fixed order, each optional | Decimate, compact, prune, strip effects |
| A new file, never in place | Every write command requires or defaults to an output path |
| Structures no pass touches stay byte-identical | Chunks written back byte for byte, verified by tests |
| Attribute-aware decimation preserving UVs, normals and skin weights | The decimator returns a remap applied to the normal, matrix-group and UV arrays |
| Deterministic decimation | Weld by exact position, order by cost then index, no random tie-breaking |
| Static model resource audit | `model audit` and the `leak model` join |
| Foreign model import | `model import` for OBJ, glTF, glb, STL and PLY |
| Texture downscale | `texture optimize` |
| WC3 limit warnings | Not shipped, section 7 |
| Appearance verification by silhouette | Not shipped, section 7 |
| Runtime leak join against a live map | Separate track, the `leak` group's session joins |

## 9. Research grounding

Simplification is a settled family. The standard is greedy edge collapse under a quadric
error metric (QEM), from Garland and Heckbert onward. The implementation follows
`2512.19959`, a comprehensive guide to edge collapse with QEM, Lindstrom-Turk and
attribute-aware variants. Other work found through OpenAlex shaped the design intent,
`2409.15458` on simplifying textured meshes in the wild, `2104.03989` on appearance-driven
simplification, `2305.06410` on intrinsic error metrics with element-quality guarantees,
and `2404.15661` on consolidating weak features.

WC3 models are small, non-manifold and split into many geosets, which is the in-the-wild
case, so the decimator welds by position and collapses over a union-find rather than a
half-edge walk. The attribute-aware and appearance-driven parts of the design are shipped
only in part, as the table in section 8 shows.

## 10. Verification

- The hermetic suite covers the decimator, the model audit, the optimiser, the importer,
  the texture optimiser, the sound optimiser, the terrain audit and optimiser, and the
  model leak, on synthetic models, textures and blank maps, with no WC3 install and no
  corpus.
- A ratio of 1 returns the model byte for byte.
- A zero-area face is compacted away, and stripping effects drops the effect chunk and the
  output still parses.
- A model with duplicate textures is pruned to one.
- A blank terrain that uses every declared tile is left unchanged and survives the save
  path byte for byte.
- A map whose cells reference an out-of-range texture is refused.
- An import of each format builds a parseable MDX, and a glTF material's base texture is
  written as a BLP beside it.

## 11. Open questions carried forward

- Whether a `model limits` checker earns its own command, or should fold into `model audit`
  as warnings.
- Whether appearance verification is worth the render round-trip, given the decimator is
  deterministic and a pinned ratio is reproducible.
