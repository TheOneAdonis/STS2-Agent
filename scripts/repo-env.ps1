Set-StrictMode -Version Latest

function Resolve-RepoRoot {
    param(
        [string]$InputRoot = "",
        [string]$FallbackPath = ""
    )

    $candidate = $InputRoot
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = $env:STS2_REPO_ROOT
    }

    if ([string]::IsNullOrWhiteSpace($candidate)) {
        if ([string]::IsNullOrWhiteSpace($FallbackPath)) {
            $FallbackPath = Join-Path $PSScriptRoot ".."
        }

        return [System.IO.Path]::GetFullPath($FallbackPath)
    }

    return (Resolve-Path $candidate).Path
}

function Resolve-OptionalPath {
    param([string]$PathValue = "")

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    return (Resolve-Path $PathValue).Path
}

function Add-UniqueString {
    param(
        [System.Collections.Generic.List[string]]$Items,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    if (-not $Items.Contains($Value)) {
        $Items.Add($Value)
    }
}

function Get-Sts2SteamLibraries {
    $libraries = [System.Collections.Generic.List[string]]::new()
    $defaultSteamRoot = "C:\Program Files (x86)\Steam"

    if (Test-Path $defaultSteamRoot) {
        Add-UniqueString -Items $libraries -Value $defaultSteamRoot
    }

    $libraryFoldersPath = Join-Path $defaultSteamRoot "steamapps\libraryfolders.vdf"
    if (Test-Path $libraryFoldersPath) {
        $content = Get-Content -Path $libraryFoldersPath -Raw
        foreach ($match in [regex]::Matches($content, '"path"\s+"(?<path>[^"]+)"')) {
            $pathValue = $match.Groups["path"].Value -replace '\\\\', '\'
            if (Test-Path $pathValue) {
                Add-UniqueString -Items $libraries -Value $pathValue
            }
        }
    }

    return $libraries
}

function Resolve-Sts2GameRoot {
    param([string]$GameRoot = "")

    $explicit = Resolve-OptionalPath -PathValue $GameRoot
    if ($explicit) {
        return $explicit
    }

    $envGameRoot = Resolve-OptionalPath -PathValue $env:STS2_GAME_ROOT
    if ($envGameRoot) {
        return $envGameRoot
    }

    foreach ($steamRoot in Get-Sts2SteamLibraries) {
        $candidate = Join-Path $steamRoot "steamapps\common\Slay the Spire 2"
        if (Test-Path (Join-Path $candidate "SlayTheSpire2.exe")) {
            return $candidate
        }
    }

    throw "Unable to locate 'Slay the Spire 2'. Set STS2_GAME_ROOT or pass -GameRoot."
}

function Resolve-Sts2ExePath {
    param(
        [string]$ExePath = "",
        [string]$GameRoot = ""
    )

    $explicit = Resolve-OptionalPath -PathValue $ExePath
    if ($explicit) {
        return $explicit
    }

    $envExePath = Resolve-OptionalPath -PathValue $env:STS2_EXE_PATH
    if ($envExePath) {
        return $envExePath
    }

    $resolvedGameRoot = Resolve-Sts2GameRoot -GameRoot $GameRoot
    $candidate = Join-Path $resolvedGameRoot "SlayTheSpire2.exe"
    if (Test-Path $candidate) {
        return $candidate
    }

    throw "Unable to locate SlayTheSpire2.exe under '$resolvedGameRoot'."
}

function Resolve-Sts2DataDir {
    param(
        [string]$DataDir = "",
        [string]$GameRoot = ""
    )

    $explicit = Resolve-OptionalPath -PathValue $DataDir
    if ($explicit) {
        return $explicit
    }

    $envDataDir = Resolve-OptionalPath -PathValue $env:STS2_DATA_DIR
    if ($envDataDir) {
        return $envDataDir
    }

    $resolvedGameRoot = Resolve-Sts2GameRoot -GameRoot $GameRoot
    $candidate = Join-Path $resolvedGameRoot "data_sts2_windows_x86_64"
    if (Test-Path $candidate) {
        return $candidate
    }

    throw "Unable to locate STS2 data directory under '$resolvedGameRoot'."
}

function Resolve-Sts2AppManifestPath {
    param(
        [string]$AppManifestPath = "",
        [string]$GameRoot = ""
    )

    $explicit = Resolve-OptionalPath -PathValue $AppManifestPath
    if ($explicit) {
        return $explicit
    }

    $envManifest = Resolve-OptionalPath -PathValue $env:STS2_APP_MANIFEST_PATH
    if ($envManifest) {
        return $envManifest
    }

    foreach ($steamRoot in Get-Sts2SteamLibraries) {
        $candidate = Join-Path $steamRoot "steamapps\appmanifest_2868840.acf"
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $resolvedGameRoot = Resolve-Sts2GameRoot -GameRoot $GameRoot
    $steamAppsRoot = Split-Path -Parent (Split-Path -Parent $resolvedGameRoot)
    $fallbackCandidate = Join-Path $steamAppsRoot "appmanifest_2868840.acf"
    if (Test-Path $fallbackCandidate) {
        return $fallbackCandidate
    }

    throw "Unable to locate appmanifest_2868840.acf. Set STS2_APP_MANIFEST_PATH or pass -AppManifestPath."
}

function Resolve-Sts2GodotLogPath {
    param([string]$PathValue = "")

    $explicit = Resolve-OptionalPath -PathValue $PathValue
    if ($explicit) {
        return $explicit
    }

    $envLogPath = Resolve-OptionalPath -PathValue $env:STS2_GODOT_LOG_PATH
    if ($envLogPath) {
        return $envLogPath
    }

    return Join-Path $env:APPDATA "SlayTheSpire2\logs\godot.log"
}

function Resolve-GodotExePath {
    param([string]$GodotExe = "")

    $explicit = Resolve-OptionalPath -PathValue $GodotExe
    if ($explicit) {
        return $explicit
    }

    $envGodot = Resolve-OptionalPath -PathValue $env:STS2_GODOT_EXE
    if ($envGodot) {
        return $envGodot
    }

    foreach ($commandName in @("godot4", "godot", "godot-mono")) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command -and $command.Source -and (Test-Path $command.Source)) {
            return $command.Source
        }
    }

    $candidatePaths = @(
        (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Links\godot.exe"),
        (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Links\godot4.exe")
    )

    foreach ($candidate in $candidatePaths) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $packageRoots = [System.Collections.Generic.List[string]]::new()
    foreach ($candidateRoot in @(
            (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages"),
            (Join-Path $env:LOCALAPPDATA "Programs"),
            $env:ProgramFiles
        )) {
        if ($candidateRoot -and (Test-Path $candidateRoot)) {
            Add-UniqueString -Items $packageRoots -Value $candidateRoot
        }
    }

    foreach ($drive in Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue) {
        $godotRoot = Join-Path $drive.Root "godot"
        if (Test-Path $godotRoot) {
            Add-UniqueString -Items $packageRoots -Value $godotRoot
        }
    }

    foreach ($packageRoot in $packageRoots) {
        $consoleExe = Get-ChildItem -Path $packageRoot -Recurse -Filter "*godot*console*.exe" -File -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
        if ($consoleExe) {
            return $consoleExe
        }

        $anyExe = Get-ChildItem -Path $packageRoot -Recurse -Filter "*godot*.exe" -File -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
        if ($anyExe) {
            return $anyExe
        }
    }

    throw "Unable to locate a Godot executable. Set STS2_GODOT_EXE or pass -GodotExe."
}

function Resolve-DotNetExecutable {
    param([string]$RepoRoot = "")

    $resolvedRepoRoot = Resolve-RepoRoot -InputRoot $RepoRoot
    $localDotNet = Join-Path $resolvedRepoRoot ".dotnet\dotnet.exe"
    if (Test-Path $localDotNet) {
        return $localDotNet
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command -and $command.Source -and (Test-Path $command.Source)) {
        return $command.Source
    }

    throw "Unable to locate dotnet. Install the .NET SDK or add repo-local .dotnet/dotnet.exe."
}

function Resolve-UvExecutable {
    $command = Get-Command uv -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command -and $command.Source -and (Test-Path $command.Source)) {
        return $command.Source
    }

    $candidatePaths = [System.Collections.Generic.List[string]]::new()
    foreach ($candidate in @(
            (Join-Path $env:USERPROFILE ".cargo\bin\uv.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\uv\uv.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\Python\Python313\Scripts\uv.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\Python\Python312\Scripts\uv.exe")
        )) {
        Add-UniqueString -Items $candidatePaths -Value $candidate
    }

    foreach ($userDir in Get-ChildItem "C:\Users" -Directory -ErrorAction SilentlyContinue) {
        foreach ($candidate in @(
                (Join-Path $userDir.FullName ".cargo\bin\uv.exe"),
                (Join-Path $userDir.FullName "AppData\Local\Programs\uv\uv.exe"),
                (Join-Path $userDir.FullName "AppData\Local\Programs\Python\Python313\Scripts\uv.exe"),
                (Join-Path $userDir.FullName "AppData\Local\Programs\Python\Python312\Scripts\uv.exe")
            )) {
            Add-UniqueString -Items $candidatePaths -Value $candidate
        }
    }

    foreach ($candidate in $candidatePaths) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Unable to locate uv. Install uv or add it to PATH."
}

function Resolve-PythonExecutable {
    param([string]$RepoRoot = "")

    foreach ($commandName in @("python", "py")) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command -and $command.Source -and (Test-Path $command.Source)) {
            return $command.Source
        }
    }

    $resolvedRepoRoot = Resolve-RepoRoot -InputRoot $RepoRoot
    $venvPython = Join-Path $resolvedRepoRoot "mcp_server\.venv\Scripts\python.exe"
    if (Test-Path $venvPython) {
        return $venvPython
    }

    $candidatePaths = [System.Collections.Generic.List[string]]::new()
    foreach ($candidate in @(
            (Join-Path $env:LOCALAPPDATA "Programs\Python\Python313\python.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\Python\Python312\python.exe")
        )) {
        Add-UniqueString -Items $candidatePaths -Value $candidate
    }

    foreach ($userDir in Get-ChildItem "C:\Users" -Directory -ErrorAction SilentlyContinue) {
        foreach ($candidate in @(
                (Join-Path $userDir.FullName "AppData\Local\Programs\Python\Python313\python.exe"),
                (Join-Path $userDir.FullName "AppData\Local\Programs\Python\Python312\python.exe")
            )) {
            Add-UniqueString -Items $candidatePaths -Value $candidate
        }
    }

    foreach ($candidate in $candidatePaths) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Unable to locate Python. Install Python 3.11+ or create mcp_server\\.venv\\Scripts\\python.exe."
}

function Set-RepoToolingEnvironment {
    param([string]$RepoRoot = "")

    $resolvedRepoRoot = Resolve-RepoRoot -InputRoot $RepoRoot
    $dotnetHome = Join-Path $resolvedRepoRoot ".dotnet_home"
    $nugetRoot = Join-Path $resolvedRepoRoot ".nuget"
    $nugetPackages = Join-Path $nugetRoot "packages"
    $nugetHttpCache = Join-Path $nugetRoot "http-cache"

    foreach ($pathValue in @($dotnetHome, $nugetPackages, $nugetHttpCache)) {
        New-Item -ItemType Directory -Force -Path $pathValue | Out-Null
    }

    $env:DOTNET_CLI_HOME = $dotnetHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:NUGET_PACKAGES = $nugetPackages
    $env:NUGET_HTTP_CACHE_PATH = $nugetHttpCache
    $env:UV_CACHE_DIR = Join-Path $resolvedRepoRoot ".uv-cache"
}
