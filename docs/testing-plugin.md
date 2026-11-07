# 测试设计插件 0.1.0

`com.projectworkbench.testing` 是独立安装的受信任进程内插件，最低宿主 0.4.0、API 1、Testing services 1。它在工作台同一个 WinUI 窗口提供中文“测试设计”页面，不嵌入独立应用、XAML 或 WebView。ALC 和能力清单不是安全沙箱。

Windows 构建 `pwsh ./scripts/build.ps1` 后，完整宿主位于 `artifacts/native/app`，测试交付单独位于 `artifacts/native/testing-plugin`。CI 对应下载名为 `workbench-win-x64`、`workbench-sdk-0.4.0` 和 `workbench-testing-plugin-0.1.0`；选择同一运行/源码提交的产物。在“插件”导入测试交付中的 `workbench-testing-plugin-0.1.0.zip`，审查指纹并确认信任启用。安装不会自动信任或运行。更新需要安全退出并重启。

测试 ZIP 精确包含 `plugin.json`、`Workbench.Testing.dll`、`Workbench.Testing.Domain.dll`、`Workbench.Testing.deps.json` 和 `Fixtures/requirements.synthetic.json`、`Fixtures/figma.synthetic.json`。不带 SDK/Core/宿主/WinUI/WinRT/.NET 运行时副本。外层交付另有 `provenance.json`、`SHA256SUMS`、`README.md`，绑定实际 ZIP、每项文件长度/哈希和源码提交/文件哈希。它不加入原有六文件业务 companion；旧 DingTalk/UPCAM 固定字节和来源保持不变。

页面支持：

- “导入需求 JSON”/“导入 Figma 快照 JSON”：原生文件选择器打开本地只读流，最多读取 1 MiB 加一个超限探测字节。Figma 格式是本地规范化 JSON，不是 `.fig` 或任意 API 返回。格式见 [测试服务](testing-services.md)。
- “打包需求示例（含故意失败）”/“打包 Figma 示例”：通过相同有界文件读取/解析路径打开实际随包 JSON；缺失或损坏时报错，不替换成嵌入的备用数据。所有示例明确为合成数据。
- 选择来源后生成用例（未选则使用全部）；选中用例集/用例/步骤编辑完整文本。最多 200 用例，每用例 20 步。输入不以 MaxLength 静默裁剪，超限草稿保留供修正。元数据编辑保留映射；修改步骤说明或预期后该步骤变手动。
- 编辑即捕获并按版本合并自动保存；“测试自动保存状态”显示输入/保存版本及安全错误码。没有普通“保存”复选框。失败阻止导出、运行和离开；同一模型/控件保留最后输入。“重试草稿写入”仅在失败时显示，RevisionConflict 才提供“保留草稿为新集并重新加载”。新集成功保存后才重新加载原记录。
- “导出 CSV…”用实际宿主窗口绑定的保存选择器选择目标，通过临时文件和替换写入。命令“导出当前用例 CSV 到插件本地目录”使用相同导出器/写入器，输出 `data/com.projectworkbench.testing/testing-cases.csv`。UTF8 BOM、CRLF、全部字段引号；所有文本单元格前加一个撇号，仅经验证的序号保留数字。CSV 单元格值有意改变，项目存储不变；不保证任意表格软件重存后的安全性。
- “运行选中用例（虚拟）”一次点击完成排队/显式启动；Cancel 独立可用，不排在 Run 的通用忙锁后。仅允许 `virtual.task-form.v1` 的 typed SetText/Click/AssertText。手动/未映射步骤不自动通过。示例分别演示正向、空标题拒绝和标注的故意断言失败。
- 选择已提交执行及事件，查看实际 journal 状态、步骤结果、错误、冻结版本、head/session/父执行和完整 NativePlainTextState。旁边 Image 解码真实持久化证据：320×180、24bpp、未压缩自下向上 BMP，172854 字节，始终标示 **SIMULATED**；它不是桌面截图。有限字形可能显示 `?`，原始 Unicode 观察文本另行完整显示。
- “取消并等待结束”区分 CancelRequested 与实际结束的 Cancelled，并等待实际工作/清理；不承诺撤销或 exactly once。“从第一步重试（新执行）”使用父执行冻结用例创建新的关联 run/session，不按断点继续。

每个 entry 只注册一个 before-leave hook。所有实际 UI、命令、picker、读取、导出、解码及 dispatcher 回调在同步前缀前登记实际所有权。离开先临时封住新用户动作、等待已接受回调，再在服务仍开放时取消/刷新控制器，最后等待准备产生的实际通知/dispatcher 工作。其他参与者准备失败时同一页保持可修正。Unloaded 退订页面/更新代际；不会停止项目控制器或丢弃已接受 picker。Stop 永久封住入口并加入实际工作；context Close 后不发起新保存。原生 picker 的原始异步操作一直持有到真实结束，不把取消 await 包装当作 picker 已退出。

