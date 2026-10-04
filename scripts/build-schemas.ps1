# build-schemas.ps1
# Generates a per-build schema folder for every Warcraft III build in jass-history.
# Usage: powershell -File scripts\build-schemas.ps1 [-JassHistoryPath <path>] [-SchemaDir <path>]
#
# Requirements:
#   - jass-history repo cloned to disk (at <JassHistoryPath>)
#   - wc3ctl built and available (src/wc3ctl/bin/Release/net8.0/win-x64/publish/wc3ctl.exe)
#   - .NET 8 SDK installed

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$JassHistoryPath,
    [string]$SchemaDir,
    [string]$ApMapsPath,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Resolve paths
$loc = (Get-Location).Path
$defaultSchemas = $loc + '\data\schemas'
if (-not $SchemaDir) { $SchemaDir = $defaultSchemas }

$repoRoot = $loc
$wc3ctlExe = Join-Path $repoRoot 'src\wc3ctl\bin\Release\net8.0\win-x64\publish\wc3ctl.exe'
$wc3ctlDotnet = Join-Path $repoRoot 'src\wc3ctl\wc3ctl.csproj'

# Choose the executable to use
$usePublished = Test-Path $wc3ctlExe
$cmd = if ($usePublished) { $wc3ctlExe } else { 'dotnet' }
$args0 = if ($usePublished) { @() } else { @('run', '--project', $wc3ctlDotnet, '-c', 'Release', '--') }

Write-Host "Using: $cmd $(if ($usePublished) { '(published)' } else { '(source)' })"
Write-Host "Schema dir: $SchemaDir"
Write-Host "Jass-history: $JassHistoryPath`n"

# Build the list of builds to process
$builds = @()

# --- Phase 1: war3extract builds ---
$war3extractRoot = Join-Path $JassHistoryPath 'war3extract'
if (Test-Path $war3extractRoot) {
    Get-ChildItem $war3extractRoot -Directory | ForEach-Object {
        $buildName = $_.Name
        $buildPath = $_.FullName

        # Find script files in this build
        $hasCommonJ = Test-Path (Join-Path $buildPath 'scripts\common.j')
        $hasBlizzardJ = Test-Path (Join-Path $buildPath 'scripts\blizzard.j')
        $hasCommonAi = Test-Path (Join-Path $buildPath 'scripts\common.ai')

        # Skip builds with no scripts at all
        if (-not ($hasCommonJ -or $hasBlizzardJ -or $hasCommonAi)) { return }

        $builds += [PSCustomObject]@{
            Name = $buildName
            Path = $buildPath
            HasCommonJ = $hasCommonJ
            HasBlizzardJ = $hasBlizzardJ
            HasCommonAi = $hasCommonAi
            Source = 'war3extract'
        }
    }
}

# --- Phase 3: MatthewMarinets/war3-ap-maps full builds ---
if (-not $ApMapsPath) {
    Write-Host "  Skipping war3-ap-maps builds: -ApMapsPath not provided."
} elseif (Test-Path $ApMapsPath) {
    $apMapsRoot = $ApMapsPath
    $apMapsPathProvided = $true
    Get-ChildItem (Join-Path $apMapsRoot 'gamedata') -Directory | ForEach-Object {
        $folderName = $_.Name
        $buildPath = $_.FullName

        # Map folder name to build version
        $versionMap = @{
            '1_30_4' = '1.30.4.11274'
        }
        $buildVersion = $versionMap[$folderName]
        if (-not $buildVersion) {
            Write-Host "  skipped $folderName, no known build version"
            return  # inside ForEach-Object, continue would end the whole script
        }

        # Find sub-layers (melee_v0, custom_v0, etc.)
        $layers = Get-ChildItem $buildPath -Directory | Where-Object {
            $_.Name -match '^(melee_v0|custom_v[0-9]+)$'
        }

        $baseHasUnits = Test-Path (Join-Path $buildPath 'units')
        $baseHasDoodads = Test-Path (Join-Path $buildPath 'doodads')
        $baseHasUi = Test-Path (Join-Path $buildPath 'ui')
        $baseHasScripts = Test-Path (Join-Path $buildPath 'scripts')
        $baseHasStrings = Test-Path (Join-Path $buildPath 'enus-strings')

        $builds += [PSCustomObject]@{
            Name = $buildVersion
            Path = $buildPath
            HasCommonJ = $baseHasScripts -and (Test-Path (Join-Path $buildPath 'scripts\common.j'))
            HasBlizzardJ = $baseHasScripts -and (Test-Path (Join-Path $buildPath 'scripts\blizzard.j'))
            HasCommonAi = $baseHasScripts -and (Test-Path (Join-Path $buildPath 'scripts\common.ai'))
            Source = 'ap-maps'
            FolderName = $folderName
            HasLayers = $layers.Count -gt 0
            Layers = $layers | ForEach-Object { $_.Name }
            HasUnits = $baseHasUnits
            HasDoodads = $baseHasDoodads
            HasUi = $baseHasUi
            HasStrings = $baseHasStrings
        }
    }
}

