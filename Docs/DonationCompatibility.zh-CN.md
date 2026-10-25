# 1.7 捐赠容器兼容

Issue #2 的日志出现七次同栈空引用：`ThingOwnerUtility.GetAllThingsRecursively → PointShopTrade.Donations → Window.Rebuild`。容器扫描在绘制前失败，导致列表闪烁和持续红字，与 issue #1 的 Vulkan 材质管线错误不同。附件没有记录具体异常容器或模组身份，不能断言由哪个前置导致。

`PointShopTrade` 使用局部遍历状态，检查空 owner/子 holder，按引用去重，限制单容器 holder 图规模并验证父链。读取回调抛异常时舍弃该容器本次结果，继续扫描其他容器；不改变或修复玩家容器数据。正常原生贸易扩展保留，枚举失败会记录诊断并继续信标区域扫描。异常物品资格检查也独立隔离。诊断带对象定义、ID、类型及所属模组，使用 WarningOnce 避免每帧刷屏。生物背包及尸体内部装备不会独立进入捐赠清单。

隔离测试使用实际主基地、账本、通电贸易信标、地面物资与范围内真实容器建筑。测试钩子注入无 owner、空子 holder、抛异常的 getter/枚举器及循环图，直接经过实际 Donations 方法和页面绘制。r10 基线复现 13 项失败；独立 RimUnity 0.1.17 与内嵌 0.1.16 各 55 项通过，共 110 项、六轮页面开关。实际 Review/Submit 回调验证地面和容器物资扣除、准确到账、重复请求拒绝、异常物资请求拒绝及未选物资保留；截图确认列表和头像可见。探针不进入正式运行目录。

版本为 RimWorld 1.7.4985 rev1269，GPU 为 NVIDIA RTX 2080 Ti。`AncientHeavyBridge` 缺失定义错误在原版与修复版均存在。未验证报告者实际存档、完整整合包、RX 560X 或 1.7 联机；需要对方更新复测。只发布 1.7 核心，1.6 二进制不变，保留 r10 图形兼容与 r9 连续体重试修复。

核心 SHA-256：`1B472D11D929D21610B599B6DB1A219A0F9BB3BB02C7A67A3A8E55E1D39843CC`。回归入口为 `Tests/DonationCompat17`；本地证据在 `BuildValidation/DonationCompat17-20261025`。更新后完全退出并重启游戏。
