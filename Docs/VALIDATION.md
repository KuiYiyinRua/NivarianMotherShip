# Release validation: 0.9.9-r1

## 0.9.9-r1

- 修复 YaOpt / PerformanceOptimizer 优化层在并行绘制下的组件缓存锁保护，保留缓存优化；重新应用优化器设置后仍保持保护。
- 精简并重写 111 条中文物品、技能和科技说明，突出功能、性能、用途及解锁获取条件。
- 包含完整模组资源、125 份核心源码及 13 份联机源码。核心文件版本保持 0.9.9，程序集绑定版本保持 0.9.8；五份联机适配 DLL 身份不变。
- 已测：1048576 次真实并发缓存查询、600 帧更新、无优化器启动、双端 120000 世界 tick 与最终状态一致性。专项单地图验证，未覆盖完整模组包、异步时间或重连矩阵。
- 更新后所有玩家需重启游戏并使用相同文件。

## Evidence

Nivarian MotherShip optimizer compatibility: local update complete.
Core SHA256: B92EDCF4BF3A728B4736C97D7F996E166B3246C0D7BB9D8C3FEF9C11EFBD388A
Original deployed core and rebuilt baseline matched: 789B74C27AAA5B883361ECAEF19DD8A637A28E5C7CF7EDF9D7C3B515B6B1A56A.
Change: optional mother ship bridge retains YaOpt/PerformanceOptimizer synchronized component caches at startup and after OnPatch. No optimizer option disabled. No gameplay mutation or new synchronization command in production code.
Baseline-SP-R1: expected regression failure, original adapter disables cache lock after applying options with simulation parallelism off. This is a deterministic guard regression, not a replay of the original first corrupting write.
Candidate-SP-R1: PASS. Three OnPatch invocations, PO transpiler retained on FieldWarpVisuals.Update, 1048576 real optimized lookups / 8 threads / 512 map objects / two component types, identities matched, 600 live update frames; no collection corruption/render errors.
Optional-SP-R1: PASS without either optimizer.
MP-R1: PASS. Two peers, native shield and warp visual activation in three synchronized rounds, frozen join-state equality, 120000 world-tick checkpoint reached, final world/map/Rand/research/currency receipts equal. Normal time settings, traces off. Original adapters preserved.
Final source rebuilt to VerifyBuild matches tested candidate byte-for-byte. Deployed exact candidate after all game processes exited; five optional MP adapter DLLs remain byte-identical. No user process was terminated.
Limits: focused 18-mod loadout, one map; not the complete player save/modpack, not an async/rejoin certification. Already corrupted running processes require restart.
Chinese copy: 111 entries rewritten, 12934 -> 8281 characters; purpose/performance/unlock/acquisition focus. Source defs, Chinese localization and inventory agree. All 5 locales pass structural/placeholder validation; other-language prose was not rewritten. Gameplay XML normalized excluding descriptions is unchanged. Evidence ../WikiCopy-20261013.
Runtime evidence predates release; remote release verification is recorded separately.

## Exact deployed assemblies

- `1.6/Assemblies/NivarianCommander.dll`: `b92edcf4bf3a728b4736c97d7f996e166b3246c0d7bb9d8c3fef9c11efbd388a`
- `Multiplayer/Assemblies/NivarianCommander.Multiplayer.dll`: `552e48477c20b373c8f9752ffb1c8c07158342eb35ecb6079d7c9cdfdefbce45`
- `Multiplayer/Assemblies/NivarianCommander.SageShip.Multiplayer.dll`: `65c844c99de8b3d5734ce32a4d8001e36e791b92bee12739e2ce48c7ca8b0918`
- `Multiplayer/Assemblies/NivarianCommander.ShipUpgrades.Multiplayer.dll`: `c5c134ba0cb521778c8116be92cb4c0d985aa12d96a850ea980cb1544789142c`
- `Multiplayer/Assemblies/NivarianCommander.StormShip.Multiplayer.dll`: `befafe23a71ce90a696c3a1bf2c43cef84341933057c9fe67e0fbe71a19a6222`
- `Multiplayer/Assemblies/NivarianCommander.WarpPrism.Multiplayer.dll`: `2b5b7f4f42010c8e5d006736d5911a5aa7c320b5350d52dff3c9c507503bec75`
