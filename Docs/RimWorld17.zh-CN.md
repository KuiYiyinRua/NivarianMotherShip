# RimWorld 1.7 兼容记录

发行版本：0.9.9-r8，2026-10-21。目标游戏为 Steam **1.7.4984 rev1242**；前置来自用户指定的创意工坊目录 `3624805128`。本次适配不修改前置文件或玩家配置。

## 安装

将完整的 `NivarianMotherShip` 文件夹放入游戏 `Mods` 目录，按 Harmony → Humanoid Alien Races → Nivarian Race → 本扩展的顺序启用，各前置均须为对应游戏版本。同一个 packageId 仅启用一份本地或创意工坊副本。

- `LoadFolders.xml` 为 1.6 和 1.7 分别选择程序集、Def 和资源。
- 1.6 核心 DLL 和五份 Multiplayer 适配 DLL 保留原文件。本次没有提供 1.7 Multiplayer 适配；1.7 不加载这些旧适配 DLL。
- 1.7 使用 .NET 10 和 `.rimunity` 延迟加载，随包携带与前置一致的 RimUnity 0.1.16 嵌入式运行时及许可证。不要把 1.6 的 DLL 复制到 1.7。
- Royalty 有、无两种加载分支继续保留。1.7 的前置本体已整合军工内容，连续体任务自动启用；固定无人机基站仍需要前置新增的算力带宽。

## 迁移内容

1. 新增独立 1.7 工程和构建脚本，1.6 与 1.7 使用不同的中间输出目录。
2. 无线供能补丁改为匹配前置新版本的实际抵扣函数；投影残骸补丁只作用于接收 Thing 的重载。
3. 研究任务限制提示接入新版上行链路研究队列，保留任务与母舰等级门槛。
4. 棱镜光束使用 RimUnity 对象类型，Lumi 构建动画和光束使用明确的生命周期。
5. 轨道、太阳射线、净化和风暴效果使用 1.7 原生 GLSL 着色器；黑洞转为 ScreenEfx 屏幕处理，保持世界图像折射、核心贴图和随地图 tick 推进的动画。
6. 更新前置冷冻熔炉贴图路径。1.7 资源由 1.6 共用内容生成，再叠加专用着色器。

## 构建

需要 .NET 10 SDK 和 PowerShell 7。在模组根目录运行，替换为自己的路径：

```powershell
./Tools/Build-RimWorld17.ps1 `
  -GameRoot 'G:/Steam/steamapps/common/RimWorld' `
  -Nivarian17Root 'G:/Steam/steamapps/workshop/content/294100/3624805128' `
  -HarmonyAssembly 'D:/Mods/Harmony/1.7/Assemblies/0Harmony.dll'
```

脚本从前置的嵌入式 provider 中读取匹配的运行时引用，不执行提取工具中的第三方程序集；输出到当前模组 `1.7`。可用 `-OutputRoot` 指向独立候选目录。复制后的文件会逐一校验 SHA-256。

1.6 仍使用 `dotnet build Source/NivarianCommander.csproj`。本次只将 1.6 重建结果输出到验证目录，没有替换原 1.6 DLL。

## 复现实机检查

`Tests/RimWorld17/Prepare.ps1` 可在新的 ValidationRoot 中复制完整游戏运行资源、前置及候选扩展，并编译探针。需要额外提供 `-HarmonyRoot` 与 `-AlienRacesRoot`。不要把测试探针安装到正常游戏。

```powershell
./Tests/RimWorld17/Prepare.ps1 -GameRoot 'D:/Games/RimWorld17' `
  -Nivarian17Root 'D:/Mods/NivarianRace' -HarmonyRoot 'D:/Mods/Harmony' `
  -AlienRacesRoot 'D:/Mods/AlienRaces'
