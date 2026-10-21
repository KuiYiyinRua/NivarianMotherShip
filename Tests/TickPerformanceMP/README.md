# Tick optimization multiplayer regression

This probe is loaded only in an isolated test mod. `-nmc-tick-mp` enables it; the production assemblies contain no probe code.

## Inputs and isolation

`Run.ps1` uses the frozen payload in `BuildValidation/TickPerformanceMP-20261021/Candidate`, the installed game/Multiplayer dependencies, and the locally built probe. Every run needs a new label. It copies the game into a short, unique TEMP path, with separate Host/Client save data and logs under the validation folder. Game assets use read-only directory junctions; mod payloads are copied and hashed. Only controller-owned process identities are stopped.

The controller verifies the core SHA-256, loaded module MVID, both mod lists, seed, required synchronization registrations, and all immutable inputs. The host loads the seed and starts a normal-time server. The client joins from the menu through the official `-connect` path. Both peers compare a frozen baseline before simulation starts.

## Coverage

Three rounds each contain eight stages:

1. Cancel an uncommitted nutrition slider, reopen and commit targets 1/2/3, then disable automatic refill through the production synchronized entry points.
2. Enable automatic refill and execute native `HaulToContainer` jobs for both tower types.
3. Disable automatic refill and execute explicit refill jobs above the automatic threshold.
4. Switch off both towers; check generator output, downstream lights and monolith power.
5. Switch on; wait for the native power net to restore all consumers.
6. Empty both stores; check loss of production and downstream power.
7. Deliver real food into the empty towers and check recovery.
8. Change a target's faction, move another out of range, spawn a hostile pawn and natural rock, and exercise a short spacetime-wave interval.

Every real monolith pulse is checked against the original full-map distance/ThingID ordering. Building damage and stun durations are checked immediately; pawn injuries and stun are checked through native health/control state. All six nutrition/operation getters are checked against uncached reference formulas. Only the client repeatedly reads inspection text and tower properties between ticks.

Both peers emit identical action receipts and state records every 250 world ticks. At completion they freeze through Multiplayer's normal packet and compare full world/map Rand states, nutrition, food stacks, production counters, currency, power consumers and attack state. A normal soak runs at least 120,000 shared world ticks with desync tracing disabled. Probe maintenance keeps target buildings alive and workers' needs filled; it replenishes controlled food stocks between hauling stages and only adds soak food when required. This is a controlled regression fixture, not a representative player save.

## Run

From the mod root, after freezing `Candidate`:

```powershell
dotnet build Tests/TickPerformanceMP/Probe.csproj -c Release
& Tests/TickPerformanceMP/Run.ps1 -Label Seed-New -Seed
$seed = Join-Path (Get-Location) 'BuildValidation/TickPerformanceMP-20261021/Seed-New/Host/Saves/TickMPSeed.rws'
& Tests/TickPerformanceMP/Run.ps1 -Label Smoke-New -Trace -Ticks 20000 -SeedSave $seed
& Tests/TickPerformanceMP/Run.ps1 -Label Soak-New -Ticks 120000 -SeedSave $seed
```

Run one process pair at a time. Never rebuild a loaded probe or overwrite a frozen candidate. `result.json` must say `PASS`; completion of the controller command alone is insufficient. Preserve failed runs for diagnosis. Optional-mod startup messages and the pre-existing reflection-only InputLegacyModule warning must be distinguished from target resolution, simulation exceptions, desync and connection failures.
