# scripts/smoke-mcp.ps1
# Starts a published MCP server the way an AI app does, over stdio, and checks it answers.
# It sends initialize and tools/list, waits for each reply before sending more (closing stdin
# early makes the server exit before it answers), and fails unless at least -MinTools tools come
# back. The release workflow runs it against the exe it is about to ship.
#
#   pwsh scripts/smoke-mcp.ps1 -Exe dist\wc3ctl.exe -Arguments 'mcp serve'
#   pwsh scripts/smoke-mcp.ps1 -Exe dist-mcp\Wc3.Mcp.exe

param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Arguments = '',
    [int] $MinTools = 50,
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$psi = [System.Diagnostics.ProcessStartInfo]::new((Resolve-Path $Exe).Path, $Arguments)
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
$p = [System.Diagnostics.Process]::Start($psi)
$p.StandardInput.AutoFlush = $true
# Drain stderr in the background so a chatty log cannot fill the pipe and stall the server.
$null = $p.StandardError.ReadToEndAsync()

function Send($json) { $p.StandardInput.Write($json + "`n") }

function Receive([int]$id) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $read = $p.StandardOutput.ReadLineAsync()
        if (-not $read.Wait([TimeSpan]::FromSeconds($TimeoutSeconds))) { break }
        $line = $read.Result
        if ($null -eq $line) { throw "the server closed stdout before answering request $id" }
        $msg = $line | ConvertFrom-Json
        if ($msg.id -eq $id) { return $msg }
    }
    throw "no answer to request $id within $TimeoutSeconds s"
}

try {
    Send '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"0"}}}'
    $init = Receive 1
    Send '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    Send '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
    $tools = @((Receive 2).result.tools)
    Write-Host "$($init.result.serverInfo.name) $($init.result.serverInfo.version), $($tools.Count) tools"
    if ($tools.Count -lt $MinTools) { throw "expected at least $MinTools tools" }
} finally {
    $p.StandardInput.Close()
    if (-not $p.WaitForExit(20000)) { $p.Kill() }
}
