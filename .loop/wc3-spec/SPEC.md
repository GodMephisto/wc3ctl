# SPEC, close the wc3-mcp specification

Compiled 2026-11-09 for the run that takes `docs/specification` from a
part assessed document to one where every requirement is either `met`
with a test or a measurement, or `gap` with a row that names why.

## Goal

Every one of the 179 requirements in `requirements.json` carries a
status of `met` or `gap`, and every `gap` is named by exactly one row in
the gap table, with the numbers in the document agreeing with the
published server.

## Definition of done

1. `python tools/sync_requirements.py` reports `unassessed 0`.
2. `python tools/check_counts.py` exits 0, all 9 declared figures.
3. `python tools/measure_surface.py` exits 0, all 13 measures against
   `C:/playground/Programming/Wc3_CLI/dist-mcp/Wc3.Mcp.exe`.
4. `python tools/build_pdf.py` and `--light` both render with 0 errors.
5. The gap set computed from `requirements.json` equals the set of ids
   named in the gap table, with no id on either side alone.
6. A commit in `wc3-mcp` holds the revision, both PDFs and
   `requirements.json`.

## The queue

The requirements table in `requirements.json` is the queue. It already
carries one id, one status and one basis per row, and the acceptance
checks in section 7 carry the commands. Mirroring 179 rows into
`queue.json` would create a second copy that drifts the first time a row
closes, so `queue.json` holds only the batch schedule, which is what the
table does not track.

## Verifiers

Per batch the verifier is the guard pair plus the claim's own evidence.
A requirement is not `met` because half of it is. A requirement is not
`met` because the code looks right, unless the claim is structural and
the reading is quoted in the basis.

## Autonomy

Decide alone, and record it. Reading, measuring, editing the document,
editing the tree, committing on a branch, the batch order. Ask first, and
do not do it, for a push, a publish, a release, a spend, or a change that
alters what the document promises to a client rather than how it is
proven.

## Budget

One batch per cluster pass, at most four guard runs per batch, stop at
the session limit with `progress.md` current. Retries, two per approach,
then record the blocker as a row and move to the next cluster.

## Stop rules

Done when definition of done holds. Stop early on a plateau, two passes
in a row where no row newly passes and no new finding appears, or when
the remaining rows all need code that changes client visible behaviour,
which is a decision for the user.
