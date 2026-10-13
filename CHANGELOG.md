# Changelog

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
