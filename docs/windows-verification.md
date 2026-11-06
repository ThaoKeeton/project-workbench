# Windows 构建与原生验证

证据分层记录。Linux Core 行为测试、跨目标 C# 参考编译、Windows 发布、真实 WinUI 自动冒烟和 Windows 人工验收各有不同范围，不互相替代。

| 检查 | 已有证据 | 范围与限制 |
| --- | --- | --- |
| Linux Core Release | 86/86，通过；包括真实临时目录、独立插件 DLL、自包含子进程 | 不覆盖 Windows 文件系统分支或原生 UI |
| Linux C# 检查 | 早期手动主程序版本参考编译通过；当前主程序 7 个 C# 文件语法与 XML 检查通过 | 采用标准 App.xaml 后，XAML 编译器必须在 Windows 执行；当前主程序没有 Linux 完整语义编译证据 |
| Linux 正常原生构建 | 按预期受阻：MSB6003，`MakePri.exe` 的 Exec format error | Windows 原生工具无法在此 Linux 环境运行，未修改生产目标绕过 |
| Windows Core 测试/发布 | [第七轮分支 CI](https://github.com/ThaoKeeton/project-workbench/actions/runs/42209148829)：Windows Core 86/86，自包含主程序与两个独立插件包构建通过；发布的 `Workbench.App.pri` 为 1,352,680 bytes | 使用 `windows-2025` runner；主程序的标准 XAML 生成、语义编译和 PRI 发布由本次 Windows 构建验证 |
| Windows 原生自动冒烟 | 同轮 `succeeded=true`、`stage=completed`，11 项检查全部通过：原生启动、占用根失败窗口、实时主题、4 个页面/设置控件、文件命令、停用/重新启用及关闭 | 使用真实 WinUI 控件和隔离临时数据；自动检查不代替视觉与交互人工验收 |
| Windows 人工验收 | 尚未执行；下方清单全部待验 | 主题外观、DPI、文件选择器、可访问性及真实重启仍需实机 |

本次通过的代码提交为 `0808aa97f20b524ef5058dffeaf9aa8e9203bc0a`，CI 的 Linux 和 Windows 两个 job 均成功。Windows 证据来自 GitHub `windows-2025` runner，不是 Windows 11 人工验收。原生报告验证了同一张卡片 Light/Dark/Light 切换时实际主题与填充/描边重新解析，以及占用用户目录的失败窗口无法进入设置/首页并可安全关闭；两个样例在隔离 `DataDirectory` 下生成 235-byte 摘要与 293-byte CSV。未连接真实业务系统。

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
5. 清除控件，停用并重新启用两个样例，再次附加原生控件；检查注册撤销与恢复。
6. 停止插件、清除注册，检查保存的 enabled 偏好仍为 true，写入 JSON 并关闭窗口。

报告包含 `succeeded`、`stage`、`userRoot`、启动阶段/线程/时间、检查列表、文件路径/大小和完成时间；失败包含异常与 HRESULT。当前从 SDK 生成入口进入 Application 构造开始，记录编译后的 Application 资源初始化、窗口和控件阶段；更早的失败由启动器报告和进程诊断定位。进程入口前的模块初始化崩溃可能无法由 EXE 写报告，脚本会补充失败 JSON；即使原生进程已写成功报告，后续退出失败或超时也会把最终 succeeded 改为 false，并保留已有检查与原生阶段。报告不含真实用户设置或业务数据。临时根目录保留用于故障检查，可在进程退出后按报告路径自行删除。超时是失败；脚本为限制测试运行时间而终止测试进程，不表示普通停用可强制取消插件。

`.github/workflows/build.yml` 在 PR（包括草稿）、`main`/`feat/native-plugin-workbench` push、手动触发时运行：

| Artifact | 原始路径 | 用途 |
| --- | --- | --- |
| `workbench-win-x64` | `artifacts/native/app/` | 完整自包含可启动目录与两个独立插件包 |
| `native-smoke-report` | `artifacts/native/native-smoke.json` | 原生运行检查、启动阶段、失败层与临时数据位置 |
| `native-host-generated-sources` | `src/Workbench.App/obj/**/App.g*.cs`、`XamlTypeInfo.g.cs` | 检查固定 SDK 实际生成的入口、元数据和 InitializeComponent |
| `native-startup-diagnostics` | `artifacts/native/native-stdout.log`、`native-stderr.log`、失败时的 `native-crash-events.json` | 进程输出及只匹配 Workbench.App 的 Windows Application 崩溃事件；无法读事件时记录原因 |
| `native-debugger-evidence` | 失败后生成的 `artifacts/native/native-debug/` | Microsoft 签名的 x64 CDB 就地采集 HRESULT、错误消息、原生调用栈和采集状态；独立于普通冒烟报告 |
| `core-linux-results` | `artifacts/tests/` | Linux TRX |
| `core-windows-results` | `artifacts/tests/` | Windows TRX（包括 Windows 文件系统分支） |

如果 Core、发布或原生启动失败，job 保持失败状态；上传已有产物供排查，缺少必需报告不会被解释为通过。

原生冒烟失败后才运行 `scripts/native-debug.ps1`，默认最多采集 240 秒；如需安装官方调试器，仅安装调试工具组件并验证 Microsoft 签名。采集只发布相关文本，不生成或上传进程内存 dump，不执行堆遍历或导出完整环境。调试器及被调试程序启动前清空继承环境，仅保留明确允许的 OS、运行时和代理配置，不继承 CI token。调试采集不修改原始失败判定。第六轮通过这些文本定位了 InfoBar 默认样式资源加载失败；第七轮补齐主 PRI 发布后完整冒烟通过，成功运行无需产生调试器 artifact。

Linux 可以执行 Core 测试、源文件语法/XML 检查，以及独立 SDK/示例的 C# 参考编译。完整主程序需要 Windows XAML 编译器，`App.xaml` 的生成代码和资源处理由 Windows 构建验证。单独 SDK 参考检查示例（不能用于发布或声称原生验收）：

```sh
dotnet build src/Workbench.WinUI.Sdk/Workbench.WinUI.Sdk.csproj -c Release \
  -p:WindowsAppSDKSelfContained=false -p:AppxGeneratePriEnabled=false
```

## Windows 实机人工清单（全部待验）

记录 Windows 版本、架构、DPI、提交 SHA 和观察结果。使用专门的测试 Windows 用户以获得干净的 `%LOCALAPPDATA%/ProjectWorkbench/`，保留实际用户数据。

- [ ] 首次启动只展示清单；两个示例未自动启用，页面/命令入口不存在。
- [ ] 插件详情可查看 ID、版本、能力说明、目录和诊断；取消信任对话框不运行插件，确认后才启用。
- [ ] 两个示例各自的页面与设置作为原生控件出现在同一窗口；无外部应用窗口和 WebView。
- [ ] 摘要生成、编辑和保存，测试用例逐行编辑和 CSV 导出，均明确标注示例；文件写到各自 data 目录。
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
