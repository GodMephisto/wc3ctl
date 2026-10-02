# Is the process pool still earning its keep now that every diff is linear?
#
# Worth asking rather than assuming. On Windows a worker is spawned rather than forked, so
# each one re-imports the module tree, and that fixed cost can easily exceed a few seconds
# of actual work spread across 75 small files.
import sys
import time
import pathlib
from concurrent.futures import ProcessPoolExecutor

HERE = pathlib.Path(__file__).parent
sys.path.insert(0, str(HERE))
import fastcache
from turbo import one, ext, BEHAVIOUR

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def main():
    st = fastcache.state()
    old, new = st["builds"]["2.0.4"]["files"], st["builds"]["3.0.0"]["files"]
    core = [p for p in sorted(old.keys() & new.keys())
            if ext(p) in BEHAVIOUR and old[p][0] != new[p][0] and "_locales" not in p.lower()]
    blobs = fastcache.many([(b, p) for p in core for b in ("2.0.4", "3.0.0")], workers=32)
    jobs = [(p, blobs[("2.0.4", p)][0], blobs[("3.0.0", p)][0]) for p in core
            if blobs[("2.0.4", p)][0] and blobs[("3.0.0", p)][0]]

    t = time.perf_counter()
    for j in jobs:
        one(j)
    serial = time.perf_counter() - t

    t = time.perf_counter()
    with ProcessPoolExecutor() as pool:
        list(pool.map(one, jobs, chunksize=2))
    par = time.perf_counter() - t

    print(f"  {len(jobs)} files")
    print(f"  serial          {serial:.2f}s")
    print(f"  process pool    {par:.2f}s")
    print("  verdict         " + ("pool helps" if par < serial
                                  else "SERIAL WINS, spawn overhead exceeds the work"))


if __name__ == "__main__":
    main()
