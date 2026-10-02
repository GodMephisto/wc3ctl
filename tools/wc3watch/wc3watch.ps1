# wc3watch, records everything that can make Warcraft III drop frames, then explains each drop.
#
# Who runs it. The player, in an ELEVATED PowerShell, before or during play. Frame timing comes from
# Windows event tracing (ETW), which needs administrator rights.
#
# What it records, and from where. Nothing here enters the game process, reads its memory or hooks
# its rendering, so it carries no anti-cheat risk. Every source is one Windows already exposes.
#   frames.csv   every frame the game presents, from Intel PresentMon 2.5.1 (ETW). Per frame it
#                gives the frame time and how long the CPU and the GPU were each busy on it, which
#                is what says whether a slow frame was CPU bound or GPU bound.
#   samples.csv  one row a second. The game's memory, handles, CPU and its busiest thread, disk and
#                paging, the menu browser, the GPU's temperature, clock, power and throttle reasons,
#                system CPU and memory, the heaviest other programs by CPU and by GPU, and whether the
#                game had focus (an unfocused game is capped on purpose and is not a drop).
#   events.csv   game start and exit, focus changes, a match ending (the game rewrites
#                LastReplay.w3g), and every line the game writes to Logs\War3Log.txt, which is where
#                it reports failures such as a model it could not create.
#
# When it stops. When the game exits, after -Minutes, or on Ctrl+C. It then runs analyze.py, which
# writes report.md into the session folder, one entry per drop with its most likely cause.
#
# How to run it.
#   pwsh -File wc3watch.ps1                 wait for the game, record until it exits
#   pwsh -File wc3watch.ps1 -Minutes 30     stop after 30 minutes
#   python analyze.py <session folder>      re-run the analysis on a recorded session
param(
    [int]$Minutes = 0,
    [string]$OutRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) "wc3watch"),
    [string]$PresentMon = (Join-Path $env:LOCALAPPDATA "wc3watch\PresentMon-2.5.1-x64.exe")
)
$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw "Run this from an elevated PowerShell. Frame timing needs administrator rights for ETW." }
if (-not (Test-Path $PresentMon)) { throw "PresentMon not found at $PresentMon. See README.md for the download." }

$Game = "Warcraft III"
$Smi = "C:\Windows\System32\nvidia-smi.exe"
$Docs = Join-Path ([Environment]::GetFolderPath('MyDocuments')) "Warcraft III"
$GameLog = Join-Path $Docs "Logs\War3Log.txt"

Add-Type -Namespace W3W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
'@

$session = Join-Path $OutRoot (Get-Date -Format "yyyy-MM-dd_HHmmss")
New-Item -ItemType Directory -Force -Path $session | Out-Null
$samplesCsv = Join-Path $session "samples.csv"
$eventsCsv = Join-Path $session "events.csv"
$framesCsv = Join-Path $session "frames.csv"
$utf8 = [Text.UTF8Encoding]::new($false)
$samplesW = [IO.StreamWriter]::new($samplesCsv, $false, $utf8)
$eventsW = [IO.StreamWriter]::new($eventsCsv, $false, $utf8)
$samplesW.WriteLine("time,focus,minimized,wc3_cpu,wc3_main_thread,wc3_priv_mb,wc3_ws_mb,wc3_handles,wc3_threads," +
    "wc3_io_mb_s,wc3_page_faults_s,wc3_gpu3d,browser_ws_mb,browser_cpu,sys_cpu,sys_core_max,cpu_perf_pct," +
    "avail_mb,commit_gb,pages_in_s,memcomp_mb,disk_queue,gpu_temp,gpu_util,gpu_clock,gpu_mem_clock,gpu_power," +
    "gpu_throttle,gpu_mem_mb,top_cpu,top_gpu")
$eventsW.WriteLine("time,kind,detail")
$samplesW.Flush(); $eventsW.Flush()

