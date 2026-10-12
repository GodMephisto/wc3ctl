# wc3watch

wc3watch records everything that can make Warcraft III drop frames, then explains each drop. It
stays outside the game process, reads only what Windows already exposes, and carries no anti-cheat
risk. Frame timing comes from Intel PresentMon over Windows event tracing, which needs administrator
rights.

Run it from an elevated PowerShell.

```
pwsh -File wc3watch.ps1                 wait for the game, record until it exits
pwsh -File wc3watch.ps1 -Minutes 30     stop after 30 minutes
```

PresentMon 2.5.1 must be at `%LOCALAPPDATA%\wc3watch\PresentMon-2.5.1-x64.exe`, or pass `-PresentMon`.

## What a session holds

| file | written by | what is in it |
|---|---|---|
| `frames.csv` | PresentMon | every frame the game presented, its frame time and how long the CPU and the GPU were busy |
| `samples.csv` | wc3watch | one row a second. The game's memory, handles, CPU and busiest thread, disk and paging, the menu browser, the GPU temperature, clock, power and throttle reasons, system CPU and memory, the heaviest other programs, and whether the game had focus |
| `events.csv` | wc3watch | game start and exit, focus changes, a match ending, a forced GC mark, and every line the game writes to `Logs\War3Log.txt` |
| `report.md` | analyze.py | one entry per drop and hitch, with the numbers behind each cause |
| `chart.svg` | analyze.py | memory and handles over time, with the drops and any forced GC marked |

## Forcing a garbage collection on a Lua map

Warcraft III frees Lua memory lazily. A memory slope measured from the very start of a recording can
therefore overstate a leak, because the collector has not run yet. For an honest baseline, force a
collection first.

1. Start the recording and let the map load.
2. In the map, force a collection twice. Many Lua maps expose a chat command for this. If not, a
   small test trigger can call `collectgarbage("collect")` twice.
3. Press `G` in the wc3watch window.

wc3watch writes a `gc_probe` event. analyze.py then measures the memory and handle slopes from that
point instead of from the start of the session, notes it in the report, and marks it on the chart.
With no `gc_probe` the slopes cover the whole session, which is right for a map that does not use Lua.

## Reading a session

```
python analyze.py <session folder>            write report.md and print its first screen
python analyze.py summary <session>           one screen, length, measures, drops and suspect windows
python analyze.py chart <session>             write chart.svg beside the data
python analyze.py diff <sessionA> <sessionB>  two sessions side by side, the delta is B minus A
```

`diff` is the A/B half of a leak investigation. Record one session with the suspect trigger disabled
and one with it enabled, then diff them. Every measure is shown side by side with the change, so a
fixed leak reads as a flatter slope on the B side.

## Handles

Warcraft III does not expose per-handle-type counters to an external sampler, so wc3watch cannot say
whether the handles that grew were files, threads, registry keys or GDI objects. What it can do is
time the growth. The report ranks 60 second windows by how many handles appeared in each, and names
the session events and game log lines within three seconds of the window. That points at a suspect
window, a mass spawn or a periodic trigger, rather than only printing a number.

## Files

| file | purpose |
|---|---|
| `wc3watch.ps1` | the recorder |
| `analyze.py` | the analyser, standard library only |
| `test_analyze.py` | tests over synthetic sessions |

Run the tests with either of these from this folder.

```
python -m unittest test_analyze
python -m pytest test_analyze.py
```
