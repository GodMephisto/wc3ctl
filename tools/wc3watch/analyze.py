"""wc3watch analyser. Reads recorded sessions and explains frame rate drops, memory and handles.

Who runs it. wc3watch.ps1 calls it when a recording ends, or a person runs it by hand on an old
session. It reads frames.csv, samples.csv and events.csv and writes report.md into the same folder.
Standard library only.

The commands.
    python analyze.py <session folder>          write report.md and print the header
    python analyze.py summary <session>         print a one screen summary
    python analyze.py chart <session>           write chart.svg beside the data
    python analyze.py diff <sessionA> <sessionB>  compare two sessions, A minus B is the change

How a drop is found. Frames are grouped into seconds. A second counts as a drop when the game had
focus and its frame rate fell below 75 percent of the median of the minute before it. A single frame
of at least 50 ms, and at least four times the minute's median frame time, counts as a hitch. Seconds
within one second of each other merge into one event.

How a cause is named. Each frame records how long the CPU and the GPU were busy on it, so an event
is first classed as CPU bound, GPU bound, or neither (waiting on present or the driver). The
one-second samples and the game's own log around the event then say why. Every rule below prints
the numbers it used, so a verdict can be checked rather than trusted. The rules are heuristics and
the report says so.

Memory and handles. A least squares slope over the session, in units per minute, with its correlation,
flags a steady rise as a leak. Lua maps free memory lazily, so a recorder marks a forced garbage
collection as a gc_probe event and the slope is measured from that point. Warcraft III does not expose
per-handle-type counters to an external sampler, so handle growth is timed and correlated with the
events and log lines around it instead of being attributed to a handle kind. See README.md.
"""
import csv
import os
import re
import statistics
import sys
from collections import Counter, defaultdict
from datetime import datetime, timedelta

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

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
    frames = []
    for r in rows:
        if r.get("SwapChainAddress") != chain:
            continue
        ft = num(r.get("MsBetweenPresents"))
        if ft is None or ft <= 0:
            continue
        try:
            t = ptime(r[tcol])
        except (KeyError, ValueError):
            continue
        frames.append({
            "t": t, "ft": ft,
            "cpu": num(r.get("MsCPUBusy"), 0.0), "gpu": num(r.get("MsGPUBusy"), 0.0),
            "mode": r.get("PresentMode", ""),
        })
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


def _esc(s):
    return str(s).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def _near_text(s):
    parts = ["%s %s" % (e["kind"], e["_t"].strftime("%H:%M:%S")) for e in s["events"]]
    parts += ["%d x %s" % (n, msg) for msg, n in s["logs"]]
    return ", ".join(parts) if parts else "no event nearby"


def _n(x, fmt="%.1f"):
    return "n/a" if x is None else fmt % x


def _signed(x, fmt="%.1f"):
    return "" if x is None else ("+" if x >= 0 else "") + fmt % x


