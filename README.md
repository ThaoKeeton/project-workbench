# Project Workbench

Windows 原生插件工作台基础版：同一个 WinUI 3 窗口承载独立插件提供的原生页面、设置与异步命令。使用标准 Windows 控件，不嵌入其他应用窗口，不使用 WebView。

默认分发两个独立构建的本地示例：工作摘要（可编辑草稿）和测试用例（可编辑列表、CSV 导出），均使用预置数据。钉钉 0.3.2 和 UPCAM 0.1.0 已在历史固定运行中通过配套主程序的原生页面、模拟业务、生命周期及重启维护测试；另以独立 UPCAM 模拟验收插件验证实际定时 Core、停止与授权恢复。本轮开发版本的完整复验尚未通过，历史记录见[验证边界](docs/windows-verification.md)。业务包需按需安装，下载配套 `workbench-business-plugins` 后按 [业务插件安装说明](docs/install-business-plugins.md) 选择内层 ZIP 或原厂文件夹；[固定来源与测试边界](tests/integration/README.md) 另列。另有按需安装的 [测试设计插件](docs/testing-plugin.md)：本地需求/规范化 Figma JSON、可追溯用例、自动保存/CSV 和 SIMULATED 虚拟执行。原生页面新增明确发起的[有界远程 Figma 只读导入](docs/figma-readonly-capture.md)及所选来源的 OpenAI 用例创作；凭据使用宿主项目凭据存储，必须人工审核，生成步骤保持手动。验证使用合成凭据、内存 HTTP 与真实 Core 项目存储；新远程控件的 Windows 实际操作、真实服务和完整受控 Windows 自动化尚未验证。

## 在 Windows 构建和启动

主要开发与验收环境为受支持的 **Windows 11 24H2 或更新版本、x64**。安装 [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/windows)（仓库固定 `10.0.403`，允许更新补丁）和 PowerShell 7。还需安装可由 `vswhere` 发现的 MSVC x64 C++ 工具和 Windows SDK；`scripts/build-owned-lab.ps1` 使用真实 `cl`、`rc`、`link` 构建静态 CRT 原生 helper，NuGet/.NET SDK 不能代替这一步。若使用 IDE，需要 Visual Studio 2026 18.0+；CLI 可使用带上述组件的 Build Tools。首次还原需要 NuGet 网络访问。

```powershell
pwsh -File .\scripts\build.ps1
.\artifacts\native\app\Workbench.App.exe
```

脚本清理 `artifacts/native`，发布自包含的 .NET/Windows App SDK 应用目录，并分别编译、暂存两个分发示例及一个独立可安装测试 ZIP（不自动安装）。分发和解压时必须保留 **整个 `artifacts/native/app/` 目录**，不能只复制 EXE。固定 Windows App SDK `1.8.260921001`；不裁剪、不 AOT、不单文件发布。API 目标为 `net10.0-windows10.0.19041.0`，该 API 下限不表示 Windows 10 已获运行时支持或通过兼容性验收；Windows 10 企业版/LTSC 需另行验证。

首次打开“插件”，审查包详情，再点击“启用”并明确确认信任。启用后左侧出现插件页面及各自设置入口。“命令”可生成本地摘要草稿或导出演示 CSV。设置页支持跟随系统、浅色和深色主题。

host 0.2.0 增加“后台任务”“凭据”和“包维护”。后台页显示登记任务的运行/停止/故障状态，自动恢复默认关闭；标记 RequiresPluginStart 的任务必须到插件页面完成本次业务确认，通用启动/恢复不可用。凭据页只显示密文是否已保存，不验证或预填值；输入框保存后清空。关闭期间仍有插件工作未结束时，工作台保留进程和用户目录锁，管理页面可查询并重试关闭；宿主自己的写入仍在进行时也不会释放锁。

摘要编辑在切换页面时保留；“保存本地草稿”可主动写入，切换项目、停用或退出前也会保存非秘密草稿。示例偏好编辑后自动保存。生成命令只创建第一份草稿，发现现有文件或编辑过的草稿就明确拒绝覆盖。

