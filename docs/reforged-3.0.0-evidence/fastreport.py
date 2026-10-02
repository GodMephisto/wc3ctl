# The whole 2.0.4 to 3.0.0 changelog, fast.
#
# Three things made the first version slow, and only one of them was the network.
#   1. difflib on a 279,377 line SLK is quadratic and never finished. SLK is a sparse grid
#      whose record order carries no meaning, so it gets a cell diff keyed by object id and
#      field name instead. That is both correct and roughly instant.
#   2. Every run re-read 40 MB of archive indices and re-decoded two multi-megabyte encoding
#      tables to rebuild maps that cannot change for a fixed build. Now cached.
#   3. Files were fetched one ranged request at a time, so the run sat waiting on round
#      trips. Now issued in parallel.
import sys
import time
import pathlib

HERE = pathlib.Path(__file__).parent
sys.path.insert(0, str(HERE))
import fastcache
import slkdiff
from report import TARGETS, text_diff

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

t_start = time.perf_counter()
fastcache.state()
t_setup = time.perf_counter()

blobs = fastcache.many([(b, p) for p in TARGETS for b in ("2.0.4", "3.0.0")])
t_fetch = time.perf_counter()

for path in TARGETS:
    a, ea = blobs[("2.0.4", path)]
    b, eb = blobs[("3.0.0", path)]
    print("=" * 78)
    print(path)
    print("=" * 78)
    if a is None or b is None:
        print(f"  2.0.4  {ea or f'{len(a):,} bytes'}")
        print(f"  3.0.0  {eb or f'{len(b):,} bytes'}")
        print()
        continue
    print(f"  bytes       {len(a):,} -> {len(b):,}")
    if path.lower().endswith(".slk"):
        print(slkdiff.diff(a.decode("utf-8", errors="replace"),
                           b.decode("utf-8", errors="replace"), path))
    else:
        print(text_diff(a, b))
    print()

t_end = time.perf_counter()
print("=" * 78)
print(f"setup {t_setup - t_start:.2f}s   fetch {t_fetch - t_setup:.2f}s   "
      f"diff {t_end - t_fetch:.2f}s   TOTAL {t_end - t_start:.2f}s")
