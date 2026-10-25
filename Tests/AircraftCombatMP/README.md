# 舰载机集火与敌舰路线：RimWorld 1.6 双端测试

独立、命令行开关控制的测试模组，默认不会运行。控制器复制游戏启动文件与模组到短路径临时目录，为主机和客户端建立各自的存档、配置和日志；后台静音运行，不写入玩家的存档或配置。

## 运行

在模组根目录执行 PowerShell：

```powershell
dotnet build Tests/AircraftCombatMP/Probe.csproj -c Release
& Tests/AircraftCombatMP/Run.ps1 -Label Seed-New -Seed
$seed = Join-Path $PWD 'BuildValidation/AircraftCombatMP-20261025/Seed-New/Host/Saves/AircraftMPSeed.rws'
& Tests/AircraftCombatMP/Run.ps1 -Label Smoke-New -Trace -Ticks 20000 -SeedSave $seed
& Tests/AircraftCombatMP/Run.ps1 -Label Soak-New -Ticks 120000 -SeedSave $seed
```

每次必须使用未占用的 Label。先将准备测试的 `About`、`1.6`、`Royalty`、`NoRoyalty`、`Multiplayer` 和 `LoadFolders.xml` 冻结到 `BuildValidation/AircraftCombatMP-20261025/Candidate/`；不要在游戏启动后改动候选或探针。游戏和依赖默认来自模组上两级的本机 1.6 安装。

种子生成使用 quicktest，再将同一份场景存档冻结并提供给双端。记录真实世界种子与存档 SHA；不要假定不同次 quicktest 生成了相同的世界种子。

## 检查内容

- 加载版本、核心和适配模块 MVID/SHA、模组顺序及全部复制输入；七个原生同步入口必须存在。
- 主机从已加载世界开服；出现 `Server started.` 后客户端才从主菜单连接。两个玩家均完成加载后，在冻结状态下核对地图、舰船与地图/世界随机数状态，再启动模拟。
- 非主机通过实际 gizmo、取消/重新打开 Targeter 及其回调下达命令；不由测试包装器代替产品的集火同步入口。
- 三轮集火、换目标、取消、召回、自动出击和暴风舰指定攻击，共 24 阶段、48 次原生同步执行；双方检查实际四机目标、发射时的 intendedTarget、实际命中伤害与回收状态。
- 航母、重力舰、暴风舰、贤者舰、折跃棱镜五种敌舰使用真实驾驶员、自动战斗 AI 和移动 Tick。可绕行时整个舰体避开厚岩顶；横贯地图的厚岩顶屏障仍可穿越。
- 长测持续集火，并每约 4,000 共享 tick 重置障碍和敌舰起点，交替绕行/穿山。每 250 tick 比较舰载机位置、目标、计时器、敌舰目标/路线及地图/世界随机数状态。
- 由主机发送统一冻结信号结束；双方初始状态、最终状态、全部执行记录和定期快照必须逐字相同。遇到错误、不同步、断线或断言失败立即中止。

## 场景控制与范围

场景是单地图、16 项模组、同步时间、同阵营、开发模式关闭。探针仅在 `-nmc-aircraft-mp` 下提高靶子耐久、恢复靶子生命/舰船燃料/测试角色需求、维持观察员征召及固定位置、将环境温度设为 21°C，并暂停无关 Storyteller 事件。舰载机保留原生命；舰船武器、伤害和移动算法未替换。雷暴球靶位与舰载机盘旋区域分开，避免正常战损干扰后续指令断言。

这些辅助不属于正式模组，也不等价于完整玩家存档或整合包回归；不覆盖跨电脑、多地图/异步时间或中途冷重连。

2026-10-25 最终记录：`Smoke-R7-20k` 24,375 tick，`Soak-R2-120k` 120,105 tick，均 PASS。详见同一验证目录中的 `REPORT.zh-CN.md`、`verified-results.json` 和双方日志。早期场景/探针失败已单独保留，不能替代最终两轮证据。
