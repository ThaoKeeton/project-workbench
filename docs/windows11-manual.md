# Windows 11 最小人工验收

本页是实际 Windows 11 客户端的操作清单和记录模板，**当前全部待实机观察**。Linux 的 `Workbench.NativeReference` 只是不可运行的 C# 参考库；Windows Server 2025 的自动探针是另一类证据。Server 的 build 26100 不能证明机器是 Windows 11，也不能代替本页。

兼容性目标保持 **Windows 11 24H2 或更新版本、x64，并且所用 edition/version 仍在微软服务期内**。截至 2026-11-07，24H2 Home/Pro 的更新已于 2026-10-13 结束，24H2 Enterprise/Education 至 2027-10-12；25H2/26H2 仍在服务期内。先记录具体机器信息，再按[微软版本服务表](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information)核对，不把“24H2+”解释为所有 24H2 edition 都受支持。[Windows App SDK 支持表](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/support)描述的是依赖平台范围，不能证明本应用已在这些系统通过验收。本任务不改变 App SDK 1.8.260921001 或 OS 下限。

## 开始前

使用专门的 Windows 测试账户，确认它的 `%LOCALAPPDATA%\ProjectWorkbench\` 没有日常业务数据。保留日常账户及独立版规范状态；不要复制活动账本，不输入实际凭据，不连接现有 DWS/profile，不授权 provider HTTP、模型或 Webhook/发送。关闭独立版业务应用；钉钉独立版若关闭到托盘，须使用“退出应用”完成退出。共享锁的版本和范围见[独立版兼容性](standalone-compatibility.md)。

在 PowerShell 7 记录客户端类型、edition、版本和真实 OS 架构：

```powershell
Get-CimInstance Win32_OperatingSystem |
    Select-Object Caption, Version, BuildNumber, OSArchitecture, ProductType
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' |
    Select-Object DisplayVersion, EditionID, InstallationType
