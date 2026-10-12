# pack-schemas.ps1
# Packs the schema dataset into a versioned zip and writes its sha256, the shape the release workflow
# already uses for the binaries (a single "hash  name" line, ascii, no trailing newline).
#
# Usage
#   powershell -File scripts\pack-schemas.ps1 -Source data\schemas -Out dist\wc3ctl-schemas-v1.2.3.zip
#   powershell -File scripts\pack-schemas.ps1 -Source data\schemas -Out dist -Version 1.2.3
#
# The dataset is not committed to git, so it ships as a release asset. This script is the step that
# turns a built data\schemas directory into that asset. When -Out is a directory the version is used to
# name the archive, and when it ends in .zip that path is used as given. A schemas.version entry is
# written into the archive so an extracted dataset knows which release carried it.
#
# It also refreshes sources.json through the CLI when a published wc3ctl is present, so the archive
# carries its own provenance index. Generating the index needs the exe, so with no exe the archive is
# packed without a refreshed index and a note is printed.

[CmdletBinding()]
param(
    [string]$Source,
    [Parameter(Mandatory=$true)]
    [string]$Out,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$loc = (Get-Location).Path
if (-not $Source) { $Source = Join-Path $loc 'data\schemas' }
if (-not (Test-Path $Source)) { throw "source directory not found, $Source" }
$Source = (Resolve-Path $Source).Path

# Version, from the argument, then the newest git tag, then a neutral fallback.
if (-not $Version) {
    $tag = git -C $loc describe --tags --abbrev=0 2>$null
    if ($LASTEXITCODE -eq 0 -and $tag) { $Version = ($tag -replace '^v', '') }
    else { $Version = '0.0.0' }
}

# Resolve the output path, naming the archive after the version when only a directory is given.
if ($Out.EndsWith('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
    $zipPath = [System.IO.Path]::GetFullPath($Out)
} else {
    if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Path $Out -Force | Out-Null }
    $zipPath = Join-Path (Resolve-Path $Out).Path "wc3ctl-schemas-$Version.zip"
}

$parent = Split-Path $zipPath -Parent
if ($parent -and -not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Refresh the provenance index through the CLI when it is available.
$wc3ctlExe = Join-Path $loc 'src\wc3ctl\bin\Release\net8.0\win-x64\publish\wc3ctl.exe'
if (Test-Path $wc3ctlExe) {
    & $wc3ctlExe gamedata sources --schemas $Source -o (Join-Path $Source 'sources.json')
    if ($LASTEXITCODE -ne 0) { throw "gamedata sources failed with $LASTEXITCODE" }
    Write-Host "refreshed sources.json"
} else {
    Write-Host "no published wc3ctl found, packing without refreshing sources.json"
}

$files = Get-ChildItem -Path $Source -Recurse -File | Sort-Object FullName
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $verEntry = $zip.CreateEntry('schemas.version')
    $verWriter = New-Object System.IO.StreamWriter($verEntry.Open())
    $verWriter.Write("$Version`n")
    $verWriter.Dispose()

    foreach ($f in $files) {
        $rel = $f.FullName.Substring($Source.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $zip.Dispose()
}

# sha256 sidecar, the same shape the release workflow writes for the binaries.
$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$name = Split-Path $zipPath -Leaf
Set-Content -Path "$zipPath.sha256" -Value "$hash  $name" -NoNewline -Encoding ascii

Write-Host "packed $($files.Count) file(s) into $zipPath"
Write-Host "wrote $zipPath.sha256"
Write-Host $hash
