# scripts/publish-all.ps1
# Publishes every front-end (CLI, Studio, MCP) into its dist folder and prints the resulting
# timestamps, so "is the binary I am testing actually fresh" is answerable at a glance.
#
# Why this exists: publishing one target and forgetting another has shipped stale binaries more than
# once. A source fix that is not published never reaches the app being tested, and the bug looks
# unfixed. Publishing all of them together removes that whole class of confusion.
#
# Studio locks its own DLLs while running, so close it before publishing or that target fails.
#
#   pwsh scripts/publish-all.ps1                 # publish into this repo's dist folders
#   pwsh scripts/publish-all.ps1 -Also 'D:\path' # ALSO publish into another tree's dist folders

param(
    [string[]] $Also = @(),
    [string]   $Configuration = 'Release',
    [string]   $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$targets = @(
    @{ Name = 'CLI';    Project = 'src\wc3ctl\wc3ctl.csproj';           Dist = 'dist';        Exe = 'wc3ctl.exe' }
    @{ Name = 'Studio'; Project = 'src\Wc3.Studio\Wc3.Studio.csproj';   Dist = 'dist-studio'; Exe = 'Wc3.Studio.exe' }
    @{ Name = 'MCP';    Project = 'src\Wc3.Mcp\Wc3.Mcp.csproj';         Dist = 'dist-mcp';    Exe = 'Wc3.Mcp.exe' }
)

$roots = @($repo) + $Also
$results = @()

foreach ($root in $roots) {
    foreach ($t in $targets) {
        $proj = Join-Path $repo $t.Project
        $out = Join-Path $root $t.Dist
        Write-Host "publishing $($t.Name) -> $out" -ForegroundColor Cyan

        # A locked exe (Studio still open) is the common failure, so report it plainly and keep going
        # rather than aborting the whole run and leaving the other targets stale.
        # Into a fresh folder, then swapped in. Publishing over the old folder keeps any older-version
        # DLL whose file date is newer, which once left the CLI unable to start its MCP server.
        $fresh = "$out.new"
        if (Test-Path $fresh) { Remove-Item -Recurse -Force $fresh }
        & dotnet publish $proj -c $Configuration --self-contained -r $Runtime -o $fresh | Out-Null
        if ($LASTEXITCODE -eq 0 -and (Test-Path $out)) {
            try { Remove-Item -Recurse -Force $out } catch { $LASTEXITCODE = 1 }
        }
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  FAILED ($($t.Name)). If this is Studio, close it and re-run." -ForegroundColor Red
            $results += [pscustomobject]@{ Target = $t.Name; Path = $out; Stamp = 'FAILED' }
            continue
        }
        Move-Item $fresh $out

        $exe = Join-Path $out $t.Exe
        $stamp = if (Test-Path $exe) { (Get-Item $exe).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') } else { 'missing' }
        $results += [pscustomobject]@{ Target = $t.Name; Path = $exe; Stamp = $stamp }
    }
}

Write-Host ''
Write-Host 'Published binaries:' -ForegroundColor Green
# Emitted as plain strings, not table objects. Format-Table output cannot be piped onward
# (Select-Object over it throws inside the formatter), and callers do pipe this.
foreach ($r in $results) { Write-Host ("  {0,-6} {1,-19} {2}" -f $r.Target, $r.Stamp, $r.Path) }
if ($results.Stamp -contains 'FAILED') { exit 1 }
