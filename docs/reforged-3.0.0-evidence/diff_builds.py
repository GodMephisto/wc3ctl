# Diffs two Warcraft III builds at file level, straight from Blizzard's own manifests.
#
# This is the changelog Blizzard does not publish. A file is unchanged when its content key
# is identical in both roots, so the comparison is exact rather than heuristic, and it needs
# no copy of either build on disk.
import collections
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from tact import blte_decode, fetch, parse_build_config, parse_encoding

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

CFG = pathlib.Path(r"C:\Warcraft III\Data\config")
BUILDS = {
    "2.0.4.23745": CFG / "9a/94/9a94ff7d25781db6c09190cd69d52458",
    "3.0.0.24268": CFG / "3a/9d/3a9d8f26806936764d2d9ad526a65e04",
}

# The extensions that decide behaviour. Everything else in the build is art or binary.
BEHAVIOUR = {".j", ".lua", ".slk", ".txt", ".fdf", ".ai", ".ini", ".json"}


def load(label, path):
    cfg = parse_build_config(path.read_text(encoding="utf-8"))
    table = parse_encoding(blte_decode(fetch(cfg["encoding"][1])))
    root_ekey = table[cfg["root"][0]][0]
    root = blte_decode(fetch(root_ekey)).decode("utf-8", errors="replace")

    files = {}
    for line in root.split("\n"):
        if not line:
            continue
        parts = line.split("|")
        if len(parts) < 2 or not parts[1]:
            continue
        files[parts[0]] = parts[1]
    sizes = {ck: sz for ck, (_ek, sz) in table.items()}
    print(f"[{label}] {len(files):,} paths, {len(table):,} content keys", file=sys.stderr)
    return cfg, files, sizes


def ext(path):
    base = path.rsplit("/", 1)[-1]
    return ("." + base.rsplit(".", 1)[1].lower()) if "." in base else "(none)"


def main():
    (oc, of, osz), (nc, nf, nsz) = (load(k, v) for k, v in BUILDS.items())
    old_label, new_label = list(BUILDS)

    added = {p: nf[p] for p in nf.keys() - of.keys()}
    removed = {p: of[p] for p in of.keys() - nf.keys()}
    common = nf.keys() & of.keys()
    changed = {p: (of[p], nf[p]) for p in common if of[p] != nf[p]}
    same = len(common) - len(changed)

    def total(paths, table, sizes):
        return sum(sizes.get(table[p], 0) for p in paths)

    print("=" * 78)
    print(f"FILE LEVEL DIFF, {old_label} -> {new_label}")
    print("=" * 78)
    print(f"  paths in {old_label:<12} {len(of):>9,}")
    print(f"  paths in {new_label:<12} {len(nf):>9,}")
    print()
    print(f"  added      {len(added):>7,} files   {total(added, nf, nsz):>14,} bytes")
    print(f"  removed    {len(removed):>7,} files   {total(removed, of, osz):>14,} bytes")
    print(f"  modified   {len(changed):>7,} files   "
          f"{total(changed, nf, nsz):>14,} bytes (new side)")
    print(f"  unchanged  {same:>7,} files")
    pct = 100.0 * (len(added) + len(removed) + len(changed)) / max(len(nf), 1)
    print(f"  touched    {pct:.1f}% of the new build's file list")
    print()

    print("=" * 78)
    print("BY EXTENSION, top 25 by files touched")
    print("=" * 78)
    rows = collections.defaultdict(lambda: [0, 0, 0])
    for p in added:
        rows[ext(p)][0] += 1
    for p in removed:
        rows[ext(p)][1] += 1
    for p in changed:
        rows[ext(p)][2] += 1
    print(f"  {'ext':<10} {'added':>8} {'removed':>8} {'modified':>9}   {'total':>8}")
    for e, (a, r, c) in sorted(rows.items(), key=lambda kv: -sum(kv[1]))[:25]:
        print(f"  {e:<10} {a:>8,} {r:>8,} {c:>9,}   {a + r + c:>8,}")
    print()

    print("=" * 78)
    print("THE FILES THAT DECIDE BEHAVIOUR")
    print("=" * 78)
    for title, coll in (("ADDED", added), ("REMOVED", removed), ("MODIFIED", changed)):
        hits = sorted(p for p in coll if ext(p) in BEHAVIOUR)
        print(f"\n--- {title}, {len(hits):,} behaviour files ---")
        for p in hits[:400]:
            print(f"    {p}")
        if len(hits) > 400:
            print(f"    ... and {len(hits) - 400:,} more")

    print()
    print("=" * 78)
    print("BUILD PROVENANCE, as Blizzard records it")
    print("=" * 78)
    for label, cfg in ((old_label, oc), (new_label, nc)):
        print(f"\n  {label}")
        for f in ("build-name", "build-comments", "build-source-branch",
                  "build-source-revision", "build-data-branch", "build-data-revision"):
            print(f"    {f:<24} {cfg.get(f, ['(absent)'])[0]}")


if __name__ == "__main__":
    main()
