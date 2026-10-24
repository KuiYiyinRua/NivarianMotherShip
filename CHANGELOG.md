# Changelog

## 0.9.9-r10 · 2026-10-25 · 1.7 点数商店图形兼容

- 为 issue #1 报告的 Vulkan 自定义 UI 材质管线错误加入兼容处理：1.7 商店头像和商品使用普通贴图，不提交全息或特殊图标材质。
- 保留头像、打字对话、商品基础颜色和交易逻辑；全息投影及材质遮罩、发光等特殊效果停用。兼容层仅作用于本模组商店，无需修改依赖模组。
- 嵌入式 RimUnity 0.1.16 和独立 0.1.17 各通过 29 项隔离单机检查，共 58 项、六轮商店开关。验证使用 NVIDIA RTX 2080 Ti；报告问题的 RX 560X、玩家实际存档和完整整合包仍待复测，不能据此确定上游或驱动的唯一责任。
- 包含 r9 连续体任务重试修复；1.6 核心、五份联机适配 DLL 和随附运行库保持原样。不提供 1.7 联机支持。更新后完全退出并重启游戏。

### English

- Added a compatibility path for the Vulkan custom UI material pipeline error reported in issue #1. On 1.7, shop portraits and merchandise use ordinary textures without submitting hologram or special icon materials.
- Preserves portraits, typewriter dialogue, base item colours and trade logic. Hologram, material-mask and glow effects are disabled within this shop. No dependency-mod changes are required.
- Passed 29 isolated single-player checks each with embedded RimUnity 0.1.16 and standalone 0.1.17: 58 checks and six shop open/close cycles. Tests used an NVIDIA RTX 2080 Ti; the reporter's RX 560X, actual save and full modpack still require retesting. The log does not establish sole responsibility in the dependency or driver.
- Includes the r9 Continuum retry fix. The 1.6 core, five Multiplayer adapters and bundled runtime are unchanged. No 1.7 Multiplayer support. Fully exit and restart after updating.

## 0.9.9-r9 · 2026-10-25 · 1.7 连续体任务重试修复

- 修复连续体任务引用丢失后停在进行中、失败任务不再生成的问题；优先找回同基地、同阶段的有效任务，避免重复任务。
- 三天重试不再受首次第七天出现条件阻挡。可恢复失败记录的旧档会扣除已经等待的时间；超过三天后继续游戏即可重试。记录已完全丢失时重新安排三天等待。
- 保留已完成阶段、仍有地图的基地和货物；已完成全部八阶段的战役不会重开。
- 将 1.7 随附 RimUnity 运行库与当前前置对齐。请更新配套的 Nivarian Race 1.7，并完全重启游戏。
- 原版复现 6 项失败，修复候选通过 46 项隔离单机检查，含待重试存档及原版故障存档的冷启动；计时为定向加速检查。未验证用户实际存档、完整整合包或 1.7 联机。
- 仅更新 1.7 运行文件；1.6 核心和五份联机适配 DLL 保持原样。共享源码包含修复，但本次未发布新的 1.6 编译文件。积分商店图形崩溃尚未确认修复。

### English

- Fixed a stalled Continuum campaign when its quest reference is missing. Recover the live quest for the same site and stage before scheduling a retry, preventing duplicate missions.
- The three-day retry no longer waits for the initial day-seven gate. Recoverable historical failures credit elapsed waiting time; overdue failures retry when simulation resumes. Missing history starts a fresh three-day wait.
- Preserves completed stages, loaded sites and cargo. Completed eight-stage campaigns do not restart.
- Aligned the bundled 1.7 RimUnity runtime with the current prerequisite. Update Nivarian Race 1.7 and fully restart the game.
- The original build reproduced six failing checks; the fixed candidate passed 46 isolated single-player checks, including cold loads of a pending retry and an old-build stuck save. Timing uses targeted acceleration. No actual user-save, full-modpack or 1.7 Multiplayer certification.
- Only 1.7 runtime files change. The 1.6 core and five Multiplayer adapters remain unchanged; shared source includes the fix but no rebuilt 1.6 binary ships. The separately reported points-shop graphics crash is not confirmed fixed.

