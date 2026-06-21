---
name: verify-ingame
description: Checklist for manually verifying an InstancedLoot feature in a running Risk of Rain 2 session. Use before claiming a mod feature works, since there is no automated test framework.
---

# Verify InstancedLoot in-game

There is no test framework for this mod. A Release build passing is necessary but not proof. Walk the user through verifying the actual behavior in-game.

## Before testing

1. Build Release and deploy the DLL (use the `/deploy` skill).
2. Enable verbose logging so behavior is observable:
   - Enable the BepInEx console.
   - Set `[Logging] LogLevels = All` in the BepInEx config.
3. Launch Risk of Rain 2 through r2modman (Default profile).

## Test setup

- **Multiplayer is required** for instancing behavior — use **at least 2 players** (a second client or a friend). Single-player won't exercise per-player instancing.
- For **hacking-beacon / Captain** features, deploy the **Captain** character and use the "Beacon: Hacking" ability. Note: auto-pop behavior only triggers when the interaction has a **null player activator** (the beacon fires it, not a human click).

## What to confirm

- Each player sees/loots their own instanced copy according to the configured instance mode.
- Ownership and pickability rules behave as configured (owner-only vs. anyone).
- Pricing is correct for non-owning players (relevant for purchasables / hacking beacon).
- No errors or unexpected warnings in the BepInEx console during the interaction.
- On stage transition, no stale state carries over (e.g. cost dictionaries cleared).

## Reporting

State explicitly what was tested, with how many players, which character, and what the console showed. Do not claim a feature works on a build-only basis.
