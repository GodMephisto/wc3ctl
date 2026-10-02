# run-local.ps1, plays every test map in maps\ locally, one after another, with no interaction.
#
# WHO     Run it yourself, on the PC with Warcraft III, while nobody is playing.
# WHAT    For each map it starts Warcraft III straight into a local game with the same command
#         line the World Editor's Test Map button uses (-launch -loadfile), lets it run, closes
#         it, and records whether the map loaded, whether the game crashed or exited early,
#         what the game logged, how memory moved, and, when run as administrator with
#         PresentMon installed, the frame rate.
# WHICH   Every .w3x and .w3m in maps\, plus every map inside the zips there, which are
#         extracted once into maps\_extracted\. Filter with -Only.
# WHEN    Warcraft III is restarted before every map, because switching maps without a restart
#         is itself a known desync and cache cause and would blur a comparison.
# WHERE   Results land in results\<yyyy-MM-dd_HHmmss>\, results.csv and summary.md.
# WHY     A local game has ONE client, so it can never disconnect or desync. This run rules out
#         the other explanations, a map that fails to load, crashes, floods errors, or leaks
#         memory, so a later two-player test only has the disconnect question left.
# HOW     powershell -ExecutionPolicy Bypass -File run-local.ps1 [-Minutes 5] [-Only Repro]
#         Run elevated to also capture fps (PresentMon needs admin for its ETW session).

param(
    [double]$Minutes = 5,
    [string]$Only = "",
    [string]$Game = "C:\Warcraft III\_retail_\x86_64\Warcraft III.exe",
    [string]$PresentMon = (Join-Path $env:LOCALAPPDATA "wc3watch\PresentMon-2.5.1-x64.exe")
)
$ErrorActionPreference = "Stop"
$Here = $PSScriptRoot
$Docs = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Warcraft III"
$LogFile = Join-Path $Docs "Logs\War3Log.txt"
$ErrDir = Join-Path $Docs "Errors"

if (-not (Test-Path $Game)) { throw "Warcraft III not found at $Game, pass -Game" }
if (Get-Process "Warcraft III" -ErrorAction SilentlyContinue) {
    throw "Warcraft III is already running. Close it first, this script never closes a game it did not start."
}
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
$fps = $admin -and (Test-Path $PresentMon)

# ---- the maps ----------------------------------------------------------------------------
$mapsDir = Join-Path $Here "maps"
$extract = Join-Path $mapsDir "_extracted"
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($z in Get-ChildItem $mapsDir -Filter *.zip) {
    $dest = Join-Path $extract $z.BaseName
    if (-not (Test-Path $dest)) {
        New-Item -ItemType Directory -Force $dest | Out-Null
        [IO.Compression.ZipFile]::ExtractToDirectory($z.FullName, $dest)
    }
}
$maps = @(Get-ChildItem $mapsDir -File | Where-Object Extension -in ".w3x", ".w3m") +
        @(Get-ChildItem $extract -Recurse -File -ErrorAction SilentlyContinue | Where-Object Extension -in ".w3x", ".w3m")
if ($Only) { $maps = $maps | Where-Object Name -like "*$Only*" }
if (-not $maps) { throw "no maps matched" }

$stamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$out = Join-Path $Here "results\$stamp"
New-Item -ItemType Directory -Force $out | Out-Null
$csv = Join-Path $out "results.csv"
"map,loaded,outcome,seconds,load_s,ingame_s,priv_mb_peak,priv_mb_start,priv_mb_end,priv_mb_per_min,log_lines,model_failed,other_errors,crash_files,avg_fps,low1_fps,worst_ms" |
    Set-Content $csv -Encoding utf8
Write-Host "$($maps.Count) map(s), $Minutes min each, fps $(if ($fps) {'on'} else {'off (run as admin with PresentMon for fps)'})"
Write-Host "results, $out"