function Now { (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff") }
function Csv([string]$s) { '"' + ($s -replace '"', '""') + '"' }
function Event([string]$kind, [string]$detail, [string]$time = (Now)) {
    $eventsW.WriteLine("$time,$kind,$(Csv $detail)"); $eventsW.Flush()
}

Write-Host "wc3watch, session folder $session"
Write-Host "Waiting for $Game to start..."
while (-not (Get-Process $Game -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 500 }
$g = Get-Process $Game | Select-Object -First 1
Event "game_start" "pid $($g.Id), started $($g.StartTime.ToString('HH:mm:ss'))"
Write-Host "Recording $Game (pid $($g.Id)). Stop with Ctrl+C, or just close the game."

# Frames. PresentMon ends itself when the game exits.
$pmSession = "wc3watch"
$pm = Start-Process -FilePath $PresentMon -PassThru -WindowStyle Hidden -ArgumentList @(
    '--process_name', "`"$Game.exe`"", '--output_file', "`"$framesCsv`"", '--date_time',
    '--no_console_stats', '--terminate_on_proc_exit', '--session_name', $pmSession, '--stop_existing_session')

# Log tail starts at the current end, so only lines written during this session are recorded.
$logPos = if (Test-Path $GameLog) { (Get-Item $GameLog).Length } else { 0 }
function Read-GameLog {
    if (-not (Test-Path $GameLog)) { return }
    $len = (Get-Item $GameLog).Length
    if ($len -lt $script:logPos) { $script:logPos = 0 }        # the game started a fresh log
    if ($len -eq $script:logPos) { return }
    $fs = [IO.File]::Open($GameLog, 'Open', 'Read', 'ReadWrite')
    try {
        $null = $fs.Seek($script:logPos, 'Begin')
        $sr = [IO.StreamReader]::new($fs)
        $text = $sr.ReadToEnd()
        $script:logPos = $fs.Position
    } finally { $fs.Dispose() }
    foreach ($line in $text -split "`r?`n") {
        if ($line -match '^(\d+)/(\d+) (\d\d:\d\d:\d\d\.\d+)\s+(.*)$') {
            $t = "{0}-{1:D2}-{2:D2} {3}" -f (Get-Date).Year, [int]$Matches[1], [int]$Matches[2], $Matches[3]
            Event "gamelog" $Matches[4] $t
        }
    }
}

# Match end. The game rewrites LastReplay.w3g under each Battle.net account folder.
function Replay-Stamp {
    Get-ChildItem (Join-Path $Docs "BattleNet") -Recurse -Filter "LastReplay.w3g" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
}
$lastReplay = (Replay-Stamp).LastWriteTime

$counters = @(
    "\Processor Information(_Total)\% Processor Time",
    "\Processor Information(_Total)\% Processor Performance",
    "\Processor Information(*)\% Processor Time",
    "\Memory\Available MBytes", "\Memory\Pages Input/sec", "\Memory\Committed Bytes",
    "\PhysicalDisk(_Total)\Current Disk Queue Length",
    "\Process($Game)\IO Data Bytes/sec", "\Process($Game)\Page Faults/sec",
    "\GPU Engine(*engtype_3D)\Utilization Percentage")
$cores = [Environment]::ProcessorCount
$prevCpu = @{}; $prevThreads = @{}; $prevStamp = Get-Date
$focusWas = $null
$end = if ($Minutes -gt 0) { (Get-Date).AddMinutes($Minutes) } else { [datetime]::MaxValue }
$selfIds = @($PID, $pm.Id)

try {
    :outer while ($true) {
        # Get-Counter is the 1 Hz clock, since each reading of a rate counter waits one interval by
        # design. It must be consumed as a pipeline. A foreach over the same call collects all 60
        # readings before running once, which stalls a minute and then replays stale data in a burst.
        # Restarting it every minute picks up processes that started since.
        Get-Counter -Counter $counters -SampleInterval 1 -MaxSamples 60 -ErrorAction SilentlyContinue | ForEach-Object {
            $sample = $_
            $g = Get-Process $Game -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $g) { Event "game_exit" "process gone"; break outer }
            if ((Get-Date) -gt $end) { Event "stop" "time limit"; break outer }

            $now = Get-Date; $dt = [Math]::Max(0.2, ($now - $prevStamp).TotalSeconds); $prevStamp = $now
            $v = @{}; $core = 0.0; $gpuByPid = @{}
            foreach ($cs in $sample.CounterSamples) {
                $p = $cs.Path
                if ($p -match 'gpu engine\(pid_(\d+)_') {
                    $gpuByPid[[int]$Matches[1]] = ($gpuByPid[[int]$Matches[1]] + $cs.CookedValue)
                } elseif ($p -match 'processor information\((\d+,\d+)\)') {
                    $core = [Math]::Max($core, $cs.CookedValue)
                } else { $v[($p -split '\\')[-1]] = $cs.CookedValue; $v[$p] = $cs.CookedValue }
            }
            $sysCpu = ($sample.CounterSamples | Where-Object { $_.Path -match 'processor information\(_total\)\\% processor time' }).CookedValue
            $perf = ($sample.CounterSamples | Where-Object { $_.Path -match '% processor performance' }).CookedValue

            # Per process CPU, as a percent of one core, from the change in total processor time.
            $procs = Get-Process -ErrorAction SilentlyContinue
            $cpuNow = @{}; $cpuPct = @{}
            foreach ($pr in $procs) {
                try { $t = $pr.TotalProcessorTime.TotalSeconds } catch { continue }
                $cpuNow[$pr.Id] = $t
                if ($prevCpu.ContainsKey($pr.Id)) { $cpuPct[$pr.Id] = 100 * ($t - $prevCpu[$pr.Id]) / $dt }
            }
            $prevCpu = $cpuNow

            # The game's busiest thread. Warcraft III runs its simulation on one thread, so one core
            # at 100 percent limits the frame rate while total CPU looks idle.
            $mainT = 0.0; $thrNow = @{}
            foreach ($th in $g.Threads) {
                try { $t = $th.TotalProcessorTime.TotalSeconds } catch { continue }
                $thrNow[$th.Id] = $t
                if ($prevThreads.ContainsKey($th.Id)) { $mainT = [Math]::Max($mainT, 100 * ($t - $prevThreads[$th.Id]) / $dt) }
            }
            $prevThreads = $thrNow

            $browser = $procs | Where-Object ProcessName -eq 'BlizzardBrowser'
            $bws = [int](($browser | Measure-Object WorkingSet64 -Sum).Sum / 1MB)
            $bcpu = ($browser | ForEach-Object { $cpuPct[$_.Id] } | Measure-Object -Sum).Sum
            $memcomp = [int](($procs | Where-Object ProcessName -eq 'Memory Compression' | Measure-Object WorkingSet64 -Sum).Sum / 1MB)

            $names = @{}; foreach ($pr in $procs) { $names[$pr.Id] = $pr.ProcessName }
            $skip = @($g.Id) + $selfIds + @(0) + @($browser.Id)
            $topCpu = ($cpuPct.GetEnumerator() | Where-Object { $_.Key -notin $skip -and $_.Value -ge 5 } |
                Sort-Object Value -Descending | Select-Object -First 4 |
                ForEach-Object { "{0}:{1:N0}" -f $names[$_.Key], $_.Value }) -join ' '
            $topGpu = ($gpuByPid.GetEnumerator() | Where-Object { $_.Key -ne $g.Id -and $_.Value -ge 2 } |
                Sort-Object Value -Descending | Select-Object -First 4 |
                ForEach-Object { "{0}:{1:N0}" -f $names[$_.Key], $_.Value }) -join ' '

            $fgWin = [W3W.U]::GetForegroundWindow()
            $fgPid = [uint32]0; $null = [W3W.U]::GetWindowThreadProcessId($fgWin, [ref]$fgPid)
            $focus = [int]($fgPid -eq $g.Id)
            $mini = [int][W3W.U]::IsIconic($g.MainWindowHandle)
            $fgKey = "$fgPid"
            if ($fgKey -ne $focusWas) {
                $tb = [Text.StringBuilder]::new(160); $null = [W3W.U]::GetWindowText($fgWin, $tb, 160)
                if ($null -ne $focusWas) {
                    Event ($(if ($focus) { "focus_gained" } else { "focus_lost" })) ("foreground " + $names[[int]$fgPid] + ", window '" + $tb.ToString() + "'")
                }
                $focusWas = $fgKey
            }

            $gpu = if (Test-Path $Smi) {
                ((& $Smi --query-gpu=temperature.gpu,utilization.gpu,clocks.gr,clocks.mem,power.draw,clocks_throttle_reasons.active,memory.used --format=csv,noheader,nounits 2>$null) -replace ' ', '')
            } else { ",,,,,," }

            $row = @(
                (Now), $focus, $mini,
                ("{0:N1}" -f $cpuPct[$g.Id]), ("{0:N1}" -f $mainT),
                [int]($g.PrivateMemorySize64 / 1MB), [int]($g.WorkingSet64 / 1MB), $g.HandleCount, $g.Threads.Count,
                ("{0:N2}" -f ($v["io data bytes/sec"] / 1MB)), ("{0:N0}" -f $v["page faults/sec"]),
                ("{0:N1}" -f $gpuByPid[$g.Id]),
                $bws, ("{0:N1}" -f $bcpu),
                ("{0:N1}" -f $sysCpu), ("{0:N1}" -f $core), ("{0:N1}" -f $perf),
                [int]$v["available mbytes"], ("{0:N2}" -f ($v["committed bytes"] / 1GB)),
                ("{0:N0}" -f $v["pages input/sec"]), $memcomp, ("{0:N1}" -f $v["current disk queue length"]),
                $gpu, (Csv $topCpu), (Csv $topGpu)) -join ','
            $samplesW.WriteLine($row.Replace("`r", ''))
            $samplesW.Flush()

            Read-GameLog
            $r = Replay-Stamp
            if ($r -and $r.LastWriteTime -ne $lastReplay) { Event "match_end" "LastReplay.w3g rewritten, $([int]($r.Length/1KB)) KB"; $lastReplay = $r.LastWriteTime }
        }
    }
} finally {
    Read-GameLog
    $samplesW.Dispose(); $eventsW.Dispose()
    if (-not $pm.HasExited) {
        $null = Start-Process -FilePath $PresentMon -ArgumentList @('--terminate_existing_session', '--session_name', $pmSession) -WindowStyle Hidden -Wait -PassThru
    }
    Write-Host "Recording stopped. Analysing..."
    $py = (Get-Command python -ErrorAction SilentlyContinue).Source
    if ($py) { & $py (Join-Path $PSScriptRoot "analyze.py") $session } else { Write-Host "python not found, run analyze.py $session by hand" }
}
