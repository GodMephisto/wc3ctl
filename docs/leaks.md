# Leak investigation

A handle leak in a JASS map is a location, group, force, effect, timer, text tag, lightning or trigger
created on a repeating path and never destroyed. Warcraft III has no garbage collector for handles, so
the game grows in memory and slows down over a long session. The workflow below goes from the static
list to a measured attribution, and every runtime number comes from a `wc3watch` session rather than
from the analyser alone.

## The steps

1. `script leaks` lists the ranked static findings, grouped by heat. Hot means reachable from a
   periodic timer or trigger, repeat means any other callback, and once means main or init only. This
   is the read-only starting point and needs only the map.
2. `leak list` presents the same static result for the runtime half. It adds the periodic drivers,
   each with the handles a minute its call tree predicts, and the hot targets a harness can name.
3. `leak harness --lang jass|lua --target <function|kind|all>` writes a small in-map harness that
   drives the named hot candidates on a short timer, so the handle and memory slope becomes
   measurable in seconds instead of an hour. Write it with `-o`, add it to a test copy of the map,
   and start the map.
4. `wc3watch` samples the running game once a second. It records memory, handles, CPU, disk and the
   frames, outside the game process and with no anti-cheat risk. Run it elevated and press `G` after
   a forced collection, as below.
5. `leak join <session-folder>` fits the handle and memory slopes of one session and aligns a rising
   handle slope with the periodic drivers from the static list. Every slope is a measurement of the
   samples given, and each driver ranking is an inference from the static list, so the two are kept
   apart in the output.
6. `leak ab <a> <b>` diffs two sessions, usually one with the suspect trigger enabled and one with it
   disabled. A fall on the handle slope is the leak being removed, and the nearest static candidate
   is named as the likely disabled one, labelled an inference.

## The Lua forced-collection caveat

Warcraft III frees Lua memory lazily, so a memory slope measured from the very start of a recording
can overstate a leak, because the collector has not run yet. Before trusting a slope on a Lua map,
force a collection twice. Many Lua maps expose a chat command for this, and a small test trigger can
call `collectgarbage("collect")` twice. Then press `G` in the wc3watch window, which writes a
`gc_probe` event. The analyser measures from that event instead of the start of the session, and marks
it on the chart. With no `gc_probe` the slopes cover the whole session, which is right for a map that
does not use Lua.

## Tools

`wc3watch` and its analyser live in `tools/wc3watch`, with `wc3watch.ps1`, `analyze.py` and
`test_analyze.py`. The recorder needs administrator rights for Intel PresentMon, and the PresentMon
2.5.1 binary must be at `%LOCALAPPDATA%\wc3watch\PresentMon-2.5.1-x64.exe` unless `-PresentMon`
points elsewhere.
