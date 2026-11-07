# Windows 构建与原生验证

证据分层记录。Linux Core 行为测试、跨目标 C# 参考编译、Windows 发布、真实 WinUI 自动冒烟和 Windows 人工验收各有不同范围，不互相替代。

0.3 项目工作区新增 `scripts/project-workspace-probe.ps1`：源码构建后在两个实际 shipping EXE 进程中依次运行 `--workspace-probe seed|verify`，根只允许 `%TEMP%/ProjectWorkbench-workspace-probe/<GUID>/`。原生报告由实际 Closed 事件写入；启动器独立核对 PID/phase/root、OS exit 0、实际根租约重新取得及完整检查列表，再允许第二进程。检查使用真实项目对话框/选择器、文本框、主题与凭据控件和文件，包含旧准备挂起、候选停止挂起、回滚新 manager 绑定、草稿、归档和重开。固定 NativeFixture 位于 `artifacts/native/fixtures/runtime`，不进入用户主程序或业务伴随包；普通下载没有此源码测试依赖。新增源码的 Linux 聚焦行为/参考编译不构成 Windows 原生通过，需本轮控制器运行完整 CI 并下载核验 `project-workspace-probes`。原有 native14、management17+theme1、platform7、business12进程51项继续保留。Windows 11 人工、真实独立 GUI/更高版本迁移/provider 验收仍延期，当前不要求用户测试。

Task4 round1 增加实际 WM_SYSCOMMAND/SC_CLOSE：真实切换在旧 hook 挂起时关闭、已接纳原生 action 挂起且后排目标待运行时关闭、Initialize 前关闭后再调用初始化。前两项核对真实 factory 启动阶段和目标插件实例文件不增加、修正/原生重试关闭、实际 Closed 与租约释放；action 用例还保留一次真实主题写入故障，防止提前封闭准入绕过失败偏好。seed 报告现在必须包含原9项加这3项共12项，verify 仍3项；辅助 startup JSON 只记录实际启动观察，不是成功判据。新增 native RED/GREEN 在 Linux 均未执行，不能把参考编译或 portable callback 测试当作原生调用方通过。最终仍需 Closed、OS exit0、重获租约的共同证据。

