# Donation container compatibility regression (RimWorld 1.7)

Issue #2 reports a null reference in `ThingOwnerUtility.GetAllThingsRecursively` while the shop rebuilds its donation list. This probe uses an isolated real game, a player home map, a powered orbital trade beacon, ground goods and two spawned container buildings. Test-only owner hooks inject malformed holder graphs without changing the production mod or requiring the reporter's unknown container mod.

Copy a complete game and matching Harmony, Alien Races, Nivarian Race and MotherShip mods into `ValidationRoot/Game`. Use unique run labels. The controller requires PowerShell 7 and .NET 10, creates separate savedata, freezes input hashes, records the loaded core hash, captures screenshots and the actual Waylith log, and manages only its own process. Do not point it at the player's live game directory.

```powershell
./Tests/DonationCompat17/Run.ps1 -ValidationRoot D:/Validation/DonationCompat17 -Label Baseline-R1 -StandaloneRuntime
./Tests/DonationCompat17/Run.ps1 -ValidationRoot D:/Validation/DonationCompat17 -Label Fixed-R1 -StandaloneRuntime
./Tests/DonationCompat17/Run.ps1 -ValidationRoot D:/Validation/DonationCompat17 -Label Embedded-R1
```

Install the r10 core before the baseline and the candidate before fixed runs. For `-StandaloneRuntime`, put RimUnity under `Game/Mods/RimUnity`. The optional Vulkan driver manifest must identify a locally installed driver. The exact `-nmc17-donation-compat` flag gates all probe activity.

Coverage includes normal, ownerless, null-child, throwing child/direct/owner/item getters, failed orbital enumeration, repeated/cyclic holder graphs and cyclic parent chains. Unsafe graph tests that would hang native traversal are skipped on the original build. Assertions require actual container-scan reachability, healthy goods remaining visible, no duplicated rows, pawn inventory exclusion, and recovery after repairing a holder. Three real donation-window opens exercise different failures. The real review/submit callback must remove the selected ground/container quantities, credit exact points, reject stale repeat requests and reject goods from an unreadable container without touching unselected goods.

Inspect raw logs and screenshots as well as assertions. The known upstream `AncientHeavyBridge` missing-def error is present in the control loadout. This is isolated single-player evidence; it does not test the reporter's save, full modpack or 1.7 Multiplayer. Probe DLLs must never be included in release assembly folders.
