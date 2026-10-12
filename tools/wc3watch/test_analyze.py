"""Tests for the wc3watch analyser. Standard library only.

Run with either of these from this folder.
    python -m unittest test_analyze
    python -m pytest test_analyze.py

Every test builds synthetic frames.csv, samples.csv and events.csv in a temporary folder, so nothing
depends on a real recording or on Warcraft III.
"""
import csv
import os
import shutil
import sys
import tempfile
import unittest
from datetime import datetime, timedelta
from xml.dom import minidom

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import analyze  # noqa: E402

SAMPLE_COLUMNS = [
    "time", "focus", "minimized", "wc3_cpu", "wc3_main_thread", "wc3_priv_mb", "wc3_ws_mb",
    "wc3_handles", "wc3_threads", "wc3_io_mb_s", "wc3_page_faults_s", "wc3_gpu3d",
    "browser_ws_mb", "browser_cpu", "sys_cpu", "sys_core_max", "cpu_perf_pct", "avail_mb",
    "commit_gb", "pages_in_s", "memcomp_mb", "disk_queue", "gpu_temp", "gpu_util",
    "gpu_clock", "gpu_mem_clock", "gpu_power", "gpu_throttle", "gpu_mem_mb", "top_cpu", "top_gpu",
]


def make_session(folder, seconds=120, mem_per_s=2.0, hand_per_s=10.0,
                 drop_seconds=(), hitch_seconds=(), gc_at=None,
                 jump_at=None, jump=0.0, extra_events=(), logs=()):
    """Write a synthetic session and return its folder."""
    os.makedirs(folder, exist_ok=True)
    base = datetime(2026, 1, 1, 12, 0, 0)
    with open(os.path.join(folder, "samples.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.writer(f)
        wr.writerow(SAMPLE_COLUMNS)
        for i in range(seconds):
            t = (base + timedelta(seconds=i)).strftime("%Y-%m-%d %H:%M:%S")
            hand = 1000 + hand_per_s * i + (jump if (jump_at is not None and i >= jump_at) else 0.0)
            row = {
                "time": t, "focus": 1, "minimized": 0, "wc3_cpu": "20.0", "wc3_main_thread": "55.0",
                "wc3_priv_mb": "%.1f" % (100 + mem_per_s * i), "wc3_ws_mb": "200",
                "wc3_handles": "%.0f" % hand, "wc3_threads": "20", "wc3_io_mb_s": "0.00",
                "wc3_page_faults_s": "10", "wc3_gpu3d": "30.0", "browser_ws_mb": "300",
                "browser_cpu": "1.0", "sys_cpu": "10.0", "sys_core_max": "40.0",
                "cpu_perf_pct": "100.0", "avail_mb": "8000", "commit_gb": "12.00",
                "pages_in_s": "0", "memcomp_mb": "500", "disk_queue": "0.0", "gpu_temp": "55",
                "gpu_util": "40", "gpu_clock": "1800", "gpu_mem_clock": "7000", "gpu_power": "120.0",
                "gpu_throttle": "", "gpu_mem_mb": "2000", "top_cpu": "", "top_gpu": "",
            }
            wr.writerow([row[c] for c in SAMPLE_COLUMNS])

    with open(os.path.join(folder, "frames.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.writer(f)
        wr.writerow(["CPUStartDateTime", "SwapChainAddress", "MsBetweenPresents",
                     "MsCPUBusy", "MsGPUBusy", "PresentMode"])
        for i in range(seconds):
            cnt = 12 if i in set(drop_seconds) else 60
            ft = 1000.0 / cnt
            for k in range(cnt):
                ms = int(k * 1000 / cnt)
                t = (base + timedelta(seconds=i, milliseconds=ms)).strftime("%Y-%m-%d %H:%M:%S.%f")
                wr.writerow([t, "0xABC", "%.3f" % ft, "%.3f" % (ft * 0.5), "%.3f" % (ft * 0.3), "Composed"])
        for i in hitch_seconds:
            t = (base + timedelta(seconds=i, milliseconds=500)).strftime("%Y-%m-%d %H:%M:%S.%f")
            wr.writerow([t, "0xABC", "80.000", "40.000", "20.000", "Composed"])

    rows = []
    rows.append(((base).strftime("%Y-%m-%d %H:%M:%S"), "game_start", "pid 1"))
    for sec, kind, detail in extra_events:
        rows.append(((base + timedelta(seconds=sec)).strftime("%Y-%m-%d %H:%M:%S"), kind, detail))
    for sec, text in logs:
        rows.append(((base + timedelta(seconds=sec)).strftime("%Y-%m-%d %H:%M:%S"), "gamelog", text))
    if gc_at is not None:
        rows.append(((base + timedelta(seconds=gc_at)).strftime("%Y-%m-%d %H:%M:%S"),
                     analyze.GC_KIND, "operator pressed G, forced GC baseline"))
    rows.append(((base + timedelta(seconds=seconds - 1)).strftime("%Y-%m-%d %H:%M:%S"), "game_exit", "process gone"))
    with open(os.path.join(folder, "events.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.writer(f)
        wr.writerow(["time", "kind", "detail"])
        wr.writerows(rows)
    return folder


class Base(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="wc3watch-test-")

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def session(self, name, **kw):
        return make_session(os.path.join(self.root, name), **kw)


class TestTrends(Base):
    def test_steady_rise_reads_as_a_leak(self):
        folder = self.session("leaky", mem_per_s=3.0, hand_per_s=20.0)
        m = analyze.session_metrics(folder)
        tr = {t["key"]: t for t in m["trends"]}
        self.assertIsNotNone(tr["wc3_priv_mb"]["slope"])
        self.assertGreater(tr["wc3_priv_mb"]["slope"], 0)
        self.assertGreater(tr["wc3_handles"]["slope"], 20)
        self.assertGreaterEqual(tr["wc3_handles"]["r"], 0.8)


class TestDrops(Base):
    def test_drop_and_hitch_are_counted(self):
        folder = self.session("drops", drop_seconds=(90,), hitch_seconds=(110,))
        m = analyze.session_metrics(folder)
        self.assertEqual(m["dropped"], 1)
        self.assertEqual(m["hitches"], 1)


class TestDiff(Base):
    def test_diff_names_both_sessions_and_shows_the_delta(self):
        a = self.session("A", mem_per_s=3.0, hand_per_s=20.0, drop_seconds=(90, 100), hitch_seconds=(110,))
        b = self.session("B", mem_per_s=0.0, hand_per_s=0.0)
        text = analyze.render_diff(a, b)
        self.assertIn("# wc3watch diff", text)
        self.assertIn("A", text)
        self.assertIn("B", text)
        self.assertIn("delta", text)
        self.assertIn("game memory", text)
        self.assertIn("game handles", text)
        # A is the leaky side, so B minus A is negative for both slopes.
        self.assertIn("-", text)
        self.assertIn("drops", text)

    def test_diff_reading_notes_a_fix(self):
        a = self.session("A", mem_per_s=3.0, hand_per_s=20.0)
        b = self.session("B", mem_per_s=0.0, hand_per_s=0.0)
        text = analyze.render_diff(a, b)
        self.assertIn("flatter", text)


class TestChart(Base):
    def test_chart_is_valid_svg_with_marks(self):
        folder = self.session("chart", drop_seconds=(90,), gc_at=60)
        svg = analyze.render_chart(folder)
        doc = minidom.parseString(svg)          # raises on malformed XML
        self.assertEqual(doc.documentElement.tagName, "svg")
        self.assertEqual(doc.documentElement.getAttribute("xmlns"), "http://www.w3.org/2000/svg")
        self.assertIn('<polyline class="series-mem"', svg)
        self.assertIn('<polyline class="series-hand"', svg)
        self.assertIn('class="drop-band"', svg)
        self.assertIn('class="gc-probe"', svg)

    def test_chart_without_samples_is_still_svg(self):
        folder = os.path.join(self.root, "empty")
        os.makedirs(folder)
        open(os.path.join(folder, "samples.csv"), "w").close()
        svg = analyze.render_chart(folder)
        doc = minidom.parseString(svg)
        self.assertEqual(doc.documentElement.tagName, "svg")


class TestGcProbe(Base):
    def test_gc_event_is_noted_in_the_report(self):
        folder = self.session("gc", mem_per_s=2.0, gc_at=60)
        report = analyze.analyse(folder)
        self.assertIn("forced garbage collection", report)
        self.assertIn("12:01:00", report)
        self.assertIn(analyze.GC_KIND, report)

    def test_gc_marks_the_slope_baseline(self):
        folder = self.session("gcb", mem_per_s=4.0, gc_at=60)
        m = analyze.session_metrics(folder)
        self.assertEqual(m["gc_times"], [datetime(2026, 1, 1, 12, 1, 0)])
        self.assertEqual(m["baseline"], datetime(2026, 1, 1, 12, 1, 0))
        tr = {t["key"]: t for t in m["trends"]}
        # From the second minute on, memory still rises, but from the lower baseline value.
        self.assertGreater(tr["wc3_priv_mb"]["slope"], 0)


class TestSummary(Base):
    def test_summary_lists_measures_drops_and_suspects(self):
        folder = self.session(
            "summary", mem_per_s=3.0, hand_per_s=10.0, drop_seconds=(90,), hitch_seconds=(110,),
            gc_at=60, jump_at=60, jump=400.0,
            logs=[(60, "spawn 5 units")],
        )
        text = analyze.render_summary(folder)
        self.assertIn("wc3watch summary", text)
        self.assertIn("game memory", text)
        self.assertIn("game handles", text)
        self.assertIn("drop", text)
        self.assertIn("hitch", text)
        self.assertIn("gc probe", text)
        self.assertIn("suspect windows for handle growth", text)
        self.assertIn("spawn", text)

    def test_summary_without_frames_says_so(self):
        folder = os.path.join(self.root, "noframes")
        os.makedirs(folder)
        open(os.path.join(folder, "samples.csv"), "w").close()
        text = analyze.render_summary(folder)
        self.assertIn("nothing to summarise", text)


class TestHandleSuspects(Base):
    def test_a_handle_jump_is_located_near_its_log(self):
        folder = self.session("jump", hand_per_s=1.0, jump_at=60, jump=500.0,
                              logs=[(60, "trigger ran spawn 12 units")])
        sess = analyze.load_session(folder)
        suspects = analyze.handle_suspects(sess["samples"], sess["events"])
        self.assertTrue(suspects)
        window = suspects[0]
        self.assertGreaterEqual(window["rise"], 500.0)
        joined = " ".join("%d x %s" % (n, msg) for msg, n in window["logs"])
        self.assertIn("spawn", joined)


if __name__ == "__main__":
    unittest.main()
