# merge-schemas.ps1
# Folds a directory of freshly built schema folders into the dataset at data\schemas.
#
# Usage
#   powershell -File scripts\merge-schemas.ps1 -From <dir> -Into <data\schemas>
#
# A build can be produced more than once from different sources. One pass over jass-history scripts
# yields a script-only folder, and a pass over a full game data dump of the same build yields one that
# also carries object and trigger data. The rule here is one-way, an incoming folder replaces the
# existing one only when the incoming folder has a section the existing one lacks. A script-only build
# therefore never downgrades a build that already carries real object data, and a real-object build
# upgrades a script-only one. Anything with no new section is left as it is.

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$From,
    [Parameter(Mandatory=$true)]
    [string]$Into
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $From)) { throw "source directory not found, $From" }
if (-not (Test-Path $Into)) { New-Item -ItemType Directory -Path $Into -Force | Out-Null }

$From = (Resolve-Path $From).Path
$Into = (Resolve-Path $Into).Path
if ($From -eq $Into) { throw "source and destination are the same directory, $From" }

# The section files a schema folder can hold. Presence of the file is the same signal the coverage
# command reports, and it needs no parse of the manifest.
$sectionFiles = @('objects.json', 'triggers.json', 'script.json')

function Get-PresentSections([string]$Dir) {
    @($sectionFiles | Where-Object { Test-Path (Join-Path $Dir $_) })
}

$added = @()
$upgraded = @()
$left = @()

Get-ChildItem $From -Directory | Sort-Object Name | ForEach-Object {
    $name = $_.Name
    $source = $_.FullName
    if (-not (Test-Path (Join-Path $source 'manifest.json'))) { return }

    $dest = Join-Path $Into $name
    if (-not (Test-Path $dest)) {
        Copy-Item -Path $source -Destination $dest -Recurse -Force
        $added += $name
        return
    }

    $have = Get-PresentSections $dest
    $incoming = Get-PresentSections $source
    $newSections = @($incoming | Where-Object { $_ -notin $have })

    if ($newSections.Count -eq 0) {
        $left += $name
        return
    }

    # Guard the destructive delete, the destination must sit strictly inside the dataset directory.
    if (-not $name) { throw "empty build folder name under $From, refusing to remove anything" }
    $destResolved = (Resolve-Path $dest).Path
    if ((Split-Path $destResolved -Parent) -ne $Into) {
        throw "destination $dest is not strictly inside $Into, refusing to remove it"
    }
    Remove-Item $destResolved -Recurse -Force
    Copy-Item -Path $source -Destination $dest -Recurse -Force
    $upgraded += "$name adds $($newSections -join ', ')"
}

Write-Host "merge-schemas, $($added.Count) added, $($upgraded.Count) upgraded, $($left.Count) left unchanged"
foreach ($a in $added) { Write-Host "  added     $a" }
foreach ($u in $upgraded) { Write-Host "  upgraded  $u" }
foreach ($l in $left) { Write-Host "  left      $l" }
