# 测试设计插件 0.2.0（开发中）

2026-11-09 已核对的功能源码为 `b1a3e9c9963c2d75798a6068f99a6270d81bd074`，见 [PUBLIC Actions42559172825](https://github.com/ThaoKeeton/project-workbench/actions/runs/42559172825)。Windows Core1561项通过、0跳过；Linux1558项通过、4项Windows专用检查明确跳过。独立安装的 SourceAcceptance 原生来源验收36项、4个不同实际进程及正常关闭/退出/租约链通过；13份实际来源、回执及人工用例记录已逐字节核对并重放严格验证。它使用合成凭据和内存HTTP，未请求真实Figma/OpenAI。

当前[生产测试插件下载](https://github.com/ThaoKeeton/project-workbench/actions/runs/42559172825/artifacts/13649496784)的内层ZIP SHA256为 `c12a7945629c558138e3ee46642edaa37254e771571888f9850c0082de53f103`；[原生证据](https://github.com/ThaoKeeton/project-workbench/actions/runs/42559172825/artifacts/13649680974)内 `artifacts/native/source-acceptance-fixture/workbench-testing-source-acceptance-0.2.0.zip` SHA256为 `eaad414132ccee5eb83d76c6077d27048746801be005d7caa4014c57a7ccf702`。实际8/10文件、同轮7项共同字节、PE/依赖/Production helper/宿主配套及245份Git源码记录已核对。SourceAcceptance是独立合成验收ID，不是供真实项目使用的业务包。下载按一天保留，当前链接到2026-11-10约04:21UTC到期，实际状态以Actions为准。

本批次还通过既有Testing本地工作流、业务51项/12个进程和owned27项；完整owned面板逐项点跑、真实账户/任意电脑控制和Windows11人工验收仍不在已完成范围。宿主/SDK0.4.0、API/服务1、程序集1.0.0.0和数据schema1保持不变。`workbench-testing`拆仓目前仅准备设计及SDK输入，尚未搬移业务源码；OpenAI订阅代理也尚未实现。数字与二进制来源均指功能提交b1a3e9c，后续文档提交不代表重新构建的程序。

`com.projectworkbench.testing` 是独立安装的受信任进程内插件，最低宿主 0.4.0、API 1、Testing services 1。它在工作台同一个 WinUI 窗口提供中文“测试设计”页面，不嵌入独立应用、XAML 或 WebView。ALC 和能力清单不是安全沙箱。

能力清单在现有四项提示上增加 `remote-figma-read` 与 `openai-case-authoring`，用于说明明确发起的远程动作；它们不是受强制执行的权限边界。开发包已升至 0.2.0，增加私有 Windows 驱动和自带合成目标 helper；最低宿主 0.4.0、SDK/API 1、程序集版本 1.0.0.0 不变。同窗口页面、生产控制器及原生来源/生命周期检查的已通过范围见本页首段；完整owned面板点跑和用户Windows11人工验收仍待进行，当前是开发目录/插件交付，没有创建签名release或安装器。

Windows 构建需要 .NET 10 SDK、PowerShell 7、MSVC x64 C++ 工具和 Windows SDK，见[构建说明](../README.md)。默认执行 `pwsh ./scripts/build.ps1` 仍只生成生产测试交付；显式 `-IncludeSourceAcceptance` 另启用成对源码夹具交付。两种模式均在任何构建前拒绝非空 `artifacts/native` 和所选 package-build 根，不再删除旧产物或证据。完整宿主位于 `artifacts/native/app`，测试交付单独位于 `artifacts/native/testing-plugin`。CI 配置的下载名为 `workbench-win-x64`、`workbench-sdk-0.4.0` 和 `workbench-testing-plugin-0.2.0`；实际有可用产物时，选择同一运行/源码提交的产物。在“插件”导入测试交付中的 `workbench-testing-plugin-0.2.0.zip`，审查指纹并确认信任启用。安装不会自动信任或运行。更新需要安全退出并重启。

CI 在 SDK 快照验证后、任何原生产物上传前，复核测试交付与业务交付；上传配额不足仍会使工作流失败，并可能没有可下载产物。Server 自动检查不替代 Windows 11 人工验收或实际下载字节审计。

测试 ZIP 精确包含八个文件：`plugin.json`、`Workbench.Testing.dll`、`Workbench.Testing.Domain.dll`、`Workbench.Testing.Windows.dll`、`Workbench.Testing.deps.json`、`Workbench.OwnedLab.exe`、`Fixtures/requirements.synthetic.json` 和 `Fixtures/figma.synthetic.json`。不带 SDK/Core/宿主/WinUI/WinRT/.NET 运行时副本。外层交付另有 `provenance.json`、`SHA256SUMS`、`README.md`，绑定实际 ZIP、每项文件长度/哈希和源码提交/文件哈希，并核验 Production helper、实际工具链和静态 CRT 来源。它不加入原有六文件业务 companion；旧 DingTalk/UPCAM 固定字节和来源保持不变。ReferenceOnly 描述符明确不可用，不能生成生产 ZIP。

页面支持：

- “导入需求 JSON”/“导入 Figma 快照 JSON”：原生文件选择器打开本地只读流，最多读取 1 MiB 加一个超限探测字节。Figma 格式是本地规范化 JSON，不是 `.fig` 或任意 API 返回。格式见 [测试服务](testing-services.md)。
- “打包需求示例（含故意失败）”/“打包 Figma 示例”：通过相同有界文件读取/解析路径打开实际随包 JSON；缺失或损坏时报错，不替换成嵌入的备用数据。所有示例明确为合成数据。
- “读取 Figma 并导入”：填写文件 key、可选版本及 1–16 个节点，每个节点每行一个手工验收条件（忽略空行，保留条件中的空格；每节点 1–16 条、总计最多 200 条）。增加/移除行保留其他输入。只读请求需 `file_content:read` 和实际文件访问权限，详见[领域契约](figma-readonly-capture.md)。通过宿主项目凭据页管理 `figma-token`（Figma 访问令牌）和 `openai-api-key`（OpenAI API 密钥）；初始化仅注册元数据。凭据不写入表单、设置、日志或命令；不支持受保护存储时禁用远程动作。
- “发送所选来源到 OpenAI 并生成”：必须在上方实际选中 1–8 个来源，最多 32 个验收条件；不会采用未选则全选的本地规则。模型固定 `gpt-5.4-mini-2026-03-17`，明确发送来源身份/版本和标题、描述、验收条件，可能产生费用。结果必须人工审核，每一步 Action 均为空，不启动自动化。先保存精确输入/响应与原始 trace 的回执，再独立创建用例集；仅回执确认时显示回执 ID、状态和安全错误，不显示创建成功，不自动重试或删除。显式刷新可检查不确定提交的实际记录。
- Figma 未提交表单、来源选择和远程状态仅保留在当前项目会话，同项目页面卸载/重开及失败后仍可修正；切换项目或退出后不恢复未提交表单。成功确认的来源、回执和用例写入项目存储。没有普通表单保存按钮；初始化、输入修改、导航、刷新和本地生成不触发远程请求。
- 选择来源后生成用例（未选则使用全部）；选中用例集/用例/步骤编辑完整文本。最多 200 用例，每用例 20 步。输入不以 MaxLength 静默裁剪，超限草稿保留供修正。元数据编辑保留映射；修改步骤说明或预期后该步骤变手动。
- 编辑即捕获并按版本合并自动保存；“测试自动保存状态”显示输入/保存版本及安全错误码。没有普通“保存”复选框。失败阻止导出、运行和离开；同一模型/控件保留最后输入。“重试草稿写入”仅在失败时显示，RevisionConflict 才提供“保留草稿为新集并重新加载”。新集成功保存后才重新加载原记录。
- “导出 CSV…”用实际宿主窗口绑定的保存选择器选择目标，通过临时文件和替换写入。命令“导出当前用例 CSV 到插件本地目录”使用相同导出器/写入器，输出 `data/com.projectworkbench.testing/testing-cases.csv`。UTF8 BOM、CRLF、全部字段引号；所有文本单元格前加一个撇号，仅经验证的序号保留数字。CSV 单元格值有意改变，项目存储不变；不保证任意表格软件重存后的安全性。
- “运行选中用例（虚拟）”一次点击完成排队/显式启动；Cancel 独立可用，不排在 Run 的通用忙锁后。仅允许 `virtual.task-form.v1` 的 typed SetText/Click/AssertText。手动/未映射步骤不自动通过。示例分别演示正向、空标题拒绝和标注的故意断言失败。
- 选择已提交执行及事件，查看实际 journal 状态、步骤结果、错误、冻结版本、head/session/父执行和完整 NativePlainTextState。旁边 Image 解码真实持久化证据：320×180、24bpp、未压缩自下向上 BMP，172854 字节，始终标示 **SIMULATED**；它不是桌面截图。有限字形可能显示 `?`，原始 Unicode 观察文本另行完整显示。
- “取消并等待结束”区分 CancelRequested 与实际结束的 Cancelled，并等待实际工作/清理；不承诺撤销或 exactly once。“从第一步重试（新执行）”使用父执行冻结用例创建新的关联 run/session，不按断点继续。
- 下方“受控 Windows 试验 · 仅操作自带的合成任务窗口”提供三个固定场景的 Run/Cancel/Retry、独立私有历史、事件、原始观察文本和持久 BMP。它调用真实 OwnedPilotController 与固定生产 Win32 工厂；窗口帧明确属于自带合成目标，上方 virtual 历史/帧仍为 SIMULATED。等待上一轮虚拟终态资源释放时，已接纳的 owned 调用已有可取消身份，尚未伪造 Queued；此时 Cancel/历史可用，取消后仍加入原退休工作且不会再启动 owned。重试使用当前驱动与父输入从第 1 步新建执行；RecoveryNeeded 保留原始数据并拒绝新增执行。固定场景不刷新来源草稿或映射 AI 步骤。Linux 只证明源码/可移植组合，未实际操作这些 WinUI 控件。
- “取消远程操作”使用异步取消回调，不阻塞 UI dispatcher；仍等待忽略取消的真实 IO、回调和 UI 后续工作。忙状态持续到实际结束。离开项目和 Stop 在等待已接收动作前先请求远程取消；同项目的页面 Unloaded 不取消远程动作。

每个 entry 只注册一个 before-leave hook。所有实际 UI、命令、picker、读取、导出、解码及 dispatcher 回调在同步前缀前登记实际所有权。离开先临时封住新用户动作、请求远程取消并等待真实取消回调和已接受动作，再在服务仍开放时取消/刷新控制器，最后等待准备产生的实际通知/dispatcher 工作。取消回调失败也执行原有草稿刷新与实际工作屏障。其他参与者准备失败时同一页保持可修正。Unloaded 退订页面/更新代际；不会停止项目控制器、取消远程动作或丢弃已接受 picker。Stop 永久封住入口并先请求远程取消，再加入实际工作；context Close 后不发起新保存。原生 picker 的原始异步操作一直持有到真实结束，不把取消 await 包装当作 picker 已退出。

虚拟/来源记录位于当前项目 `.workbench-project/testing`，包含捕获的 projectId、严格 schema1、托管 GUID ID 与哈希。损坏/未知字段/孤儿数据保留并拒绝，不清空重建；没有任意恢复按钮、自动删旧数据或把显示名/定位符拼成存储路径。来源、用例、执行与证据的具体边界见 [测试服务](testing-services.md)。受控执行另存于捕获的插件 DataDirectory 下 `owned-lab-v1`，不写入 Core 虚拟证据。实际插件和可移植测试共用固定生命周期组合：永久关闭页面生产者、退订及实际解码完成后才由 pilot.Stop 释放私有日志锁；同项目页面卸载仅分离 UI。临时离开仅允许当前精确租约投影重开，不跳过嵌套/永久/实际活动屏障；页面和命令均保留活动释放后的最新渲染。

实际有用命令 ID：`import-example-requirements`、`import-example-figma`、`generate-all`、`export-local-csv`、`queue-mapped`、`start-queued`、`cancel-run`、`retry-selected`、`refresh`。现有无参数 PluginCommand ABI 未改动。普通页面 Run 仍为一次点击；排队/启动命令供明确本地工作流使用。

独立源码夹具 `com.projectworkbench.testing.source-acceptance`（0.2.0，最低宿主 0.4.0）通过内部组合入口委托同一个生产 TestingPlugin、页面、控制器与生命周期。它只有终端内存 HTTP，使用固定合成值、明确的单次 arm/release 与有界 schema1 只读计数；计数仅代表夹具处理器进入/返回，不是 provider 送达或存储提交。被选 Figma 来源与 AI 回执由真实解析器和 Core 文件存储产生，AI 步骤仍为手动。生产默认入口继续使用真实客户端；初始化、编辑、刷新和导航不会自动请求。

夹具私有的 retained explicit release v1 在初始化时仅以 `JsonElement` 读取一次设置 `source-acceptance-release-v1`，默认空字符串。空字符串不创建伴随文件或 monitor；非字符串、null 或非小写十六进制32位 runId 均以固定初始化错误拒绝。启用时捕获的 DataDirectory 必须精确为 `%TEMP%/ProjectWorkbench-source-acceptance-probe/<runId>/data/com.projectworkbench.testing.source-acceptance`，或该根下 `.workspaces/projects/<projectId>/data/com.projectworkbench.testing.source-acceptance`；projectId 为 default 或小写十六进制32位身份。拒绝其他根、路径层级和链接/reparse point；不接受调用者路径、provider 输入或端点。这是同用户受信夹具的存储约定，不是任意受信代码或文件系统竞态的安全边界。

仅实际校验并接纳 Hold 请求时，夹具在与 Stop 相同的 ledger 封口锁内绑定精确 ticket 和新 releaseId，并先保存 monitor completion；文件 IO、轮询和回调均在锁外。描述文件固定为 `source-acceptance-release-v1.held.json`，请求文件固定为 `source-acceptance-release-v1.release.json`；同目录原子发布使用分别对应的 `.held.tmp` / `.release.tmp` 固定 staging 文件，写入 flush 并关闭后再替换。夹具拒绝遗留 held staging 目标，保留其字节并报告发布失败。描述字段精确为 `protocolVersion=1`、`fixtureId`、`pluginId`、`runId`、`projectId`、`instanceId`、正 Int64 `generation`、`provider=figma|ai`、`scenario=hold`、`releaseId`；两个 fixture 身份均为夹具 ID，instanceId/releaseId 均为小写十六进制32位。请求须原样包含这些字段，另加 `action=release`。严格 UTF-8、2048字节和深度8上限，拒绝缺少/未知/重复字段、错误类型和尾随 JSON；不以文件存在代替身份匹配。匹配重复释放幂等，旧代际/实例/nonce 请求不能重新指向新 ticket。

每个已接纳 Hold 的 monitor 每25ms只观察固定请求文件和精确 release task，不依赖 SDK lifetime 或调用者等待 token，也不注册 SDK 后台任务。取消、超时、Stop、Dispose 和 IO/观察失败都不会解除真实 IO；开放 context 的原有显式 SDK release 仍能解除并加入 monitor，关闭后的旧 SDK 委托继续拒绝。Stop 先封口并保留唯一真实 inner Stop，再依次加入 inner Stop、所有已登记 monitor，最后分别尝试两个 handler Dispose；保留顺序中的首个失败，全部实际工作完成后才应用首次调用者取消。monitor 不等待外层 Stop。描述发布或观察失败仍持有 completion，开放 context 需要显式 cleanup；关闭后无法传递的信号仍是待处理失败，不能伪称清理成功。十个 SDK 夹具命令、schema1/8192字节状态和共享32次接纳上限保持不变；状态不新增原生 handler 释放回执。

可移植测试运行实际终端 HTTP、Core 存储和生产生命周期组合，分别固定真实 IO、通知尾部、monitor-start/Stop 屏障和两个 handler 的内部释放顺序。测试时钟只推进观察轮询，不能释放 held IO。实际夹具 entry 另经四属性 ReferenceOnly 编译；这些检查不证明已安装 Windows 文件消费、WinUI/DPAPI、manager Stop/Closed/OS退出或租约回收，仍需独立原生验收。

现已加入封闭的 `Production|SourceAcceptance` 包配置，默认仍为 Production。显式 Windows 成对模式从同一次完整传递 Rebuild 导出八文件生产包和十文件夹具包；夹具仅替换 `plugin.json`，另加 `Workbench.Testing.SourceAcceptance.dll` 和 `.deps.json`。另外七项与显式配对的生产 ZIP 逐字节一致。夹具输出为 `artifacts/native/source-acceptance-fixture/workbench-testing-source-acceptance-0.2.0.zip`，外层仍精确四个文件，始终不进入普通 `app/Plugins`。`New-TestingPluginDelivery` / `Test-TestingPluginDelivery -Profile SourceAcceptance` 必须显式传入同轮 `-ProductionDirectory`；Production 拒绝该参数。清单要求 API1、最低宿主0.4.0、固定 entry 和六项生产提示加唯一 `source-acceptance-fixture` 提示。本页首段的b1a3e9c批次已通过实际Windows成对打包/安装、已安装控件、合成值的真实Windows DPAPI以及原生Stop释放协议；Stop撤销SDK命令后，使用先前捕获的专用文件v1释放能力并加入原始工作，不再调用被撤销的release命令。Linux 测试用同一份夹具源码、真实 Core/supervisor 和明确的合成 protector，不是 WinUI/DPAPI/真实 provider 证明。凭据沿用 `runtime userRoot + pluginId` 命名空间；生产工作区协调器为不同项目选择独立 runtime root，协议另核对捕获的 projectId。同一 userRoot/同一 pluginId 仅改 projectId 元数据仍共用既有 secret store；受信进程内夹具不宣称更强隔离。

源码验证与 Windows 验收分开记录。Linux 可执行：

```sh
dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj -c Release --nologo
dotnet build tests/Workbench.NativeReference/Workbench.NativeReference.csproj -c Release --nologo -p:OwnedLabReferenceOnly=true -p:UseWinUI=false -p:AppxGeneratePriEnabled=false -p:WindowsAppSDKSelfContained=false
pwsh ./scripts/package-testing-plugin.ps1 -ReferenceOnly
pwsh ./tests/scripts/TestingPluginPackage.Tests.ps1 -HelperGuardsOnly
pwsh ./tests/scripts/TestingPluginPackage.Tests.ps1 -HelperGuardsOnly -Profile SourceAcceptance
pwsh ./tests/scripts/TestingPluginPackage.Tests.ps1 -StructuralGuardsOnly
pwsh ./tests/scripts/TestingPluginProbe.Tests.ps1
pwsh ./scripts/export-sdk.ps1 -ReferenceOnly
pwsh ./tests/scripts/SdkSnapshot.Tests.ps1 -SnapshotRoot ./artifacts/sdk-reference-snapshot
```

`package-testing-plugin.ps1 -ReferenceOnly` 只编译不可用驱动，不生成 ZIP 或新的交付目录；不能接着用旧目录假装验证了新包。SDK 的 ReferenceOnly 快照也不证明 Windows PRI 或运行。两个配置的交付输出及保留的构建根均不覆盖旧目录；单独重新打包时需保留旧根并使用新的干净 checkout/保留根，不要拿旧 DLL 续作新交付。`package-testing-plugin.ps1 -Profile SourceAcceptance` 使用保留的 `artifacts/testing-source-acceptance-package-build` 和可选 `-PairedProductionDirectory`（默认普通生产目录），只构建夹具图一次并从其共同输出导出两包；不能分开 Rebuild 后声称字节一致。ReferenceOnly 不生成任一交付。Windows 构建后才能执行实际包与原生检查：

```powershell
pwsh ./tests/scripts/TestingPluginPackage.Tests.ps1 -Directory ./artifacts/native/testing-plugin -RequireCurrentSource
dotnet run --project tests/Workbench.Testing.PackageProbe -c Release -- artifacts/native/testing-plugin/workbench-testing-plugin-0.2.0.zip
# 以下检查适用于在新 checkout 以 build.ps1 -IncludeSourceAcceptance 完成的成对构建；不能接着覆盖上次输出。
pwsh ./tests/scripts/TestingPluginPackage.Tests.ps1 -Directory ./artifacts/native/source-acceptance-fixture -Profile SourceAcceptance -ProductionDirectory ./artifacts/native/testing-plugin -RequireCurrentSource
dotnet run --project tests/Workbench.Testing.PackageProbe -c Release -- artifacts/native/source-acceptance-fixture/workbench-testing-source-acceptance-0.2.0.zip --profile SourceAcceptance --report artifacts/source-package-install-receipt.json
pwsh ./scripts/owned-lab-probe.ps1 -DeliveryDirectory ./artifacts/native/testing-plugin
pwsh ./scripts/testing-plugin-probe.ps1 -TimeoutSeconds 240
```

共享私有 ZIP reader 同时供完整交付验证和可移植结构测试使用；结构测试不绕过原生 helper / 当前宿主 / managed 验证，也不构成完整交付正向证明。清单/deps JSON 有2 MiB、深度32、严格 UTF-8、重复字段（不区分大小写）和尾随 JSON 拒绝；单项 payload 上限16 MiB。`TestingManagedPayloadAudit.cs` 只读取 PE/JSON，不加载程序集：核验私有名称、assembly1.0.0.0/file0.2.0.0/info0.2.0+commit、x64/Domain portable、真实方法体、非 reference assembly、公开具体 IWorkbenchPlugin entry 和公开无参构造，以及固定私有 deps 图、runtime DLL 路径和实际宿主共享/内嵌框架目录身份。外部第三方 deps 项按真实宿主图核对，不把所有第三方 runtime 资产复制进 ZIP。provenance 中的审计事实必须重算一致；新增 helper/test 源也进入源码哈希拒绝检查。

包存储探针保留原单参数生产调用，并支持显式 `--profile SourceAcceptance` 与可选 `--report <new-file>`。报告必须位于四文件交付外且不得覆盖，schema1/64 KiB 上限；stdout 也输出 JSON。它用真实 PluginPackageStore、新隔离根、精确安装文件/字节、disabled/untrusted/unloaded 发现和重复拒绝，并检查原始包在宿主0.3.0拒绝/0.4.0安装，以及错误API/高于宿主 minimum 的变体拒绝；保留 owned probe 根以便审计。安装检查本身不证明激活或WinUI运行；本批次另有同源码的四进程/36项原生来源结果，见首段。

独立 owned-lab 检查使用真实自带合成窗口，要求全部 27 项；它不证明插件内受控执行页面或通用电脑控制。现有虚拟执行探针只打开 `%TEMP%/ProjectWorkbench-testing-probe/<GUID>` 的专用合成根。真实 shipping EXE 两进程 seed/verify 使用实际安装 ZIP、注册页面/命令和公开 Core 存储，不静态引用测试 entry/domain。

其中 `GdiBlock` 是 AcceptanceFixture 专用的 **synthetic capture-stage hang**：在原生 broker 的 Capture 完成真实目标身份核验后阻塞，用于验证父进程实际加入进程、job0、IO、回调并释放租约。它不是已复现的 Win32 `PrintWindow` 死锁；Production 编译不带该宏，仍使用真实 `PrintWindow` 与像素校准。矩阵失败时，observations 根中的 `native-failure.json` 以 schema1、CreateNew 和最多 64 KiB 保留首个失败的安全枚举、状态、计数及实际退休观察；未知观察保持 null，不写异常文本、路径或 UI 内容，也不增加 27 项成功记录或替换原始失败。当前修改仍需对应源码的实际 Windows 全矩阵验收。

Seed 验证打包 JSON/CSV 实际 IO、最后原生输入、映射失效、真实文件共享阻塞导致保存/普通 Close 失败且同一控件可修正、冲突副本与原字节、虚拟 pass/negative/labeled failure、真实 BMP Image 解码、实际 store IO 等待中的取消和原生项目隔离。取消探针明确是在第一步前等待真实存储锁，并非中断真实桌面动作。Seed 在最后一次 context 切换后用公开存储生成 **synthetic inflight journal seed**：只有 RunStarted/StepStarted，没有设备动作或结果。它不是 OS 崩溃。另一个实际 EXE 的新 context 初始化将其 Interrupted；正常 Retry 用冻结用例从第一步完成新 session。Verify 还重开实际 journal/receipt/BMP，比较精确观察文本和字节。最终输入在真实 TextBox 接受后立即进入普通离开/Close，不等自动保存。

每阶段必须满足实际 WinUI Closed、宿主 OS exit 0 和目录锁重新取得，PID 独立于检查数量。报告记录 Server OS/架构、ZIP 哈希、实际 PID、合成 seed 标志和原始 event/receipt/CSV/BMP 文件哈希。原生报告和一次性 runner sidecar 分开保留；失败停止后续阶段且不会覆盖已有证据。输出为 `artifacts/native/testing-probes/<UTC-GUID>`，当前 CI 收进 `native-verification-evidence`；经独立进程出生/EXE/安装文件与 PNG 校验的窗口内容图像另在 `workbench-ui-gallery`。图中的 SIMULATED 虚拟 BMP 仍是模拟层，宿主 PNG 才是 WinUI 内容渲染；owned 面板空闲画面不证明实际受控执行。Seed 的文件副本在最后立即离开输入之前采集；最终保存由 verify 重开的实际记录证明。

截至诊断修复 `58f56ef`，其 [CI 42457380425](https://github.com/ThaoKeeton/project-workbench/actions/runs/42457380425) 未观察到运行器或任何执行步骤。后续读取到的两个 job 失败注释均明确指出：账户近期付款失败，或支出上限需要提高；具体是哪一种、金额及剩余额度尚未确认。这些是当时的历史阻塞；随后用户公开仓库并授权标准 public runner Actions，没有改动账单。协调器实际核验的后续源码运行及各项通过/失败范围见 [Windows 验证记录](windows-verification.md)，不得用旧阻塞或其他源码的结果替代本阶段 SourceAcceptance 验收。之前 `8692de5` 的 Windows 构建、核心 1238 项和八文件安装检查通过，但 owned-lab 只完成 9/27 项后异常退出，原因仍待定位；该轮没有上传产物。上述历史结果不补作当前版本的结果。完整门禁仍需准确源码对应的全新 Windows 运行，保留 host/runtime/workspace/virtual/business 51 项及 12 个 PID+出生实例、SDK 和两项最终交付复核。Windows Server 自动化不代替 Windows 11 人工 picker/IME/DPI/屏幕阅读器/真实设备验收。远程 Figma 与 OpenAI 控制器以合成凭据、内存 HTTP 和真实 Core 项目存储验证；原生远程控件的 Linux reference 编译不代表 Windows 实际操作。该历史阶段的native probe尚未单独操作远程控件；本页首段b1a3e9c已补齐独立合成来源原生验收。真实 Figma/OpenAI 账号、业务 provider、Webhook/消息和真实用户电脑均未操作。

实际源码控件通道现以独立 `--source-acceptance-probe seed|verify|stop-figma|stop-ai` 接入宿主正常工作区初始化。只接受 `%TEMP%/ProjectWorkbench-source-acceptance-probe/<小写 GUID-N>`；未知、大小写变体、缺失或重复的源码选项直接拒绝。Prepare 使用真实包存储安装十文件夹具；运行时通过公共 manager 设置显式 release-v1 runId、确认信任并启用普通 `testing` contribution。宿主不引用夹具、Testing 或 Domain 私有程序集，不增加 ALC sharing；控件以原有 AutomationProperties.Name 定位，真实 AutomationPeer 发起动作。

`pwsh ./scripts/source-acceptance-probe.ps1 -TimeoutSeconds 240` 在实际 Windows 构建后顺序保留四个进程句柄。seed/verify 共享一个新根，两个 Stop 使用各自新根。固定输入、合成 Figma/OpenAI 凭据仅通过真实 CredentialPage 保存，并检查 CurrentUser DPAPI 读回、密文不含明文、编辑框清空和项目隔离。被测终端始终是内存 HTTP；无真实 provider 请求、真实密钥、Webhook 或计算机控制。原生 HTTP 零进入计数不等于 DPAPI 零读取计数；宿主没有后者指标。

seed 的15项覆盖生产表单、精确 Figma 原始响应/哈希、只选择 Figma 的 AI 请求和精确回执、返回顺序2/0/1与原始追溯顺序0/1/2、手动步骤/禁用运行、503/畸形输入保留、真实 receipt-only partial、held 导航、两种取消和两种 held 项目切换。verify 的5项在另一个真实进程检查新实例/会话重置、持久字节/追溯/手动 UI、合成 DPAPI 与被动操作零发布。每种 Stop 的8项先由真实控件进入 hold 并保存全部身份绑定的 v1 描述，再实际 Disable/撤销 SDK、保留原 Stop/拒绝新实例；finally 显式写入保存的 release capability，并重试/加入同一个 manager Stop。取消、超时、Stop 或 Dispose 均不代发 release；不声称原生测量每个私有 handler 的 Dispose 时间或消费过错误文件。

源码 schema1 验证器与原生产 Testing/BMP/CSV 验证器分开。36项 phase-qualified 观察以外，必须另有原生实际 Closed、原生 PID/创建 FILETIME、独立 launcher 的 PID/创建/退出 FILETIME 与 exit0、所有实际 base/project host.lock 同时重获。FILETIME 全程是 Int64；PID 数值复用只有出生不同才允许。失败终止先记录为失败，只对保留的 CI Process 句柄操作，不能算正常退役。报告、source/receipt/case JSON 均检查类型、字段/重复项、路径/链接、大小和字节哈希；不导出 secret/ciphertext 文件。输出为 `artifacts/native/source-acceptance-probes/<UTC-GUID>/`；包安装收据为同父目录 `package-install.json`。夹具四文件目录和这些证据复用既有 native-verification-evidence，两条上传分支均包含，不增加 artifact。

这些入口已在本页首段b1a3e9c的公开Windows批次执行，四个实际进程/36项检查和实际下载审计通过。Windows Server 自动证明、实际下载包审计、Windows 11 人工主题/IME/DPI/可访问性/选择器，以及未来经授权的 live Figma/OpenAI 账户/权限/模型/费用证明，仍是不同门槛。Production 与 SourceAcceptance ReferenceOnly 分别使用保留的 `artifacts/testing-reference-build`、`artifacts/testing-source-acceptance-reference-build`；都在旧根非空时拒绝，均不输出 ZIP 或成对交付。
