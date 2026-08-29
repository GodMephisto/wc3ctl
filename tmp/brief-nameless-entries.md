# Brief: describe the entries whose names cannot be recovered

Repo `D:/playground/Programming/wc3ctl`, branch `development`. Work on a new branch
`nameless-entry-types` off `development`.

## The measurement, already done. Do not re-derive it.

An MPQ locates a file by the hash of its name and stores no name anywhere, so a map with a
stripped `(listfile)` leaves entries that can be READ (wc3ctl reads them by block index today)
but not NAMED. `MapDocument.HarvestAssetNames` guesses names from script string literals and
from path-like values in object data, and it does well, recovering 37 to 50 percent on most
maps. It recovers exactly ZERO on two maps that have a readable multi-megabyte script, almost
certainly because those scripts are obfuscated and build asset paths at runtime rather than
holding them as literals. Those names are gone for good.

The name is only needed to LOCATE a file. The contents answer "what is it" directly. Measured
across the library by `tests/Wc3.Tests/UnnamedEntryTypeProbe.cs`, which already exists on
`development` and which you should read first:

- 19 of 37 maps carry nameless entries, 142,051 of them, about 4 GB.
- 100,194 are EMPTY, and roughly 50,000 of those sit in each of the two protected maps as
  padding (77 and 79 percent of their nameless entries).
- Of the 41,857 nameless entries that hold anything, about 99 percent are identified from their
  leading bytes.
- The census: BLP texture 18,790, MDX model 9,697, DDS texture 9,297, MP3 audio 2,690, WAV audio
  668, text 164, JPEG 14, TrueType 6, unknown 529.
- On 17 of the 19 maps, 95 to 100 percent of nameless entries are typed.

So on those maps the Files panel shows blank rows for thousands of entries that are perfectly
readable BLP textures and MDX models.

## What to build

1. A shared, reusable type sniffer. Put it in `Wc3.MapDocument` (namespace `Wc3.Model`) so both
   the command layer and the model can use it, unless you find a better home that respects the
   layering. `UnnamedEntryTypeProbe.Sniff` has a working implementation to start from, but treat
   it as a sketch rather than finished code. It must:
   - Recognise at least the types in the census above.
   - Return a small result type carrying a display name, a likely file extension, and whether the
     entry is empty. Do not return a bare string.
   - Never guess. If the bytes do not match a known signature and are not clearly text, say
     unknown. A plausible wrong answer is worse than "unknown" here.
   - Be cheap. It must only need the first few hundred bytes, not the whole entry, because some
     of these archives are 250 MB. Look at `MapFileEntry` for how bytes are read lazily and do
     NOT force a full decompression of every entry just to type it.

2. Surface it in the Studio Files panel (`src/Wc3.Studio/Panels/FilesView.axaml*`). A nameless
   entry should show its type and size instead of nothing. Keep it obvious that the NAME is
   unknown, do not invent a filename that looks real.

3. Surface it in the CLI. There is currently NO verb that lists archive entries at all, which is
   its own gap. Add one (something like `files list <map>`, follow the naming of the existing
   verbs) that lists entries with name or type, size, and whether the name was recovered by the
   harvest. Support `--json` like the other verbs.

4. If, and only if, points 1 to 3 are done and verified, make extraction of a nameless entry use
   a sensible generated filename with the right extension. Check how extraction currently
   handles a nameless entry before changing it.

## Verification, all of it required

- `dotnet test Wc3.sln --filter "Category!=Corpus&Category!=GameData"` stays green. It is about
  1284 tests.
- New hermetic tests for the sniffer, one per recognised type, built from synthetic byte arrays,
  plus a test that unknown bytes report unknown rather than a guess.
- A Corpus-category test that runs the sniffer across the library and asserts the identification
  rate on non-empty nameless entries stays above 90 percent, so a regression that breaks the
  sniffer fails rather than silently degrades.
- Studio tests follow the pattern in `tests/Wc3.Studio.Tests/`. Note two traps that have already
  cost time here: you must call `window.UpdateLayout()` before walking the visual tree, and you
  must close windows you open.
- Measure and report the panel's load time on a map with many nameless entries before and after,
  because typing 3,000 entries must not make the panel slow. `tests/Wc3.Studio.Tests/PanelLoadCostTests.cs`
  is the existing pattern for this.

## Rules for this repo, follow them exactly

- Build with `dotnet build Wc3.sln` (a bare `dotnet build` fails, there is no project at the root).
- Layering: `Wc3.MapDocument` must not reference `Wc3.Commands`, the CLI, the GUI or
  `Wc3.GameData`. `Wc3.Commands` must not reference any front-end.
- Reuse before you write. If a control or helper exists, compose it. The Studio has reusable
  controls in `src/Wc3.Studio/Controls/`.
- Any new CLI verb needs a matching MCP tool, or the parity test in
  `tests/Wc3.Tests/CliMcpParityTests.cs` fails. Read that test before adding the verb.
- PROSE RULE, absolute: in code comments, XML docs, commit messages and your report, never use an
  em dash, an en dash, a dash as punctuation, a colon used as punctuation, or a semicolon. Use
  commas, periods and parentheses. Hyphens inside compound words are fine. Semicolons in actual
  C# code are obviously fine. This is a hard user rule and it is checked.
- Commit locally on your branch, subject 50 characters or fewer, body wrapped at 72. Do NOT push.
  No `Co-Authored-By` and no AI attribution line.
- Never `git add -A`. Stage explicit paths.
- Do not touch `dist/`, `dist-studio/` or `dist-mcp/`.

## Report back

What you changed, the measured identification rate, the panel load-time before and after, real
test numbers, and anything that did not work. If a part of this turns out to be a bad idea once
you are in the code, say so with the evidence rather than building it anyway.
