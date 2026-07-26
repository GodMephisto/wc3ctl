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
        & dotnet publish $proj -c $Configuration --self-contained -r $Runtime -o $out | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  FAILED ($($t.Name)). If this is Studio, close it and re-run." -ForegroundColor Red
            $results += [pscustomobject]@{ Target = $t.Name; Path = $out; Stamp = 'FAILED' }
            continue
        }

        $exe = Join-Path $out $t.Exe
        $stamp = if (Test-Path $exe) { (Get-Item $exe).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') } else { 'missing' }
        $results += [pscustomobject]@{ Target = $t.Name; Path = $exe; Stamp = $stamp }
    }
}

Write-Host ''
Write-Host 'Published binaries:' -ForegroundColor Green
$results | Format-Table -AutoSize
if ($results.Stamp -contains 'FAILED') { exit 1 }