def render_chart(folder):
    """An SVG line chart of wc3_priv_mb and wc3_handles over time, with drop bands and GC marks."""
    sess = load_session(folder)
    samples, events, frames = sess["samples"], sess["events"], sess["frames"]
    mem = [(s["_t"], num(s.get("wc3_priv_mb"))) for s in samples]
    mem = [(t, v) for t, v in mem if v is not None]
    hand = [(s["_t"], num(s.get("wc3_handles"))) for s in samples]
    hand = [(t, v) for t, v in hand if v is not None]
    W, H = 960, 540
    L, R, T, B = 84, 96, 60, 64
    pw, ph = W - L - R, H - T - B
    name = _esc(os.path.basename(os.path.normpath(folder)))
    out = []
    w = out.append
    w('<?xml version="1.0" encoding="UTF-8"?>')
    w('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 %d %d">' % (W, H, W, H))
    w('<rect x="0" y="0" width="%d" height="%d" fill="#ffffff"/>' % (W, H))
    w('<text x="%d" y="32" font-family="sans-serif" font-size="18" fill="#111111">wc3watch, %s</text>' % (L, name))
    allpts = mem + hand
    if not allpts:
        w('<text x="%d" y="%d" font-family="sans-serif" font-size="16" fill="#666666">No samples were recorded.</text>'
          % (L, T + ph // 2))
        w('</svg>')
        return "\n".join(out) + "\n"
    t0 = min(t for t, _ in allpts)
    t1 = max(t for t, _ in allpts)
    span = max(1.0, (t1 - t0).total_seconds())
    mem_lo, mem_hi = (min(v for _, v in mem), max(v for _, v in mem)) if mem else (0.0, 1.0)
    hand_lo, hand_hi = (min(v for _, v in hand), max(v for _, v in hand)) if hand else (0.0, 1.0)

    def xmap(t):
        return L + pw * ((t - t0).total_seconds() / span)

    def ymap(v, lo, hi):
        if hi <= lo:
            hi = lo + 1.0
        return T + ph * (1 - (v - lo) / (hi - lo))

    # Drop and hitch bands, drawn first so the series sit on top.
    if frames:
        per, secs, fps = per_second(frames)
        flagged, groups = detect_flagged(per, secs, fps)
        for g in groups:
            x0 = xmap(g[0])
            x1 = xmap(g[-1] + timedelta(seconds=1))
            w('<rect class="drop-band" x="%.1f" y="%d" width="%.1f" height="%d" fill="#d73a49" opacity="0.14"/>'
              % (x0, T, max(1.0, x1 - x0), ph))

    # Horizontal gridlines and the two value axes.
    for k in range(5):
        frac = k / 4
        y = T + ph * frac
        w('<line x1="%d" y1="%.1f" x2="%d" y2="%.1f" stroke="#e4e4e4" stroke-width="1"/>' % (L, y, L + pw, y))
        mv = mem_hi - (mem_hi - mem_lo) * frac
        w('<text x="%d" y="%.1f" font-family="sans-serif" font-size="11" fill="#1f6feb" text-anchor="end">%.0f</text>'
          % (L - 8, y + 4, mv))
        hv = hand_hi - (hand_hi - hand_lo) * frac
        w('<text x="%d" y="%.1f" font-family="sans-serif" font-size="11" fill="#e36209" text-anchor="start">%.0f</text>'
          % (L + pw + 8, y + 4, hv))
    w('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="#333333" stroke-width="1.5"/>' % (L, T, L, T + ph))
    w('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="#333333" stroke-width="1.5"/>' % (L, T + ph, L + pw, T + ph))

    # Time labels.
    for k in range(5):
        frac = k / 4
        x = L + pw * frac
        t = t0 + timedelta(seconds=span * frac)
        anchor = "start" if k == 0 else "end" if k == 4 else "middle"
        w('<text x="%.1f" y="%d" font-family="sans-serif" font-size="11" fill="#333333" text-anchor="%s">%s</text>'
          % (x, T + ph + 20, anchor, t.strftime("%H:%M:%S")))

    if mem:
        pts = " ".join("%.1f,%.1f" % (xmap(t), ymap(v, mem_lo, mem_hi)) for t, v in mem)
        w('<polyline class="series-mem" fill="none" stroke="#1f6feb" stroke-width="1.8" points="%s"/>' % pts)
    if hand:
        pts = " ".join("%.1f,%.1f" % (xmap(t), ymap(v, hand_lo, hand_hi)) for t, v in hand)
        w('<polyline class="series-hand" fill="none" stroke="#e36209" stroke-width="1.8" points="%s"/>' % pts)

    for e in events:
        if e["kind"] == GC_KIND:
            x = xmap(e["_t"])
            w('<line class="gc-probe" x1="%.1f" y1="%d" x2="%.1f" y2="%d" stroke="#22863a" '
              'stroke-width="1.4" stroke-dasharray="5 4"/>' % (x, T, x, T + ph))
            w('<text x="%.1f" y="%d" font-family="sans-serif" font-size="10" fill="#22863a">GC</text>'
              % (x + 3, T + 12))

    # Legend.
    lx, ly = L + pw - 210, T + 4
    w('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="#1f6feb" stroke-width="2"/>' % (lx, ly, lx + 22, ly))
    w('<text x="%d" y="%d" font-family="sans-serif" font-size="12" fill="#111111">game memory MB</text>' % (lx + 28, ly + 4))
    w('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="#e36209" stroke-width="2"/>' % (lx, ly + 18, lx + 22, ly + 18))
    w('<text x="%d" y="%d" font-family="sans-serif" font-size="12" fill="#111111">game handles</text>' % (lx + 28, ly + 22))
    w('<rect x="%d" y="%d" width="10" height="10" fill="#d73a49" opacity="0.4"/>' % (lx, ly + 34))
    w('<text x="%d" y="%d" font-family="sans-serif" font-size="12" fill="#111111">drop or hitch</text>' % (lx + 16, ly + 43))
    w('</svg>')
    return "\n".join(out) + "\n"


def render_summary(folder):
    """One screen. Length, measures with their slope and steady flag, drops, and suspect windows."""
    sess = load_session(folder)
    m = metrics_from_session(sess, folder)
    out = []
    w = out.append
    w("wc3watch summary  %s" % m["name"])
    w("")
    if not m["frame_count"]:
        w("  No frames were recorded, so there is nothing to summarise.")
        return "\n".join(out)
    w("  length     %.1f min, %s to %s" % (m["duration_min"], m["start"].strftime("%Y-%m-%d %H:%M:%S"),
                                           m["end"].strftime("%H:%M:%S")))
    low = ", 1%% low %.0f fps" % m["one_low"] if m["one_low"] else ""
    w("  frames     %d at %.0f fps average" % (m["frame_count"], m["fps_mean"]))
    w("  frame time p50 %.1f ms, p95 %.1f ms, p99 %.1f ms, worst %.0f ms%s"
      % (m["p50"], m["p95"], m["p99"], m["worst"], low))
    w("  drops      %d drop, %d hitch" % (m["dropped"], m["hitches"]))
    if m["gc_times"]:
        w("  gc probe   %d recorded, last %s" % (len(m["gc_times"]), max(m["gc_times"]).strftime("%H:%M:%S")))
    else:
        w("  gc probe   none recorded")
    if m["clock_note"]:
        w("  clock      %s" % m["clock_note"])
    w("")
    w("  measure                    start       end   per minute  steady  reading")
    for t in m["trends"]:
        if t["n"] == 0:
            continue
        if t["slope"] is None:
            w("  %-24s %9.0f %9.0f   too short to tell" % (t["label"], t["first"], t["last"]))
            continue
        steady = t["r"] is not None and t["r"] >= STEADY_R
        reading = "looks like a leak" if (steady and t["slope"] >= t["leak"]) else ""
        w("  %-24s %9.0f %9.0f   %+9.1f  %-6s  %s"
          % (t["label"], t["first"], t["last"], t["slope"], "yes" if steady else "no", reading))
    w("")
    suspects = handle_suspects(sess["samples"], sess["events"])
    if suspects:
        w("  suspect windows for handle growth")
        for s in suspects:
            w("    %s to %s  %+.0f handles  %s"
              % (s["start"].strftime("%H:%M:%S"), s["end"].strftime("%H:%M:%S"), s["rise"], _near_text(s)))
    else:
        w("  handle growth  no window showed a clear rise")
    return "\n".join(out)


def render_diff(folder_a, folder_b):
    """Two sessions side by side, delta is B minus A."""
    A = session_metrics(folder_a)
    B = session_metrics(folder_b)
    out = []
    w = out.append
    w("# wc3watch diff")
    w("")
    w("A is %s, B is %s. The delta is B minus A, so a negative number is B having less of it." % (A["name"], B["name"]))
    w("")
    w("| measure | A | B | delta |")
    w("|---|---|---|---|")
    rows = [
        ("recorded, min", A["duration_min"], B["duration_min"], "%.1f"),
        ("frames", A["frame_count"], B["frame_count"], "%.0f"),
        ("average fps", A["fps_mean"], B["fps_mean"], "%.1f"),
        ("frame p50, ms", A["p50"], B["p50"], "%.1f"),
        ("frame p95, ms", A["p95"], B["p95"], "%.1f"),
        ("frame p99, ms", A["p99"], B["p99"], "%.1f"),
        ("worst frame, ms", A["worst"], B["worst"], "%.1f"),
        ("1% low fps", A["one_low"], B["one_low"], "%.1f"),
        ("drops", A["dropped"], B["dropped"], "%.0f"),
        ("hitches", A["hitches"], B["hitches"], "%.0f"),
    ]
    for label, a, b, fmt in rows:
        delta = (b - a) if (a is not None and b is not None) else None
        w("| %s | %s | %s | %s |" % (label, _n(a, fmt), _n(b, fmt), _signed(delta, fmt)))
    w("")
    w("## Trends, change per minute")
    w("")
    w("| measure | A | B | delta | steady A | steady B | reading |")
    w("|---|---|---|---|---|---|---|")
    for ta, tb in zip(A["trends"], B["trends"]):
        if ta["slope"] is None or tb["slope"] is None:
            w("| %s | too short | too short | | | | |" % ta["label"])
            continue
        sa = ta["r"] is not None and ta["r"] >= STEADY_R
        sb = tb["r"] is not None and tb["r"] >= STEADY_R
        reading = ""
        if sa and ta["slope"] >= ta["leak"] and (not sb or abs(tb["slope"]) < abs(ta["slope"]) * 0.5):
            reading = "A rises, B is flatter, consistent with the change fixing it"
        elif sb and tb["slope"] >= tb["leak"]:
            reading = "B still rises, the change did not stop it"
        w("| %s | %+.1f %s | %+.1f %s | %+.1f | %s | %s | %s |"
          % (ta["label"], ta["slope"], ta["unit"], tb["slope"], tb["unit"], tb["slope"] - ta["slope"],
             "yes" if sa else "no", "yes" if sb else "no", reading))
    w("")
    for m in (A, B):
        if m["gc_times"]:
            w("%s recorded a forced garbage collection at %s, so its memory and handle slopes are measured "
              "from that point." % (m["name"], max(m["gc_times"]).strftime("%H:%M:%S")))
    w("")
    w("## Handle growth, top window per session")
    w("")
    w("Warcraft III does not expose per-handle-type counters, so this times the growth and names what "
      "was around it.")
    w("")
    w("| session | window | rise | near |")
    w("|---|---|---|---|")
    for m in (A, B):
        sess = load_session(m["folder"])
        sus = handle_suspects(sess["samples"], sess["events"], top=1)
        if sus:
            s = sus[0]
            w("| %s | %s to %s | %+.0f | %s |" % (m["name"], s["start"].strftime("%H:%M:%S"),
                                                 s["end"].strftime("%H:%M:%S"), s["rise"], _near_text(s)))
        else:
            w("| %s | none | | |" % m["name"])
    w("")
    return "\n".join(out)


def analyse(folder):
    sess = load_session(folder)
    frames, samples, events, mode, clock_note = (sess["frames"], sess["samples"], sess["events"],
                                                 sess["mode"], sess["clock_note"])
    by_sec = {sec(s["_t"]): s for s in samples}
    logs = [e for e in events if e["kind"] == "gamelog"]
    baseline = last_gc_probe(events)

    out = []
    w = out.append
    w("# wc3watch report, session %s" % os.path.basename(os.path.normpath(folder)))
    w("")
    if not frames:
        w("No frames were recorded. Either the game never drew a frame while recording, or PresentMon "
          "was not running with administrator rights.")
        return "\n".join(out)

    # Per second frame statistics.
    per, secs, fps = per_second(frames)

    def focused(s):
        row = by_sec.get(s) or by_sec.get(s - timedelta(seconds=1))
        return row is None or (row.get("focus") == "1" and row.get("minimized") != "1")

    # Which window had keyboard focus at a given moment, from the recorder's focus events. A player
    # can have the game on screen while another window holds focus, a second monitor or the last
    # window clicked, so unfocused seconds are analysed like any other and labelled, never dropped.
    focus_marks = sorted((e["_t"], e["detail"].replace("foreground ", ""))
                         for e in events if e["kind"] in ("focus_lost", "focus_gained"))

    def foreground_at(t):
        name = None
        for ft, who in focus_marks:
            if ft > t:
                break
            name = who
        return name

    all_ft = [f["ft"] for f in frames]
    dur = (frames[-1]["t"] - frames[0]["t"]).total_seconds()
    focused_secs = [s for s in secs if focused(s)]
    ffps = [fps[s] for s in focused_secs] or [0]
    srt = sorted(all_ft)
    one_low = 1000 / statistics.mean(srt[int(len(srt) * 0.99):]) if len(srt) > 100 else None

    # Drop and hitch seconds.
    flagged, groups = detect_flagged(per, secs, fps)

    def rows_between(a, b):
        return [r for r in samples if a <= r["_t"] <= b]

    def field(rows, k):
        return [num(r.get(k)) for r in rows if num(r.get(k)) is not None]

    base_rows_cache = {}

    def baseline_rows(start):
        key = start.replace(second=0)
        if key not in base_rows_cache:
            lo = start - timedelta(seconds=90)
            base_rows_cache[key] = [r for r in rows_between(lo, start - timedelta(seconds=3))
                                    if sec(r["_t"]) not in flagged and r.get("focus") == "1"]
        return base_rows_cache[key]

    drops = []
    for grp in groups:
        a, b = grp[0], grp[-1]
        kind = "drop" if any(flagged[s][0] == "drop" for s in grp) else "hitch"
        base_fps = median([flagged[s][1] for s in grp])
        fr = [f for s in grp for f in per[s]]
        low_fps = min(fps[s] for s in grp)
        worst = max(f["ft"] for f in fr)
        cpu_ratio = median([f["cpu"] / f["ft"] for f in fr])
        gpu_ratio = median([f["gpu"] / f["ft"] for f in fr])
        now = rows_between(a - timedelta(seconds=2), b + timedelta(seconds=1))
        base = baseline_rows(a)

        def m(rows, k):
            return median(field(rows, k), None)

        causes, facts = [], []
        if not all(focused(s) for s in grp):
            who = foreground_at(a) or "another window"
            causes.append(("game not focused", "%s had keyboard focus, so Windows ranked the game below it "
                           "and the game's own background frame cap can apply" % who))
        bound = "GPU" if gpu_ratio >= 0.8 else "CPU" if cpu_ratio >= 0.8 else "neither"
        facts.append("frames %s bound (CPU busy %d%%, GPU busy %d%% of frame time)"
                     % (bound, 100 * cpu_ratio, 100 * gpu_ratio))

        thr = 0
        for r in now:
            try:
                thr |= int(r.get("gpu_throttle") or "0", 16)
            except ValueError:
                pass
        t_now, t_base = m(now, "gpu_temp"), m(base, "gpu_temp")
        c_now, c_base = m(now, "gpu_clock"), m(base, "gpu_clock")
        u_now = m(now, "gpu_util")
        if t_now is not None:
            facts.append("GPU %s C, clock %s MHz (was %s), load %s%%"
                         % (int(t_now), int(c_now or 0), int(c_base or 0), int(u_now or 0)))

        if bound == "GPU":
            if thr & HEAT_BITS or (t_now or 0) >= 87 or (
                    c_base and c_now and c_now <= 0.8 * c_base and (u_now or 0) >= 80):
                causes.append(("heat", "the GPU was hot and slowed its own clock (throttle 0x%x)" % thr))
            if thr & POWER_BIT:
                causes.append(("power limit", "the GPU hit its power limit (throttle 0x%x)" % thr))
            others = [(n, v) for r in now for n, v in parse_top(r.get("top_gpu"))]
            heavy = sorted({n: v for n, v in others if v >= 10}.items(), key=lambda x: -x[1])
            if heavy:
                causes.append(("background GPU app", ", ".join("%s %d%%" % x for x in heavy[:3])))
            if not causes:
                causes.append(("heavier scene", "the GPU had more to draw, many units or effects on screen"))

        if bound == "CPU":
            mt_now = m(now, "wc3_main_thread")
            if mt_now is not None:
                facts.append("game's busiest thread %d%% of a core" % mt_now)
            if (mt_now or 0) >= 85:
                causes.append(("game simulation", "the game's main thread was saturated, so triggers, units "
                               "or effects in the map cost more than one core could do"))
            p_now, p_base = m(now, "cpu_perf_pct"), m(base, "cpu_perf_pct")
            if p_now is not None and p_base and p_now <= 0.85 * p_base:
                causes.append(("CPU throttling", "CPU speed fell to %d%% of its usual %d%%" % (p_now, p_base)))
            others = [(n, v) for r in now for n, v in parse_top(r.get("top_cpu"))]
            heavy = sorted({n: v for n, v in others if v >= 50}.items(), key=lambda x: -x[1])
            if heavy or (m(now, "sys_cpu") or 0) >= 85:
                causes.append(("background CPU app", ", ".join("%s %d%% of a core" % x for x in heavy[:3])
                               or "system CPU at %d%%" % m(now, "sys_cpu")))
            if not causes:
                causes.append(("game simulation", "the CPU side of the frame took longer, "
                               "the game was doing more work per frame"))

        # Memory, disk and log evidence apply whatever the frames were bound by.
        pin, pin_b = m(now, "pages_in_s"), m(base, "pages_in_s")
        pf, pf_b = m(now, "wc3_page_faults_s"), m(base, "wc3_page_faults_s")
        if (pin or 0) >= 200 or (pf and pf_b and pf >= 5 * max(pf_b, 50)):
            causes.append(("paging", "memory was being read back from disk, %d pages in a second, "
                           "the game's page faults %d a second against a usual %d"
                           % (pin or 0, pf or 0, pf_b or 0)))
        io, dq = m(now, "wc3_io_mb_s"), m(now, "disk_queue")
        # The disk queue alone is not evidence the GAME was loading. In the 2026-09-26 session it
        # read 870 to 1800 in the same seconds nvidia-smi returned 0 C, a glitched sample, while
        # the game itself read 0.0 MB a second. So the game has to be reading something too.
        if (io or 0) >= 10 or ((io or 0) >= 1 and 2 <= (dq or 0) < 100):
            causes.append(("loading from disk", "the game read %.1f MB a second, disk queue %.1f, "
                           "usually new models, sounds or textures loading" % (io or 0, dq or 0)))
        near = [e for e in logs if a - timedelta(seconds=3) <= e["_t"] <= b + timedelta(seconds=1)]
        if near:
            top = Counter(norm_log(e["detail"]) for e in near).most_common(2)
            causes.append(("game log errors", ", ".join("%d x %s" % (n, msg) for msg, n in top)))
        if bound == "neither" and not causes:
            causes.append(("present or driver wait", "neither CPU nor GPU was busy for most of the frame, "
                           "the frame waited on the display path"))

        drops.append({"start": a, "end": b, "kind": kind, "base": base_fps, "low": low_fps,
                      "worst": worst, "causes": causes, "facts": facts})

    # Summary.
    w("## Summary")
    w("")
    w("| | |")
    w("|---|---|")
    w("| recorded | %s to %s, %d min |" % (frames[0]["t"].strftime("%H:%M:%S"), frames[-1]["t"].strftime("%H:%M:%S"), dur / 60))
    w("| frames | %d |" % len(frames))
    w("| average fps | %.0f overall, %.0f while the game had focus |" % (statistics.mean(fps.values()), statistics.mean(ffps)))
    if one_low:
        w("| 1%% low fps | %.0f |" % one_low)
    w("| worst frame | %.0f ms |" % max(all_ft))
    w("| drops and hitches | %d |" % len(drops))
    unf = len(secs) - len(focused_secs)
    who = Counter(foreground_at(s) for s in secs if not focused(s))
    w("| seconds another window had focus | %d%s |" % (unf, (", " + ", ".join("%s %d s" % kv for kv in who.most_common(4) if kv[0])) if unf else ""))
    w("| present mode | %s |" % mode)
    w("")
    if clock_note:
        w(clock_note)
        w("")
    tally = Counter(c for d in drops for c, _ in d["causes"])
    if tally:
        w("Causes across all drops, a drop can have more than one.")
        w("")
        w("| cause | drops |")
        w("|---|---|")
        for c, n in tally.most_common():
            w("| %s | %d |" % (c, n))
        w("")
    if mode and "Composed" in mode:
        w("The game presented through Windows composition (%s). That is the slowest display path for a "
          "windowed game. Fullscreen, or Windows' 'Optimizations for windowed games' setting, moves it "
          "to a faster path." % mode)
        w("")

    # Every drop.
    w("## Every drop")
    w("")
    if not drops:
        w("None found under the rules above.")
    for i, d in enumerate(drops, 1):
        span = d["start"].strftime("%H:%M:%S")
        if d["end"] != d["start"]:
            span += " to " + d["end"].strftime("%H:%M:%S")
        w("### %d. %s, %s, %d fps down to %d, worst frame %.0f ms" % (i, span, d["kind"], d["base"], d["low"], d["worst"]))
        w("")
        for c, why in d["causes"]:
            w("- **%s**, %s" % (c, why))
        for fct in d["facts"]:
            w("- %s" % fct)
        w("")

    # Trends.
    w("## Trends across the session")
    w("")
    if baseline:
        w("A forced garbage collection was recorded at %s. Lua frees memory lazily, so a slope measured "
          "from the start of a recording overstates a leak. The slopes below are measured from that "
          "point instead." % baseline.strftime("%H:%M:%S"))
        w("")
    w("| measure | start | end | change per minute | steady | reading |")
    w("|---|---|---|---|---|---|")
    for k, label, unit, leak in TREND_MEASURES:
        pts, sl, r = trend_for(samples, k, baseline)
        vals = [v for _, v in pts]
        if not vals:
            continue
        if sl is None:
            w("| %s | %s | %s | too short to tell | | |" % (label, vals[0], vals[-1]))
            continue
        steady = r is not None and r >= 0.8
        reading = "rising steadily, a leak if it never comes back down" if steady and sl >= leak else ""
        w("| %s | %.0f %s | %.0f %s | %+.1f %s | %s | %s |" % (label, vals[0], unit, vals[-1], unit, sl, unit,
                                                             "yes" if steady else "no", reading))
    w("")

    # Handle growth, timed and correlated, since Warcraft III gives no per-type counters.
    w("## Handle growth")
    w("")
    w("Warcraft III does not expose per-handle-type counters to an external sampler, so this report "
      "cannot say whether the handles that grew were files, threads, registry keys or GDI objects. It "
      "can only time the growth and name what was happening around it, which is what the windows below "
      "do. A window is %d seconds. The windows are ranked by how many handles appeared in them, and the "
      "top non-overlapping ones are shown with any session event or game log line within three seconds "
      "of the window." % int(HANDLE_WINDOW_S))
    w("")
    suspects = handle_suspects(samples, events)
    if suspects:
        w("| window | handles | rise | near |")
        w("|---|---|---|---|")
        for s in suspects:
            w("| %s to %s | %.0f to %.0f | %+.0f | %s |"
              % (s["start"].strftime("%H:%M:%S"), s["end"].strftime("%H:%M:%S"),
                 s["first"], s["last"], s["rise"], _near_text(s)))
    else:
        w("No window showed a rise of more than %d handles, or the session was too short to tell." % int(HANDLE_MIN_RISE))
    w("")

    # Game log.
    w("## What the game itself logged")
    w("")
    if logs:
        grp = defaultdict(list)
        for e in logs:
            grp[norm_log(e["detail"])].append(e["_t"])
        w("| count | first | last | message |")
        w("|---|---|---|---|")
        for msg, ts in sorted(grp.items(), key=lambda kv: -len(kv[1]))[:20]:
            w("| %d | %s | %s | `%s` |" % (len(ts), ts[0].strftime("%H:%M:%S"), ts[-1].strftime("%H:%M:%S"), msg.replace("|", "/")))
    else:
        w("Nothing was written to War3Log.txt during the recording.")
    w("")

    # Background programs overall.
    w("## Other programs that competed for the machine")
    w("")
    cpu_tot, gpu_tot = Counter(), Counter()
    for s in samples:
        for n, v in parse_top(s.get("top_cpu")):
            cpu_tot[n] += v / 100
        for n, v in parse_top(s.get("top_gpu")):
            gpu_tot[n] += v / 100
    if cpu_tot or gpu_tot:
        w("Summed over the session, in core-seconds of CPU and GPU-seconds of 3D engine.")
        w("")
        w("| program | CPU | GPU |")
        w("|---|---|---|")
        for n in sorted(set(cpu_tot) | set(gpu_tot), key=lambda n: -(cpu_tot[n] + gpu_tot[n]))[:12]:
            w("| %s | %.0f | %.0f |" % (n, cpu_tot[n], gpu_tot[n]))
    else:
        w("None used a noticeable share.")
    w("")

    others = [e for e in events if e["kind"] in ("game_start", "game_exit", "match_end", "focus_lost", "focus_gained", "stop", GC_KIND)]
    if others:
        w("## Session events")
        w("")
        for e in others:
            w("- %s, %s, %s" % (e["_t"].strftime("%H:%M:%S"), e["kind"], e["detail"]))
        w("")

    w("## Not measured")
    w("")
    w("CPU temperature needs a hardware monitoring driver, so CPU heat shows only indirectly as CPU speed "
      "falling. Network lag is a different problem from low frame rate and is not recorded. Per-handle-type "
      "counts are not exposed by the game to an external sampler. Every cause above is a rule applied to "
      "measurements, printed with its numbers so it can be checked.")
    return "\n".join(out)


def usage():
    print("usage")
    print("  python analyze.py <session folder>            write report.md and print the header")
    print("  python analyze.py summary <session>           print a one screen summary")
    print("  python analyze.py chart <session>             write chart.svg beside the data")
    print("  python analyze.py diff <sessionA> <sessionB>  compare two sessions")


def main(argv=None):
    args = list(sys.argv[1:] if argv is None else argv)
    if not args:
        usage()
        sys.exit(2)
    cmd = args[0]
    if cmd == "summary":
        if len(args) != 2:
            usage()
            sys.exit(2)
        print(render_summary(args[1]))
    elif cmd == "chart":
        if len(args) != 2:
            usage()
            sys.exit(2)
        svg = render_chart(args[1])
        path = os.path.join(args[1], "chart.svg")
        with open(path, "w", encoding="utf-8") as f:
            f.write(svg)
        print("chart written, %s" % path)
    elif cmd == "diff":
        if len(args) != 3:
            usage()
            sys.exit(2)
        print(render_diff(args[1], args[2]))
    else:
        folder = cmd
        report = analyse(folder)
        path = os.path.join(folder, "report.md")
        with open(path, "w", encoding="utf-8") as f:
            f.write(report + "\n")
        head = report.split("## Every drop")[0]
        print(head)
        print("full report, %s" % path)


if __name__ == "__main__":
    main()
