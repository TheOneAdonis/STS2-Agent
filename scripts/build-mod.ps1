param(
    [string]$Configuration = "Debug",
    [string]$ProjectRoot = "",
    [string]$GameRoot = "",
    [string]$GodotExe = "",
    [string]$Sts2DataDir = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "repo-env.ps1")

$ProjectRoot = Resolve-RepoRoot -InputRoot $ProjectRoot
$GameRoot = Resolve-Sts2GameRoot -GameRoot $GameRoot
$GodotExe = Resolve-GodotExePath -GodotExe $GodotExe
$Sts2DataDir = Resolve-Sts2DataDir -DataDir $Sts2DataDir -GameRoot $GameRoot
Set-RepoToolingEnvironment -RepoRoot $ProjectRoot
$dotnetExe = Resolve-DotNetExecutable -RepoRoot $ProjectRoot

$modName = "CreativeAI"
$modProject = Join-Path $ProjectRoot "STS2AIAgent/STS2AIAgent.csproj"
$desktopProject = Join-Path $ProjectRoot "STS2AIAgent.Desktop/STS2AIAgent.Desktop.csproj"
$buildOutputDir = Join-Path $ProjectRoot "STS2AIAgent/bin/$Configuration/net9.0"
$desktopOutputDir = Join-Path $ProjectRoot "STS2AIAgent.Desktop/bin/$Configuration/net9.0-windows"
$stagingDir = Join-Path $ProjectRoot "build/mods/$modName"
$desktopStagingDir = Join-Path $stagingDir "desktop"
$modsDir = Join-Path $GameRoot "mods"
$installedModDir = Join-Path $modsDir $modName
$installedDesktopDir = Join-Path $installedModDir "desktop"
$manifestSource = Join-Path $ProjectRoot "STS2AIAgent/mod_manifest.json"
$externalManifestSource = Join-Path $ProjectRoot "STS2AIAgent/$modName.json"
$modImageSource = Join-Path $ProjectRoot "STS2AIAgent/mod_image.png"
$dllSource = Join-Path $buildOutputDir "$modName.dll"
$pckOutput = Join-Path $stagingDir "$modName.pck"
$dllTarget = Join-Path $stagingDir "$modName.dll"
$builderProjectDir = Join-Path $ProjectRoot "tools/pck_builder"
$builderScript = Join-Path $builderProjectDir "build_pck.gd"

Write-Host "[build-mod] Repo root: $ProjectRoot"
Write-Host "[build-mod] Game root: $GameRoot"
Write-Host "[build-mod] STS2 data dir: $Sts2DataDir"
Write-Host "[build-mod] Godot exe: $GodotExe"

Write-Host "[build-mod] Building C# mod project..."
& $dotnetExe build $modProject -c $Configuration "-p:Sts2DataDir=$Sts2DataDir" | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

Write-Host "[build-mod] Building desktop companion..."
& $dotnetExe build $desktopProject -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "desktop build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $dllSource)) {
    throw "Built DLL not found: $dllSource"
}

New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null
Copy-Item -Force $dllSource $dllTarget
New-Item -ItemType Directory -Force -Path $desktopStagingDir | Out-Null
Copy-Item -Recurse -Force (Join-Path $desktopOutputDir "*") $desktopStagingDir

if (-not (Test-Path $manifestSource)) {
    throw "Manifest not found: $manifestSource"
}

Write-Host "[build-mod] Packing mod_manifest.json into PCK..."
& $GodotExe --headless --path $builderProjectDir --script $builderScript -- $manifestSource $pckOutput | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Godot PCK build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $pckOutput)) {
    throw "PCK output not found: $pckOutput"
}

Write-Host "[build-mod] Preparing game mods directory..."
New-Item -ItemType Directory -Force -Path $modsDir | Out-Null
New-Item -ItemType Directory -Force -Path $installedModDir | Out-Null

$legacyDllPath = Join-Path $modsDir "$modName.dll"
$legacyPckPath = Join-Path $modsDir "$modName.pck"
if (Test-Path $legacyDllPath) {
    Remove-Item -Force $legacyDllPath
}
if (Test-Path $legacyPckPath) {
    Remove-Item -Force $legacyPckPath
}

$staleBaseLibDir = Join-Path $modsDir "BaseLib"
if (Test-Path $staleBaseLibDir) {
    Write-Host "[build-mod] Removing stale BaseLib install from previous experiment..."
    Remove-Item -Recurse -Force $staleBaseLibDir
}

Copy-Item -Force $dllTarget (Join-Path $installedModDir "$modName.dll")
Copy-Item -Force $pckOutput (Join-Path $installedModDir "$modName.pck")
if (Test-Path $installedDesktopDir) {
    Remove-Item -Recurse -Force $installedDesktopDir
}
Copy-Item -Recurse -Force $desktopStagingDir $installedDesktopDir
if (Test-Path $externalManifestSource) {
    Copy-Item -Force $externalManifestSource (Join-Path $installedModDir "$modName.json")
}
if (Test-Path $modImageSource) {
    Copy-Item -Force $modImageSource (Join-Path $installedModDir "mod_image.png")
}

Write-Host "[build-mod] Done."
Write-Host "[build-mod] Installed files:"
Write-Host "  $(Join-Path $installedModDir "$modName.dll")"
Write-Host "  $(Join-Path $installedModDir "$modName.pck")"
Write-Host "  $(Join-Path $installedDesktopDir "CreativeAI.Desktop.exe")"
if (Test-Path (Join-Path $installedModDir "$modName.json")) {
    Write-Host "  $(Join-Path $installedModDir "$modName.json")"
}
