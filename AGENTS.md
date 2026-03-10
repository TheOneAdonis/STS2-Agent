# Repository Guidelines

## Project Structure & Module Organization
`STS2AIAgent/` contains the .NET 9 mod entrypoint, HTTP router, and in-game action/state services. `mcp_server/src/sts2_mcp/` wraps the local mod HTTP API as a FastMCP server. `scripts/` holds PowerShell automation for build and smoke testing. `tools/pck_builder/` contains the Godot packer used by the build script. `docs/` stores setup and troubleshooting notes. `extraction/decompiled/` is read-only reference material from the game; do not edit it. Generated artifacts land in `build/mods/` and `STS2AIAgent/bin/` or `STS2AIAgent/obj/`.

## Build, Test, and Development Commands
`dotnet build "STS2AIAgent/STS2AIAgent.csproj"` compiles the mod DLL against local STS2 assemblies.

`powershell -ExecutionPolicy Bypass -File "scripts/build-mod.ps1"` builds the DLL, packs `mod_manifest.json`, and installs the mod into the game `mods/` folder.

`powershell -ExecutionPolicy Bypass -File "scripts/test-mod-load.ps1"` launches the game briefly and checks `http://127.0.0.1:8080/health`.

`cd "mcp_server"` then `uv sync` and `uv run sts2-mcp-server` installs Python dependencies and starts the MCP server over stdio.

## Coding Style & Naming Conventions
Use 4-space indentation across C#, Python, and PowerShell. Follow existing C# patterns: file-scoped namespaces, `PascalCase` for types and methods, `_camelCase` for private fields, and `snake_case` only for serialized DTO fields that must match the wire format. Keep nullable reference types enabled. In Python, keep type hints on public functions and use `snake_case` for tool names, modules, and environment variables.

## Testing Guidelines
There is no dedicated automated test project yet, so every behavior change needs a smoke check. At minimum, rerun `scripts/build-mod.ps1` and `scripts/test-mod-load.ps1` for mod changes. For `mcp_server` changes, start the server with `uv run sts2-mcp-server` and verify it can read `/health` or `/state` from a running mod. Add future tests close to the owning module, not under `extraction/`.

## Commit & Pull Request Guidelines
This repository has no stable Git history yet, so start with Conventional Commits such as `feat: add reward flow action` or `fix: guard missing combat state`. Keep pull requests focused to one module or workflow. Include a short summary, validation commands, required local prerequisites, and screenshots or log snippets when game UI or HTTP behavior changes.

## Configuration Tips
Keep machine-specific paths in `STS2AIAgent/local.props`, script parameters, or environment variables such as `STS2_API_BASE_URL` and `STS2_API_TIMEOUT_SECONDS`. Do not hardcode personal Steam or Godot installation paths into tracked files.