[Runtime.InteropServices.RuntimeInformation]::OSArchitecture
```

要求 `ProductType=1`、Windows 11 客户端及 `OSArchitecture=X64`；同时记录 `winver`。仅有“64-bit”、进程位数、注册表旧 ProductName 或某个 build 号不足以判定 x64 Windows 11。记录显示器缩放、窗口尺寸、输入法和屏幕阅读器；本页不收集账户名或真实配置值。

从[成功的 Windows Actions 运行](https://github.com/ThaoKeeton/project-workbench/actions/workflows/build.yml)下载同次 `workbench-win-x64` 与 `workbench-business-plugins`，完整解压到普通本地目录。按[安装指南](install-business-plugins.md)核对 manifest、宿主 EXE/DLL 和五个配套载荷的 SHA256/字节数，记录 run ID、host.sourceCommit、packagingSourceCommit/Dirty。保留整个 app 目录及 PRI/依赖；不能从 ZIP 内运行或只复制 EXE。配套内层 `dingtalk/package.zip`、`upcam/package.zip` 才是导入对象。

## 最小冒烟（六步，逐步记录）

1. 启动 `Workbench.App.exe`，确认首页、插件、命令、后台任务、凭据、包维护和工作台设置可导航。默认只有两个本地示例；配套业务包尚未导入和信任。首页应说明生产插件可选安装。
2. 在“包维护”分别打开 ZIP、文件夹选择器后取消，确认无新增包或待重启操作。按安装指南导入两个生产内层 ZIP，导入后仍未启用。取消一次信任对话框，确认没有插件页面/命令；审查固定 ID/版本后才明确信任启用。
3. 打开两个示例和两个业务插件的页面/设置，确认属于同一个原生窗口，无独立 GUI/WebView。钉钉保持演示模式；UPCAM 仅使用本地 sample/preview、虚构输入。缺少自动业务授权的开始拒绝是预期边界，不用真实凭据绕过。凭据页只检查空编辑框和固定状态；需要保存测试时仅用明确标记的合成值，随后删除。
4. 用本地示例/合成任务检查跨页面运行、Stop 后状态和正常退出重启；需插件确认的任务保持专属入口，不能用通用 Start/Resume 绕过。Stop 不等于插件退役：切回独立版前须“停用”插件或安全退出宿主，并确认宿主自有 worker/业务连接已实际退役。若关闭等待未结束，保留进程和锁，使用“重试关闭”。不要强杀来证明正常停止。重启后核对启用偏好、当前任务状态及主题；停用不表示热卸载程序集。
5. 在系统/浅色/深色之间切换，观察同一卡片、插件控件和焦点对比度；覆盖 100%、150%、200% 缩放、窄窗口、Tab/Shift+Tab/Enter、选择器/信任弹窗取消及屏幕阅读器名称。损坏主题附加检查：先安全退出，在**本测试账户**用户根保留原 `host-settings.json` 的副本，再把主题文件写为无效 JSON；重新启动应回退系统主题、出现 `ThemeSettingsFailed (InvalidData).`，且首页/包维护/设置仍可用。由设置页重新选择并保存主题后重启验证；只处理本次合成文件，不修改日常账户。
6. 查看包维护的中文阶段、代码历史和诊断。只有 Prepared 且实际尚未移动代码的暂存能取消；Prepared 显示后取消被拒绝时，也须保留文件/journal，安全退出并重启。已开始的维护无取消/删除入口。若没有真实更高版本，仅记录“更新/回滚/移除实机未执行”，不把冻结生产包或合成 manifest 版本当作上游迁移证明。失败提示不能证明旧代码已恢复；核对快照、当前版本和诊断。代码回滚不恢复当前业务数据/凭据，外部规范状态/profile 不在宿主自动备份范围。

## 完整 18 项目标

六步冒烟只能记录所观察范围；不能据此勾选全部目标。下表对应[原始 18 项人工清单](windows-verification.md#windows-实机人工清单全部待验)，每项仍须单独证据；具体行为和关闭所有权要求以原清单为准。

| 项 | 完整目标 | 本次结果/证据 |
| --- | --- | --- |
| 1 | 首次清单发现不自动启用 | 未执行 |
| 2 | 包详情与信任取消/确认 | 未执行 |
| 3 | 两示例原生页面和设置 | 未执行 |
| 4 | 编辑、跨页未保存内容、恢复与 CSV 示例导出 | 未执行 |
| 5 | 插件偏好恢复、停用后撤销入口 | 未执行 |
| 6 | 整进程重启、启用/停用和主题恢复 | 未执行 |
| 7 | 第二个普通实例的用户根占用失败窗口 | 未执行 |
| 8 | 文件夹安装、重复/无效/拒绝路径 | 未执行 |
| 9 | 包内容变化撤销信任、重启维护 | 未执行 |
| 10 | 系统/浅/深主题与现有卡片实时更新 | 未执行 |
| 11 | 100/150/200% DPI、宽窄窗口与表格 | 未执行 |
| 12 | 键盘、焦点、屏幕阅读器与对话框取消 | 未执行 |
| 13 | 命令/设置/用户状态故障隔离与安全诊断 | 未执行 |
| 14 | 正常关闭等待、重复关闭与启用意图 | 未执行 |
| 15 | 后台跨页、Stop/Faulted 与业务确认门槛 | 未执行 |
| 16 | 合成凭据空框、保存/删除和无明文 | 未执行 |
| 17 | ZIP/文件夹取消，维护/代码与数据边界 | 未执行 |
| 18 | 关闭等待时管理查询、重试及包写入保锁 | 未执行 |

仍需另行验证：真实更高业务版本/schema 迁移；固定维护版 standalone GUI 与平台在同一专用测试账户/合成规范状态下的双向互斥；任意旧版、不同登录 session/profile 的边界。这些都没有由 Server 自动矩阵或最小冒烟证明。首次双 GUI 验证须先由业务发布方确认兼容版本和测试根，禁用所有 provider/发送目的；不要用真实规范数据制造证据。

## 结果模板

复制以下模板填写；每项使用“通过/失败/未执行”，附实际观察及证据路径。不得仅因命令或清单存在就填“通过”。截图/日志只含合成内容，隐藏账户路径及配置值。

```text
日期/测试者代号：
OS Caption / ProductType / InstallationType：
EditionID / DisplayVersion / build / winver：
OSArchitecture（须 X64）/ 微软服务期核对日期：
Actions run ID / artifact 名：
host.sourceCommit / packagingSourceCommit / packagingSourcesDirty：
宿主 EXE/DLL SHA256 与 manifest 配对核验：未执行
五个载荷 SHA256/字节数核验：未执行
专用测试账户、无真实凭据/provider/发送：未执行
DPI / 窗口尺寸 / 键盘 / 屏幕阅读器：
最小步骤 1～6：逐项结果、实际现象、证据路径
主题损坏：安全代码 / 系统主题回退 / 导航 / 明确保存修复 / 重启
完整目标 1～18：逐项结果、实际现象、证据路径
Stop / 停用 / 安全退出 / worker 实际退役 / 重启：
真实高版本迁移 / 双 GUI / 其他 session/profile：未执行
失败、限制及复测计划：
结论（仅限本次实际观察的范围）：
```
