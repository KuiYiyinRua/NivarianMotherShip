# Release validation: 0.9.9-r5

## 0.9.9-r5 · 2026-10-19

- 暗影能源塔和超维空间巨石的折跃面板改用涅瓦莲本体构造体图标与明确的建造提示。
- 新部署接入本体构造流程：接收材料、打印成形、生成成品。保留原材料、支援费用及基础施工时间（20 秒／45 秒）；旧存档中已开工的施工核心继续兼容。
- 连续体通讯核心、供能核心都换成以水晶塔为基础的黑红连续体风格贴图，并用不同造型区分。生命值、占地、供电和任务功能保持不变。
- 暗影能源塔以占地 2×2 的四格分别为圆心，合并四个半径 7 格的圆形成隐身范围。选中塔时显示外边界，折跃预览与实际覆盖使用相同范围。
- 连续体任务仍要求摧毁通讯核心并清除守军、炮塔；包含此前的明确目标提示和定位修复。
- 验证：沿用开发阶段原生构造体 24 项、暗影范围 18 项隔离单机检查和游戏内截图核验；范围测试含周围 400 格实际覆盖对比；发布时重新校验五语并从包内源码重建。未覆盖殖民者实际搬运、冷读档、双端联机或完整整合包。
- 核心程序集更新，五份联机适配程序集保持原样。更新后重启游戏，联机双方使用相同文件。

### English

- The shadow energy tower and hyperdimensional monolith deployment panel now uses the native Nivarian construct icon and clearer construction instructions.
- New deployments use the base mod's material container, printing animation and completion flow. Original materials, support costs and base construction times (20/45 seconds) are retained. Existing legacy construction cores remain supported.
- Both Continuum communications and power cores now use distinct black-and-red crystal-pylon sprites. Durability, footprint, electricity and quest functions are unchanged.
- The shadow tower now cloaks the union of four circles, each with radius 7 cells and centered on one of its 2×2 occupied cells. Selecting the tower shows the outer boundary; placement preview uses the same area as actual coverage.
- Victory still requires destroying the communications core and clearing defenders and turrets. Includes the previous explicit objective hints and locator fix.
- Validation: the implementation stages passed 24 native-construction checks and 18 shadow-range checks, including 400-cell actual coverage comparison and in-game screenshots. Release checks revalidate five languages and rebuild from packaged source. Colonist hauling, cold loading, two-client Multiplayer and full-modpack runtime behavior were not certified.
- Updated the core assembly; all five Multiplayer adapters remain unchanged. Restart after updating and use matching files on all peers.

## Previous release evidence

# Release validation: 0.9.9-r4

## 0.9.9-r4 · 2026-10-19

- 保留连续体任务的完成条件：摧毁基地中央的「连续体通讯核心」，并清除守军和防御设施。
- 清空防御但核心仍存活时，显示一次可点击定位的提醒，明确要求手动命令部队或舰船攻击核心，任务完成后再撤离。
- 任务面板显示核心剩余生命值，并提供「定位连续体通讯核心」按钮。核心消失或任务结束后移除提示。
- 兼容现有进行中的任务；已结束的失败任务不会自动恢复，需要读取撤离前存档或等待三天重试。
- 验证：旧版复现缺少核心定位，新版 21 项单机定向检查通过，包括核心 360 血状态、相机跳转及选中、提醒去重与存档、摧毁核心后完成与阶段推进。五语校验通过；未做本轮双端联机、冷读档或整合包实机测试。
- 更新后重启游戏；联机双方使用相同版本。仅更新核心 DLL，五份联机适配 DLL 保持原样。

### English

- Continuum victory still requires destroying the central Continuum communications core and clearing the defenders and defenses.
- Added a one-time clickable reminder when defenses are cleared but the core survives. It explicitly tells players to order an attack on the core and finish the mission before leaving.
- The quest panel shows the surviving core's hit points and provides a core location button. Hints disappear after completion or core removal.
- Existing active quests receive these hints. Already failed quests are not restored; load a save from before departure or wait for the three-day retry.
- The old build reproduced the missing locator; the candidate passed 21 targeted single-player checks, including native camera selection, reminder serialization and completion after core destruction. Five-language validation passed. No new two-client Multiplayer, cold-load or full-modpack runtime certification.
- Restart after updating and use matching files on all peers. Only the core assembly changed; all five Multiplayer adapters are unchanged.

## Previous release evidence

# Release validation: 0.9.9-r3

## 0.9.9-r3 · 2026-10-17

- 修复进入连续体基地后，角色登舰或地面玩家单位暂时消失导致任务提前失败的问题。基地地图仍存在时继续保留任务和胜利判定。
- 入场识别包含舱内人员与玩家舰船；忽略旧的误撤退标记，攻陷原基地后可正常完成任务并推进下一阶段。
- 保留地图真正移除后的失败和三天重试；已被旧版判定失败的任务不会自动恢复，可读取失败前存档或等待重试。
- 核心和主联机适配已更新，其他四份舰船适配不变。包含完整源码和本次回归测试。
- 验证：旧版实机复现误失败，新版 13 项单机定向回归通过。核心和主联机适配编译均为 0 警告、0 错误；本轮未做双端联机、冷读档或整合包测试。
- 更新后请重启游戏；联机双方需使用相同版本。

### English

- Fixed Continuum base missions failing when colonists board a ship or temporarily leave the spawned-pawn list. Missions keep tracking victory while the base map exists.
- Entry detection includes passengers and player aircraft. Legacy false-retreat flags no longer prevent completion or stage progression.
- Actual map removal still triggers failure and the three-day retry. Previously failed quests are not automatically restored; load an earlier save or wait for a retry.
- Updated the core and main Multiplayer adapter; the four ship adapters are unchanged. Includes full source and the new regression probe.
- The old build reproduced the bug; the fixed build passed 13 targeted single-player checks. No new two-client Multiplayer, cold-load or full-modpack certification is claimed.
- Restart the game after updating. All multiplayer participants must use matching files.

See [Continuum entry regression](ContinuumEntryFix.zh-CN.md) for the reproduction, assertions and exact artifact hashes.

## Previous release evidence

# Release validation: 0.9.9-r2

## 0.9.9-r2 · 2026-10-16

- 修正折跃打印效果在人物并行绘制线程中加载 Unity 资源的问题，材质准备和更新改由主线程执行。
- 新增世界远行队折跃至主能量场或展开棱镜；整队人员、动物、囚犯和货物随行，落点不足或目标失效时取消操作。
- 更新远行队折跃的联机同步入口和五种语言说明。
- 贤者舰、暴风舰自动索敌仅选择敌方单位和炮塔；普通敌方建筑仍可手动指定攻击。
- 全量包包含当前资源、完整源码及五份联机适配程序集。核心文件版本仍为 0.9.9，绑定版本仍为 0.9.8；本次核心及主联机 DLL 已更新。
- 验证：源码重建与本地 DLL 逐字节一致，五语校验通过。此前远行队改动通过单机专项；联机测试仅部分执行，后续人物折跃断言失败，未确认原因。自动索敌尚未实机测试，本次不宣称完整联机或整合包验证通过。
- 更新后所有玩家须重启游戏并使用相同文件。

## Previous release evidence (0.9.9-r1 only)

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
