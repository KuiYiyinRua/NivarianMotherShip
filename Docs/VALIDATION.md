# Release validation: 0.9.8

This release includes the complete expansion C# source, compiled core and optional Multiplayer adapter, build projects, localization resources/tools, documentation and five-language Workshop descriptions. Game/dependency assemblies, player data, test drivers, logs, backups, the stale nested package and old release folders are excluded by the package allowlist.

## Build and artifact evidence

- Before updating the release, the complete current sources rebuilt byte-for-byte to the deployed integrated 0.9.7 binaries: core `9D71FBD6AE9F6EC159529094BF82C040771EA48F43AC7FF37FF315A70883AFFA`, adapter `E0041E9313C17FB523221C4F01B227BB000122073695D0EC495E25EE1656AE39`.
- Isolated 0.9.8 core and adapter Release builds report zero warnings and zero errors. Five-language localization verification reports 491 runtime keys and 308 definition fields.
- Final 0.9.8 core SHA-256: `1CB2DA9D50B36E720CEA84C821850D949BF93754956D86AB89BBC145B6EE4843`. Final adapter SHA-256: `552E48477C20B373C8F9752FFB1C8C07158342EB35ECB6079D7C9CDFDEFBCE45`.
- The archived build and local formal assemblies match. The ZIP and each distributed file are checked against the generated package manifest before upload; Workshop and GitHub release assets are read back after publication.
- The independent release review identified the carrier interceptor manufacture countdown missing the S06 multiplier. The final code applies eligible friendly-building work speed and clamps completion to zero, preserving per-interceptor material costs and all other ship clocks.

## Runtime evidence and limits

The preceding integrated 0.9.7 pair above ran an actual host and remote client on two maps. The non-host invoked three real S06 casts; the first two were cancelled and the third expired naturally. Six functional stages and a checkpoint after 10,006 shared ticks produced eight exactly matching state receipts, including full world/map Rand states. Both peers reported two players, two maps and `desynced=False`.

The user then explicitly requested the end of testing and local, GitHub and Workshop deployment. That run is recorded as `USER_STOPPED`, since its planned 14,000-tick endpoint was not reached. The final carrier manufacture correction and 0.9.8 binaries are compiled and statically inspected; **final 0.9.8 runtime remains unverified**. No additional game process was launched.

No final-version S06 120,000-tick soak, cold rejoin, async-time regression, representative 249-mod user-save run or exhaustive combat/layout test is claimed. Previous individual feature tests remain historical evidence. The next runtime matrix, if requested, is a short final-artifact functional smoke followed by the full-loadout soak and two-map async/rejoin coverage. All Multiplayer peers must completely exit RimWorld and restart the same updated mod version.

Local evidence is archived under `BuildValidation/SpacetimeRetuneMP-20261012` and `BuildValidation/WorkshopRelease-20261012`; test files are not distributed. The public machine-readable summary is `Workshop/release-validation.json`.