| 检查 | 已有证据 | 范围与限制 |
| --- | --- | --- |
| Task 3 Linux Core | 一次完整运行 221 通过、2 项明确 Windows DPAPI 跳过，共 223 项；随后新增 OS 进程用例的最终聚焦运行 12/12 通过 | 聚焦运行包含 6 个实际子进程，验证通用 opt-in Restore、RequiresPluginStart 不恢复、停用后不复活；另含 HostRuntime 持锁/Stop 重试、Apply 顺序、坏元数据独立根及只读凭据状态。未把更新后的完整套件宣称为已重新运行；TRX 在 Task 3 报告 |
| Task 3 Linux C# reference | App 与独立 NativeFixture 参考编译 0 warnings/errors | `tests/Workbench.NativeReference` 仅为不可运行的类库；InitializeComponent 占位直接抛异常，没有生产入口/资源替代；不验证 SDK 生成 Main、XAML、主 PRI 或实际 WinUI |
| Task 3 Windows/SDK native（Task 4 BASE） | [42285023349](https://github.com/ThaoKeeton/project-workbench/actions/runs/42285023349)，提交 `27154465c08d132798c8ea7b637f137f7340a90b`：Windows Core 226 通过、原生 14 项、management 10 项、六个连续实际进程 7 项、默认 Windows SDK source/NuGet export 均通过 | 控制器已检查实际报告；该结果只证明 Task 4 之前的 BASE，不是下述新增独立业务包探针的 Windows 执行证据 |
| Task 4 历史 Linux Core/旧业务包 | [42295396164](https://github.com/ThaoKeeton/project-workbench/actions/runs/42295396164)，源码 `ccb6ef2`：237 通过、2 项 Windows DPAPI 明确跳过，共 239 项；16 项脚本检查及两个真实生产 ZIP 的逐文件审计通过 | Linux 不执行 WinUI 或业务程序集；参考编译仅为不可运行的类库 |
| Task 4 历史 Windows/旧业务包 | 同轮源码 `ccb6ef2`：Windows Core 239/239（含两项真实 DPAPI），发布、原生 14 项、management 10 项、六个连续平台重启进程 7 项、SDK 默认 Windows export，以及真实生产业务包六进程 26 项均通过 | 当时使用旧钉钉 0.3.1，且只有生产 UPCAM 缺授权拒绝路径；其结果不代替下列最新源码正向验收 |
| 当前 Linux Core | [42307917231](https://github.com/ThaoKeeton/project-workbench/actions/runs/42307917231)，源码 `a54a668`：265 通过、2 项明确 Windows DPAPI 跳过，共 267 项；脚本检查通过 | 不执行 WinUI 或 Windows DPAPI |
| 当前 Windows 平台/SDK | 同轮 `a54a668`：267/267（含两项真实合成值 DPAPI）、发布、原生 14 项、management 17 项、六阶段平台重启 7 项；默认 Windows SDK 导出/完整性通过 | Server 2025 实际 EXE；9 份源码、2 包 README/哈希、真实 SDK PRI 704 bytes；保留独立 Windows 11 人工门槛 |
| 当前独立业务包/正向验收 | 同轮当前发布目录 EXE：钉钉 0.3.2/UPCAM 0.1.0 的六生产进程 27 项，另一个隔离根的独立 UPCAM acceptance-ID 后六进程 24 项，共 51 项 | 全部实际 WinUI Closed、OS exit0、退出后重获 lease、来源/包哈希/PID 一致；定时 Core 和本地 fake providers 通过，真实送达未测试 |
| Linux Core Release | 86/86，通过；包括真实临时目录、独立插件 DLL、自包含子进程 | 不覆盖 Windows 文件系统分支或原生 UI |
| Linux C# 检查（历史基线） | 早期手动主程序版本参考编译及 7 个 C# 文件语法/XML 检查通过 | 后续不可运行的 NativeReference 语义编译已单列；生产 XAML/PRI 工具仍必须在 Windows 执行 |
| Linux 正常原生构建 | 按预期受阻：MSB6003，`MakePri.exe` 的 Exec format error | Windows 原生工具无法在此 Linux 环境运行，未修改生产目标绕过 |
| Windows Core 测试/发布（历史基线） | [第八轮分支 CI](https://github.com/ThaoKeeton/project-workbench/actions/runs/42209929309)：Windows Core 86/86，自包含主程序与两个独立插件包构建通过；发布的 `Workbench.App.pri` 为 1,352,680 bytes | 提交 `d615fa064c8962ed849b494b8b42d1ca54e67b0a`，使用 `windows-2025` runner |
| Windows 原生自动冒烟（历史基线） | 同轮 `succeeded=true`、`stage=completed`，原有 11 项检查全部通过：原生启动、占用根失败窗口、实时主题、4 个页面/设置控件、文件命令、停用/重新启用及关闭 | 没有编辑/导航/重复生成回归，不能证明该行为安全；自动检查不代替人工验收 |
| Windows 编辑回归 RED | [第九轮分支 CI](https://github.com/ThaoKeeton/project-workbench/actions/runs/42213219634)，提交 `9e97dc6b596d3821803189b0c55d112f2be9ba48`：实际原生进程 exit 1，`Summary unsaved draft was lost after navigating through home.` | Windows Core 86/86、主程序与独立插件编译发布成功后，在真实 TextBox 编辑/新工厂导航断言失败；不是编译或初始化故障 |
| Windows 本轮构建 GREEN | [第十二轮分支 CI](https://github.com/ThaoKeeton/project-workbench/actions/runs/42216264601)：Windows Core 86/86，主程序与两个独立插件发布通过，主 PRI 1,352,680 bytes；两个 job 均成功 | 实测代码提交 `a5d9825a9662710ff10dd833e0f9d53cfeaf8cdd`，使用 `windows-2025` runner |
| Windows 本轮编辑回归 GREEN | 同轮原生进程 exit 0，`succeeded=true`、`stage=completed`、UI 线程 2，全部 13 项通过；最终摘要 100 bytes、CSV 293 bytes | 包含新工厂导航/真实编辑/保存/拒绝覆盖/停用恢复；原有 11 项检查保留 |
| Windows 人工验收 | 尚未执行；下方清单全部待验 | 主题外观、DPI、文件选择器、可访问性及真实重启仍需实机 |

当前 [CI 42307917231](https://github.com/ThaoKeeton/project-workbench/actions/runs/42307917231) 的两个 job 均成功，实际运行源码为 `a54a668d3f2c98fda7ab826ed3cf72b9c64a2b06`。生产阶段 seed/restart/update/fault/rollback/remove 的应用 PID 为 3532/6348/1404/8644/6564/9052，检查 6/5/5/3/5/3，共 27 项；独立 acceptance-seed/restore/stopped/config/missing/disabled 的 PID 为 2664/9196/8804/6100/1792/6836，检查 5/5/3/4/4/3，共 24 项。12 个进程均已实际退出，OS exitCode 0、WinUI Closed、启动器重新取得 host lease、来源与哈希核验全部通过。两组使用不同全新 GUID 根，前组及其 worker 全退役后才启动后组。

完整应用 ZIP 已下载核验：90,314,660 bytes，SHA256 `271d2742bb849e334469216526ff0b15c38cb230cb14f42a20184f6145270cc4`，523 个条目 CRC 通过；其中 `Workbench.App.exe` SHA256 为 `c126936e8707cef25878e35edc54b92846023714ee7cb850c5c0e7b56bbfd7d0`，与全部 12 个业务报告一致；主 PRI 为 1,352,680 bytes。默认只分发两个示例，业务插件独立安装。该产物是未签名的完整自包含目录，无安装器或正式 release 部署。

| 本轮产物 | 下载与校验 |
| --- | --- |
| 完整 Windows x64 应用 | [13547711575](https://github.com/ThaoKeeton/project-workbench/actions/runs/42307917231/artifacts/13547711575)，SHA256 `271d2742bb849e334469216526ff0b15c38cb230cb14f42a20184f6145270cc4` |
| 12 进程业务报告 | [13547057995](https://github.com/ThaoKeeton/project-workbench/actions/runs/42307917231/artifacts/13547057995)，61,637 bytes，SHA256 `ad05df1d141fd01061ccf95f70889b75c3ee7fadc9dc04deb600f96c6c4de850` |
| 独立 SDK 0.2.0 | [13547796386](https://github.com/ThaoKeeton/project-workbench/actions/runs/42307917231/artifacts/13547796386)，45,753 bytes，SHA256 `2ebdee26f619c152f20d3bcc1ce1e09a2d511e7a7d02359a2020af1a7000376e` |
| 平台管理/重启报告 | [13547561800](https://github.com/ThaoKeeton/project-workbench/actions/runs/42307917231/artifacts/13547561800)，9,134 bytes，SHA256 `d86cf83a9f9e01bac6a57e263619eb4765463f6961063a72bd1b5eb26540fa8f` |
| Windows Core TRX | [13546963320](https://github.com/ThaoKeeton/project-workbench/actions/runs/42307917231/artifacts/13546963320)，65,504 bytes，SHA256 `c760422761bfde3549847f61a51b8d698b749e0e3e0e065cd1f1a257a46cb189` |

此前实际复现并修正正常完成任务 Stop 状态与 UPCAM 控件替换后的探针就绪问题；本轮还修复宿主异常文本边界、保留运行实例的 Stop 入口、已备份但未替换阶段的安全中止，以及两处已被真实 CI 暴露的测试时序问题。当前正向自动验收已执行，不能把生产包缺授权 Faulted 单独当作正向证明。未执行真实 Webhook/HTTP、企业接口或业务发送。

历史基线的 11 项检查没有输入用户编辑，摘要为 235 bytes。第九轮在原来的无文档模型/无覆盖保护实现上出现上述导航断言 RED。第十轮修复模型后仍因原始 LF 字面预期失败；第十一轮增加隔离诊断，确认编辑前后与新工厂文本全文相同、均为 62 字符，唯一差异是输入字面的第 42 位 LF（U+000A）被 WinUI 规范为 CR（U+000D）。TextChanging 已读到完整新文本，随后 TextChanged 的值一致。

因此回归先仅统一 CRLF/CR/LF 核对完整输入内容，再捕获真实 `TextBox.Text`；此后导航、保存文件、重复生成和停用恢复全部按该原生值严格逐字比较。没有 Trim、部分匹配或省略多行内容。第十二轮在该测试边界修正后完成全部 13 项，100-byte 摘要是实际编辑并保存的版本。Windows 证据来自 GitHub `windows-2025` runner，Windows 11 人工清单的 18 项仍全部待验。未连接真实业务系统。

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
6. 使用不同的 SummaryPreparedDraft 验证离开前准备写入；再编辑为另一份 SummaryStoppedDraft，清除控件、停用并重新启用两个样例，检查终止 Stop 写入最新草稿及实际文件/行重载。导航、主动保存和重复生成不覆盖的原有断言继续保留；偏好自动保存必须在准备之前读取磁盘确认。
7. 停止插件、清除注册，检查保存的 enabled 偏好仍为 true，写入 JSON 并关闭窗口。

报告包含 `succeeded`、`stage`、`userRoot`、启动阶段/线程/时间、检查列表、文件路径/大小和完成时间；失败仅包含安全错误代码/类别与 HRESULT，不读取异常 Message/ToString。当前从 SDK 生成入口进入 Application 构造开始，记录编译后的 Application 资源初始化、窗口和控件阶段；更早的失败由启动器报告和进程诊断定位。进程入口前的模块初始化崩溃可能无法由 EXE 写报告，脚本会补充失败 JSON；即使原生进程已写成功报告，后续退出失败或超时也会把最终 succeeded 改为 false，并保留已有检查与原生阶段。报告不含真实用户设置或业务数据。临时根目录保留用于故障检查，可在进程退出后按报告路径自行删除。超时是失败；脚本为限制测试运行时间而终止测试进程，不表示普通停用可强制取消插件。

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

## host 0.2 管理与实际进程重启探针（Server 2025 自动验证通过）

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

## Task 4 独立钉钉与 UPCAM 生产包验收（已实现的六进程范围通过）

此探针运行当前 `artifacts/native/app/Workbench.App.exe`，使用当前 `HostRuntime`、真实 Store 和原生管理界面。它不运行上游 source-linked 旧宿主，不把两个平台样例当作业务插件，也不静态引用业务程序集。固定输入和来源记录见 [integration inputs](../tests/integration/README.md)。

```powershell
pwsh ./scripts/build.ps1
pwsh ./scripts/import-business-plugin.ps1
pwsh ./scripts/business-plugin-smoke.ps1 -TimeoutSeconds 300
```

仅显式 `--business-probe <phase> --probe-root <root> --smoke-report <report>` 开启此模式；root 必须是 `%TEMP%/ProjectWorkbench-business-probe/<GUID>` 的直接子目录，并排除 bundled 发现根。普通启动不读取或安装这些输入。六个阶段共享同一隔离根，每个阶段实际退出后才启动下一个。

| 阶段 | 必需业务断言 |
| --- | --- |
| seed | 真正安装两个独立 ZIP；逐文件匹配已固定上游字节、禁用/未信任/未加载；实际信任弹窗取消及确认。钉钉 0.3.2 实际 host-DPAPI 合成模型值的取消/完整批次保存、单独批准并读取 host commit/revision、立即撤销、删除及无授权再保存；Webhook 字段留空，模型 key 值读取及模型/来源/发送调度未执行。随后实际设置、加密草稿、demo Core 读取/搜索/整理/历史，单个真实 MonitorSupervisor 跨页面继续；RequiresPluginStart 通用 Start/Resume 拒绝、原生 Stop/host Stop/停用完整退役。UPCAM 实际生产无窗口 worker/Core fictional preview、原生页面/设置/默认 profile 绑定、缺少自动授权的明确开始拒绝与真实 registered callback Faulted、安全 Stop/停用/worker 退役 |
| restart | 新 OS 进程恢复可信插件登记；钉钉任务仍停止、无业务连接/调度，必须新预览；UPCAM 恢复前无 worker/任务；实际加密草稿/非敏感偏好/profile 绑定保留；暂存两个只改 manifest 版本的明确合成升级，当前 DLL 不变，发现后 Apply 拒绝 |
| update | 启动发现前 Apply 到合成版本，撤销信任/启用/恢复；原厂 DLL 逐文件不变、当前业务数据及实际备份保留；重新信任后真实页面可用。再暂存更高合成版本，仅损坏未加载的 staged manifest，供下一进程测试失败 |
| fault | 真实启动 Apply 因 staged 指纹损坏失败，旧当前代码/数据不变；保留 Prepared journal；取消后按实际 HistoryId 暂存原版代码回滚 |
| rollback | 原版完整字节恢复且需新信任；不恢复旧业务数据；真实页面/worker sample 仍可用，暂存保留数据的移除 |
| remove | 启动前移除两个代码目录；业务程序集未加载，保留当前 data/settings/已有 secrets 和六份代码历史；再次验证当前文件 SHA256 |

业务探针的 `artifacts/native/business-probes/<phase>.json` 和 `summary.json` 由 CI artifact `independent-business-plugin-probes` 收集，包含实际 PID、**实际 OS exitCode**、来源提交/ZIP SHA256、合成更新 ZIP SHA256、当前文件/备份/停止状态、已观察 worker 的实际 PID/exitCode、实际 WinUI Closed 事件和启动器退出后重新获取租约的证据。缺失报告、非零退出、PID/来源不匹配或超时均失败；已有原生成功报告也不能覆盖进程失败。超时保留已知进程所有权并停止后续阶段，不强杀来证明正常停止。

UPCAM backend 的 Ready 状态与原生控件就绪分别核对：探针解析当前 `ContentControl.Content` 的 TabView，明确选择 Profile/Run 页，并等待当前所选内容加载、测量、附着到同一 XamlRoot 且与可视树一致；连续观察必须属于同一当前 host/content，替换后重新解析。期限保持不变。失败诊断仅含有界的控件类型、AutomationID、加载/布局信息；`<phase>.evidence.json` 另保留失败前已完成检查/观察和已见 worker PID，不替代主报告或把失败变成成功，不输出 Text/Password/配置值。

本轮 Linux 只能执行固定包审计/真实 Store 安装、报告与隔离参数测试及不可运行的 C# reference 编译；Windows 原生执行必须以当前提交的实际 CI 结果为准。原有样例 13 项加管理导航、独立平台 fixture 的 runtime probes 均保留，业务结果单独记录。

覆盖边界：钉钉 0.3.2 的四项 host secret definitions 与异步完整批次提供方已接入；本轮原生测试仅配置合成模型值及 `credentials.commit`，实际通过取消、保存、模型用途单独批准并读取 commit/revision、撤销保留密文、删除、无授权再保存和空编辑框检查。批准仅读 commit/revision；真正 provider dispatch 前的模型 key 值读取未执行。Webhook 两字段留空，发送用途未批准；运行 demo Core 前已撤销/删除模型批准。UPCAM 生产路径仍没有自动业务授权；其 host secret 只用于生成的恢复绑定 key，业务凭据由原 worker DPAPI 管理。正向闭环使用独立上游 `48e67e8`/CI 42299664328/artifact 13544710526 的编译模拟变体，publisher ZIP SHA256 `5d3fa64b1389acad36e425be9b274291315b4f7ed373df2b533ef99cf72e9f9c`，与生产插件分开核验。没有真实 DWS/Teambition/OpenAI/Webhook/HTTP、企业凭据或业务消息。钉钉 0.3.3/0.3.4、UPCAM 0.1.1/0.1.2 及 staged 故障只是明确合成的版本/损坏集成输入，未宣称上游新业务发布；UserRoot 备份不含外部独立版规范状态或 DWS profiles，不证明任意 schema 降级或 live 去重。

## 独立 UPCAM 模拟验收（Server 2025 实际六进程通过）

运行器在六个生产进程及其 worker 全部退出后，使用另一个全新 GUID 用户根，依次启动以下真实 shipping App 进程。独立编译 ID 为 `com.upcam.task-digest.acceptance`；原生 CI-synthetic banner、Ready 握手和 profile 标记先于服务动作。与生产插件共享上游 worker 单例，禁止重叠。只调用上游编译变体的本地 fake providers，synthetic sent 不代表真实钉钉送达。

| 阶段 | 已执行断言 |
| --- | --- |
| acceptance-seed | 原生确认合成范围/本地模型缓存并保存授权；实际未来到期的 15 秒定时器运行 Core，历史出现一条 fake sent 和虚构摘要；原生 Stop 后观察 16 秒，revision/规范数据库哈希不变，同一空闲 worker 保留；重新明确 Start 并单独开启 Resume |
| acceptance-restore | 新进程未打开插件页面即恢复 Running；原 DPAPI key 密文/绑定不变、只有一个 worker；实际定时检查继续且不重放已记录时段；host Stop 后再观察 16 秒静默 |
| acceptance-stopped | 真实重启保持 Stopped，无 worker/backend；即使 Resume 偏好保留也不恢复运行意图；随后明确新 Start 准备下一阶段 |
| acceptance-config | 启动前只改变本测试根的合成计划；Restore Faulted、16 秒无 Core 进展、不改旧 key/绑定、不补造授权；明确原生 Save 才重新授权 |
| acceptance-missing | 启动前只删本插件生成的 key；Restore 拒绝、不造 key、不改绑定/数据；明确新 Start 后活动 Disable 等待任务/worker 结束并清 key/绑定/运行意图；重新启用为 Stopped、独立 Resume 偏好仍在，且无 key/绑定/worker |
| acceptance-disabled | 真实重启不加载该插件代码/任务/worker，不恢复授权；合成历史、数据与设置哈希保留 |

每阶段报告实际 PID、Closed、OS exit0、退出后租约重获、来源和哈希。固定 worker 最终由其自身 `TerminateJobObject(job, 1)` 策略退役，所有已观察 worker 均结束、实际 exit1；没有把它记为优雅 exit0，也没有声称宿主能强制终止任意插件工作。注册任务/生命周期回调/插件 Stop 之外的资源仍由可信插件负责完整收束。

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
