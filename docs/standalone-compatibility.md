# 工作台与独立版的切换边界

本页记录冻结版本的源码协议和已有自动观察，**没有执行独立版旧 GUI 与工作台的双 GUI 联合实机检查**。不要并行使用同一业务状态，不复制活动账本来绕过互斥。宿主“代码信任”、业务连接/预览确认、凭据用途批准及任务恢复各有独立含义；共享锁不会替代这些批准。

| 范围 | 固定版本与来源 | 互斥范围 |
| --- | --- | --- |
| 工作台 | 普通启动的 `%LOCALAPPDATA%\ProjectWorkbench\` | UserRoot 的 `host.lock` 独占文件租约；复制 EXE/应用目录仍用同一普通根。它不锁全部业务 profile 或独立应用 |
| UPCAM | standalone 0.4.3 与生产插件 0.1.0，同一提交 `6370b9df46af564fde022924d1dbeace7d575c6a` | 同一 Windows 登录 session 中共享命名对象；即使 profile 不同也互斥 |
| 钉钉洞察 | 同一维护源码的 Desktop 与生产插件 0.3.2，提交 `c0c1d3303ae8b59c36dd7c4c1de85ddc6578abcf` | 对同一规范 `state.dpapi` 的真实文件锁；任意复制账本不在该锁内 |

## UPCAM：共享命名对象的存在性协议

固定源码 [PluginWorkerClient.cs:16](https://github.com/ThaoKeeton/upcam-task-digest/blob/6370b9df46af564fde022924d1dbeace7d575c6a/plugins/UPCAM.Workbench/PluginWorkerClient.cs#L16) 与 [App.xaml.cs:57–64](https://github.com/ThaoKeeton/upcam-task-digest/blob/6370b9df46af564fde022924d1dbeace7d575c6a/winui/UPCAMTaskDigest/App.xaml.cs#L57) 均使用 `Local\UPCAMTaskDigest.Singleton.v1`。插件在 [131–135 行](https://github.com/ThaoKeeton/upcam-task-digest/blob/6370b9df46af564fde022924d1dbeace7d575c6a/plugins/UPCAM.Workbench/PluginWorkerClient.cs#L131) 创建 `new Mutex(false, SingletonName, out bool createdNew)`，若已存在就拒绝创建 worker；维护独立版使用相同构造/createdNew 检查，拒绝第二实例。

这是命名内核对象的**存在性**协议，不是通过 `WaitOne`/`ReleaseMutex` 判定线程所有权。插件 session 持有该对象直到退役；只要同一 session 中已有维护版进程/worker 持有它，另一个 profile 也不能因此并行。`Local\` 不构成跨 Windows 登录 session 的全局保证。不同用户、不同 session、绕过/未实现此约定的旧版本均未被证明互斥，复制账本也不能获得全局发送回执/去重保证。

上游[固定集成说明](https://github.com/ThaoKeeton/upcam-task-digest/blob/6370b9df46af564fde022924d1dbeace7d575c6a/integrations/project-workbench/README.zh-CN.md)要求先退出独立版再绑定已有 profile，业务 DPAPI 留在规范 profile，禁止复制活动账本。生产 0.1.0 没有自动业务授权，不应以测试用 acceptance 插件替代它；`com.upcam.task-digest.acceptance` 只用于另一个隔离根的模拟验收，不随生产配套交付。

## 钉钉：规范状态与旧版文件租约

固定 Desktop [MainWindow.xaml.cs:73–76](https://github.com/ThaoKeeton/dingtalk-insights/blob/c0c1d3303ae8b59c36dd7c4c1de85ddc6578abcf/src/DingTalkInsights.Desktop/MainWindow.xaml.cs#L73) 在普通运行中打开 `%LOCALAPPDATA%\DingTalkInsights`，创建 DPAPI cipher 和 `BusinessStore`。插件的 [CanonicalStoreLocation.cs:9–23](https://github.com/ThaoKeeton/dingtalk-insights/blob/c0c1d3303ae8b59c36dd7c4c1de85ddc6578abcf/src/DingTalkInsights.Runtime/CanonicalStoreLocation.cs#L9) 仅在明确审查后解析同一规范根，并拒绝把任意复制账本当成排除旧独立版的目标。

[BusinessStore.cs:7–17](https://github.com/ThaoKeeton/dingtalk-insights/blob/c0c1d3303ae8b59c36dd7c4c1de85ddc6578abcf/src/DingTalkInsights.Runtime/BusinessStore.cs#L7) 持有兼容旧版的状态租约和 hash-only 发送 sidecar；[StateStore.cs:17–21](https://github.com/ThaoKeeton/dingtalk-insights/blob/c0c1d3303ae8b59c36dd7c4c1de85ddc6578abcf/src/DingTalkInsights.Core/StateStore.cs#L17) 对真实 `state.dpapi.lock` 使用 `OpenOrCreate`、`ReadWrite`、`FileShare.None`，Dispose 才释放所持 FileStream。锁文件是否存在不能替代打开句柄的所有权，也不要通过删除锁文件来解除占用。

因此固定维护版在连接同一规范文件时采用相同排他协议；这不证明无关复制目录、不同用户的规范根或忽略该协议的任意旧版会被排除。状态、预算、attempts/unknown-send 与 receipts 仍以规范存储为准；外部规范状态不属于宿主 UserRoot 的自动备份，代码回滚不回滚它，也不证明旧业务版本能降级读取新 schema。

## Stop、退役与切回独立版

1. 切到插件前，先在维护独立版停止工作并**真正退出进程**。钉钉 Desktop 有“关闭到托盘”设置；关闭窗口不一定释放 store，应使用“退出应用”并确认进程已经结束。只在确认兼容和明确审查后连接既有规范状态；首次体验按[生产安装指南](install-business-plugins.md)使用专用空白测试账户和本地演示。
2. 插件中的任务 **Stop 不是终端退役**。钉钉的[固定生命周期说明](https://github.com/ThaoKeeton/dingtalk-insights/blob/c0c1d3303ae8b59c36dd7c4c1de85ddc6578abcf/docs/workbench-plugin.md#L19)明确：任务 Stop 保留 canonical connection 和已批准本地凭据用途，后续开始仍需新预览；终端 Disable/shutdown 才等待真实 controller/operations/loop/store lease 退出并撤销连接。既有 UPCAM 模拟自动观察也显示 Stop 后同一空闲私有 worker 仍在；session/PluginWorkerClient 未 Dispose 前单例仍可持有。
3. 要切回独立版，先停止工作，再在“插件”**停用该插件或安全退出整个宿主**，确认终端退役完成、其自有 worker/连接已释放，之后才启动维护独立版。若显示停止/关闭等待，保留进程和锁，等待真实工作结束并使用“重试停用/重试关闭”；不要强杀来假定无未知发送。停用不会热卸载程序集，不会自动清除当前业务状态。
4. 若对方仍报告占用，核对该账户/session 的进程、worker 和版本，不删除规范数据、锁或 journal，不另复制一个活动账本。不同版本/schema 的切换先由业务发布方确认数据格式兼容性。

## 证据与待验范围

| 证据层级 | 能说明什么 | 尚未说明什么 |
| --- | --- | --- |
| 固定提交源码检查 | 上述名字、createdNew、规范路径、FileShare.None 和 Stop/退役协议；未打开真实用户规范根 | 两个维护版 GUI 的实际互斥交互；任意旧版本、session 或 profile 组合 |
| 既有 Server 2025 自动矩阵 | 实际宿主、原生页面、独立生产字节、真实 worker 生命周期；生产与 acceptance 使用分离根，原有 14 原生/17 管理/7 平台阶段/51 业务断言保留 | 实际 Windows 11 客户端 UI/辅助功能、独立 GUI 联合运行、多日运行 |
| 当前交付修改 | 新增隔离的损坏主题启动检查单列 `additionalChecks`；当前源码的完整 Windows CI/下载产物核验待控制器执行 | 不能以 Linux reference 编译或旧 CI 结果宣称新增原生检查已通过 |
| Windows 11 人工与真实迁移 | [最小顺序与记录模板](windows11-manual.md)已提供，当前未执行 | 真实更高版本/schema 迁移、维护版双 GUI 及跨 session/旧版本测试仍待单独观察 |

只读源码检查和自动模拟验收没有使用真实凭据、provider HTTP、Webhook 或业务消息。首次双 GUI 验收也应由业务发布方确认专用测试用户/合成规范根与版本，禁用所有发送/provider 目的，不以日常规范数据制造验证。
