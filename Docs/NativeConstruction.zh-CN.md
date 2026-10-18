# 原生构造体与连续体核心外观（2026-10-19）

暗影能源塔、超维空间巨石的折跃按钮和图标改为涅瓦莲构造体。新部署使用本体 `Building_SelBuilding`、`CompSelfBuilding` 与材料容器，按本体流程等待材料、打印、生成成品。材料和支援费用不变，基础施工时间分别为 20 秒和 45 秒。

本体初始化时会从成品读取 `WorkToBuild` 和材料清单，因此工时设在成品 Def 上；构造体只指定成品和施工倍率。旧版施工核心的 Def 与组件保留，兼容已有施工对象。

连续体通讯核心和供能核心分别使用以水晶塔为参考制作的透明贴图，采用本体连续体的黑色装甲、红色晶体与标记。通讯核心保留任务目标功能，供能核心保留电源功能；生命值、占地、供电量和胜利条件不变。任务仍要求摧毁通讯核心并清除守军、炮塔。

## 验证

- 主程序集与测试探针编译通过；五种语言的 526 个运行时键、343 个 Def 字段校验通过。
- 隔离单机实测 24 项断言通过，验证实际部署、扣费、原生材料需求、打印过程、完工数量与工时，以及两种核心的贴图加载和属性。
- 材料由探针加入原生容器；本轮未覆盖殖民者搬运、冷读档或双端联机。
- SP-R1/R2 发现本体会覆盖构造体工时，修正成品工时后 SP-R3 通过。SP-R4 在游戏启动前遇到 PowerShell 5.1 管道差异，修复脚本后 SP-R5 通过，并完成近景截图核验。
- 证据：`BuildValidation/NativeConstruction-20261019/SP-R5/`。测试只操作独立临时游戏，不修改玩家存档。

## 复现

在模组根目录执行（依赖路径由 `Directory.Build.props` 配置）：

```powershell
dotnet build Source/NivarianCommander.csproj -c Release -p:OutputPath=../BuildValidation/NativeConstruction-20261019/CandidateBuild/
dotnet build Tests/NativeConstruction/Probe.csproj -c Release
./Tests/NativeConstruction/Run.ps1 -Label FreshRun
python Tools/Localization/verify.py
```

每次使用新的 Label。测试探针仅在指定命令行标记存在时运行，不随正式模组加载。

此变更纳入 GitHub 与 Steam 工坊的 v0.9.9-r5 更新。正在运行的游戏需完全退出后重新启动，联机双方使用相同文件。
