# Tick performance regression

This is an isolated, command-line-gated single-player test. The probe assembly is never installed in the normal mod folder.

`Run.ps1` expects `BuildValidation/<ValidationFolder>/Payload` to contain the frozen mod load folders and `LoadFolders.xml`, plus `BaselineBuild/NivarianCommander.dll` and `CandidateBuild/NivarianCommander.dll`. Before editing, rebuild the installed source and require its DLL hash to match the installed binary. Build the candidate to its separate directory. Compile `Probe.csproj` in Release, then run:

```powershell
./Tests/TickPerformance/Run.ps1 -Label Baseline -Baseline -ValidationFolder <folder>
./Tests/TickPerformance/Run.ps1 -Label Candidate -ValidationFolder <folder>
./Tests/TickPerformance/Run.ps1 -Label PairedReads -ReadBenchmark -ValidationFolder <folder>
./Tests/NutrientRefill/Run.ps1 -Label Refill -ValidationFolder <folder>
```

Each run creates its own game copy, configuration and save directory, records input hashes and its owned PID, and runs muted/offscreen. A candidate is accepted only with `complete`, no `failed`, unchanged input hashes and a passing `result.json`. Failed attempts remain in separate run directories.

Coverage:

- 6,000 irrelevant map items, 48 enemy buildings and a native non-Pawn drone; exact target-order comparison against the previous full-map LINQ algorithm, faction changes, movement, despawn/respawn and multiple target limits.
- Real monolith damage pulse, target count/order, original cooldown and spacetime-wave cooldown transitions.
- Both tower types: original nutrition/haul/operation predicates, physical food and numeric buffer, same-tick split/merge/stack change/rot, switch, repair, starvation and cloak range.
- Exact per-call nutrition consumption, save/load against the numeric values actually serialized by Verse, and the existing refill probe's four real automatic/forced hauling jobs.
- Five samples per initial microbenchmark. `-ReadBenchmark` additionally alternates seven long old-algorithm/current getter batches in one paused process to distinguish very small method costs from process-to-process timing noise. Reference getters keep the buffer and container fixed outside the timing loop; some compound references use delegates, so their costs are approximate.

These are controlled microbenchmarks and functional checks, not whole-game TPS measurements, a representative player-save test, or a two-client Multiplayer certification. No timing values or caches affect the normal simulation.
