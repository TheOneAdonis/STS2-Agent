param(
    [string]$Configuration = "Debug",
    [switch]$LaunchDesktopUi = $true,
    [switch]$EnableDebugActions,
    [int]$ApiPort = 8081,
    [switch]$KeepExistingProcesses
)

$ErrorActionPreference = "Stop"

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build-mod.ps1") -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "build-mod.ps1 failed."
}

if ($LaunchDesktopUi) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "start-agent-desktop.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "start-agent-desktop.ps1 failed."
    }
}

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "start-game-session.ps1") `
    -EnableDebugActions:$EnableDebugActions `
    -ApiPort $ApiPort `
    -KeepExistingProcesses:$KeepExistingProcesses