记录位于当前项目 `.workbench-project/testing`，包含捕获的 projectId、严格 schema1、托管 GUID ID 与哈希。损坏/未知字段/孤儿数据保留并拒绝，不清空重建；没有任意恢复按钮、自动删旧数据或把显示名/定位符拼成存储路径。来源、用例、执行与证据的具体边界见 [测试服务](testing-services.md)。

实际有用命令 ID：`import-example-requirements`、`import-example-figma`、`generate-all`、`export-local-csv`、`queue-mapped`、`start-queued`、`cancel-run`、`retry-selected`、`refresh`。现有无参数 PluginCommand ABI 未改动。普通页面 Run 仍为一次点击；排队/启动命令供明确本地工作流使用。

源码验证与 Windows 验收分开记录。Linux 可执行：

```sh
dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj -c Release --nologo
dotnet build tests/Workbench.NativeReference/Workbench.NativeReference.csproj -c Release --nologo -p:UseWinUI=false -p:AppxGeneratePriEnabled=false -p:WindowsAppSDKSelfContained=false
pwsh ./scripts/package-testing-plugin.ps1 -ReferenceOnly
pwsh ./tests/scripts/TestingPluginPackage.Tests.ps1 -Directory ./artifacts/native/testing-plugin -AllowReferenceOnly
dotnet run --project tests/Workbench.Testing.PackageProbe -c Release -- artifacts/native/testing-plugin/workbench-testing-plugin-0.1.0.zip
pwsh ./tests/scripts/TestingPluginProbe.Tests.ps1
pwsh ./scripts/export-sdk.ps1 -ReferenceOnly
pwsh ./tests/scripts/SdkSnapshot.Tests.ps1 -SnapshotRoot ./artifacts/sdk-reference-snapshot
```

`-ReferenceOnly` 明确记录不生成/验证 Windows PRI 与实际运行。交付输出不覆盖旧目录；重做时使用新的 `-OutputDirectory`。Windows 构建完成后执行 `pwsh ./scripts/testing-plugin-probe.ps1 -TimeoutSeconds 240`，只打开 `%TEMP%/ProjectWorkbench-testing-probe/<GUID>` 的专用合成根。真实 shipping EXE 两进程 seed/verify 使用实际安装 ZIP、注册页面/命令和公开 Core 存储，不静态引用测试 entry/domain。

Seed 验证打包 JSON/CSV 实际 IO、最后原生输入、映射失效、真实文件共享阻塞导致保存/普通 Close 失败且同一控件可修正、冲突副本与原字节、虚拟 pass/negative/labeled failure、真实 BMP Image 解码、实际 store IO 等待中的取消和原生项目隔离。取消探针明确是在第一步前等待真实存储锁，并非中断真实桌面动作。Seed 在最后一次 context 切换后用公开存储生成 **synthetic inflight journal seed**：只有 RunStarted/StepStarted，没有设备动作或结果。它不是 OS 崩溃。另一个实际 EXE 的新 context 初始化将其 Interrupted；正常 Retry 用冻结用例从第一步完成新 session。Verify 还重开实际 journal/receipt/BMP，比较精确观察文本和字节。最终输入在真实 TextBox 接受后立即进入普通离开/Close，不等自动保存。

每阶段必须满足实际 WinUI Closed、宿主 OS exit 0 和目录锁重新取得，PID 独立于检查数量。报告记录 Server OS/架构、ZIP 哈希、实际 PID、合成 seed 标志和原始 event/receipt/CSV/BMP 文件哈希。原生报告和一次性 runner sidecar 分开保留；失败停止后续阶段且不会覆盖已有证据。输出为 `artifacts/native/testing-probes/<UTC-GUID>`，CI 名 `testing-plugin-probes`。Seed 的文件副本在最后立即离开输入之前采集；最终保存由 verify 重开的实际记录证明。

当前源阶段不能宣称新的实际 Windows CI 或下载字节审计已通过。由本轮准确 clean HEAD 的 Windows 报告确认新增门禁，同时保留旧 native14、management17/theme1、restart7、workspace16+3 和 business27+24/12进程门禁。Windows Server 自动化不代替 Windows 11 人工 picker/IME/DPI/屏幕阅读器/真实设备验收。远程 Figma 已实现私有、有界的只读 REST 领域后端（见[后端契约](figma-readonly-capture.md)），尚无原生页面入口，也未执行真实服务调用；AI/模型、业务 provider、Webhook/消息和真实电脑控制仍未接入或执行，没有未来供应商/设备占位页面。