host 0.3.0 首次直接打开原来的本地项目，不移动旧目录。只有一个可用项目时，只显示当前项目标题与内容；“文件 → 新建项目/打开项目”是主动发现入口。创建第二个项目后出现切换框，选择即在当前窗口切换。“打开项目”可归档其他项目或恢复入口；归档不会删除或搬动文件。左侧“项目上下文”编辑后自动保存，冲突时先保留本地副本再重新读取。主题、插件设置、凭据和数据跟随当前项目；新项目不复制旧凭据、信任或业务授权。

插件是与主程序同进程运行的可信代码，能够使用当前用户权限。能力声明和程序集加载上下文不是权限沙箱。停用会撤销注册、清除页面引用并请求停止，但不会卸载程序集。更新或删除包前必须退出工作台并重启。

## 本地文件与插件安装

正常运行使用 `%LOCALAPPDATA%/ProjectWorkbench/`；同一个用户根目录只允许一个工作台实例持有写入锁。

```text
ProjectWorkbench/
  host.lock                         # 进程持有的独占句柄；文件本身可在退出后保留
  host-settings.json                # 工作台主题
  plugin-state.json                 # 内容指纹信任与下次启动启用偏好
  background-tasks.json             # 独立的恢复偏好与运行意图
  Plugins/<plugin-id>/              # 从本地文件夹或 ZIP 安装的新包
  Plugins/.workbench-packages/      # 待重启 journal、历史代码及宿主管理状态备份
  settings/<plugin-id>/settings.json
  data/<plugin-id>/                 # 草稿和导出
  secrets/<plugin-id>/*.bin          # Windows DPAPI CurrentUser 密文
  .workbench-project/context.json   # 当前项目的非秘密上下文
  .workbench-project/testing/       # 项目测试源/用例/执行/模拟证据，严格 schema1
  .workspaces/catalog.json          # 项目列表；旧 default 仍使用上面的原目录
  .workspaces/projects/<GUID>/      # 新项目的独立根，含各自 settings/data/secrets 等
```

随应用分发的 `app/Plugins/` 是另一个发现目录；运行期间不向该目录写入用户数据。安装 UI 导入选中的文件夹或 ZIP，不执行、不自动批准、不启用；安装后重新发现。拒绝重复 ID、覆盖安装、路径穿越、符号链接和缺失入口。相同 ID 出现在用户目录与分发目录时，两者都显示诊断。JSON 文件不是凭据保险箱。

托管包可在详情或“包维护”暂存更新/回滚/移除；Prepared 阶段可取消。当前代码和运行实例保持到安全退出，下次启动在独占租约内、发现前 Apply。维护失败显示本地诊断，未受影响的分发根仍可使用。维护后撤销信任/启用/恢复意图，需重新审查。备份只覆盖 UserRoot 管理的 data/settings/secrets 和后台文档；代码回滚不回写或降级当前业务数据。明确连接的独立版规范状态、外部 DWS profile 等外部存储不自动备份或恢复，资源租约/schema/发送回执由业务运行时负责。

## 验证

跨平台真实 Core 测试：

```sh
dotnet test ProjectWorkbench.Core.slnx -c Release --nologo
```

Windows 原生冒烟（先构建）：

```powershell
pwsh -File .\scripts\native-smoke.ps1 -TimeoutSeconds 120
pwsh -File .\scripts\native-runtime-probe.ps1 -TimeoutSeconds 180
pwsh -File .\scripts\project-workspace-probe.ps1 -TimeoutSeconds 180
```

冒烟使用新的 `%TEMP%/ProjectWorkbench-smoke/<随机 ID>/` 用户根目录，只复制、发现两个已知分发示例，不读取真实用户设置或已安装插件。它在真实 WinUI UI 线程创建并附加页面和设置控件，执行本地命令；编辑真实摘要 TextBox，跨页面导航后检查草稿、调用保存按钮并验证重复生成保留编辑；停用/重新启用时检查恢复已保存版本，最后停止/关闭，输出 `artifacts/native/native-smoke.json`。失败、超时或无法初始化 WinUI 都返回非零退出码。临时数据保留用于排查，位置写入报告。它不代替主题外观、DPI、键盘和屏幕阅读器的人工验收。

