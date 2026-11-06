# Windows 构建与原生验证

证据分层记录。Linux Core 行为测试、跨目标 C# 参考编译、Windows 发布、真实 WinUI 自动冒烟和 Windows 人工验收各有不同范围，不互相替代。

| 检查 | 已有证据 | 范围与限制 |
| --- | --- | --- |
| Linux Core Release | 83/83，通过；包括真实临时目录、独立插件 DLL、自包含子进程 | 不覆盖 Windows 文件系统分支或原生 UI |
| Linux 全解决方案参考编译 | 零警告、零错误 | 通过命令行关闭原生资源步骤；不能用于分发 |
| Linux 正常原生构建 | 按预期受阻：MSB6003，`MakePri.exe` 的 Exec format error | Windows 原生工具无法在此 Linux 环境运行，未修改生产目标绕过 |
| Windows Core 测试/发布 | 由 PR 的 `native-windows` job 执行，结果以实际 CI 记录为准 | 构建通过仍不代表原生启动 |
| Windows 原生自动冒烟 | 由 CI 启动发布 EXE，结果以 `native-smoke-report` 为准 | 初始化失败或没有可用桌面会失败，不能跳过为通过 |
| Windows 人工验收 | 尚未执行；下方清单全部待验 | 主题外观、DPI、文件选择器、可访问性及真实重启仍需实机 |

## 干净 Windows 环境

使用受支持的 Windows 11 24H2+ x64、PowerShell 7、仓库 `global.json` 所需的 .NET 10 SDK。IDE 可选；若使用，需 Visual Studio 2026 18.0+。SDK 的 NuGet 依赖提供 Windows 原生构建工具。API 目标 19041 只是调用 API 下限，不承诺 Windows 10 已获支持或验收。

```powershell
dotnet --info
dotnet test ProjectWorkbench.Core.slnx -c Release --nologo
pwsh -File .\scripts\build.ps1
.\artifacts\native\app\Workbench.App.exe
```

发布目标为 win-x64、unpackaged、自包含目录，保留完整 `app/` 及其 `Plugins/` 子目录。没有安装器、签名、上线或部署步骤。构建脚本遇到任意非零 dotnet 退出码即失败，并从干净的 `artifacts/native/` 开始，避免旧插件混入。

## 自动冒烟与 CI 产物

```powershell
pwsh -File .\scripts\native-smoke.ps1 -TimeoutSeconds 120
```

脚本启动 `artifacts/native/app/Workbench.App.exe --smoke-test --smoke-report <报告绝对路径>`，最多等待 120 秒。EXE 在真实 `Application.Start` UI 线程运行以下检查：

1. 为每次运行创建新的临时用户根目录，仅复制并发现 `com.projectworkbench.summary` 与 `com.projectworkbench.test-cases` 两个分发样例，检查默认禁用/未信任。
2. 验证第二个持有者不能打开同一个用户根目录锁。
3. 为测试明确批准并启用这两个样例，验证共享 WinUI/WinRT 契约身份，并逐个创建、附加两个页面与两个设置控件到真实 XamlRoot。
4. 应用系统/浅色/深色请求主题，运行两个本地命令，检查隔离目录下的非空摘要与 CSV 文件。
5. 清除控件，停用并重新启用两个样例，再次附加原生控件；检查注册撤销与恢复。
6. 停止插件、清除注册，检查保存的 enabled 偏好仍为 true，写入 JSON 并关闭窗口。

报告包含 `succeeded`、`stage`、`userRoot`、检查列表、文件路径/大小和完成时间；失败包含异常。进程入口前的模块初始化崩溃可能无法由 EXE 写报告，脚本会补充失败 JSON；即使原生进程已写成功报告，后续退出失败或超时也会把最终 succeeded 改为 false，并保留已有检查与原生阶段。报告不含真实用户设置或业务数据。临时根目录保留用于故障检查，可在进程退出后按报告路径自行删除。超时是失败；脚本为限制测试运行时间而终止测试进程，不表示普通停用可强制取消插件。

`.github/workflows/build.yml` 在 PR（包括草稿）、`main`/`feat/native-plugin-workbench` push、手动触发时运行：

| Artifact | 原始路径 | 用途 |
| --- | --- | --- |
| `workbench-win-x64` | `artifacts/native/app/` | 完整自包含可启动目录与两个独立插件包 |
| `native-smoke-report` | `artifacts/native/native-smoke.json` | 原生运行检查、失败层与临时数据位置 |
| `core-linux-results` | `artifacts/tests/` | Linux TRX |
| `core-windows-results` | `artifacts/tests/` | Windows TRX（包括 Windows 文件系统分支） |

如果 Core、发布或原生启动失败，job 保持失败状态；上传已有产物供排查，缺少必需报告不会被解释为通过。

仅供开发环境参考编译的命令如下，不能用于发布或声称原生验收：

```sh
dotnet build ProjectWorkbench.slnx -c Release \
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
- [ ] 同时启动第二个普通实例显示用户目录占用错误；隔离冒烟可与普通实例并存。
- [ ] 本地文件夹选择器可选择独立的新 ID 开发包并安装；安装不执行或自动启用；重复 ID、无效清单和被拒绝路径显示可见错误。
- [ ] 关闭程序后更改包内容，再启动会撤销继承的信任；更新/删除需退出并重启，停用不声称卸载程序集。
- [ ] 系统/浅色/深色主题下查看主程序及两插件的对比度、焦点、文字可读性；切换系统主题时跟随行为正常。
- [ ] 在 100%、150%、200% DPI 及不同窗口宽度下查看滚动、文本换行与可编辑表格；没有固定内容宽度截断。
- [ ] 键盘 Tab、Shift+Tab、Enter、焦点提示和屏幕阅读器名称可用，文件选择与信任弹窗可取消。
- [ ] 运行命令异常、损坏设置/用户状态文件时显示对应错误，正常插件仍可使用。
- [ ] 关闭窗口先等待插件停止，再退出；重入关闭不重复启动停止流程；enabled 偏好不被写成 false。不响应的可信插件不能被普通生命周期机制强制中断。
