param(
    [string]$Configuration = "Debug",
    [string]$ProjectRoot = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "repo-env.ps1")

$ProjectRoot = Resolve-RepoRoot -InputRoot $ProjectRoot
Set-RepoToolingEnvironment -RepoRoot $ProjectRoot
$dotnetExe = Resolve-DotNetExecutable -RepoRoot $ProjectRoot

$projectPath = Join-Path $ProjectRoot "STS2AIAgent.Desktop\\STS2AIAgent.Desktop.csproj"
$outputDir = Join-Path $ProjectRoot "STS2AIAgent.Desktop\\bin\\$Configuration\\net9.0-windows"
$exePath = Join-Path $outputDir "STS2AIAgent.Desktop.exe"

& $dotnetExe build $projectPath -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $exePath)) {
    throw "Desktop UI exe not found: $exePath"
}

Start-Process -FilePath $exePath | Out-Null
Write-Host "[start-agent-desktop] Started $exePath"