GitHub Actions 对 PR（含草稿）以及 `main`、`feat/native-plugin-workbench` 分支提交运行 Linux Core 测试及 Windows Core/发布/原生冒烟。产物 `workbench-win-x64` 是完整应用目录，`native-smoke-report` 是 JSON 报告，另有两平台测试结果。当前开发环境是 Linux；跨目标 C# 编译不证明 Windows 原生启动成功。[Windows 验证记录和待验清单](docs/windows-verification.md) 区分这些证据。

新增 runtime probe 显式使用 `%TEMP%/ProjectWorkbench-runtime-probe/<GUID>/`，测试夹具位于 `artifacts/native/fixtures/`，不进入应用 `Plugins/` 或普通启动。它验证原生凭据操作、后台响应、关闭超时/回调/重试所有权，以及六个独立 OS 进程的恢复、停用、更新、回滚和移除。[历史 host/SDK 0.2 Server 2025 CI](https://github.com/ThaoKeeton/project-workbench/actions/runs/42307917231)（源码 `a54a668`）已通过 Windows Core 267 项、原生 14 项、管理 17 项、六阶段平台重启 7 项，以及独立业务插件 12 进程共 51 项检查。后六进程使用不同 ID、不同隔离根的上游模拟验收二进制；实际定时 Core 和本地模拟发送成功不代表真实送达。钉钉仅测试合成模型值保存及 commit/revision 读取，模型 key 值消费与 provider dispatch 未执行。没有真实 Webhook/HTTP/业务发送。Windows 11 的 18 项主题/DPI/选择器/可访问性等人工清单全部待验。完整 [Windows 应用目录](https://github.com/ThaoKeeton/project-workbench/actions/runs/42307917231/artifacts/13547711575) 和校验值见 [验证记录](docs/windows-verification.md)。

当前主程序及两个 SDK 预览版本为 0.4.0，程序集/API/运行服务身份保持 1；可选项目服务 1 的契约和已知工作保留规则见 [项目服务](docs/project-services.md)。真实旧 SDK 0.2 业务包与固定来源不变；新的 Windows 原生/独立业务兼容结果需由当前提交的 CI 单独验证。

新增 workspace probe 只使用 `%TEMP%/ProjectWorkbench-workspace-probe/<GUID>/` 的合成数据，以两个实际 EXE 进程运行 seed16/verify3；检查原生项目控件、上下文/主题/设置/空凭据分离、草稿、失败重试、归档、窗口重开与进程恢复。新增检查在实际上下文控件接受文字后立即切换/关闭，不等异步事件或自动保存，并在同一插件实例的私有编辑器内修正已结束的准备失败后显式重试。成功需同时具备实际 WinUI Closed、OS exit 0 和退出后重新取得目录锁。阶段报告只完成一次，保留原生 PID、首个拒绝原因和实际租约结果。产物为 `project-workspace-probes`。此脚本需要源码构建生成的独立测试夹具，普通下载的主程序目录不包含夹具；它不是下载用户的操作步骤。当前新增源码仍需本轮 Windows CI 的实际报告，参考编译不等于原生执行。Windows 11 人工、真实旧独立 GUI、更高版本迁移和真实 provider 验收继续延期，不要求用户现在参与测试。

独立 SDK 导出：`pwsh ./scripts/export-sdk.ps1`。CI artifact `workbench-sdk-0.4.0` 含固定版本源码、两个 NuGet 包及提交/文件哈希清单；接入步骤见 [插件契约与独立构建](docs/integrations/plugin-contract-v1.md)。

- [插件 SDK、清单和包布局](docs/plugin-sdk.md)
- [运行服务、停止与凭据](docs/runtime-services.md)
- [包维护与代码/数据边界](docs/packages.md)
- [Core 生命周期与存储约定](src/Workbench.Core/README.md)
- [设计范围](docs/superpowers/specs/2026-11-06-native-workbench-design.md)

可选测试服务 1（最低宿主 0.4.0）已接入项目源/用例/执行/证据上下文、共享控制器和独立原生“测试设计”入口。下载同一运行的 `workbench-testing-plugin-0.1.0`，在“插件”安装其中 ZIP，再审查并信任启用；其包不加入固定六文件业务 companion。已有预置测试列表仍是独立示例。新增源码的实际 Windows CI 与下载字节审计由当前运行报告确认，不能用 Linux 参考编译替代。见 [测试插件安装与操作](docs/testing-plugin.md) 和 [测试服务](docs/testing-services.md)。
