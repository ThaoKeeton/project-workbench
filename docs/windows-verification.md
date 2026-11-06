# Windows 构建与原生验证

证据分层记录。Linux Core 行为测试、跨目标 C# 参考编译、Windows 发布、真实 WinUI 自动冒烟和 Windows 人工验收各有不同范围，不互相替代。

| 检查 | 已有证据 | 范围与限制 |
| --- | --- | --- |
| Task 3 Linux Core | 一次完整运行 221 通过、2 项明确 Windows DPAPI 跳过，共 223 项；随后新增 OS 进程用例的最终聚焦运行 12/12 通过 | 聚焦运行包含 6 个实际子进程，验证通用 opt-in Restore、RequiresPluginStart 不恢复、停用后不复活；另含 HostRuntime 持锁/Stop 重试、Apply 顺序、坏元数据独立根及只读凭据状态。未把更新后的完整套件宣称为已重新运行；TRX 在 Task 3 报告 |
| Task 3 Linux C# reference | App 与独立 NativeFixture 参考编译 0 warnings/errors | `tests/Workbench.NativeReference` 仅为不可运行的类库；InitializeComponent 占位直接抛异常，没有生产入口/资源替代；不验证 SDK 生成 Main、XAML、主 PRI 或实际 WinUI |
| Task 3 Windows/SDK native（Task 4 BASE） | [42285023349](https://github.com/ThaoKeeton/project-workbench/actions/runs/42285023349)，提交 `27154465c08d132798c8ea7b637f137f7340a90b`：Windows Core 226 通过、原生 14 项、management 10 项、六个连续实际进程 7 项、默认 Windows SDK source/NuGet export 均通过 | 控制器已检查实际报告；该结果只证明 Task 4 之前的 BASE，不是下述新增独立业务包探针的 Windows 执行证据 |
| Task 4 Linux Core/固定业务包 | 完整运行 234 通过、2 项 Windows DPAPI 明确跳过，共 236 项；两个真实生产 ZIP 的 Store 安装及逐文件 SHA256 校验通过 | 不执行 WinUI 或业务程序集；新参数/脚本报告验证与不可运行的 App C# reference 编译另行记录在 Task 4 报告 |
| Task 4 Windows 独立业务包 | 当前新增提交仍待控制器运行 CI | 已编写下述实际 shipping App 六进程测试；不得把 Linux、上游 source-linked host 或 Task 3 BASE GREEN 当作通过 |
| Linux Core Release | 86/86，通过；包括真实临时目录、独立插件 DLL、自包含子进程 | 不覆盖 Windows 文件系统分支或原生 UI |
| Linux C# 检查 | 早期手动主程序版本参考编译通过；当前主程序 7 个 C# 文件语法与 XML 检查通过 | 采用标准 App.xaml 后，XAML 编译器必须在 Windows 执行；当前主程序没有 Linux 完整语义编译证据 |
| Linux 正常原生构建 | 按预期受阻：MSB6003，`MakePri.exe` 的 Exec format error | Windows 原生工具无法在此 Linux 环境运行，未修改生产目标绕过 |
| Windows Core 测试/发布（历史基线） | [第八轮分支 CI](https://github.com/ThaoKeeton/project-workbench/actions/runs/42209929309)：Windows Core 86/86，自包含主程序与两个独立插件包构建通过；发布的 `Workbench.App.pri` 为 1,352,680 bytes | 提交 `d615fa064c8962ed849b494b8b42d1ca54e67b0a`，使用 `windows-2025` runner |
| Windows 原生自动冒烟（历史基线） | 同轮 `succeeded=true`、`stage=completed`，原有 11 项检查全部通过：原生启动、占用根失败窗口、实时主题、4 个页面/设置控件、文件命令、停用/重新启用及关闭 | 没有编辑/导航/重复生成回归，不能证明该行为安全；自动检查不代替人工验收 |
| Windows 编辑回归 RED | [第九轮分支 CI](https://github.com/ThaoKeeton/project-workbench/actions/runs/42213219634)，提交 `9e97dc6b596d3821803189b0c55d112f2be9ba48`：实际原生进程 exit 1，`Summary unsaved draft was lost after navigating through home.` | Windows Core 86/86、主程序与独立插件编译发布成功后，在真实 TextBox 编辑/新工厂导航断言失败；不是编译或初始化故障 |
| Windows 本轮构建 GREEN | [第十二轮分支 CI](https://github.com/ThaoKeeton/project-workbench/actions/runs/42216264601)：Windows Core 86/86，主程序与两个独立插件发布通过，主 PRI 1,352,680 bytes；两个 job 均成功 | 实测代码提交 `a5d9825a9662710ff10dd833e0f9d53cfeaf8cdd`，使用 `windows-2025` runner |
| Windows 本轮编辑回归 GREEN | 同轮原生进程 exit 0，`succeeded=true`、`stage=completed`、UI 线程 2，全部 13 项通过；最终摘要 100 bytes、CSV 293 bytes | 包含新工厂导航/真实编辑/保存/拒绝覆盖/停用恢复；原有 11 项检查保留 |
| Windows 人工验收 | 尚未执行；下方清单全部待验 | 主题外观、DPI、文件选择器、可访问性及真实重启仍需实机 |

历史基线的 11 项检查没有输入用户编辑，摘要为 235 bytes。第九轮在原来的无文档模型/无覆盖保护实现上出现上述导航断言 RED。第十轮修复模型后仍因原始 LF 字面预期失败；第十一轮增加隔离诊断，确认编辑前后与新工厂文本全文相同、均为 62 字符，唯一差异是输入字面的第 42 位 LF（U+000A）被 WinUI 规范为 CR（U+000D）。TextChanging 已读到完整新文本，随后 TextChanged 的值一致。

因此回归先仅统一 CRLF/CR/LF 核对完整输入内容，再捕获真实 `TextBox.Text`；此后导航、保存文件、重复生成和停用恢复全部按该原生值严格逐字比较。没有 Trim、部分匹配或省略多行内容。第十二轮在该测试边界修正后完成全部 13 项，100-byte 摘要是实际编辑并保存的版本。Windows 证据来自 GitHub `windows-2025` runner，Windows 11 人工清单的 14 项仍全部待验。未连接真实业务系统。

## 干净 Windows 环境

使用受支持的 Windows 11 24H2+ x64、PowerShell 7、仓库 `global.json` 所需的 .NET 10 SDK。IDE 可选；若使用，需 Visual Studio 2026 18.0+。SDK 的 NuGet 依赖提供 Windows 原生构建工具。API 目标 19041 只是调用 API 下限，不承诺 Windows 10 已获支持或验收。

```powershell
dotnet --info
dotnet test ProjectWorkbench.Core.slnx -c Release --nologo
pwsh -File .\scripts\build.ps1
.\artifacts\native\app\Workbench.App.exe
```

发布目标为 win-x64、unpackaged、自包含目录，保留完整 `app/` 及其 `Plugins/` 子目录。没有安装器、签名、上线或部署步骤。构建脚本遇到任意非零 dotnet 退出码即失败，并从干净的 `artifacts/native/` 开始，避免旧插件混入；还要求主程序资源图 `app/Workbench.App.pri` 存在且非空。该文件由标准 SDK PRI 生成/合并/发布目标产生，不能仅保留框架的 PRI 文件。

## 自动冒烟与 CI 产物

```powershell
pwsh -File .\scripts\native-smoke.ps1 -TimeoutSeconds 120
```

脚本启动 `artifacts/native/app/Workbench.App.exe --smoke-test --smoke-report <报告绝对路径>`，最多等待 120 秒。EXE 在真实 `Application.Start` UI 线程运行以下检查：

1. 为每次运行创建新的临时用户根目录，仅复制并发现 `com.projectworkbench.summary` 与 `com.projectworkbench.test-cases` 两个分发样例，检查默认禁用/未信任。
2. 仅对实际文件锁获取验证 Windows 共享/锁冲突；创建同隔离根目录的第二个测试窗口，检查启动失败后无法导航到设置/首页且可安全关闭。
3. 为测试明确批准并启用这两个样例，验证共享 WinUI/WinRT 契约身份，并逐个创建、附加两个页面与两个设置控件到真实 XamlRoot。
4. 在同一个已附加的主程序卡片上检查 Light/Dark/Light 的实际主题与填充/描边颜色重新解析，再应用系统主题。运行两个本地命令，检查隔离目录下的非空摘要与 CSV 文件。
5. 设置真实摘要 TextBox.Text 为不同于预置内容的多行文本，等待原生文本事件并核对完整输入；经过首页、命令、主程序设置和两插件设置后用新工厂返回，检查捕获的原生文本逐字不变。重复生成须明确拒绝且保留模型/文件；通过 `ButtonAutomationPeer.Invoke` 请求真实保存按钮并等待异步 UI 状态后检查磁盘文本，再次生成仍不得覆盖。物理键盘、焦点及屏幕阅读器行为仍由下方人工清单验收。
6. 再修改摘要但不保存；清除控件，停用并重新启用两个样例，再次附加原生控件；检查注册撤销与恢复，摘要仅恢复已保存版本且文件不变。
7. 停止插件、清除注册，检查保存的 enabled 偏好仍为 true，写入 JSON 并关闭窗口。

报告包含 `succeeded`、`stage`、`userRoot`、启动阶段/线程/时间、检查列表、文件路径/大小和完成时间；失败包含异常与 HRESULT。当前从 SDK 生成入口进入 Application 构造开始，记录编译后的 Application 资源初始化、窗口和控件阶段；更早的失败由启动器报告和进程诊断定位。进程入口前的模块初始化崩溃可能无法由 EXE 写报告，脚本会补充失败 JSON；即使原生进程已写成功报告，后续退出失败或超时也会把最终 succeeded 改为 false，并保留已有检查与原生阶段。报告不含真实用户设置或业务数据。临时根目录保留用于故障检查，可在进程退出后按报告路径自行删除。超时是失败；脚本为限制测试运行时间而终止测试进程，不表示普通停用可强制取消插件。

`.github/workflows/build.yml` 在 PR（包括草稿）、`main`/`feat/native-plugin-workbench` push、手动触发时运行：

| Artifact | 原始路径 | 用途 |
| --- | --- | --- |
| `workbench-win-x64` | `artifacts/native/app/` | 完整自包含可启动目录与两个独立插件包 |
| `native-smoke-report` | `artifacts/native/native-smoke.json` | 原生运行检查、启动阶段、失败层与临时数据位置 |
| `native-host-generated-sources` | `src/Workbench.App/obj/**/App.g*.cs`、`XamlTypeInfo.g.cs` | 检查固定 SDK 实际生成的入口、元数据和 InitializeComponent |
| `native-startup-diagnostics` | `artifacts/native/native-stdout.log`、`native-stderr.log`、失败时的 `native-crash-events.json` | 进程输出及只匹配 Workbench.App 的 Windows Application 崩溃事件；无法读事件时记录原因 |
| `native-debugger-evidence` | 尝试失败诊断时生成的 `artifacts/native/native-debug/` | Microsoft 签名的 x64 CDB 就地采集 HRESULT、错误消息、原生调用栈和采集状态；仅在尝试采集后上传，采集失败仍上传已有部分证据 |
| `core-linux-results` | `artifacts/tests/` | Linux TRX |
| `core-windows-results` | `artifacts/tests/` | Windows TRX（包括 Windows 文件系统分支） |

如果 Core、发布或原生启动失败，job 保持失败状态；上传已有产物供排查，缺少必需报告不会被解释为通过。

原生冒烟失败后才运行 `scripts/native-debug.ps1`，默认最多采集 240 秒；如需安装官方调试器，仅安装调试工具组件并验证 Microsoft 签名。采集只发布相关文本，不生成或上传进程内存 dump，不执行堆遍历或导出完整环境。调试器及被调试程序启动前清空继承环境，仅保留明确允许的 OS、运行时和代理配置，不继承 CI token。调试采集不修改原始失败判定。第六轮通过这些文本定位了 InfoBar 默认样式资源加载失败；第七轮补齐主 PRI 发布后完整冒烟通过。成功运行会跳过采集及其 artifact 上传；采集已尝试但失败或取消时，仍上传已有部分证据。必需的应用目录和冒烟报告仍严格要求存在。

第十/十一轮实际原生失败后均尝试采集并成功上传 `native-debugger-evidence`，保留故障文本。第十二轮实际 job 结果显示采集与对应 artifact 上传均为 skipped；必需应用/报告 artifact 上传成功，日志没有缺少调试文件的 `No files were found` 警告。

Linux 可以执行 Core 测试、源文件语法/XML 检查，以及独立 SDK/示例的 C# 参考编译。完整主程序需要 Windows XAML 编译器，`App.xaml` 的生成代码和资源处理由 Windows 构建验证。单独 SDK 参考检查示例（不能用于发布或声称原生验收）：

```sh
dotnet build src/Workbench.WinUI.Sdk/Workbench.WinUI.Sdk.csproj -c Release \
  -p:WindowsAppSDKSelfContained=false -p:AppxGeneratePriEnabled=false
```

## host 0.2 管理与实际进程重启探针（本轮 Windows 待运行）

构建脚本额外编译 `tests/Workbench.NativeFixture` 到 `artifacts/native/fixtures/runtime/`，只暂存夹具 DLL、deps 与清单。它不进入 `app/Plugins/`，普通启动不会安装、发现、信任或执行它。原有冒烟保留全部 13 项，再增加 Background/Credentials/Package maintenance 初始化导航检查。

```powershell
pwsh ./scripts/native-runtime-probe.ps1 -TimeoutSeconds 180
pwsh ./scripts/export-sdk.ps1
pwsh ./tests/scripts/SdkSnapshot.Tests.ps1 -SnapshotRoot ./artifacts/sdk-snapshot
```

runner 为 `management` 建立一个新 GUID 根，并为下列六阶段共享另一个专用 GUID 根；每阶段实际 Start-Process、等待退出成功及报告成功后才启动下一进程。原生 EXE 只接受 `%TEMP%/ProjectWorkbench-runtime-probe/<GUID>` 下的 probe-root；非 probe 模式不接受该参数。测试只使用固定的本地夹具与合成 DPAPI 值，无真实业务凭据、网络提供方或消息发送。

| 阶段 | 实际行为与必需断言 |
| --- | --- |
| management | 初始化管理导航、默认空 PasswordBox、真实 Save/Read/Delete/重新进入空框；同步前缀后台工作期间 UI dispatcher/导航响应、Stopped/Faulted 状态与安全错误；RequiresPluginStart 禁用通用控件、转到真实插件页面确认；初始化失败撤销贡献；held Stop、Lifetime callback、registered task 三种关闭超时保留同一 Stop/实例/租约、可查询重试，真实结束才释放；宿主异步包操作未结束时拒绝关闭；本地维护元数据损坏不阻断无关 bundled 根 |
| restart-seed | 明确信任合成插件、保存数据/设置/DPAPI，手动开启 generic worker 恢复，插件页面策略任务仅运行本次确认的工作 |
| restart-verify | 新进程自动 Restore generic worker；RequiresPluginStart 任务保持 Stopped/Resume=false/DesiredRunning=false，历史运行数不增加；验证页面/设置/数据/DPAPI 后明确停用 |
| restart-disabled | 第三进程保持停用、无新任务执行；暂存 v2 更新，当前仍 v1 |
| update-verify | 第四进程在发现前 Apply 到 v2，撤销信任/启用/恢复；重新明确批准后校验当前数据与密文，按 HistoryId 暂存代码回滚 |
| rollback-verify | 第五进程回到 v1、仍需重新信任；当前数据/凭据保持，暂存移除 |
| remove-verify | 第六进程移除代码，保留当前 data/settings/secrets/代码历史；generic/guarded 执行计数没有增加 |

报告 `artifacts/native/runtime-probes/<phase>.json` 包括该进程 PID、临时根、检查列表和真实启动顺序；stdout/stderr 与 summary.json 同目录。超时视为失败，runner 保留尚在运行的隔离进程并报告 PID，不以强杀证明停止。CI artifact 为 `native-runtime-probes`。后台/凭据原生行为、Windows 文件锁分支与 DPAPI 只有实际 Windows 运行成功后才算通过。

SDK artifact `workbench-sdk-0.2.0` 另行导出 manifest、两个 0.2.0 nupkg 和固定版本源码；完整重建/消费步骤与 Linux ReferenceOnly 限制见 [独立插件契约](integrations/plugin-contract-v1.md)。Task 3 不导入真实 UPCAM/钉钉包；真实 RequiresPluginStart 监控仍须遵守其业务自己的新预览/停用撤销策略，不能用通用夹具 Restore 结果冒充业务授权。

Linux 参考命令（只编译 C#，不运行应用）：

```sh
dotnet build tests/Workbench.NativeReference/Workbench.NativeReference.csproj -c Release \
  -p:UseWinUI=false -p:AppxGeneratePriEnabled=false -p:WindowsAppSDKSelfContained=false
pwsh ./scripts/export-sdk.ps1 -ReferenceOnly
pwsh ./tests/scripts/SdkSnapshot.Tests.ps1 -SnapshotRoot ./artifacts/sdk-reference-snapshot
```

## Task 4 独立钉钉与 UPCAM 生产包验收（本轮 Windows 待运行）

此探针运行当前 `artifacts/native/app/Workbench.App.exe`，使用当前 `HostRuntime`、真实 Store 和原生管理界面。它不运行上游 source-linked 旧宿主，不把两个平台样例当作业务插件，也不静态引用业务程序集。固定输入和来源记录见 [integration inputs](../tests/integration/README.md)。

```powershell
pwsh ./scripts/build.ps1
pwsh ./scripts/import-business-plugin.ps1
pwsh ./scripts/business-plugin-smoke.ps1 -TimeoutSeconds 300
```

仅显式 `--business-probe <phase> --probe-root <root> --smoke-report <report>` 开启此模式；root 必须是 `%TEMP%/ProjectWorkbench-business-probe/<GUID>` 的直接子目录，并排除 bundled 发现根。普通启动不读取或安装这些输入。六个阶段共享同一隔离根，每个阶段实际退出后才启动下一个。

| 阶段 | 必需业务断言 |
| --- | --- |
| seed | 真正安装两个独立 ZIP；逐文件匹配已固定上游字节、禁用/未信任/未加载；实际信任弹窗取消及确认。钉钉实际设置、加密草稿、demo Core 读取/搜索/整理/历史，单个真实 MonitorSupervisor 跨页面继续；RequiresPluginStart 通用 Start/Resume 拒绝、原生 Stop/host Stop/停用完整退役。UPCAM 实际生产无窗口 worker/Core fictional preview、原生页面/设置/默认 profile 绑定、缺少自动授权的明确开始拒绝与真实 registered callback Faulted、安全 Stop/停用/worker 退役 |
| restart | 新 OS 进程恢复可信插件登记；钉钉任务仍停止、无业务连接/调度，必须新预览；UPCAM 恢复前无 worker/任务；实际加密草稿/非敏感偏好/profile 绑定保留；暂存两个只改 manifest 版本的明确合成升级，当前 DLL 不变，发现后 Apply 拒绝 |
| update | 启动发现前 Apply 到合成版本，撤销信任/启用/恢复；原厂 DLL 逐文件不变、当前业务数据及实际备份保留；重新信任后真实页面可用。再暂存更高合成版本，仅损坏未加载的 staged manifest，供下一进程测试失败 |
| fault | 真实启动 Apply 因 staged 指纹损坏失败，旧当前代码/数据不变；保留 Prepared journal；取消后按实际 HistoryId 暂存原版代码回滚 |
| rollback | 原版完整字节恢复且需新信任；不恢复旧业务数据；真实页面/worker sample 仍可用，暂存保留数据的移除 |
| remove | 启动前移除两个代码目录；业务程序集未加载，保留当前 data/settings/已有 secrets 和六份代码历史；再次验证当前文件 SHA256 |

业务探针的 `artifacts/native/business-probes/<phase>.json` 和 `summary.json` 由 CI artifact `independent-business-plugin-probes` 收集，包含实际 PID、**实际 OS exitCode**、来源提交/ZIP SHA256、合成更新 ZIP SHA256、当前文件/备份/停止状态、已观察 worker 的实际 PID/exitCode、实际 WinUI Closed 事件和启动器退出后重新获取租约的证据。缺失报告、非零退出、PID/来源不匹配或超时均失败；已有原生成功报告也不能覆盖进程失败。超时保留已知进程所有权并停止后续阶段，不强杀来证明正常停止。

UPCAM backend 的 Ready 状态与原生控件就绪分别核对：探针解析当前 `ContentControl.Content` 的 TabView，明确选择 Profile/Run 页，并等待当前所选内容加载、测量、附着到同一 XamlRoot 且与可视树一致；连续观察必须属于同一当前 host/content，替换后重新解析。期限保持不变。失败诊断仅含有界的控件类型、AutomationID、加载/布局信息；`<phase>.evidence.json` 另保留失败前已完成检查/观察和已见 worker PID，不替代主报告或把失败变成成功，不输出 Text/Password/配置值。

本轮 Linux 只能执行固定包审计/真实 Store 安装、报告与隔离参数测试及不可运行的 C# reference 编译；Windows 原生执行必须以当前提交的实际 CI 结果为准。原有样例 13 项加管理导航、独立平台 fixture 的 runtime probes 均保留，业务结果单独记录。

覆盖边界：钉钉三个 host secret definitions 已登记，但上游明确推迟实际 host value 消费；UPCAM host secret 只用于程序生成的恢复绑定 key，业务凭据仍使用原 worker DPAPI。本探针没有自动业务授权，不创建恢复 key。UPCAM 上游 workflow 没有发布其不同 ID 的 synthetic acceptance 包，因此正向自动业务提供方执行未验；两插件均未执行发送、真实 Webhook/HTTP、真实 DWS/Teambition/OpenAI、企业权限或真实凭据测试。代码更新/故障版本是明确合成的集成输入，不是上游新发布；UserRoot 备份不包含外部独立版规范状态或 DWS profiles，不证明任意 schema 降级或 live Webhook 去重。Windows11 人工清单仍全部待验。

## Windows 实机人工清单（全部待验）

记录 Windows 版本、架构、DPI、提交 SHA 和观察结果。使用专门的测试 Windows 用户以获得干净的 `%LOCALAPPDATA%/ProjectWorkbench/`，保留实际用户数据。

- [ ] 首次启动只展示清单；两个示例未自动启用，页面/命令入口不存在。
- [ ] 插件详情可查看 ID、版本、能力说明、目录和诊断；取消信任对话框不运行插件，确认后才启用。
- [ ] 两个示例各自的页面与设置作为原生控件出现在同一窗口；无外部应用窗口和 WebView。
- [ ] 摘要生成、编辑和保存，跨页面保留未保存编辑，重复生成明确拒绝覆盖；停用/退出前保存的版本可恢复。测试用例逐行编辑和 CSV 导出均明确标注示例；文件写到各自 data 目录。
- [ ] 各插件设置保存后，停用/重新启用恢复偏好；页面和命令停用后立即消失，停止出错时也不残留可执行入口。
- [ ] 退出整个进程再重新启动，未变化且已信任/启用的插件恢复；停用的插件保持停用；主题与设置恢复。
- [ ] 同时启动第二个普通实例显示用户目录占用错误；该失败窗口不能导航到需要管理器的页面并能安全关闭。隔离冒烟可与普通实例并存。
- [ ] 本地文件夹选择器可选择独立的新 ID 开发包并安装；安装不执行或自动启用；重复 ID、无效清单和被拒绝路径显示可见错误。
- [ ] 关闭程序后更改包内容，再启动会撤销继承的信任；更新/删除需退出并重启，停用不声称卸载程序集。
- [ ] 系统/浅色/深色主题下查看主程序及两插件的对比度、焦点、文字可读性；应用主题与系统主题不同时以及现有卡片的实时切换正常。切换系统主题时跟随行为正常。
- [ ] 在 100%、150%、200% DPI 及不同窗口宽度下查看滚动、文本换行与可编辑表格；没有固定内容宽度截断。
- [ ] 键盘 Tab、Shift+Tab、Enter、焦点提示和屏幕阅读器名称可用，文件选择与信任弹窗可取消。
- [ ] 运行命令异常、损坏设置/用户状态文件时显示对应错误，正常插件仍可使用。
- [ ] 关闭窗口先等待插件停止，再退出；重入关闭不重复启动停止流程；enabled 偏好不被写成 false。不响应的可信插件不能被普通生命周期机制强制中断。
- [ ] 后台任务在切换业务页面/设置后继续运行，停止/故障可见；需业务预览的任务只能从所属页面开始，通用恢复不可用。
- [ ] 凭据框每次打开均为空，保存/删除后清空；仅显示“已保存（未验证值）”，错误及日志不包含值；使用合成凭据验收。
- [ ] ZIP 与文件夹选择器均可取消；更新/回滚/移除提示重启、重新信任和代码/当前数据/外部存储边界；Prepared 可取消，已开始的恢复 journal 不能丢弃。
- [ ] 关闭等待期间可查看管理状态并重试，不能重新启用或启动新工作；宿主包写入未结束时同样保留锁，Windows 11 多窗口/重复关闭实际交互正常。
