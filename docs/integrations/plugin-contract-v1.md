# 独立业务插件接入契约

两个 SDK 包固定为 **0.2.0**，API 1、可选运行服务 1 和程序集身份 **1.0.0.0** 保持兼容；文件版本为 0.2.0.0。.NET SDK 固定 10.0.403，Windows App SDK 固定 1.8.260921001，插件目标为 net10.0-windows10.0.19041.0/x64。SDK 源码与包分发现在通过下述可重建快照提供；独立业务 CI 不需要引用主程序或获得其他私有仓库的 token。

当前已核验源码 `ccb6ef297ab042e329d60e6c0fda517f8686379c` 的 [CI 42295396164](https://github.com/ThaoKeeton/project-workbench/actions/runs/42295396164)：Windows Core 239/239、原生 14 项、management 10 项、六个连续平台重启进程 7 项，以及钉钉 0.3.1/UPCAM 0.1.0 两个独立生产业务包的六进程 26 项检查均通过；Linux Core 为 237 通过、2 项明确 Windows DPAPI 跳过。默认 Windows SDK source/NuGet 导出与完整性检查通过，[SDK artifact 13542805335](https://github.com/ThaoKeeton/project-workbench/actions/runs/42295396164/artifacts/13542805335) 的 manifest 为 referenceOnly=false、sdkSourcesDirty=false，包含真实 WinUI SDK PRI。新固定输入钉钉 0.3.2 已实际采用宿主异步凭据批次提供方，新增仅合成模型用途的原生保存、批准并读取 host commit/revision、撤销和删除检查，需由当前提交的联合 Windows CI 验证；模型 key 的值在真正 provider dispatch 前读取，该路径未执行。不能沿用旧版仅声明元数据的结论。UPCAM 正向自动调度的独立模拟验收包仍缺失，真实业务接口及 Windows 11 人工验收仍不在上述通过范围。Linux 参考编译或 ReferenceOnly 产物不能替代真实 Windows 证据。

## 固定 SDK 分发快照与独立构建

在所选源码提交的 Windows 构建机执行：

```powershell
pwsh -File ./scripts/export-sdk.ps1
pwsh -File ./tests/scripts/SdkSnapshot.Tests.ps1 -SnapshotRoot ./artifacts/sdk-snapshot
```

CI artifact `workbench-sdk-0.2.0` 包含 `sdk-snapshot.zip`、`source/`、两个 `.nupkg` 和 `manifest.json`。清单记录完整 sourceCommit、SDK 源码是否有未提交修改、每个源码/包的 SHA256 及上述版本。固定并核对该清单后再进入独立 CI；若 sdkSourcesDirty 为 true，源码以逐文件哈希为准，不能仅凭 sourceCommit 复现。快照仅复制九个明确列出的源文件：两个 SDK 项目、三个契约源文件、两份消费者 README、Directory.Build.props 和 global.json；两个 `.nupkg` 均声明并包含各自的 README。不包含 App/Core/Desktop DLL、用户配置、凭据或业务数据。可按相同工具版本从源码重建程序集；ZIP/包哈希用于核对该次分发，不承诺不同打包时间的容器字节相同。

源码方式解压后引用 `source/src/Workbench.Sdk/Workbench.Sdk.csproj` 和 `source/src/Workbench.WinUI.Sdk/Workbench.WinUI.Sdk.csproj`。NuGet 方式将快照 `packages/` 加入本地源，在独立插件项目固定：

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
  <EnableWindowsTargeting>true</EnableWindowsTargeting>
  <UseWinUI>true</UseWinUI>
  <WindowsPackageType>None</WindowsPackageType>
  <PlatformTarget>x64</PlatformTarget>
  <EnableDynamicLoading>true</EnableDynamicLoading>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Workbench.Sdk" Version="[0.2.0]" />
  <PackageReference Include="Workbench.WinUI.Sdk" Version="[0.2.0]" />
  <PackageReference Include="Microsoft.WindowsAppSDK" Version="[1.8.260921001]" />
</ItemGroup>
```

```powershell
dotnet nuget add source C:/verified-sdk/packages --name workbench-sdk-0.2.0
dotnet restore ./Independent.Plugin.csproj
dotnet build ./Independent.Plugin.csproj -c Release --no-restore
```

只暂存自己的插件 DLL、deps、私有依赖和清单；排除两个共享 SDK、WinUI/WinRT/.NET 框架、Desktop 二进制及实际凭据。插件清单仍要求 apiVersion 1；依赖运行服务时 minimumHostVersion 为 0.2.0。业务仓库应保留快照清单、源码提交、包 SHA256 和自身 CI 结果。宿主不静态引用任何真实业务插件。

Linux 可显式执行 `pwsh ./scripts/export-sdk.ps1 -ReferenceOnly`，生成独立命名的 `artifacts/sdk-reference-snapshot/` 和 ZIP；清单 referenceOnly=true。该模式关闭 WinUI XAML/PRI 工具及 PRI 打包，仅证明 SDK C# 参考编译、包身份与快照完整性，不能作为 Windows 原生分发已验证的证据。普通 Windows 导出不使用这些开关。

## 构建与边界

- 插件是独立类库：`net10.0-windows10.0.19041.0`、x64、Windows App SDK `1.8.260921001`。引用 `Workbench.Sdk`、`Workbench.WinUI.Sdk`，SDK 稳定程序集身份 `1.0.0.0`。
- 可复用无 UI 的 `DingTalkInsights.Core`（现有 net8.0）及其真实业务逻辑；不引用/复制独立 Desktop EXE/DLL，不带其不同版本 WinUI、WinRT 或框架 DLL。两仓库保留独立开发及发布。
- 根目录为 `plugin.json`、插件 DLL、deps 和私有业务依赖。包必须排除实际凭据、用户设置、收集的数据和构建机路径配置。平台没有业务项目静态引用。
- 原生页面/设置工厂返回 `FrameworkElement`，在宿主 UI 线程调用；导航重建控件，业务状态放插件实例或持久存储。无外部窗口重挂载、WebView 或外部插件 XAML/PRI。
- 所有配置/数据使用 context.Settings、context.DataDirectory，不能自动沿用独立应用的实际用户目录或凭据。DataDirectory 是存储约定，不是沙箱；业务数据自身的加密/版本迁移由插件负责。

## API 1 与 host 0.2 扩展

原 `IWorkbenchPlugin.InitializeAsync/StopAsync`、`IPluginContext`、`WinUiView` 不改。新插件通过 `context is IPluginRuntimeContext runtime` 检查服务；依赖这些服务时清单最低 host 为 `0.2.0`，API 仍为 `1`。完整设计在 [运行服务设计](../superpowers/specs/2026-11-06-business-plugin-platform-design.md)。

`runtime.BackgroundTasks` 登记 `PluginBackgroundTask(id, title, Func<IPluginBackgroundExecution, Task>)`，只在 Initialize 登记，提交后才可启动。`execution.StartReason` 区分 UserRequest/Restore，`execution.CancellationToken` 必须贯穿所有工作；`execution.StopReason` 及 `runtime.StopReason` 区分用户停止、停用、宿主退出和初始化回滚。

钉钉适配应把真实 `MonitorSupervisor.Completion` 纳入平台登记任务并等待 Stop 完成，不使用独立应用的托盘、窗口或第二份常驻程序。Restore 需继续执行业务自己的配置指纹/授权/预算检查；HostShutdown 保留恢复意图，用户停止/停用撤销恢复。平台恢复默认关闭，更新换包后旧任务意图不直接继承。

需要业务页面完成具体范围确认的任务应标记 `RequiresPluginStart = true`：宿主通用 Start/Resume 不代替业务确认，插件拿到有效预览确认后才调用自己的 `context.BackgroundTasks.StartAsync`。目前钉钉 Runtime 的默认策略是停用/退出撤销所有具体权限，不能因为 host 0.2 提供恢复就改成无人值守授权；保留业务自己的更严格策略。

`runtime.Secrets` 保存声明过的键，Windows DPAPI CurrentUser 加密，JSON 仅保存非敏感引用。建议 ID：`openai.api-key`、`dingtalk.webhook-url`、`dingtalk.webhook-secret`。Webhook URL 也按凭据处理。错误与任务标题不能包含值；不能把此接口称为防其他可信插件访问的安全隔离。

平台只观察登记任务；插件自己的子进程、未登记任务、流和状态库仍必须在取消后停止，并在已知工作完成后才 Dispose。超时不会自动强杀或假装停止。

## 安全的首个业务验证

首个闭环用真实业务 Core 和模拟输入/模型/发送实现，不使用真实 DWS、账号、API key 或 Webhook。默认外发关闭；“信任插件”“恢复后台任务”和“允许读取/模型上传/发送”是不同决定。宿主的用户根锁不能保证旧独立应用不同时监控；业务侧需明确共享资源所有权及迁移步骤，旧版监控未停止时不得声称不存在重复发送风险。

业务仓库提供独立插件 ZIP、源码提交、成功 CI、包 SHA256、命令/任务 ID 和测试模式说明。平台可将已验证包作为带来源的固定测试输入，避免给跨私有仓库 CI 增加额外 PAT；测试包不作为默认 bundled 插件分发。

实际 Windows 测试覆盖安装后默认未信任、明确批准、页面/设置、模拟业务读/搜索/整理、后台页切换继续运行、停止、退出真实进程后的恢复，以及错误配置和重启更新/回滚。报表必须注明来源和模拟范围；Server 2025 自动检查不等于 Windows 11 人工验收。

包回滚只回退代码、保留当前业务数据，更新前保留数据/配置/密文备份；不自动回放历史发送回执或做任意数据格式降级。业务插件需自己维护 schema 和去重兼容。