## 0.9.9-r8 · 2026-10-21 · RimWorld 1.7 兼容

- 新增 RimWorld 1.7 的独立程序集、资源和版本加载规则；保留 1.6 核心及五份联机适配 DLL。
- 对接 1.7 前置的无线供能函数、研究队列和内置军工内容，更新投影残骸重载与冷冻熔炉贴图路径。
- 迁移 Lumi/RimUnity 动画和棱镜接口，新增光束 GLSL 着色器；黑洞使用新版屏幕特效接口，保留场景扭曲和暂停/读档行为。
- 提供可复现构建、隔离测试与完整打包入口。实测版本、证据和限制见 [1.7 兼容记录](Docs/RimWorld17.zh-CN.md)。本次未提供 1.7 Multiplayer 适配。

### English

- Added a separate RimWorld 1.7 build, resources and version-specific loading. Existing 1.6 core and Multiplayer binaries are unchanged.
- Adapted native power hooks, research queue hints, integrated military content, projection leavings and the changed cryo-forge texture path.
- Ported Lumi/RimUnity effects and beam shaders; black-hole scene distortion now uses ScreenEfx with map-tick animation and save reconstruction.
- Added reproducible build and isolated runtime probes. See the linked migration record for tested scope and limitations. No 1.7 Multiplayer adapter is included.

## 0.9.9-r7 · 2026-10-21

- 优化超维空间巨石索敌与 tick：减少全图遍历、重复排序和分配，保留目标顺序、攻击频率、伤害及控制规则。
- 优化普通/暗影能源塔的营养读取、补料判断、供能与生产 tick；减少重复查询，营养和运行状态仍实时计算。范围外的隐身查询也减少了能源塔状态检查。
- 恢复暗影核心、超维空间巨石在支援面板中的原图标。
- 矩阵投射调整为 2 天冷却（120,000 tick）、300P / 100V，旧档更长的剩余冷却会缩短。投影仅在落点显示 5 秒原生构建特效。
- 矩阵投射支持三种军武防御无人机基站：投射时回收本体无人机、转供现有电池，结束时归还剩余电量；同步更新五语说明。
- 巨石/能源塔双端回归通过 20,160 tick 短测与 120,120 tick 长测；长测完成 968 次真实攻击、18 次原生补料、480 组相同状态记录，未发现失步。测试为同机、单图、正常时间的 17 项隔离加载，未覆盖完整整合包、异步、多阵营或冷重连。
- 单端优化回归 228 项、补料回归 165 项（含 4 次真实搬运）通过；投射功能此前候选通过 83 项单端检查，本次未追加投射专项双端测试。详见 Docs/TickPerformance.zh-CN.md。
- 核心版本仍为 0.9.9，绑定版本仍为 0.9.8；五份联机适配 DLL 保持原样。更新后完全重启游戏，所有联机玩家使用相同文件。

### English

- Reduced monolith targeting/tick work and allocations while preserving target order, attack timing, damage and control rules.
- Reduced repeated nutrition, refill, operation and production queries for both energy towers. Nutrition and operating state remain live; out-of-range cloak queries avoid unnecessary tower checks.
- Restored the original Shadow Core and Hyperdimensional Monolith support-panel icons.
- Matrix projection now costs 300P / 100V with a two-day (120,000-tick) cooldown; longer saved cooldowns are capped on load. A five-second native printing effect appears only at the destination.
- Added projection support for three military defensive drone hubs, with source-drone recall, battery transfer and return of remaining charge. Updated all five languages.
- Monolith/tower Multiplayer regressions passed 20,160-tick smoke and 120,120-tick soak runs. The soak recorded 968 actual volleys, 18 native deliveries and 480 identical paired states with no desync. Scope: two local clients, one map, normal time and 17 isolated loadout entries; no full modpack, async, multifaction or cold-rejoin coverage.
- Single-player optimization and refill regressions passed 228 and 165 checks (including four real deliveries). The prior projection candidate passed 83 single-player checks; projection-specific Multiplayer was not retested in this release.
- Core version remains 0.9.9, binding version 0.9.8; all five Multiplayer adapters are unchanged. Fully restart and use matching files on every peer.

