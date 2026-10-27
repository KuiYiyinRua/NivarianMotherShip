# 1.7 范围覆盖层透明度修复

v0.9.9-r13 修复母舰使 TD Enhancement Pack - Continued 范围覆盖层变得不透明的问题。

Waylith 按模组优先级搜索着色器，并会递归匹配文件名；仅使用 NMC 子目录不能隔离与原版同名的 `VertexColor.frag.glsl`。原版着色器会把顶点颜色乘以材质 `_Color`，母舰特效版本只使用顶点颜色。误用后，TD 通过 `CellBoolDrawer` 设置的材质透明度被忽略。

母舰源码改名为 `NMC_VertexColor.vert.glsl` 和 `NMC_VertexColor.frag.glsl`，同步 ShaderDef 引用。自身特效的透明度和混合方式保留，核心程序集、运行库及联机适配文件未变。

修复前复现 6 项失败。修复后在 1.7.4985 / RimUnity 0.1.16 和 1.7.4990 / 前置提供的 RimUnity 0.1.19 各通过 19 项隔离单机检查，并核对实际游戏截图：原版着色器回到 Core、肥沃度覆盖层两档透明度正常、贸易信标覆盖半透明，母舰原透明着色器样本正常。新增 Core 同名文件检查，预防再次出现此类覆盖。

未验证玩家原存档、完整整合包或其他显卡；有效对照与修复轮次均有原先存在的 AncientHeavyBridge 缺失定义报错。1.7 仍仅支持单机。

更新后完全退出并重新启动游戏。手动安装 ZIP 时请用新文件夹替换旧模组文件夹，避免合并解压遗留旧着色器文件。

回归源码与运行方法：[TDOverlay17](../Tests/TDOverlay17/README.md)。
