# Brief: make protected maps saveable

Repo `D:/playground/Programming/wc3ctl`, branch `development`. Work on a new branch
`mpq-salvage-save` off `development`.

## The problem, already diagnosed. Do not re-derive it.

Two maps in the library load fine and cannot be saved at all.

```
ORDR_S2_2.305[R]_english.w3x   134,758,606 bytes  65,536 entries, 20 named, 22 unreadable
PumpkinTD_v2.3b.w3x            171,806,945 bytes  65,536 entries, 25 named,  8 unreadable
GGGA_V0.04g.w3x  (control)     252,409,928 bytes   7,914 entries, all named, saves fine
```

The standing explanation in the project notes was "65,534 of 65,536 hash slots stuffed". That is
wrong, or at least beside the point. Both hash tables are 65,536 slots with 65,536 live entries,
so they are full, but that is NOT what fails.

The measured root cause, from `tests/Wc3.Tests/ProtectedMapSaveProbe.cs` which already exists on
`development` and which you should read first:

```
System.ArgumentOutOfRangeException: Stream length must be non-negative and less than
2^31 - 1 - origin. (Parameter 'offset')
  at War3Net.IO.Mpq.MpqStreamFactory.TryPeekCompressionType(...)
  at War3Net.IO.Mpq.MpqStreamFactory.ValidateBlockPositions(...)
  at War3Net.IO.Mpq.MpqStreamFactory.FromStream(...)
  at War3Net.IO.Mpq.MpqArchive.GetMpqFiles()
  at War3Net.IO.Mpq.MpqArchiveBuilder..ctor(MpqArchive originalMpqArchive)
```

So: `MapDocument.Save` builds `new MpqArchiveBuilder(originalArchive)` to preserve entries it
could not read, and that constructor EAGERLY opens every one of the 65,536 entries. A handful of
them (22 and 8 respectively) carry block positions that protection has corrupted, and opening
one throws. One bad entry among 65,536 aborts the entire save.

Both maps also carry deliberately bogus header fields, which is useful context but probably not
the thing to fix: `headerSize=2097410` where a real MPQ v0 header is 32 bytes, and PumpkinTD
declares a sector size of 16,777,216 bytes (block size shift 15) where the normal value is 4,096
(shift 3).

## What to build

A salvage path in `src/Wc3.MapDocument/MapDocument.cs`, used ONLY when the normal rebuild throws.

1. Keep the current behaviour exactly as it is for every map that saves today. This is
   non-negotiable. 35 of 37 maps save now and must continue to produce byte-identical output.
   There is an existing guard for this, see below.

2. When `new MpqArchiveBuilder(originalArchive)` throws, fall back to building the archive from
   scratch with `new MpqArchiveBuilder()` and adding entries one at a time, skipping only the
   entries that cannot be opened.

3. Carrying an entry with no recoverable name IS possible and is required, since 65,516 of 65,536
   entries on these maps have none. The API is public:

   ```
   MpqFile.New(Stream stream, MpqHash mpqHash, uint hashIndex, uint hashCollisions,
               uint? encryptionSeed)
   ```

   You will need each entry's `MpqHash`, its hash index and its collision count from the source
   archive. Investigate what `MpqArchive` exposes for this (it may need the hash table, or an
   enumeration that pairs entries with hashes). If `MpqArchive` genuinely does not expose enough
   to reconstruct an unnamed entry, STOP and report that as the finding rather than inventing a
   workaround.

4. Every skipped entry must be recorded as a `Diagnostic` on the document naming why, so the user
   is told what was dropped rather than discovering it later. Reuse the existing `Diagnostic`
   type and the `_diagnostics` list.

5. Update `DescribeUnrebuildable` so its message reflects reality. It currently claims the map
   "cannot be rebuilt, so it cannot be saved", which will no longer be true.

## Verification, all of it required

- `dotnet test Wc3.sln --filter "Category!=Corpus&Category!=GameData"` must stay green. It is
  1284 tests right now.
- `tests/Wc3.Tests/SerializerFidelitySweep.cs` is the guard that every model-backed file in the
  library round-trips byte for byte. It must stay green, which is what proves you did not disturb
  the 35 healthy maps.
- Add a new Corpus-category test that saves each of the two protected maps, reloads the result,
  and asserts the named files that were readable before are still readable after with identical
  bytes. That is the real success criterion: a salvaged save must not corrupt the content that
  WAS intact.
- Report the byte size of each salvaged map and how many entries were dropped.
- If a salvaged map cannot be reloaded by `MapDocument.Load`, the work is not done.

## Rules for this repo, follow them exactly

- Build with `dotnet build Wc3.sln` (the solution is `Wc3.sln`, a bare `dotnet build` fails).
- Architecture layering: `Wc3.MapDocument` (namespace `Wc3.Model`) must not reference
  `Wc3.Commands`, the CLI, the GUI or `Wc3.GameData`.
- PROSE RULE, absolute: in code comments, XML docs, commit messages and your report, never use an
  em dash, an en dash, a dash as punctuation, a colon used as punctuation, or a semicolon. Use
  commas, periods and parentheses. Hyphens inside compound words are fine. Semicolons in actual
  C# code are obviously fine. This is a hard user rule and it is checked.
- Comments should explain WHY, especially where a measured fact drove the design. This codebase
  documents the trap, not the mechanism. Match that style.
- Commit locally on your branch with a subject of 50 characters or fewer and a body wrapped at
  72 columns. Do NOT push. Do NOT add any `Co-Authored-By` or AI attribution line.
- Never `git add -A`. Stage explicit paths.
- Do not modify anything under `dist/`, `dist-studio/` or `dist-mpc/`.

## Report back

State plainly: what you changed, the measured before and after for both protected maps, how many
entries were dropped and why, the test results with real numbers, and anything you tried that did
not work. If the whole approach turns out to be impossible, say so with the evidence. A negative
result reported honestly is worth more than a workaround that appears to succeed.