## 0.9.9-r6 · 2026-10-19

- 能源塔和暗影能源塔只有在营养储量低于设定目标的 50% 时才自动补料，减少少量消耗引起的反复搬运。专用补料工作和普通搬运均使用此规则。
- 自动补料开关对两种自动搬运路径都生效；已开始的投料及手动强制补料仍可补至设定目标。
- 主能量场部署、迁移冷却统一为 1 分钟（常速下 60 秒／3,600 tick），不受“跨域人员折跃”研究影响。旧存档中更长的剩余冷却在载入时缩短至 1 分钟以内。
- 同步更新五种语言的技能和研究说明，附带补料回归测试源码。核心版本仍为 0.9.9，五份联机适配 DLL 保持原样。
- 验证：补料改动在开发阶段通过 165 项隔离单机断言，含两种塔的 4 次实际搬运；组合发布包完成源码重建一致性、冷却编译结果及五语校验。本轮未新增能量场实机、冷读档、双端联机或整合包测试。
- 更新后重启游戏；联机双方使用相同文件。

### English

- Nutrient and shadow energy towers now request automatic refills only when stored nutrition is below 50% of the configured target. Both dedicated refill work and general hauling follow this rule.
- The auto-fill toggle applies to both automatic hauling paths. Deliveries already in progress and explicit player orders can still fill to the configured target.
- Main energy field deployment and relocation now share a one-minute cooldown (60 seconds at normal speed / 3,600 ticks), regardless of personnel recall research. Longer remaining cooldowns in existing saves are capped at one minute on load.
- Updated ability and research descriptions in all five languages and included the nutrient refill regression source. Core version remains 0.9.9; all five Multiplayer adapter DLLs are unchanged.
- Validation: the refill implementation passed 165 isolated single-player assertions, including four actual hauling jobs across both tower types. The combined release package was checked by rebuilding its source, inspecting the compiled cooldown, and validating all five languages. No new field gameplay, cold-load, two-client Multiplayer or full-modpack runtime test is claimed.
- Restart after updating and use matching files on all multiplayer peers.

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

## 0.9.9-r2 · 2026-10-16

- 修正折跃打印效果在人物并行绘制线程中加载 Unity 资源的问题，材质准备和更新改由主线程执行。
- 新增世界远行队折跃至主能量场或展开棱镜；整队人员、动物、囚犯和货物随行，落点不足或目标失效时取消操作。
- 更新远行队折跃的联机同步入口和五种语言说明。
- 贤者舰、暴风舰自动索敌仅选择敌方单位和炮塔；普通敌方建筑仍可手动指定攻击。
- 全量包包含当前资源、完整源码及五份联机适配程序集。核心文件版本仍为 0.9.9，绑定版本仍为 0.9.8；本次核心及主联机 DLL 已更新。
- 验证：源码重建与本地 DLL 逐字节一致，五语校验通过。此前远行队改动通过单机专项；联机测试仅部分执行，后续人物折跃断言失败，未确认原因。自动索敌尚未实机测试，本次不宣称完整联机或整合包验证通过。
- 更新后所有玩家须重启游戏并使用相同文件。

## 0.9.9-r1

- 修复 YaOpt / PerformanceOptimizer 优化层在并行绘制下的组件缓存锁保护，保留缓存优化；重新应用优化器设置后仍保持保护。
- 精简并重写 111 条中文物品、技能和科技说明，突出功能、性能、用途及解锁获取条件。
- 包含完整模组资源、125 份核心源码及 13 份联机源码。核心文件版本保持 0.9.9，程序集绑定版本保持 0.9.8；五份联机适配 DLL 身份不变。
- 已测：1048576 次真实并发缓存查询、600 帧更新、无优化器启动、双端 120000 世界 tick 与最终状态一致性。专项单地图验证，未覆盖完整模组包、异步时间或重连矩阵。
- 更新后所有玩家需重启游戏并使用相同文件。

