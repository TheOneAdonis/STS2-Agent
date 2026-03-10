param(
    [string]$Configuration = "Debug",
    [string]$ProjectRoot = "C:/Users/chart/Documents/project/sp",
    [string]$GameRoot = "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2",
    [string]$GodotExe = "C:/Users/chart/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"

$modName = "STS2AIAgent"
$modProject = Join-Path $ProjectRoot "STS2AIAgent/STS2AIAgent.csproj"
$buildOutputDir = Join-Path $ProjectRoot "STS2AIAgent/bin/$Configuration/net9.0"
$stagingDir = Join-Path $ProjectRoot "build/mods/$modName"
$modsDir = Join-Path $GameRoot "mods"
$manifestSource = Join-Path $ProjectRoot "STS2AIAgent/mod_manifest.json"
$dllSource = Join-Path $buildOutputDir "$modName.dll"
$pckOutput = Join-Path $stagingDir "$modName.pck"
$dllTarget = Join-Path $stagingDir "$modName.dll"
$builderProjectDir = Join-Path $ProjectRoot "tools/pck_builder"
$builderScript = Join-Path $builderProjectDir "build_pck.gd"

Write-Host "[build-mod] Building C# mod project..."
dotnet build $modProject -c $Configuration | Out-Host

if (-not (Test-Path $dllSource)) {
    throw "Built DLL not found: $dllSource"
}

New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null
Copy-Item -Force $dllSource $dllTarget

if (-not (Test-Path $manifestSource)) {
    throw "Manifest not found: $manifestSource"
}

Write-Host "[build-mod] Packing mod_manifest.json into PCK..."
& $GodotExe --headless --path $builderProjectDir --script $builderScript -- $manifestSource $pckOutput | Out-Host

if (-not (Test-Path $pckOutput)) {
    throw "PCK output not found: $pckOutput"
}

Write-Host "[build-mod] Preparing game mods directory..."
New-Item -ItemType Directory -Force -Path $modsDir | Out-Null
Copy-Item -Force $dllTarget (Join-Path $modsDir "$modName.dll")
Copy-Item -Force $pckOutput (Join-Path $modsDir "$modName.pck")

Write-Host "[build-mod] Done."
Write-Host "[build-mod] Installed files:"
Write-Host "  $(Join-Path $modsDir "$modName.dll")"
Write-Host "  $(Join-Path $modsDir "$modName.pck")"
