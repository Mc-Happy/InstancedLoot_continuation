---
name: deploy
description: Copy the built InstancedLoot.dll into the local r2modman BepInEx plugins folder so it can be tested in-game. Use after a Release build, when asked to deploy/install the mod locally.
disable-model-invocation: true
---

# Deploy InstancedLoot locally

Copies the freshly built mod DLL into the local r2modman profile so it loads next time Risk of Rain 2 starts.

## Steps

1. Build Release with native dotnet (it's on PATH via `.bashrc`):

   ```bash
   dotnet build --configuration Release
   ```

   Confirm the artifact exists:

   ```bash
   ls -l InstancedLoot/bin/Release/netstandard2.1/InstancedLoot.dll
   ```

2. Copy it to the r2modman plugins folder:

   ```bash
   cp InstancedLoot/bin/Release/netstandard2.1/InstancedLoot.dll \
     "/mnt/c/Users/Radek/AppData/Roaming/r2modmanPlus-local/RiskOfRain2/profiles/Default/BepInEx/plugins/InstancedLoot/InstancedLoot/"
   ```

3. Confirm the copy and report the artifact's mtime so the user can tell it's fresh:

   ```bash
   ls -l "/mnt/c/Users/Radek/AppData/Roaming/r2modmanPlus-local/RiskOfRain2/profiles/Default/BepInEx/plugins/InstancedLoot/InstancedLoot/InstancedLoot.dll"
   ```

4. Remind the user to (re)launch Risk of Rain 2 via r2modman for the new DLL to take effect.
