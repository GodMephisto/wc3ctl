"""Reading a wc3watch recording, and the numbers a report is built from.

frames.csv, samples.csv and events.csv land in a session folder while the game runs. This
module turns them into a session, finds the seconds that dropped and the frames that hitched,
measures a trend per recorded measure and names the windows where handles climbed. It renders
nothing and prints nothing, so the report code in analyze.py can be changed without touching
how a recording is read.
"""
import csv
import os
import re
import statistics
import sys
from collections import Counter, defaultdict
from datetime import datetime, timedelta


HEAT_BITS = 0x08 | 0x20 | 0x40      # hardware slowdown, software thermal, hardware thermal
POWER_BIT = 0x04                    # software power cap
DROP_RATIO = 0.75
HITCH_MS = 50.0

# measure key, label, unit, and the units per minute a steady rise has to clear to read as a leak.
TREND_MEASURES = (
    ("wc3_priv_mb", "game memory", "MB", 5),
    ("wc3_handles", "game handles", "", 20),
    ("browser_ws_mb", "menu browser memory", "MB", 5),
    ("gpu_temp", "GPU temperature", "C", 0.5),
    ("memcomp_mb", "Windows memory compression", "MB", 5),
)

GC_KIND = "gc_probe"
HANDLE_WINDOW_S = 60.0
HANDLE_MIN_RISE = 20.0
STEADY_R = 0.8


def ptime(s):
    """Parse 'YYYY-MM-DD HH:MM:SS[.fraction]' with any number of fraction digits."""
    s = s.strip()
    if "." in s:
        head, frac = s.split(".", 1)
        s = head + "." + (frac + "000000")[:6]
        return datetime.strptime(s, "%Y-%m-%d %H:%M:%S.%f")
    return datetime.strptime(s, "%Y-%m-%d %H:%M:%S")


def num(x, default=None):
    try:
        return float(x)
    except (TypeError, ValueError):
        return default


