param(
    [string]$ProjectRoot = "",
    [string]$Configuration = "Release",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "repo-env.ps1")

function Resolve-FullPath {
    param([string]$PathValue)

    return [System.IO.Path]::GetFullPath($PathValue)
}

function Get-UniquePath {
    param(
        [string]$BasePath,
        [string]$Extension = ""
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($Extension)) {
        $BasePath
    } else {
        "$BasePath$Extension"
    }

    if (-not (Test-Path $candidate)) {
        return $candidate
    }

    $index = 2
    while ($true) {
        $candidate = if ([string]::IsNullOrWhiteSpace($Extension)) {
            "$BasePath-$index"
        } else {
            "$BasePath-$index$Extension"
        }

        if (-not (Test-Path $candidate)) {
            return $candidate
        }

        $index += 1
    }
}

$ProjectRoot = Resolve-RepoRoot -InputRoot $ProjectRoot

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $ProjectRoot "build/release"
} else {
    $OutputRoot = Resolve-FullPath -PathValue $OutputRoot
}

$manifestPath = Join-Path $ProjectRoot "STS2AIAgent/mod_manifest.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$version = $manifest.version
$releaseBaseName = "creative-ai-v$version-windows"

$buildScript = Join-Path $ProjectRoot "scripts/build-mod.ps1"
$stagingModDir = Join-Path $ProjectRoot "build/mods/CreativeAI"
$releaseDir = Get-UniquePath -BasePath (Join-Path $OutputRoot $releaseBaseName)
$zipPath = Get-UniquePath -BasePath (Join-Path $OutputRoot $releaseBaseName) -Extension ".zip"

$modOutputDir = Join-Path $releaseDir "mod/CreativeAI"

Write-Host "[package-release] Building release mod artifacts..."
powershell -ExecutionPolicy Bypass -File $buildScript -ProjectRoot $ProjectRoot -Configuration $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "build-mod.ps1 failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
New-Item -ItemType Directory -Force -Path $modOutputDir | Out-Null

Copy-Item -Recurse -Force (Join-Path $stagingModDir "*") $modOutputDir

Copy-Item -Path (Join-Path $ProjectRoot "README.md") -Destination (Join-Path $releaseDir "README.md") -Force

Compress-Archive -Path (Join-Path $releaseDir "*") -DestinationPath $zipPath

Write-Host "[package-release] Release directory: $releaseDir"
Write-Host "[package-release] Release zip: $zipPath"
