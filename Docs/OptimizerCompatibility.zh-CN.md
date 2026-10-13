# 优化器兼容

母舰检测到 Yet another Optimizer 与 Performance Optimizer 同时加载时，启用它们已有的线程安全组件缓存。组件查询加速保留，无需关闭优化选项。

YaOpt 1.1.4 原先只根据模拟并行选项决定是否启用缓存锁。母舰的并行绘制也会查询地图组件，因此即使这些模拟选项关闭，缓存仍需线程安全保护。兼容桥在启动和优化器重新应用设置后保持保护，不更改用户配置、存档或模拟逻辑。

兼容桥不要求安装优化器。其他版本若改变适配接口，启动日志会报告无法解析接口，不会猜测替代目标。

更新后需完全重启游戏；已经损坏的进程内缓存不会由新 DLL 热修复。联机各端应更新相同文件。

实现：`Source/CommanderOptimizerCompatibility.cs`。专项回归：`Tests/OptimizerCompatibility`。本轮证据：`BuildValidation/OptimizerBridge-20261013`。
