# 暗影能源塔隐身范围

能源塔占地 2×2 格。隐身范围是以四个占地格分别为圆心、每个圆半径 7 格的范围并集。目标所在格进入任意一个圆，即满足距离条件；四个圆不会重复叠加效果。

选中能源塔时显示合并范围的外边界，折跃放置预览也显示同一范围。边界表示正常运转时可覆盖的区域；缺少营养、关闭或损坏时仍停止隐身。塔身继续不受自身隐身场保护。

实际判定、选中边界和放置预览共用 `ShadowCloakRange`。距离通过目标格到最近占地格的水平距离计算，与逐个遍历四个圆等价；显示格集合过滤地图外格子。此计算不使用随机数，也不新增存档字段。

定向探针位于 `Tests/ShadowFootprint`，包含旧单格圆漏覆盖负例、400 格实际覆盖对比、地图边缘显示集合、缺粮/关闭/损坏以及选中截图检查。依赖路径由 `Directory.Build.props` 指定。

```powershell
dotnet build Source/NivarianCommander.csproj -c Release -p:OutputPath=../BuildValidation/ShadowFootprint-20261019/CandidateBuild/
dotnet build Tests/ShadowFootprint/Probe.csproj -c Release
./Tests/ShadowFootprint/Run.ps1 -Label FreshRun
python Tools/Localization/verify.py
```

测试在独立临时游戏中运行，每轮使用新的 Label；未涵盖双端联机。此范围调整纳入 v0.9.9-r5 更新，需重启游戏加载；联机双方使用相同文件。