def load_csv(path):
    if not os.path.exists(path) or os.path.getsize(path) == 0:
        return []
    with open(path, encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def load_frames(path):
    rows = load_csv(path)
    if not rows:
        return [], None
    tcol = "CPUStartDateTime" if "CPUStartDateTime" in rows[0] else "TimeInDateTime"
    by_chain = Counter(r.get("SwapChainAddress") for r in rows)
    chain = by_chain.most_common(1)[0][0]          # the game's main swap chain
    frames, unreadable = [], 0
    for r in rows:
        if r.get("SwapChainAddress") != chain:
            continue
        ft = num(r.get("MsBetweenPresents"))
        if ft is None or ft <= 0:
            continue
        try:
            t = ptime(r[tcol])
        except (KeyError, ValueError):
            unreadable += 1
            continue
        frames.append({
            "t": t, "ft": ft,
            "cpu": num(r.get("MsCPUBusy"), 0.0), "gpu": num(r.get("MsGPUBusy"), 0.0),
            "mode": r.get("PresentMode", ""),
        })
    if unreadable:
        print("%d frame row(s) had no readable timestamp" % unreadable, file=sys.stderr)
    frames.sort(key=lambda f: f["t"])
    return frames, Counter(f["mode"] for f in frames).most_common(1)[0][0] if frames else None


def sec(t):
    return t.replace(microsecond=0)


def median(xs, default=0.0):
    xs = [x for x in xs if x is not None]
    return statistics.median(xs) if xs else default


def slope_per_min(pts):
    """Least squares slope in units per minute, plus the correlation, over (datetime, value)."""
    pts = [(t, v) for t, v in pts if v is not None]
    if len(pts) < 30:
        return None, None
    t0 = pts[0][0]
    xs = [(t - t0).total_seconds() / 60 for t, _ in pts]
    ys = [v for _, v in pts]
    mx, my = statistics.mean(xs), statistics.mean(ys)
    sxx = sum((x - mx) ** 2 for x in xs)
    syy = sum((y - my) ** 2 for y in ys)
    sxy = sum((x - mx) * (y - my) for x, y in zip(xs, ys))
    if sxx == 0 or syy == 0:
        return 0.0, 0.0
    return sxy / sxx, sxy / (sxx * syy) ** 0.5


def parse_top(s):
    """Parse a sample such as 'Discord 23 chrome 11' into [('Discord', 23.0), ...]. In the recorded
    text each name and its value are joined by a colon."""
    out = []
    for tok in (s or "").split():
        if ":" in tok:
            name, _, val = tok.rpartition(":")
            v = num(val)
            if v is not None:
                out.append((name, v))
    return out


def norm_log(msg):
    return re.sub(r"\d+", "N", msg)[:110]


def correct_clock(frames, samples):
    """PresentMon's date_time output is not on the local clock. Measured on 2026-09-26 on a UTC+8
    machine, it wrote local time plus a further 8 hours, so every frame landed on the wrong second
    and every cause was read from the wrong sample. A first version rounded the gap to the start of
    recording, which broke for any capture begun later. Now the candidates are whole multiples of
    the machine's UTC offset, and the one that puts the most frames inside the sampled span wins."""
    if not (frames and samples):
        return None
    off = datetime.now().astimezone().utcoffset().total_seconds()
    lo, hi = samples[0]["_t"], samples[-1]["_t"]
    best, best_n = 0, -1
    for k in (0, 1, -1, 2, -2):
        shift = k * off
        n = sum(1 for f in frames[::50] if lo <= f["t"] - timedelta(seconds=shift) <= hi)
        if n > best_n:
            best, best_n = shift, n
    if best:
        for f in frames:
            f["t"] -= timedelta(seconds=best)
        return "PresentMon's clock was %+.2f h off the local clock and was corrected." % (best / 3600)
    return None


def load_session(folder):
    """Read a session folder into frames, samples and events with parsed timestamps."""
    frames, mode = load_frames(os.path.join(folder, "frames.csv"))
    samples = load_csv(os.path.join(folder, "samples.csv"))
    events = load_csv(os.path.join(folder, "events.csv"))
    for s in samples:
        s["_t"] = ptime(s["time"])
    for e in events:
        e["_t"] = ptime(e["time"])
    note = correct_clock(frames, samples)
    return {"frames": frames, "samples": samples, "events": events, "mode": mode, "clock_note": note}


def last_gc_probe(events):
    """The time of the last forced garbage collection, or None. Lua frees lazily, so the slope is
    measured from here and a baseline recording forces a collection first."""
    ts = [e["_t"] for e in events if e["kind"] == GC_KIND]
    return max(ts) if ts else None


def per_second(frames):
    per = defaultdict(list)
    for f in frames:
        per[sec(f["t"])].append(f)
    secs = sorted(per)
    fps = {s: len(per[s]) for s in secs}
    return per, secs, fps


def detect_flagged(per, secs, fps):
    """Drop and hitch seconds, and the groups seconds within two of each other form."""
    flagged = {}
    for i, s in enumerate(secs):
        # The first and last second of a capture are partial, so their frame counts are not rates.
        if i == 0 or i == len(secs) - 1:
            continue
        window = [fps[x] for x in secs[max(0, i - 60):i]]
        base = median(window, None)
        if base is None or len(window) < 10 or base < 20:
            continue
        fts = [f["ft"] for f in per[s]]
        med_ft = median([f["ft"] for x in secs[max(0, i - 60):i] for f in per[x]], 16.7)
        worst = max(fts)
        if fps[s] < DROP_RATIO * base:
            flagged[s] = ("drop", base)
        elif worst >= max(HITCH_MS, 4 * med_ft):
            flagged[s] = ("hitch", base)
    groups = []
    for s in sorted(flagged):
        if groups and (s - groups[-1][-1]).total_seconds() <= 2:
            groups[-1].append(s)
        else:
            groups.append([s])
    return flagged, groups


def frame_percentiles(frames):
    ft = sorted(f["ft"] for f in frames)
    pct = {"p50": None, "p95": None, "p99": None, "worst": None, "one_low": None}
    n = len(ft)
    if not n:
        return pct
    pct["p50"] = ft[int(0.50 * (n - 1))]
    pct["p95"] = ft[int(0.95 * (n - 1))]
    pct["p99"] = ft[int(0.99 * (n - 1))]
    pct["worst"] = ft[-1]
    if n > 100:
        pct["one_low"] = 1000 / statistics.mean(ft[int(n * 0.99):])
    return pct


def trend_for(samples, key, after=None):
    """(datetime, value) points for one sample key, optionally from a baseline time onward."""
    pts = [(s["_t"], num(s.get(key))) for s in samples if after is None or s["_t"] >= after]
    pts = [(t, v) for t, v in pts if v is not None]
    sl, r = slope_per_min(pts)
    return pts, sl, r


def metrics_from_session(sess, folder):
    """The numbers diff and summary are built from. One place computes them for both."""
    frames, samples, events = sess["frames"], sess["samples"], sess["events"]
    m = {"name": os.path.basename(os.path.normpath(folder)), "folder": folder,
         "mode": sess["mode"], "clock_note": sess["clock_note"],
         "frame_count": len(frames), "sample_count": len(samples), "event_count": len(events),
         "dropped": 0, "hitches": 0, "groups": [], "flagged": {},
         "duration_min": 0.0, "fps_mean": 0.0, "one_low": None,
         "p50": None, "p95": None, "p99": None, "worst": None, "start": None, "end": None}
    if frames:
        per, secs, fps = per_second(frames)
        flagged, groups = detect_flagged(per, secs, fps)
        m["flagged"] = flagged
        m["groups"] = groups
        m["dropped"] = sum(1 for g in groups if any(flagged[s][0] == "drop" for s in g))
        m["hitches"] = len(groups) - m["dropped"]
        m["duration_min"] = (frames[-1]["t"] - frames[0]["t"]).total_seconds() / 60
        m["fps_mean"] = statistics.mean(fps.values())
        m.update(frame_percentiles(frames))
        m["start"] = frames[0]["t"]
        m["end"] = frames[-1]["t"]
    m["gc_times"] = [e["_t"] for e in events if e["kind"] == GC_KIND]
    m["baseline"] = last_gc_probe(events)
    trends = []
    for key, label, unit, leak in TREND_MEASURES:
        pts, sl, r = trend_for(samples, key, m["baseline"])
        vals = [v for _, v in pts]
        trends.append({"key": key, "label": label, "unit": unit, "leak": leak,
                       "slope": sl, "r": r, "first": vals[0] if vals else None,
                       "last": vals[-1] if vals else None, "n": len(vals)})
    m["trends"] = trends
    return m


def session_metrics(folder):
    return metrics_from_session(load_session(folder), folder)


def handle_suspects(samples, events, window_s=HANDLE_WINDOW_S, top=3, min_rise=HANDLE_MIN_RISE):
    """Windows where handles rose fastest, with the events and log lines around each.

    Warcraft III does not expose per-handle-type counters, so the best an external sampler can do is
    time the growth and name what else was happening. A window is window_s seconds, the windows are
    ranked by how many handles appeared in them, and the top non-overlapping ones come back with any
    event or game log line within three seconds of the window."""
    pts = [(s["_t"], num(s.get("wc3_handles"))) for s in samples]
    pts = [(t, v) for t, v in pts if v is not None]
    if len(pts) < 3:
        return []
    cands = []
    i = 0
    for j in range(len(pts)):
        while (pts[j][0] - pts[i][0]).total_seconds() > window_s:
            i += 1
        if j > i:
            cands.append((pts[j][1] - pts[i][1], pts[i][0], pts[j][0], pts[i][1], pts[j][1]))
    cands.sort(key=lambda c: -c[0])
    chosen = []
    for c in cands:
        rise, a, b, _, _ = c
        if rise < min_rise:
            break
        if any(not (b < ca or a > cb) for _, ca, cb, _, _ in chosen):
            continue
        chosen.append(c)
        if len(chosen) >= top:
            break
    chosen.sort(key=lambda c: c[1])
    out = []
    for rise, a, b, va, vb in chosen:
        lo, hi = a - timedelta(seconds=3), b + timedelta(seconds=3)
        near = [e for e in events if lo <= e["_t"] <= hi and e["kind"] != "gamelog"]
        logs = [e for e in events if lo <= e["_t"] <= hi and e["kind"] == "gamelog"]
        out.append({"start": a, "end": b, "rise": rise, "first": va, "last": vb,
                    "events": near,
                    "logs": Counter(norm_log(e["detail"]) for e in logs).most_common(3)})
    return out
