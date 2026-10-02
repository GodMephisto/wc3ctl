# Content diff for EVERY behaviour file that changed between the two builds, not just the
# hand-picked ones. 388 modified plus 7 added plus 4 removed.
#
# Locale string tables are separated out rather than dropped, because they are most of the
# count and almost none of the meaning, and folding them in would hide the real changes.
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).parent
sys.path.insert(0, str(HERE))
import fastcache
import slkdiff
from report import text_diff

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

BEHAVIOUR = {".j", ".lua", ".slk", ".txt", ".fdf", ".ai", ".ini", ".json"}


def ext(p):
    base = p.rsplit("/", 1)[-1]
    return ("." + base.rsplit(".", 1)[1].lower()) if "." in base else ""


t0 = time.perf_counter()
st = fastcache.state()
old = st["builds"]["2.0.4"]["files"]
new = st["builds"]["3.0.0"]["files"]

added = sorted(p for p in new.keys() - old.keys() if ext(p) in BEHAVIOUR)
removed = sorted(p for p in old.keys() - new.keys() if ext(p) in BEHAVIOUR)
changed = sorted(p for p in old.keys() & new.keys()
                 if ext(p) in BEHAVIOUR and old[p][0] != new[p][0])

# A locale table is a translation, so it is counted and listed but not diffed line by line.
is_locale = lambda p: "_locales" in p.lower()
core = [p for p in changed if not is_locale(p)]
loc = [p for p in changed if is_locale(p)]

print("=" * 78)
print("EVERY CHANGED BEHAVIOUR FILE, 2.0.4.23745 -> 3.0.0.24268")
print("=" * 78)
print(f"  added                 {len(added):,}")
print(f"  removed               {len(removed):,}")
print(f"  modified              {len(changed):,}")
print(f"     core               {len(core):,}")
print(f"     locale strings     {len(loc):,}")
print()

pairs = [(b, p) for p in core for b in ("2.0.4", "3.0.0")]
blobs = fastcache.many(pairs, workers=24)
t_fetch = time.perf_counter()
print(f"  fetched {len(pairs)} blobs in {t_fetch - t0:.1f}s")
print()

fails = []
for i, path in enumerate(core, 1):
    a, ea = blobs[("2.0.4", path)]
    b, eb = blobs[("3.0.0", path)]
    print("=" * 78)
    print(f"[{i}/{len(core)}] {path}")
    print("=" * 78)
    if a is None or b is None:
        print(f"  2.0.4  {ea or 'ok'}")
        print(f"  3.0.0  {eb or 'ok'}")
        fails.append(path)
        print()
        continue
    print(f"  bytes       {len(a):,} -> {len(b):,}")
    try:
        if path.lower().endswith(".slk"):
            print(slkdiff.diff(a.decode("utf-8", errors="replace"),
                               b.decode("utf-8", errors="replace"), path, limit=25))
        else:
            print(text_diff(a, b, limit=12))
    except Exception as e:
        print(f"  diff failed {type(e).__name__} {e}")
        fails.append(path)
    print()
    sys.stdout.flush()

print("=" * 78)
print("LOCALE STRING TABLES CHANGED, listed not diffed")
print("=" * 78)
for p in loc:
    print("  " + p)
print()
print("=" * 78)
print(f"ADDED FILES ({len(added)})")
for p in added:
    print("  + " + p)
print(f"REMOVED FILES ({len(removed)})")
for p in removed:
    print("  - " + p)
print()
print(f"failures {len(fails)}  total {time.perf_counter() - t0:.1f}s")
