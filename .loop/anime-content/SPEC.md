# Spec, anime content generation

Goal, turn a hero brief like "Kurosaki Ichigo, two forms, nine abilities" into a playable
package, and make every asset in it either supplied or generated.

Source of truth, the two reference maps in `~/Documents/Warcraft III/Maps/Download`, and
`wc3-mcp/docs/specification` section 21.

## Definition of done, per item

1. The logic is in `Wc3.Commands`.
2. A test named after the behaviour fails before and passes after.
3. Every generated artifact is parsed back by its own reader, a generated script compiles, and a generated model audits with zero issues. No game launch, no test-load.
4. `dotnet test --filter "Category!=Corpus&Category!=GameData&Category!=Doctor"` passes.
5. `dist` and `dist-mcp` are republished and smoke reports the tool count.
6. The spec records what exists, not what is planned.

## Verification, compiler not game

The gate is `doctor check` or `JassScriptCheck` for script, and `model audit` with zero
issues for a model. `test-load` is not run. The engine is not the judge in this loop, so a
classic engine caveat stays a stated risk rather than a test.

## Assets rule

No scraping of Discord or any authenticated service. A local folder plus a manifest with
source, licence note and digest. The tool ingests the folder.

## Queue

1. `sound trim` with silence cut, margin, peak normalise.
2. `sound import --events`, catalog write and dispatcher with cooldown and max voices.
3. Emitter writer, PREM and PRE2, proven by read back and an in-game load.
4. Sequence and keyframe writer.
5. Material and texture composition.
6. Primitive mesher, capsule, ring, cone, card.
7. Package generator, model, icon, sound, effect, manifest.
8. `hero generate`, forms, abilities, transform, install, audits.
