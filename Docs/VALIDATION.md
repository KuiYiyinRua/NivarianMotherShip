# Release validation: 0.9.9

This package includes all current expansion source, the core and five optional Multiplayer assemblies, game assets, five-language localization, build projects, tools and documentation. Game/dependency assemblies, player saves, test probes, private desync investigations, old nested packages and historical release folders are excluded.

## Build and identity

Core and all five adapter source projects compile with zero warnings and errors. Localization verifies 523 runtime keys and 339 definition fields across five languages.

Core file/informational version is 0.9.9; its binding version stays at 0.9.8. The five tested adapter DLLs retain their existing identities because third-party compatibility code can resolve their owners by MVID. They are not replaced merely to change a display version.

## Focused runtime checks

- Settings: default off; only mission conditions bypassed. All research progress remains unchanged, technology prerequisites and support mothership levels remain required. Three toggle cycles and a cold save reload pass. The former complete-all setting is not carried forward; already finished research remains finished.
- Storm Ship: the author-adjusted native damage is 20 direct and 25 blast, with 0.8/0.35 armor penetration and building damage of 100/125. Strict 3×3 bounds and once-per-building blast processing pass. Native world travel, battery delivery/refund, fuel use and two-phase field recall also pass.
- Multiplayer setting: two real peers with different local preferences pass the frozen world/map/Rand/research equality gate. The non-host invokes the production synchronized setter for three on/off cycles. Mission gates follow the shared option while research remains incomplete. At least 10,000 shared server frames and exact final receipts pass. This is a synchronized-method test, not a mouse-driven checkbox test.

## Limits

No new full-modpack run, 120,000-world-tick soak, async-time matrix or three cold Multiplayer rejoins is claimed. Prior feature-specific results remain historical evidence. All players must exit and restart RimWorld on identical files.

GitHub workflow configuration remains local because the current credential lacks workflow scope. Local builds, localization checks and runtime tests are the verification evidence; no GitHub Actions success is claimed.

Local test evidence: `BuildValidation/WorkshopRelease-20261013`. Machine-readable summary: `Workshop/release-validation.json`.

## Exact deployed assemblies

- `1.6/Assemblies/NivarianCommander.dll`: `789b74c27aaa5b883361ecaef19dd8a637a28e5c7cf7edf9d7c3b515b6b1a56a`
- `Multiplayer/Assemblies/NivarianCommander.Multiplayer.dll`: `552e48477c20b373c8f9752ffb1c8c07158342eb35ecb6079d7c9cdfdefbce45`
- `Multiplayer/Assemblies/NivarianCommander.SageShip.Multiplayer.dll`: `65c844c99de8b3d5734ce32a4d8001e36e791b92bee12739e2ce48c7ca8b0918`
- `Multiplayer/Assemblies/NivarianCommander.ShipUpgrades.Multiplayer.dll`: `c5c134ba0cb521778c8116be92cb4c0d985aa12d96a850ea980cb1544789142c`
- `Multiplayer/Assemblies/NivarianCommander.StormShip.Multiplayer.dll`: `befafe23a71ce90a696c3a1bf2c43cef84341933057c9fe67e0fbe71a19a6222`
- `Multiplayer/Assemblies/NivarianCommander.WarpPrism.Multiplayer.dll`: `2b5b7f4f42010c8e5d006736d5911a5aa7c320b5350d52dff3c9c507503bec75`