Write-Host "Found $($builds.Count) build(s) to process`n"

# Create schema directory
if (-not (Test-Path $SchemaDir)) {
    New-Item -ItemType Directory -Path $SchemaDir -Force | Out-Null
}

$successCount = 0
$skipCount = 0
$failCount = 0

# Process each build
foreach ($build in $builds) {
    $buildDir = Join-Path $SchemaDir $build.Name
    Write-Host "Processing: $($build.Name) (source: $($build.Source))..."

    # Check if we need to skip (rerun check)
    if (-not $Force) {
        $existingManifest = Join-Path $buildDir 'manifest.json'
        $skip = $false
        if (Test-Path $existingManifest) {
            $existing = Get-Content $existingManifest | ConvertFrom-Json
            $inputSha256s = @()
            foreach ($srcFile in @('common.j', 'blizzard.j', 'common.ai')) {
                $srcPath = Join-Path $build.Path ('scripts\' + $srcFile)
                if (Test-Path $srcPath) {
                    $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash(
                        [System.IO.File]::ReadAllBytes($srcPath))
                    $inputSha256s += [System.BitConverter]::ToString($hash).Replace('-', '').ToLower()
                }
            }
            $inputSha256s = $inputSha256s | Sort-Object
            $existingInputs = @($existing.inputs | ForEach-Object { $_.sha256 }) | Sort-Object
            if (($inputSha256s -join '') -ne ($existingInputs -join '')) {
                $skip = $false
            } else {
                $skip = $true
            }
        }
        if ($skip) {
            Write-Host "  Skipped (unchanged)"
            $skipCount++
            continue
        }
    }

    # Clean and rebuild
    if (Test-Path $buildDir) {
        if (-not $build.Name) {
            Write-Host "  ERROR: build name is empty, refusing Remove-Item on $buildDir (Failed)"
            $failCount++
            continue
        }
        $buildDirResolved = (Resolve-Path $buildDir).Path
        $schemaDirResolved = (Resolve-Path $SchemaDir).Path
        if ((Split-Path $buildDirResolved -Parent) -ne $schemaDirResolved) {
            Write-Host "  ERROR: $buildDir is not strictly inside $SchemaDir, refusing Remove-Item (Failed)"
            $failCount++
            continue
        }
        Remove-Item $buildDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

    # Try running wc3ctl gamedata schema
    $schemaArgs = @($args0, 'gamedata', 'schema', $buildDir, '--from', $build.Path, '--build', $build.Name)

    try {
        $output = & $cmd @schemaArgs 2>&1
        $exitCode = $LASTEXITCODE

        if ($exitCode -ne 0) {
            Write-Host "  FAILED (exit $exitCode):"
            $output | Where-Object { $_ } | ForEach-Object { Write-Host "    $_" }
            $failCount++
            continue
        }

        Write-Host "  OK"
        $successCount++
    } catch {
        Write-Host "  EXCEPTION: $_"
        $failCount++
    }
}

Write-Host "`n=== Summary ==="
Write-Host "  Total builds found:  $($builds.Count)"
Write-Host "  Successfully built:  $successCount"
Write-Host "  Skipped (unchanged): $skipCount"
Write-Host "  Failed:              $failCount"

# Build index.json through the CLI
Write-Host "`nBuilding index.json through schema-index..."
$schemaIndexArgs = @($args0, 'gamedata', 'schema-index', $SchemaDir)
& $cmd @schemaIndexArgs
$indexExit = $LASTEXITCODE
if ($indexExit -ne 0) {
    Write-Host "`nERROR: schema-index exited with $indexExit"
} else {
    Write-Host "`nIndex built successfully."
}

# Check git status for unwanted files
$gitStatus = git -C $loc status --porcelain 2>&1
$scriptFiles = $gitStatus | Where-Object { $_ -match '^\? ' -and $_ -match 'data/schemas/.*\.(j|ai)$' }
if ($scriptFiles.Count -gt 0) {
    Write-Host "`nWARNING: $scriptFiles.Count .j/.ai files would be committed:"
    $scriptFiles | ForEach-Object { Write-Host "  $_" }
    Write-Host "  Do NOT commit these files."
} else {
    Write-Host "`nNo .j/.ai files committed (good)."
}