## 0.9.9

- Added the Storm Ship, Sage Ship and Warp Prism with their assets, sounds and optional Multiplayer adapters.
- Preserves the author's Storm Ship balance: 20 direct damage and 25 blast damage over a 3×3 area, both ×5 against buildings.
- Warp departure and arrival each take 240 ticks. Damage applies during both phases; airborne ships retain flight and preserve ground objects.
- Added ship field recall, refundable battery upgrades, native lotus-pod deliveries and energy-field performance improvements.
- Replaced the complete-all-research option with a default-off mission requirement bypass. Technology prerequisites, research work, material costs and support mothership levels remain required. Completed research is retained. The old checkbox is not carried into this new option.
- Includes complete current source, five-language localization and five optional Multiplayer assemblies. Their existing assembly identities are retained for compatibility; the core assembly binding version remains 0.9.8 while its file/release version is 0.9.9.
- Validation coverage and limitations are recorded in Docs/VALIDATION.md.

## 0.9.8

- Retuned Spacetime Wave: friendly pawns and animals move at 2× speed, friendly work runs at 10× speed, and friendly turret burst cooldowns become one third. Nivarian research and manufacturing run at 10× speed, including carrier interceptor manufacturing; P/V income remains unchanged.
- Added persistent nutrient target sliders and automatic-refill switches for nutrient and shadow energy towers, with synchronized Multiplayer commands.
- Updated orbital and solar bombardment to a shared 3×3 impact, wider beams and revised impact visuals. Expanded void stasis to radius 18 and allowed overlapping placement.
- Continuum missions persist until resolved; the first offer arrives after seven days, with broader valid settlement and caravan targeting. Added the updated world-map base icon.
- Carrier and gravity-ship destruction now returns 50% of their recipe materials. Optimized projection and power lookup paths while preserving saved state.
- Includes all current expansion source, compiled assemblies, five-language localization and Workshop materials. Added the GitHub source link to the bottom of every Workshop description.
- Validation: focused two-client/two-map Spacetime checks reached 10,006 shared ticks with three casts and matching state/Rand receipts before the user stopped testing. The subsequent carrier manufacturing fix and 0.9.8 build are compiled and statically inspected; no additional game test, full mod-pack soak or cold rejoin is claimed.

## 0.9.7

- Added a default-off setting to complete all expansion research without Continuum missions and bypass support level requirements. Costs and cooldowns remain active; completed research is retained when the option is disabled. The saved toggle is synchronized in Multiplayer.
- Updated the author credit to 尹怨怨 and prepared five-language Steam Workshop descriptions with actual in-game screenshots.
- Includes current carrier, warship research, hostile campaign reinforcement and performance updates.

## 0.9.6

- Continuum stages 6/7/8 add respectively 2/2/4 hostile gravity ships and 0/1/2 hostile carriers, with one real pilot per hull. Gravity ships automatically pursue and orbit targets; carriers hover near their launch range and manufacture interceptors from 500 dragonfrost steel and 8,000 chemfuel each. Reinforcement and flight state persist through saves.
- Automatic support targeting skips downed pawns, and fleet weapons release targets that become downed.
- Purification beam visuals now use the actual four-cell damage radius for the impact footprint and wider column.
- Added Simplified Chinese, English, Russian, Japanese and French localization for commander UI, supports, research, shop dialogue, equipment, ships, campaign and messages.
- Precision ally recognition now unlocks through a separate research project. The old purchased protection is retained and migrated automatically.
- Includes the 0.9.5 Milira starship battery upgrades: ten batteries for the carrier, two for the gravity ship, then fuel is consumed on takeoff.
- Keeps Royalty and NoRoyalty loading branches and the optional Multiplayer adapter.
- Fixed translation file precedence between Royalty and the base ability definitions.
