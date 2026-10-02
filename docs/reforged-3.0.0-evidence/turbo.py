# The whole behaviour changelog, as fast as it goes.
#
# Everything quadratic is gone. SLK gets a cell diff, section files get a key diff, source
# files get a function diff, and anything else gets a line multiset. All of them are one
# pass. The remaining work is CPU bound and independent per file, so it runs across cores.
import pathlib
import sys
import time
from concurrent.futures import ProcessPoolExecutor

HERE = pathlib.Path(__file__).parent
sys.path.insert(0, str(HERE))

BEHAVIOUR = {".j", ".lua", ".slk", ".txt", ".fdf", ".ai", ".ini", ".json"}


def ext(p):
    base = p.rsplit("/", 1)[-1]
    return ("." + base.rsplit(".", 1)[1].lower()) if "." in base else ""


def one(job):
    """Runs in a worker process. Takes decoded bytes so workers never touch the network."""
    import slkdiff
    import structdiff
    path, a, b = job
    ta = a.decode("utf-8", errors="replace")
    tb = b.decode("utf-8", errors="replace")
    head = f"  bytes       {len(a):,} -> {len(b):,}"
    try:
        if path.lower().endswith(".slk"):
            return path, head + "\n" + slkdiff.diff(ta, tb, path, limit=25)
        return path, head + "\n" + structdiff.diff(path, ta, tb, limit=25)
    except Exception as e:
        return path, head + f"\n  diff failed {type(e).__name__} {e}"


def main():
    import fastcache
    t0 = time.perf_counter()
    st = fastcache.state()
    old, new = st["builds"]["2.0.4"]["files"], st["builds"]["3.0.0"]["files"]
    t_setup = time.perf_counter()

    changed = sorted(p for p in old.keys() & new.keys()
                     if ext(p) in BEHAVIOUR and old[p][0] != new[p][0])
    core = [p for p in changed if "_locales" not in p.lower()]
    loc = [p for p in changed if "_locales" in p.lower()]
    added = sorted(p for p in new.keys() - old.keys() if ext(p) in BEHAVIOUR)
    removed = sorted(p for p in old.keys() - new.keys() if ext(p) in BEHAVIOUR)

    blobs = fastcache.many([(bl, p) for p in core for bl in ("2.0.4", "3.0.0")], workers=32)
    t_fetch = time.perf_counter()

    jobs = [(p, blobs[("2.0.4", p)][0], blobs[("3.0.0", p)][0]) for p in core
            if blobs[("2.0.4", p)][0] and blobs[("3.0.0", p)][0]]
    with ProcessPoolExecutor() as pool:
        results = dict(pool.map(one, jobs, chunksize=2))
    t_diff = time.perf_counter()

    print("=" * 78)
    print("EVERY CHANGED BEHAVIOUR FILE, 2.0.4.23745 -> 3.0.0.24268")
    print("=" * 78)
    print(f"  modified {len(changed):,}  (core {len(core):,}, locale {len(loc):,})")
    print(f"  added {len(added):,}   removed {len(removed):,}")
    print()
    for p in core:
        print("=" * 78)
        print(p)
        print("=" * 78)
        print(results.get(p, "  (not fetched)"))
        print()
    print("=" * 78)
    print(f"LOCALE STRING TABLES CHANGED ({len(loc)}), listed not diffed")
    print("=" * 78)
    for p in loc:
        print("  " + p)
    print()
    print(f"ADDED ({len(added)})")
    for p in added:
        print("  + " + p)
    print(f"REMOVED ({len(removed)})")
    for p in removed:
        print("  - " + p)
    print()
    print("=" * 78)
    print(f"setup {t_setup - t0:.2f}s   fetch {t_fetch - t_setup:.2f}s   "
          f"diff {t_diff - t_fetch:.2f}s   TOTAL {t_diff - t0:.2f}s")


if __name__ == "__main__":
    main()
