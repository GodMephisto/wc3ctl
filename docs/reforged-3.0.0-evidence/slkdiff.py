# Cell level diff for SLK tables, which is the only comparison that means anything for them.
#
# A line diff on AbilityData.slk compares 279,377 one-cell records whose order carries no
# meaning, so it reports churn rather than change and takes minutes to do it. An SLK is a
# sparse grid, so parse it into cells, key rows by their object id, and compare field by
# field against the header row. That turns a wall of noise into a list a person can read.
import re

CELL = re.compile(r"^C;(.*)$")


def parse(text):
    """Return (rows, header) where rows is {row_index: {col_index: value}} and header maps
    a column index to the field name that row 1 gives it."""
    rows, x, y = {}, 0, 0
    for line in text.splitlines():
        if not line.startswith("C;"):
            continue
        # X and Y are sticky across records, so a record giving only K reuses the last
        # position. Losing that is how a naive parser silently shifts every value.
        for tok in line[2:].split(";"):
            if not tok:
                continue
            head, rest = tok[0], tok[1:]
            if head == "X":
                x = int(rest)
            elif head == "Y":
                y = int(rest)
            elif head == "K":
                rows.setdefault(y, {})[x] = rest.strip().strip('"')
    header = rows.get(1, {})
    return rows, header


def by_id(rows, id_col=1):
    out = {}
    for y, cells in rows.items():
        if y == 1:
            continue
        key = cells.get(id_col)
        if key:
            out.setdefault(key, cells)
    return out


def diff(old_text, new_text, label, limit=60):
    orows, ohdr = parse(old_text)
    nrows, nhdr = parse(new_text)
    oid, nid = by_id(orows), by_id(nrows)

    added = sorted(nid.keys() - oid.keys())
    removed = sorted(oid.keys() - nid.keys())
    ocols = {ohdr[c] for c in ohdr}
    ncols = {nhdr[c] for c in nhdr}
    new_fields = sorted(ncols - ocols)
    gone_fields = sorted(ocols - ncols)

    # Compare by FIELD NAME, not by column index, because an inserted column shifts every
    # index after it and would report the whole table as changed.
    changed = []
    oname = {ohdr[c]: c for c in ohdr}
    nname = {nhdr[c]: c for c in nhdr}
    shared_fields = sorted(ocols & ncols)
    for key in sorted(oid.keys() & nid.keys()):
        orow, nrow = oid[key], nid[key]
        for f in shared_fields:
            ov = orow.get(oname[f], "")
            nv = nrow.get(nname[f], "")
            if ov != nv:
                changed.append((key, f, ov, nv))

    out = [f"  rows        {len(oid):,} -> {len(nid):,}",
           f"  columns     {len(ocols):,} -> {len(ncols):,}",
           f"  rows added  {len(added):,}",
           f"  rows gone   {len(removed):,}",
           f"  cells changed on shared rows and fields  {len(changed):,}"]
    if new_fields:
        out.append(f"  NEW COLUMNS ({len(new_fields)}): " + ", ".join(new_fields[:40]))
        if len(new_fields) > 40:
            out.append(f"      ... and {len(new_fields) - 40} more")
    if gone_fields:
        out.append(f"  REMOVED COLUMNS ({len(gone_fields)}): " + ", ".join(gone_fields[:40]))
    if added:
        out.append(f"  new rows: " + ", ".join(added[:30]) +
                   (f" ... +{len(added) - 30}" if len(added) > 30 else ""))
    if removed:
        out.append(f"  gone rows: " + ", ".join(removed[:30]) +
                   (f" ... +{len(removed) - 30}" if len(removed) > 30 else ""))
    if changed:
        out.append(f"  sample of changed cells")
        for key, f, ov, nv in changed[:limit]:
            out.append(f"      {key:<12} {f:<20} {ov!r} -> {nv!r}")
        if len(changed) > limit:
            out.append(f"      ... and {len(changed) - limit:,} more")
    return "\n".join(out)
