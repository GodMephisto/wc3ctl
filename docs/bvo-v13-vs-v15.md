# BleachVsOnepiece v13 against v15, measured

Run 2026-09-19 with `wc3ctl` against the two untouched originals in
`Documents\Warcraft III\Maps\Download\`. The question was whether reverting to
v13 would avoid the defects v15 shows under Reforged 3.0.0.24268. It would not,
and this records why.

## The two files

| | v13 | v15 |
|---|---|---|
| bytes | 8,156,683 | 8,603,933 |
| sha256 head | 66CFC6B5 | 8A9B8AAF |
| archive entries | 851 | 851 |
| entries the listfile names | 2 | 58 |
| script path | `scripts\war3map.j` | `war3map.j` |

Both are protected by listfile stripping, v13 more aggressively. The toolkit now
recovers internal names by MPQ hash rather than by listfile, which is what makes
the rest of this table possible at all.

## Object data is byte-identical

Every one of the seven object kinds carries the same population and the same
field values in both maps. Not similar, identical.

| kind | v13 | v15 | differing fields |
|---|---|---|---|
| ability | 897 | 897 | 0 |
| unit | 654 | 654 | 0 |
| item | 327 | 327 | 0 |
| buff | 241 | 241 | 0 |
| destructable | 8 | 8 | 0 |
| doodad | 12 | 12 | 0 |
| upgrade | 0 | 0 | 0 |

The backing files match on size too. `war3map.w3a` is 1,695,229 bytes in both,
`war3map.w3u` is 522,834, and `war3map.w3i` is 759.

Pinned by `ProtectedMapLoadTests.V13_and_v15_carry_identical_object_data`.

## Both audit to exactly the same 301 issues

Same count, same breakdown, same 20 requirement errors. Since every check in the
audit reads object data, and the object data is identical, this is the expected
result rather than a coincidence, and it is worth stating because it settles the
question. Every defect present in v15 is present in v13.

## Only the trigger script differs

| | v13 | v15 | delta |
|---|---|---|---|
| characters | 4,426,759 | 4,522,585 | +95,826 |
| functions | 13,531 | 13,665 | +134 |

The v15 additions are three rawcodes (`AInv`, `Apiv`, `manh`), eight globals
(`udg_hear`, six `udg_zzARc` style slots, and `udg_zzMegT`), and a much larger
chat command console. `SubString` calls rise by 359 and
`DisplayTimedTextToPlayer` by 94, which is the shape of added text commands. The
commands v15 adds and v13 does not have include `-act`, `-add`, `-addhp`,
`-additem`, `-agi`, `-ally`, `-allyall`, `-area`, `-attack` and `-autoh`.

Function names are obfuscated in both, so a name level diff carries no
information. Rawcodes, natives and globals survive obfuscation, which is why the
comparison uses those.

## What this means for the repair

Reverting to v13 buys nothing. It loses the v15 script additions and keeps every
data defect, because the data defects predate both builds. The repair therefore
proceeds from v15, which is what BVO17 is.

## How to reproduce

```
wc3ctl audit "...\BleachVsOnepiece13.w3x" --json
wc3ctl audit "...\BleachVsOnepiece15.w3x" --json
wc3ctl object list --kind ability "...\BleachVsOnepiece13.w3x" --json
dotnet test --filter "Category=Corpus"
```
