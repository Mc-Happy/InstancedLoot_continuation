# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

InstancedLoot is a **BepInEx 5 plugin mod for Risk of Rain 2** (C#, `netstandard2.1`, plugin version `3.0.0`). It lets players in multiplayer runs loot items and open interactables (chests, shrines, shops) separately — each player can get an instanced copy of a loot object, with per-object ownership and pickability rules.

## Build & deploy

- Build with native Linux .NET (SDK 8.0 at `~/.dotnet`, on PATH via `.bashrc`): `dotnet build --configuration Release` (target is `netstandard2.1`; CI also uses .NET SDK 8.0). Do **not** use Windows `dotnet.exe` — it fails from a WSL-path working directory.
- Build output: `InstancedLoot/bin/Release/netstandard2.1/InstancedLoot.dll`.
- To test in-game, copy that DLL into the local r2modman plugins folder. Use the `/deploy` skill — it does this copy for you.
- **Path guard:** a PreToolUse hook (`.claude/hooks/guard-wsl-paths.py`) blocks all access to Windows-mounted `/mnt/*` paths except the r2modman deploy directory. Stay on the WSL filesystem; the only `/mnt` path you can touch is the deploy target.
- **GameLibs version (`RiskOfRain2.GameLibs 1.4.0-r.0`) is load-bearing.** A version mismatch breaks MonoMod hook signatures against the game's compiled methods. Don't bump it casually.

## Architecture

The mod patches the game at runtime via MonoMod hooks and routes object-specific behavior through handlers:

- `InstancedLoot.cs` — plugin entry point (`BaseUnityPlugin`, `[BepInPlugin]`).
- `Hooks/` (~28 files) — one hook handler per game method/object being patched. Registered/unregistered centrally by `HookManager.cs`. Hook handlers derive from `AbstractHookHandler`.
- `ObjectHandlers/` — per-object-type logic (e.g. `ChestHandler`, `ShrineChanceHandler`), derived from `AbstractObjectHandler`, managed by `ObjectHandlerManager.cs`.
- `Components/` — runtime Unity components: `InstanceHandler` (core per-player instancing), `InstanceInfoTracker` (ownership + instance mode per object).
- `Networking/SyncInstances.cs` — network sync of instance state (uses R2API.Networking).
- `Enums.cs` — instance mode enums/definitions.
- `Configuration/` — config system, presets, and a migrator.

## Verifying changes

There is **no test framework**. A clean Release build is necessary but not sufficient — features must be verified manually in-game. See the `/verify-ingame` skill for the loop (multiplayer ≥2 players, Captain character for hacking-beacon features, verbose BepInEx logging). Don't claim a feature works without an in-game check.

## Code style

- C# 10 file-scoped namespaces (`namespace InstancedLoot.Hooks;`).
- No analyzer/formatter config in the repo; follow Visual Studio / Rider defaults and match surrounding code.

## Git etiquette

Branch off `master` and open a PR to `master`. Don't commit directly to `master`.
