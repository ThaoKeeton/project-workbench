# 钉钉与 UPCAM 生产插件安装

2026-11-09 当前已核对的配套为 [Actions42559172825](https://github.com/ThaoKeeton/project-workbench/actions/runs/42559172825)，宿主0.4.0/API与服务1，功能源码 `b1a3e9c9963c2d75798a6068f99a6270d81bd074`：[完整Windows x64宿主](https://github.com/ThaoKeeton/project-workbench/actions/runs/42559172825/artifacts/13649448205)与[业务插件配套](https://github.com/ThaoKeeton/project-workbench/actions/runs/42559172825/artifacts/13649572382)均已下载核对实际字节及EXE/DLL/manifest配对。宿主原始ZIP90,666,891bytes/SHA256 `fad691b874f66bf1ef522f5be38e9aa12edb81b69a0000da63609351409a1139`，配套ZIP `6399a0f3919baf2c31277900926e5da9b4440d30e91e04b54871166bf3f609f7`。目录含.NET10.0.14运行时，完整解压后运行 `Workbench.App.exe`。

**UPCAM版本限制：此配套仍为生产0.1.0，共享core0.4.3，不含用户报告的只读诊断卡死修复。** 新core/独立版0.4.4及插件0.1.1由独立任务处理；待正式包到位，须重新核对并在实际宿主检查，不把本轮旧包51项报告当成新包结果。当前51项/12进程证明旧包安装、原生页面、本地虚构预览、后台及退役；不证明真实Teambition账户可用性。

只安装UPCAM时，解压业务配套外层ZIP，选择其中 `upcam/package.zip` 导入“包维护”，再在“插件”审查并明确启用。其根清单ID `com.upcam.task-digest` /0.1.0/API1/最低宿主0.2.0；ZIP SHA256仍为 `3e07b7db0e3f17871b88ce1e45ac09d3b8724e6628a35fd5e0cdc1eb9ad95dbe`。不得将GitHub外层ZIP当作插件包。

普通工作台数据根为 `%LOCALAPPDATA%\ProjectWorkbench`。默认单项目的UPCAM配置绑定在 `settings\com.upcam.task-digest\settings.json`，默认业务profile为 `data\com.upcam.task-digest\profile`；其他项目使用各自runtime根。业务凭据留在该profile的当前Windows用户DPAPI存储，宿主保险库仅保存生成的恢复绑定密钥。不会自动读取旧独立版 `%LOCALAPPDATA%\UPCAM\TaskDigest`；接续原目录须完全退出独立版并由用户明确选择，保留原账本，不复制出第二个可调度副本。实际Teambition凭据与权限仍由用户在本机配置；本轮未连接真实profile、使用Webhook、调用真实模型或发送钉钉。

此批次还通过Windows Core1561、Linux1558/4明确跳过、来源36项/4实际进程及owned27项。WindowsServer2025自动验证不等于Windows11用户实测。链接按一天保留，当前约2026-11-10 04:21UTC到期；没有创建安装器或release。不可变的下载包README保留构建时历史655批次说明，当前b1配套以本页链接及其manifest中的宿主来源/哈希为准。下列历史记录仍保留。

历史2026-11-08已核对的配套批次为 [Actions42518602297](https://github.com/ThaoKeeton/project-workbench/actions/runs/42518602297)，功能源码 `655d7b6496067daad7c5e4cefb1f1da1b8379536`、宿主0.4.0/API1：[完整宿主](https://github.com/ThaoKeeton/project-workbench/actions/runs/42518602297/artifacts/13633291911)与[两个业务生产插件](https://github.com/ThaoKeeton/project-workbench/actions/runs/42518602297/artifacts/13632778983)已下载核对实际字节；原生自动矩阵及51项双插件检查通过。下方安装层级/固定插件版本与本批次一致。产物按一天保留，链接是否仍有效以Actions页面为准；这不是installer、签名release或Windows11人工验收。全程只用合成数据，没有真实Webhook或钉钉消息；当前不要求用户开始人工集中验收。

从本仓库**同一次成功的 Windows Actions 运行**下载 `workbench-win-x64` 和 `workbench-business-plugins`。前者是完整宿主目录；后者是可按需安装的两个生产插件。保留整个宿主目录后启动 `Workbench.App.exe`，不能只复制 EXE。人工验收目标为仍在微软服务期内的 Windows 11 24H2 或更新版本、x64；版本下限不表示该版本所有 edition 仍受支持，请核对[微软版本服务信息](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information)。Server 2025 自动检查与 Windows 11 人工检查分别记录。

`workbench-business-plugins` 的 GitHub 外层下载 ZIP 先解压到普通目录，得到：

```text
dingtalk/package.zip
dingtalk/provenance.json
upcam/package.zip
upcam/provenance.json
manifest.json
README.zh-CN.md
```

里面的两个 `package.zip` 才是导入对象，各自根目录直接包含 `plugin.json`。**不要在导入 ZIP 时选择 GitHub 外层 ZIP**，也不要选择包含两个插件的总目录。宿主严格要求根清单，不会猜测任意包装层。这个配套下载不包含宿主、SDK、独立 Desktop 应用或 UPCAM acceptance 测试插件；默认宿主仍只附带两个本地示例。

## 核对下载

配套 `manifest.json` 使用 schemaVersion 1、Production 模式，记录插件来源、版本、上游 CI、ZIP SHA256/字节数，以及除 manifest 自身外的全部五个载荷文件哈希。`host.sourceCommit` 来自配对 `Workbench.App.dll` 的 ProductVersion，EXE/DLL 哈希与它一起标识实际文件；`packagingSourceCommit` 和 `packagingSourcesDirty` 另记打包源码。旧宿主与新打包器的来源不能混为一谈。宿主版本解析接受三段数值 ProductVersion + 完整小写源码提交，要求 FileVersion 为对应三段版本加 `.0`、AssemblyVersion 为 `1.0.0.0`；因此历史 0.2 与当前预览 0.4 的文件/来源都必须自洽，严格当前来源和干净输入检查不变。生产业务包的 minimumHostVersion 仍固定为 0.2.0。版本资源是二进制声明的来源信息，哈希证明字节一致，**两者都不是独立发布者签名**。

使用 PowerShell 7，在解压的配套目录运行下面的检查（只读文件，不运行插件）；将 `$app` 改成同次下载的宿主目录：

```powershell
$app = 'C:\WorkbenchTrial\app'
$manifest = Get-Content .\manifest.json -Raw | ConvertFrom-Json
foreach ($file in $manifest.files) {
    if ((Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash -ne $file.sha256 -or
        (Get-Item -LiteralPath $file.path).Length -ne $file.bytes) { throw '配套文件校验失败' }
}
if ((Get-FileHash (Join-Path $app 'Workbench.App.exe')).Hash -ne $manifest.host.executableSha256 -or
    (Get-FileHash (Join-Path $app 'Workbench.App.dll')).Hash -ne $manifest.host.dllSha256) { throw '宿主与插件配套清单不匹配' }
```

以下两个 ZIP 的冻结来源是：

| 插件 | ID / 版本 | 来源提交 | 导入 ZIP SHA256 |
| --- | --- | --- | --- |
| 钉钉 | `com.projectworkbench.dingtalk-insights` / `0.3.2` | `c0c1d3303ae8b59c36dd7c4c1de85ddc6578abcf` | `38d7a8f30ce6d4d5c6f6e26c757cbb6b9fc9f812546bf1db2ecdbadb9b578200` |
| UPCAM 生产 | `com.upcam.task-digest` / `0.1.0` | `6370b9df46af564fde022924d1dbeace7d575c6a` | `3e07b7db0e3f17871b88ce1e45ac09d3b8724e6628a35fd5e0cdc1eb9ad95dbe` |

## 在空白测试账户安装和模拟试用

1. 使用专门的 Windows 测试账户，确认该账户的 `%LOCALAPPDATA%\ProjectWorkbench\` 尚无实际业务数据。普通启动固定使用这里；解压到另一位置不会创建另一个用户数据根。不要移动、清空或改写日常账户的目录，也不要复制一份活动业务账本。先关闭独立版业务应用。
2. 打开工作台的“包维护”→“导入 ZIP”。在 FilePicker 中浏览到已解压配套目录的 `dingtalk` 子目录，选择 `package.zip`；再对 `upcam\package.zip` 重复。选择器可取消，取消不安装。导入本身不执行插件、不批准信任、不启用。
3. 到“插件”审查 ID、版本、能力和诊断，再点击“启用”。信任确认可取消；只有明确确认后才运行。同进程可信插件拥有当前用户权限，能力声明与加载上下文不是权限沙箱，也没有嵌入独立 GUI/WebView。
4. 首次保持钉钉默认模拟模式，只使用内置演示数据。UPCAM 使用其本地 sample/preview 功能和虚构输入；没有自动业务授权时的开始拒绝应保留。不要输入实际 API key、Webhook 或业务凭据，不连接现有 DWS/独立版 profile，不授权模型调用、采集或消息发送。
5. 插件代码信任、业务连接/开始确认、模型用途授权、发送确认及后台恢复偏好各有自己的含义；确认信任不会代替后续业务批准。RequiresPluginStart 任务不能用通用后台启动/恢复绕过业务确认。宿主用户根锁也不能代替业务规范状态的锁。
6. 试用导航、Stop、停用和正常退出，再启动核对状态。Stop 请求由插件完成；停用不会热卸载程序集，宿主不会任意强制停止不合作代码。若退出仍在等待后台工作，保留进程和锁，按管理页面提示处理。

## 已有的上游下载如何选择

若上游产物仍在有效期内，也可从下列运行获取原包；已过期时选择同次新构建的业务配套，避免把独立应用目录当插件：

- [钉钉 42295585483 的产物](https://github.com/ThaoKeeton/dingtalk-insights/actions/runs/42295585483/artifacts/13542127319)：先解压 GitHub 外层下载，再找 `workbench-plugin/DingTalkInsights.Workbench-0.3.2-win-x64.zip`。该内层 ZIP 已有根 `plugin.json`，在“导入 ZIP”选择它。
- [UPCAM 42268731102 的产物](https://github.com/ThaoKeeton/upcam-task-digest/actions/runs/42268731102/artifacts/13529430900)：先解压 GitHub 外层下载，找到 `UPCAMTaskDigest-workbench-production-0.1.0-win-x64.zip`，再解压该原厂内层 ZIP，进入 `com.upcam.task-digest` 文件夹。使用“包维护”→“导入文件夹”（或“插件”→“从本地文件夹安装”），在 FolderPicker 中选择**直接包含 `plugin.json` 的这个文件夹**。不要选其父目录；选择器可取消。

UPCAM 原厂内层 ZIP 有固定的单层包装；配套 `upcam/package.zip` 仅移除这个已核验前缀，全部 714 个文件内容与原厂一致。它是传输重打包，不是新上游发布。原厂 ZIP SHA256 为 `5f905bc4d79dea32de7e07739904416a0f88ca727458a887c406ad02c7154a2d`，与配套根清单 ZIP 的哈希不同。其实际 PR 构建 checkout 为 `1454dc3ff21de2152539d6f3462a3ddad13c4398`，与记录的 workflow head 无文件差异；两个来源均保留在 provenance 中。

## 更新、回滚和证据边界

相同 ID 已安装时使用“更新 ZIP/更新文件夹”，不能再次导入覆盖。更新须同 ID、兼容宿主且版本严格更高；本配套固定版本不构成更高版本迁移验证。更新、代码回滚、移除都先暂存，安全退出后由下次启动执行，再重新审查并信任。界面仅在 Prepared 提供取消，且必须尚未移动代码；若崩溃后 Prepared 元数据与实际目录不同，Core 会拒绝取消。拒绝时及维护已开始后保留文件/journal，在下次启动恢复，查看当前版本、代码历史和诊断，不能从失败提示推断回退成功。

代码回滚只切换历史代码，不恢复、删除或降级当前业务数据、设置和凭据。宿主管理状态快照只覆盖 UserRoot 下的数据/设置/密文及后台文档；明确连接的独立版规范状态、外部 DWS profile 等不自动备份/恢复。移除保留当前数据和代码历史。不要用回滚来尝试跨业务数据 schema 降级。

要切回独立版，先停止工作，再在“插件”停用该插件或安全退出整个宿主，确认其业务连接与自有 worker 已实际退役，再启动维护独立版。仅任务 Stop 可保留钉钉 canonical connection/租约或 UPCAM 空闲 worker/单例；钉钉独立版关闭到托盘也不等于真正退出。保留测试账户的数据根；本指南的空白模拟试用不需要复制旧账本。若曾连接外部规范状态，先由业务发布方核对共享锁和数据格式兼容性，不能假定旧版可降级读取。固定版本、session/profile 范围及源码证据见[独立版兼容性](https://github.com/ThaoKeeton/project-workbench/blob/feat/native-plugin-workbench/docs/standalone-compatibility.md)；本轮尚未执行两个旧 GUI 与宿主的联合实机测试，也不保证任意旧版本都遵守同一互斥协议。

自动矩阵包含真实宿主生命周期与独立生产包的模拟业务检查；UPCAM acceptance 是另一个 ID、另一个测试根中的合成验收，未分发给用户，合成 sent 不代表真实送达。该下载是目录/插件交付，没有安装器或签名保证，也没有建立真实接口/消息、独立旧 GUI 联合运行或多日运行的证据。Windows 11 的主题、DPI、键盘、选择器、屏幕阅读器等人工清单仍待实机观察，见[最小人工验收与结果模板](https://github.com/ThaoKeeton/project-workbench/blob/feat/native-plugin-workbench/docs/windows11-manual.md)和[完整验证记录](https://github.com/ThaoKeeton/project-workbench/blob/feat/native-plugin-workbench/docs/windows-verification.md)。

开发者可在发布完整宿主后运行 `pwsh ./scripts/export-business-plugins.ps1`。它拒绝非空输出、路径相交和链接，只清理自己创建的暂存；重新导出请显式选择新的空目录。Windows CI 使用 `-RequireCurrentSource` 强制实际 DLL 声明来源与当前提交相同、打包源码干净，并将同一导出目录交给原有生产业务矩阵，成功后上传配套产物。
