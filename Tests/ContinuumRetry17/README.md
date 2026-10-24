# Continuum retry regression probe (RimWorld 1.7)

Run only in an isolated validation directory containing a complete `Game` copy, matching dependencies in `Game/Mods`, and the candidate MotherShip mod. Never use a player installation as the validation Game directory. The runner creates a new savedata folder, snapshots inputs and previous default logs, starts its own game process and archives evidence under the validation root. It requires PowerShell 7 and the .NET 10 SDK.

```powershell
./Tests/ContinuumRetry17/Run.ps1 -ValidationRoot D:/Validation/ContinuumRetry17 -Label Fixed-R1
./Tests/ContinuumRetry17/Run.ps1 -ValidationRoot D:/Validation/ContinuumRetry17 -Label Cold-Pending-R1 -SavePath D:/Validation/ContinuumRetry17/Fixed-R1/Single/Saves/ContinuumRetry17-Pending.rws -ReloadCase Pending
./Tests/ContinuumRetry17/Run.ps1 -ValidationRoot D:/Validation/ContinuumRetry17 -Label Cold-Overdue-R1 -SavePath D:/Validation/ContinuumRetry17/Baseline-R3/Single/Saves/ContinuumRetry17-Overdue.rws -ReloadCase Overdue
```

Use unique labels. An optional `-VulkanDriverManifest` can select a locally installed Vulkan driver. To generate the baseline save, run the original r8 core with matching runtime/dependencies before installing the fixed candidate into the isolated Game copy. Preserve failed baseline evidence.

The command-line-gated probe covers normal retreat and failed quests, last-stage retry, retries before day seven, missing active/failed references, overdue historical failures, serialization, completed campaigns and initial appearance timing. It invokes real campaign ticks with explicit time acceleration. Cold tests start a new game process and use native autostart; developer mode is disabled by the probe after load. A result marker does not replace inspecting the raw game log.

The r9 candidate passed 36 normal, six pending cold-load and four original-build overdue cold-load checks. This is targeted single-player evidence, not full-modpack, natural combat-duration or Multiplayer certification. No probe DLL belongs in the shipped runtime folders.
