param(
    [string]$ProjectRoot = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "repo-env.ps1")

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Host "[preflight] $Name"
    & $Action
    Write-Host "[preflight] OK - $Name"
}

$ProjectRoot = Resolve-RepoRoot -InputRoot $ProjectRoot

$modProject = Join-Path $ProjectRoot "STS2AIAgent/STS2AIAgent.csproj"
$desktopProject = Join-Path $ProjectRoot "STS2AIAgent.Desktop/STS2AIAgent.Desktop.csproj"
$buildScript = Join-Path $ProjectRoot "scripts/build-mod.ps1"
$testScript = Join-Path $ProjectRoot "scripts/test-mod-load.ps1"
$stateInvariantScript = Join-Path $ProjectRoot "scripts/test-state-invariants.ps1"
$releaseDoc = Join-Path $ProjectRoot "README.md"
$requiredDocs = @(
    (Join-Path $ProjectRoot "README.md"),
    (Join-Path $ProjectRoot "STS2AIAgent/mod_manifest.json"),
    (Join-Path $ProjectRoot "STS2AIAgent/CreativeAI.json")
)

Invoke-Step -Name "Build mod project ($Configuration)" -Action {
    dotnet build $modProject -c $Configuration | Out-Host
}

Invoke-Step -Name "Build desktop companion ($Configuration)" -Action {
    dotnet build $desktopProject -c $Configuration | Out-Host
}

Invoke-Step -Name "Check release documents" -Action {
    $missing = $requiredDocs | Where-Object { -not (Test-Path $_) }

    if ($missing.Count -gt 0) {
        throw "Missing release docs: $($missing -join ', ')"
    }

    foreach ($doc in $requiredDocs) {
        Write-Host "  - $doc"
    }
}

Write-Host ""
Write-Host "[preflight] Static preflight complete."
Write-Host "[preflight] Manual validation next:"
Write-Host "  1. powershell -ExecutionPolicy Bypass -File `"$buildScript`" -Configuration $Configuration"
Write-Host "  2. powershell -ExecutionPolicy Bypass -File `"$testScript`" -DeepCheck"
Write-Host "  3. powershell -ExecutionPolicy Bypass -File `"$stateInvariantScript`""
Write-Host "  4. Follow the manual checklist in `"$releaseDoc`""
