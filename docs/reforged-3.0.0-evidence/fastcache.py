# Makes the build walk fast by paying the parse cost once instead of once per run.
#
# Every run was rebuilding the same three structures from scratch. The archive map re-reads
# 40 MB of indices, and each build re-decodes a multi-megabyte BLTE encoding table and then
# walks a thousand-odd 4 KB pages to rebuild a map that never changes for a fixed build.
# None of that depends on which file is being asked for, so it is pickled and reloaded.
#
# Fetches are also issued in parallel, because each file is one small ranged HTTP request
# and the run was spending its time waiting on round trips rather than on CPU.
# On pickle. This file is written and read only by this script, in the session scratchpad,
# from data this script derived itself. It never loads a pickle from anywhere else, so the
# arbitrary-execution risk of untrusted pickles does not apply. It is used rather than JSON
# because the payload is a few million binary keys and tuples, which JSON round trips far
# more slowly and much larger. Delete fastcache.pickle to force a rebuild.
import pathlib
import pickle
import sys
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor

HERE = pathlib.Path(__file__).parent
sys.path.insert(0, str(HERE))
from tact import CDN, blte_decode, fetch, parse_build_config, parse_encoding
from archidx import build_map

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

CFG = pathlib.Path(r"C:\Warcraft III\Data\config")
BUILDS = {
    "2.0.4": CFG / "9a/94/9a94ff7d25781db6c09190cd69d52458",
    "3.0.0": CFG / "3a/9d/3a9d8f26806936764d2d9ad526a65e04",
}
STORE = HERE / "fastcache.pickle"

_state = None


def _build_state(report=True):
    t0 = time.perf_counter()
    arch = build_map(verbose=False)
    t1 = time.perf_counter()
    builds = {}
    for label, path in BUILDS.items():
        cfg = parse_build_config(path.read_text(encoding="utf-8"))
        table = parse_encoding(blte_decode(fetch(cfg["encoding"][1])))
        root = blte_decode(fetch(table[cfg["root"][0]][0])).decode("utf-8", errors="replace")
        files = {}
        for line in root.split("\n"):
            parts = line.split("|")
            if len(parts) >= 2 and parts[1]:
                files.setdefault(parts[0], parts[1])
        # Collapse to exactly what a lookup needs, path -> (ekey, decoded size). Keeping the
        # full encoding table would make the pickle several times larger for no benefit.
        resolved = {}
        for p, ck in files.items():
            hit = table.get(ck)
            if hit:
                resolved[p] = hit
        builds[label] = dict(cfg=cfg, files=resolved)
    t2 = time.perf_counter()
    if report:
        counts = ", ".join(f"{k} {len(v['files']):,}" for k, v in builds.items())
        print(f"  archive indices  {t1 - t0:6.2f}s  ({len(arch):,} keys)", file=sys.stderr)
        print(f"  encoding + root  {t2 - t1:6.2f}s  ({counts})", file=sys.stderr)
    return dict(arch=arch, builds=builds)


def state():
    global _state
    if _state is not None:
        return _state
    if STORE.exists():
        t0 = time.perf_counter()
        _state = pickle.loads(STORE.read_bytes())
        print(f"  cache load       {time.perf_counter() - t0:6.2f}s", file=sys.stderr)
        return _state
    _state = _build_state()
    t0 = time.perf_counter()
    STORE.write_bytes(pickle.dumps(_state, protocol=5))
    print(f"  cache write      {time.perf_counter() - t0:6.2f}s  "
          f"({STORE.stat().st_size / 1024**2:.1f} MB)", file=sys.stderr)
    return _state


def raw(label, path):
    """Bytes of one file from one build, or (None, reason)."""
    st = state()
    hit = st["builds"][label]["files"].get(path)
    if hit is None:
        return None, "not present in this build"
    ekey = hit[0]
    loc = st["arch"].get(ekey)
    if loc is None:
        return None, "ekey not in any archive index"
    archive, off, size = loc
    url = f"{CDN}/data/{archive[0:2]}/{archive[2:4]}/{archive}"
    req = urllib.request.Request(url, headers={"Range": f"bytes={off}-{off + size - 1}"})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return blte_decode(r.read()), None
    except Exception as e:
        return None, f"{type(e).__name__} {e}"


def many(pairs, workers=16):
    """Fetch (label, path) pairs concurrently. Returns {(label, path): (bytes, err)}."""
    state()
    with ThreadPoolExecutor(max_workers=workers) as pool:
        futs = {pool.submit(raw, lbl, p): (lbl, p) for lbl, p in pairs}
        return {futs[f]: f.result() for f in futs}