# The game starts War3Log.txt afresh at every launch, and this script launches once per map, so
# after the game closes the whole file is this map's session. Reading from the previous size
# instead returned a fragment of the old session, "n Ended", measured on the first full run.
function SessionLog {
    if (-not (Test-Path $LogFile)) { return @() }
    $fs = [IO.File]::Open($LogFile, "Open", "Read", "ReadWrite")
    try { $text = (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
    return @($text -split "`r?`n" | Where-Object { $_ })
}
# When the game started, when it opened THIS map (the full -loadfile path, because at startup the
# game also opens every map in its Maps folder by name to list them, copies of these included),
# and when it opened a different map after that, which is the menu listing maps again.
function Stamp([string]$line) {
    if ($line -match '^\D*(\d+)/(\d+) (\d+):(\d+):(\d+)\.(\d+)') {
        return Get-Date -Month $Matches[1] -Day $Matches[2] -Hour $Matches[3] -Minute $Matches[4] `
            -Second $Matches[5] -Millisecond $Matches[6]
    }
    return $null
}
function Stages($lines, [string]$wanted) {
    $start = $null; $open = $null; $back = $null
    foreach ($l in $lines) {
        if (-not $start -and $l -match 'GameMain Started') { $start = Stamp $l }
        if ($l -match 'Opening map - (.*)$') {
            $p = $Matches[1].Trim()
            if (-not $open -and $p -ieq $wanted) { $open = Stamp $l }
            elseif ($open -and -not $back -and $p -ine $wanted) { $back = Stamp $l }
        }
    }
    return [pscustomobject]@{ Start = $start; Open = $open; Back = $back }
}
function Slope($pts) {
    if ($pts.Count -lt 3) { return 0 }
    $n = $pts.Count; $mx = ($pts | Measure-Object X -Average).Average; $my = ($pts | Measure-Object Y -Average).Average
    $num = 0; $den = 0
    foreach ($p in $pts) { $num += ($p.X - $mx) * ($p.Y - $my); $den += ($p.X - $mx) * ($p.X - $mx) }
    if ($den -eq 0) { return 0 } else { return $num / $den }
}

$i = 0
foreach ($m in $maps) {
    $i++
    Write-Host ("[{0}/{1}] {2}" -f $i, $maps.Count, $m.Name)

    $crashBefore = @(Get-ChildItem $ErrDir -File -ErrorAction SilentlyContinue).Count
    $frames = Join-Path $out ("frames_{0}.csv" -f $i)

    # -editor is what the World Editor's Test Map adds since patch 3.0.0. Without it the game can
    # stop on the Battle.net login page at every launch, and this script launches once per map.
    # With it the login is asked once (Luashine, Hive command line guide, post of 2026-09-15).
    $proc = Start-Process -FilePath $Game -PassThru -ArgumentList @(
        "-launch", "-editor", "-loadfile", "`"$($m.FullName)`"", "-windowmode", "windowed")
    $t0 = Get-Date
    $pm = $null
    $samples = New-Object System.Collections.Generic.List[object]
    $outcome = "still in game at the time limit"
    $wanted = $m.FullName.Replace('\', '/')
    $st = $null
    while ($true) {
        Start-Sleep -Seconds 5
        $g = Get-Process "Warcraft III" -ErrorAction SilentlyContinue | Select-Object -First 1
        $el = ((Get-Date) - $t0).TotalSeconds
        if (-not $g) { $outcome = "game exited by itself"; break }
        if ($fps -and -not $pm) {
            $pm = Start-Process -FilePath $PresentMon -PassThru -WindowStyle Hidden -ArgumentList @(
                "--process_name", "`"Warcraft III.exe`"", "--output_file", "`"$frames`"",
                "--no_console_stats", "--terminate_on_proc_exit", "--session_name", "wc3run",
                "--stop_existing_session")
        }
        $samples.Add([pscustomobject]@{ X = $el / 60; Y = $g.PrivateMemorySize64 / 1MB })
        # A test map with one player and no enemy ends in a melee victory within seconds, and the
        # game goes back to the menu. Waiting out the timer after that measures the menu.
        $st = Stages (SessionLog) $wanted
        if ($st.Back) { $outcome = "game ended, back at the menu"; Start-Sleep -Seconds 5; break }
        if ($el -ge $Minutes * 60) { break }
    }
    $seconds = [int]((Get-Date) - $t0).TotalSeconds

    # Close the game this script started, politely first.
    $g = Get-Process "Warcraft III" -ErrorAction SilentlyContinue
    if ($g) {
        $g | ForEach-Object { $_.CloseMainWindow() | Out-Null }
        Start-Sleep -Seconds 10
        Get-Process "Warcraft III" -ErrorAction SilentlyContinue | Stop-Process -Force
    }
    if ($pm) { try { $pm.WaitForExit(15000) | Out-Null } catch {} }
    Start-Sleep -Seconds 3

    $lines = SessionLog
    $st = Stages $lines $wanted
    $opened = $null -ne $st.Open
    $loadS = if ($st.Open -and $st.Start) { [math]::Round(($st.Open - $st.Start).TotalSeconds, 1) } else { "" }
    $gameS = if ($st.Open -and $st.Back) { [math]::Round(($st.Back - $st.Open).TotalSeconds, 1) } else { "" }
    $peak = if ($samples) { [int]($samples | Measure-Object Y -Maximum).Maximum } else { "" }
    $modelFailed = @($lines | Where-Object { $_ -match "model creation failed" }).Count
    $otherErr = @($lines | Where-Object { $_ -match "error|fail|crash|invalid" -and $_ -notmatch "model creation failed" }).Count
    $crashes = @(Get-ChildItem $ErrDir -File -ErrorAction SilentlyContinue).Count - $crashBefore
    if ($crashes -gt 0) { $outcome = "crashed" }
    $lines | Set-Content (Join-Path $out ("log_{0}.txt" -f $i)) -Encoding utf8

    # Memory over the run, ignoring the first minute of loading.
    $steady = @($samples | Where-Object X -ge 1)
    $slope = [math]::Round((Slope $steady), 1)
    $memStart = if ($steady) { [int]$steady[0].Y } else { "" }
    $memEnd = if ($samples) { [int]$samples[-1].Y } else { "" }

    $avg = ""; $low = ""; $worst = ""
    if (Test-Path $frames) {
        $ft = @(Import-Csv $frames | ForEach-Object { [double]$_.MsBetweenPresents } | Where-Object { $_ -gt 0 })
        if ($ft.Count -gt 10) {
            $sorted = $ft | Sort-Object
            $avg = [math]::Round(1000 / (($ft | Measure-Object -Average).Average), 0)
            $p99 = $sorted[[int]([math]::Floor($sorted.Count * 0.99)) - 1]
            $low = [math]::Round(1000 / $p99, 0)
            $worst = [math]::Round($sorted[-1], 0)
        }
    }

    $row = '"{0}",{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}' -f $m.Name, $opened, $outcome,
        $seconds, $loadS, $gameS, $peak, $memStart, $memEnd, $slope, $lines.Count, $modelFailed, $otherErr, $crashes, $avg, $low, $worst
    Add-Content $csv $row -Encoding utf8
    Write-Host ("    loaded {0} in {1} s, {2} after {3} s in game, peak {4} MB, {5} failed models, {6} other errors" -f `
        $opened, $loadS, $outcome, $gameS, $peak, $modelFailed, $otherErr)
}

# ---- summary -------------------------------------------------------------------------------
$rows = Import-Csv $csv
$md = @("# Local run $stamp", "",
    "$($rows.Count) map(s), up to $Minutes min each. A local game has one client, so this cannot show a",
    "disconnect. It shows whether each map loads, how long loading takes, whether it crashes, and",
    "how much memory it takes. One-player test maps end in a melee victory within seconds, which",
    "reads as 'game ended, back at the menu' and is normal.", "",
    "| map | loaded | load s | in game s | outcome | peak MB | failed models | other errors | avg fps | 1% low |",
    "|---|---|---|---|---|---|---|---|---|---|")
foreach ($r in $rows) {
    $md += "| $($r.map) | $($r.loaded) | $($r.load_s) | $($r.ingame_s) | $($r.outcome) | $($r.priv_mb_peak) | $($r.model_failed) | $($r.other_errors) | $($r.avg_fps) | $($r.low1_fps) |"
}
$bad = @($rows | Where-Object { $_.loaded -ne "True" -or $_.outcome -in "game exited by itself", "crashed" })
$md += "", "$($bad.Count) map(s) did not load, crashed, or closed by themselves."
$md | Set-Content (Join-Path $out "summary.md") -Encoding utf8
Write-Host "done, $(Join-Path $out 'summary.md')"