./Tests/RimWorld17/Run.ps1 -Label Baseline -Baseline
./Tests/RimWorld17/Run.ps1 -Label Smoke
./Tests/RimWorld17/Run.ps1 -Label Abilities -Probe Abilities
./Tests/RimWorld17/Run.ps1 -Label Projection -Probe Projection
./Tests/RimWorld17/Run.ps1 -Label Construction -Probe Construction
./Tests/RimWorld17/Run.ps1 -Label NoRoyalty -NoRoyalty
```

测试串行执行，使用独立的短路径 savedata、静音和不激活的后台窗口，保留真实渲染。每轮保存加载配置、进程命令行、输入哈希、日志、断言和截图，结束后再次校验输入。`PROBE_PASS` 仍须结合原始日志和画面检查。

本机本轮出现 Vulkan 自动驱动发现失败，后续仅通过测试子进程的 `-VulkanDriverManifest` 指定现有 NVIDIA 驱动 JSON；未修改系统驱动或注册表。窗口须恢复为非最小化状态后移至屏幕外，否则 Waylith 可继续 tick 却不绘制。部分早期失败来自旧接口、缺失测试算力主机或部署副本损坏，均保留原始记录，不能作为通过证据。

## 实测结果

最终记录见下方测试表；原始证据保存在本地 `BuildValidation/RimWorld17-20261021`，不包含在发行包中。

| 运行 | 结果 | 覆盖内容 |
| --- | --- | --- |
| Baseline-R2 / Baseline-NoRoyalty | 通过，各 1,200 以上自然 tick | 仅游戏及前置，不加载本扩展的有、无 Royalty 对照 |
| Final-Abilities-R2 | 55 项通过 | 19 项真实支援指令及扣费、五种舰船起飞和存档重建；黑洞实际画面、暂停、重载与取消 |
| Final-Projection | 85 项通过 | 三类原生基站与巨石投射、构建期、电池转移、伤害回传、读档、召回/销毁/到期、原生算力占用与释放 |
| Final-Construction | 24 项通过 | 两种构造体材料交付、原生打印动画、20/45 秒基础工期、完成结构与核心贴图 |
| Final-Smoke | 27 项通过，1,203 tick | 完整初始化、19 个支援定义、原生窗口内指挥页真实绘制 |
| Final-NoRoyalty | 27 项通过，1,200 以上 tick | 不启用 Royalty 的版本分支、面板绘制与持续运行 |
| Final-Chinese | 27 项通过，1,203 tick | 简体中文实际加载、中文指挥面板截图和持续运行 |

上述最终候选均使用同一份核心文件，SHA-256：

```text
1.7/Assemblies/NivarianCommander.rimunity
F3079EF81913EBA8A454851FFF6D0F6639A33B47B5F0C63A868F23E7FB843F57

1.7/Assemblies/RimUnity.Embedded.dll
A0223C224D0E22835EB5BE5516BC37BC98CAB27383122A5F60CED40907E283C5
```

重复从源码构建得到相同核心哈希；实际测试目录与交付的 215 个 1.7 文件逐一相同。1.6 源码可编译，保留的 1.6 核心 SHA-256 为 `14EA4040CC4E1E079F32B0B90520CCDFF641D1578B0AB8937030928FA4514586`。五语校验通过 526 个运行时键和 343 个 Def 字段。

**日志并非全零错误。** 对照组复现了同样的上游缺失引用：启用 Royalty 时存在 `AncientHeavyBridge`；不启用 Royalty 时另有 `Apparel_NivarianSpiritArtEarring`。后者来自前置种族服装列表对 DLC 条件物品的引用。本扩展没有删除或屏蔽这些日志，也没有修改前置以掩盖它们；最终候选未引入额外的错误项。

## 范围

这是一组隔离单机回归，覆盖扩展加载、主要支援调用、渲染、施工、投射及同进程存档重建。没有覆盖完整整合包、整个八阶段战役、所有敌人和武器组合、冷启动旧档迁移、长时间游玩或 1.7 联机。

接口依据：安装中的 1.7 游戏和前置程序集，以及 [RimUnity ScreenEfx 文档](https://github.com/chezhou9413/RimUnity/blob/main/Docs/en/ScreenEfx.md)。
