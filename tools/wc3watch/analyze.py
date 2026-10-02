"""wc3watch analyser. Reads one recorded session and explains every frame rate drop in it.

Who runs it. wc3watch.ps1 calls it when a recording ends, or a person runs it by hand on an old
session, as `python analyze.py <session folder>`. It reads frames.csv, samples.csv and events.csv
and writes report.md into the same folder. Standard library only.

How a drop is found. Frames are grouped into seconds. A second counts as a drop when the game had
focus and its frame rate fell below 75 percent of the median of the minute before it. A single frame
of at least 50 ms, and at least four times the minute's median frame time, counts as a hitch. Seconds
within one second of each other merge into one event.

How a cause is named. Each frame records how long the CPU and the GPU were busy on it, so an event
is first classed as CPU bound, GPU bound, or neither (waiting on present or the driver). The
one-second samples and the game's own log around the event then say why. Every rule below prints
the numbers it used, so a verdict can be checked rather than trusted. The rules are heuristics and
the report says so.
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
    """'Discord:23 chrome:11' into [('Discord', 23.0), ...]."""
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


def analyse(folder):
    frames, mode = load_frames(os.path.join(folder, "frames.csv"))
    samples = load_csv(os.path.join(folder, "samples.csv"))
    events = load_csv(os.path.join(folder, "events.csv"))
    for s in samples:
        s["_t"] = ptime(s["time"])
    for e in events:
        e["_t"] = ptime(e["time"])

    # PresentMon's date_time output is not on the local clock. Measured on 2026-09-26 on a UTC+8
    # machine, it wrote local time plus a further 8 hours, so every frame landed on the wrong second
    # and every cause was read from the wrong sample. A first version rounded the gap to the start of
    # recording, which broke for any capture begun later. Now the candidates are whole multiples of
    # the machine's UTC offset, and the one that puts the most frames inside the sampled span wins.
    clock_note = None
    if frames and samples:
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
            clock_note = "PresentMon's clock was %+.2f h off the local clock and was corrected." % (best / 3600)
    by_sec = {sec(s["_t"]): s for s in samples}
    logs = [e for e in events if e["kind"] == "gamelog"]

    out = []
    w = out.append
    w("# wc3watch report, session %s" % os.path.basename(os.path.normpath(folder)))
    w("")
    if not frames:
        w("No frames were recorded. Either the game never drew a frame while recording, or PresentMon "
          "was not running with administrator rights.")
        return "\n".join(out)

    # Per second frame statistics.
    per = defaultdict(list)
    for f in frames:
        per[sec(f["t"])].append(f)
    secs = sorted(per)
    fps = {s: len(per[s]) for s in secs}

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
            causes.append(("game log errors", "; ".join("%d x %s" % (n, msg) for msg, n in top)))
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
    w("| measure | start | end | change per minute | steady | reading |")
    w("|---|---|---|---|---|---|")
    for k, label, unit, leak in (("wc3_priv_mb", "game memory", "MB", 5), ("wc3_handles", "game handles", "", 20),
                                 ("browser_ws_mb", "menu browser memory", "MB", 5), ("gpu_temp", "GPU temperature", "C", 0.5),
                                 ("memcomp_mb", "Windows memory compression", "MB", 5)):
        pts = [(s["_t"], num(s.get(k))) for s in samples]
        vals = [v for _, v in pts if v is not None]
        if not vals:
            continue
        sl, r = slope_per_min(pts)
        if sl is None:
            w("| %s | %s | %s | too short to tell | | |" % (label, vals[0], vals[-1]))
            continue
        steady = r is not None and r >= 0.8
        reading = "rising steadily, a leak if it never comes back down" if steady and sl >= leak else ""
        w("| %s | %.0f %s | %.0f %s | %+.1f %s | %s | %s |" % (label, vals[0], unit, vals[-1], unit, sl, unit,
                                                             "yes" if steady else "no", reading))
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

    others = [e for e in events if e["kind"] in ("game_start", "game_exit", "match_end", "focus_lost", "focus_gained", "stop")]
    if others:
        w("## Session events")
        w("")
        for e in others:
            w("- %s, %s, %s" % (e["_t"].strftime("%H:%M:%S"), e["kind"], e["detail"]))
        w("")

    w("## Not measured")
    w("")
    w("CPU temperature needs a hardware monitoring driver, so CPU heat shows only indirectly as CPU speed "
      "falling. Network lag is a different problem from low frame rate and is not recorded. Every cause "
      "above is a rule applied to measurements, printed with its numbers so it can be checked.")
    return "\n".join(out)


def main():
    if len(sys.argv) != 2:
        print("usage: python analyze.py <session folder>")
        sys.exit(2)
    folder = sys.argv[1]
    report = analyse(folder)
    path = os.path.join(folder, "report.md")
    with open(path, "w", encoding="utf-8") as f:
        f.write(report + "\n")
    head = report.split("## Every drop")[0]
    print(head)
    print("full report, %s" % path)


if __name__ == "__main__":
    main()
