# install.ps1, installs or updates wc3ctl for the current Windows user.
#
#   irm https://raw.githubusercontent.com/GodMephisto/wc3ctl/master/install.ps1 | iex
#
# It downloads the latest release, checks the SHA-256 of each zip, unpacks the CLI into
# %LOCALAPPDATA%\Programs\wc3ctl and Studio into %LOCALAPPDATA%\Programs\wc3ctl\studio, puts the
# CLI folder on your user PATH, adds a Start menu shortcut for Studio, and sets up every supported
# AI app it finds to use the MCP server (wc3ctl mcp serve). No admin rights are needed. Run it
# again to update.
#
# To pass options, download it first, then run for example
#   .\install.ps1 -Version v0.1.0 -InstallDir D:\Tools\wc3ctl -NoRegister -NoStudio
# -NoPath leaves PATH alone, for a portable copy.
# or set $env:WC3CTL_VERSION, $env:WC3CTL_DIR, $env:WC3CTL_NO_REGISTER=1 or $env:WC3CTL_NO_STUDIO=1
# before the one-liner.

[CmdletBinding()]
param(
    [string]$Version = $(if ($env:WC3CTL_VERSION) { $env:WC3CTL_VERSION } else { 'latest' }),
    [string]$InstallDir = $(if ($env:WC3CTL_DIR) { $env:WC3CTL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\wc3ctl' }),
    [switch]$NoRegister = [bool]$env:WC3CTL_NO_REGISTER,
    [switch]$NoStudio = [bool]$env:WC3CTL_NO_STUDIO,
    [switch]$NoPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = 'GodMephisto/wc3ctl'

if (-not [Environment]::Is64BitOperatingSystem) { throw 'wc3ctl needs 64-bit Windows.' }

function Test-Checksum([string]$zipPath, [string]$sumPath) {
    $expected = ((Get-Content -Path $sumPath -Raw) -split '\s+')[0].Trim().ToLowerInvariant()
    $actual = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { throw "Checksum mismatch for $zipPath. Expected $expected, got $actual." }
    Write-Host "checksum ok, $([IO.Path]::GetFileName($zipPath))"
}

# Downloads one asset and its checksum, verifies it, and unpacks it into $dest, replacing what is there.
function Install-Asset($release, [string]$pattern, [string]$dest, [string]$exeName, [string]$work) {
    $zip = $release.assets | Where-Object { $_.name -like $pattern } | Select-Object -First 1
    $sum = $release.assets | Where-Object { $_.name -eq "$($zip.name).sha256" } | Select-Object -First 1
    if (-not $zip) { throw "Release $($release.tag_name) has no asset matching $pattern." }
    $zipPath = Join-Path $work $zip.name
    Invoke-WebRequest -Uri $zip.browser_download_url -OutFile $zipPath -UseBasicParsing
    if ($sum) {
        Invoke-WebRequest -Uri $sum.browser_download_url -OutFile "$zipPath.sha256" -UseBasicParsing
        Test-Checksum $zipPath "$zipPath.sha256"
    } else {
        Write-Warning "$($zip.name) has no checksum file, so it was not verified."
    }
    $unpacked = Join-Path $work ([IO.Path]::GetFileNameWithoutExtension($zip.name))
    Expand-Archive -Path $zipPath -DestinationPath $unpacked
    if (-not (Test-Path (Join-Path $unpacked $exeName))) { throw "$($zip.name) does not contain $exeName." }

    # Replace the old copy, keeping the studio subfolder when this is the CLI folder.
    if (Test-Path $dest) {
        try {
            Get-ChildItem -Path $dest -Force | Where-Object { $_.Name -ne 'studio' } | Remove-Item -Recurse -Force
        } catch {
            throw "Could not replace $dest. Close Studio and any AI app running wc3ctl, then run this again."
        }
    } else {
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
    }
    Copy-Item -Path (Join-Path $unpacked '*') -Destination $dest -Recurse -Force
}

$api = if ($Version -eq 'latest') { "https://api.github.com/repos/$repo/releases/latest" }
       else { "https://api.github.com/repos/$repo/releases/tags/$Version" }
$release = Invoke-RestMethod -Uri $api -Headers @{ 'User-Agent' = 'wc3ctl-installer' }
Write-Host "wc3ctl $($release.tag_name)"

$studioDir = Join-Path $InstallDir 'studio'
$work = Join-Path ([IO.Path]::GetTempPath()) ("wc3ctl-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Install-Asset $release 'wc3ctl-v*-win-x64.zip' $InstallDir 'wc3ctl.exe' $work
    if (-not $NoStudio) { Install-Asset $release 'wc3ctl-studio-v*-win-x64.zip' $studioDir 'Wc3.Studio.exe' $work }
} finally {
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}
$exe = Join-Path $InstallDir 'wc3ctl.exe'
Write-Host "installed to $InstallDir"

if (-not $NoStudio) {
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'wc3ctl Studio.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = Join-Path $studioDir 'Wc3.Studio.exe'
    $link.WorkingDirectory = $studioDir
    $link.Save()
    Write-Host 'added wc3ctl Studio to the Start menu'
}

# Put the CLI folder on the user PATH once, so wc3ctl works in any new terminal.
# PATH is read and written raw, keeping its registry type, so entries such as
# %USERPROFILE%\go\bin stay as written instead of being expanded into fixed paths.
$envKey = Get-Item -Path 'HKCU:\Environment'
$userPath = $envKey.GetValue('Path', '', 'DoNotExpandEnvironmentNames')
$kind = if ($envKey.GetValueNames() -contains 'Path') { $envKey.GetValueKind('Path') } else { 'ExpandString' }
if ($kind -ne 'String') { $kind = 'ExpandString' }
$parts = @($userPath -split ';' | Where-Object { $_ })
$present = $parts | Where-Object { [Environment]::ExpandEnvironmentVariables($_).TrimEnd('\') -ieq $InstallDir.TrimEnd('\') }
if ($NoPath) {
    Write-Host 'left your PATH unchanged (-NoPath)'
} elseif (-not $present) {
    Set-ItemProperty -Path 'HKCU:\Environment' -Name Path -Value (($parts + $InstallDir) -join ';') -Type $kind
    # Tell Explorer the environment changed, so new terminals see it.
    Add-Type -Namespace Wc3Ctl -Name Native -MemberDefinition '[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, UIntPtr w, string l, uint f, uint t, out UIntPtr r);'
    $r = [UIntPtr]::Zero
    [void][Wc3Ctl.Native]::SendMessageTimeout([IntPtr]0xFFFF, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$r)
    Write-Host 'added to your PATH (open a new terminal to use it)'
}
$env:Path = "$env:Path;$InstallDir"

if ($NoRegister) {
    Write-Host 'Skipped AI app setup. Run  wc3ctl mcp install --all  when ready.'
} else {
    # PATH was handled above (or skipped with -NoPath), so setup leaves it alone.
    & $exe mcp install --all --no-path
}
Write-Host ''
& $exe mcp doctor
