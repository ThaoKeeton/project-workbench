# 插件契约 v2 预览

契约名 `workbench.plugin.v2`，版本 `2.0.0-preview.1`。它位于稳定的 API 1 旁边。`IWorkbenchPlugin`、`IPluginContext` 和 WinUI 视图工厂都不变。v1 清单仍然要求 `apiVersion: 1`。

v2 包使用单独的 `plugin.v2.json`。不要把它放到现有 WinUI 宿主的插件发现目录里；那个目录只认 `plugin.json`。

## 清单

```json
{
  "contract": "workbench.plugin.v2",
  "contractVersion": "2.0.0-preview.1",
  "id": "com.projectworkbench.test-cases.v2",
  "name": "测试用例 · 示例",
  "version": "0.1.0",
  "description": "本地示例。不执行测试，不连接网络，不发送消息。",
  "autoSend": false,
  "backend": {
    "entryAssembly": "Workbench.TestCases.V2.dll",
    "entryType": "Workbench.TestCases.V2.TestCasesModule"
  },
  "ui": {
    "kind": "web-package",
    "module": "./ui/TestCasesPanel.tsx",
    "mount": "embedded"
  },
  "permissions": [
    { "id": "project.read", "reason": "按当前项目隔离示例用例", "sensitive": false }
  ]
}
```

规则：

- `contract` 和 `contractVersion` 必须精确等于上面的预览值。
- `id` 与 v1 相同：小写、分段、可以含连字符，长度不超过 128。本预览的示例使用新 id，避免和 `com.projectworkbench.test-cases` 冲突。
- `autoSend` 必须是 `false`。`true` 时宿主拒绝加载，诊断代码 `AutoSendRejected`。
- `backend.entryAssembly` 只能是包目录里的一个 `.dll` 文件名，不能带目录。
- `ui.kind` 目前只有 `web-package`，`ui.mount` 必须是 `embedded`。界面嵌在主窗口里，不打开新窗口。
- `permissions` 至少一项、至多 32 项。每项都要有 `id` 和 `reason`。`sensitive` 缺省为 `false`。

未知字段、重复字段和无法解析的 JSON 都是 `InvalidManifest`。相同 id 出现两次是 `DuplicatePluginId`。入口程序集不在包里是 `MissingAssembly`。类型加载失败是 `LoadFailed`。这些代码可以出现在 `/api/v2/plugins`，消息正文不会带路径。

## 允许的权限

| id | 含义 | 本预览 |
| --- | --- | --- |
| `project.read` | 读取当前项目 id，用来隔离插件自己的数据 | 可授予 |
| `plugin.data.read` | 读取该插件在当前项目下的 v2 数据目录 | 可授予 |
| `plugin.data.write` | 写入该目录 | 可授予 |
| `local.file.export` | 在该目录写本地导出，并把字节交回界面下载 | 可授予 |

不在表里的 id 得到 `PermissionRejected`，模块不加载。`sensitive: true` 得到 `SensitiveActionRequiresConfirmation`，本预览也不授予。发送类权限（例如 `message.send`）同时命中这两条。以后真的要发送，必须先在本机由人确认，确认不能被自动发送或远程请求代替。

宿主把授予的权限集合交给模块。模块在每次动作前再检查一次。缺权限返回 `PermissionDenied`，不读取数据。

## 后端

后端引用 `Workbench.Service.Abstractions`，实现 `IWorkbenchPluginModule`：

- `Id` 必须等于清单 id。
- `StartAsync` 只接收授予的权限。不要在这里打开套接字。
- `HandleAsync` 接收动作名、当前项目 id、已经限制在项目根内的数据目录、权限集合和可选 JSON 正文。

数据目录是 `<项目根>/v2-plugins/<插件 id>/`。这不是 schema 1 的 `data/<插件 id>/`，也不是 `.workbench-project/testing`。插件自己的文件要带 v2 schema 名。

加载时，宿主把抽象程序集固定为自己的副本，再从包目录加载入口程序集。这仍然是进程内的可信代码，不是安全沙箱。权限清单是宿主强制执行的声明，不是隔离边界。

## 界面

`ui.module` 指向包内的网页模块。宿主把它嵌进现有页面，而不是新开窗口。本预览的示例模块是 `plugins/v2/Workbench.TestCases.V2/ui/TestCasesPanel.tsx`，由主界面在 `/plugins/test-cases` 挂载。模块通过宿主传入的函数读写 `/api/v2`，自己不保存会话令牌。

## 主题契约（v2 预览，不改 schema 1）

宿主把当前主题放在文档根上：

- `document.documentElement.dataset.theme`：`light` 或 `dark`，这是已经解析后的主题。
- `document.documentElement.dataset.themeSetting`：`system`、`light` 或 `dark`，这是用户的选择。

插件样式使用宿主的自定义属性，不要写死颜色：`--bg`、`--surface-1`、`--surface-2`、`--surface-3`、`--border`、`--text-primary`、`--text-secondary`、`--text-muted`、`--accent`、`--accent-on`、`--success`、`--warn`、`--danger`、`--focus-ring`、`--radius-sm`、`--radius-md`。深色主按钮的文字用 `--accent-on`。色值见 [架构 v2](architecture-v2.md) 的墨色主题一节。

## 示例插件

`plugins/v2/Workbench.TestCases.V2` 是「测试用例 · 示例」的 v2 证明：

- 三行预置数据和 v1 示例相同：EX-001、EX-002、EX-003。
- 可以改步骤和预期，保存到 `cases.v2.json`（`schemaVersion: 2`）。
- 导出写 `sample-test-cases.csv`，格式与 v1 示例的本地 CSV 相同。响应只含文件字节，不含路径。
- 切换项目后读写另一份数据。
- 没有网络、webhook 或发送。

v1 的 `plugins/Workbench.TestCases.Sample` 继续给现有 WinUI 宿主使用。

另外三个预览插件使用同一份清单规则，不增加字段：

| 插件 | id | 数据文件 |
| --- | --- | --- |
| UPCAM 站会日报 | `com.projectworkbench.upcam-standup.v2` | `digest.v2.json`，schema `workbench.upcam.digest.v2.preview` |
| 钉钉 Insights | `com.projectworkbench.dingtalk-insights.v2` | `chat.v2.json` 与 `insights.v2.json` |
| 测试设计 | `com.projectworkbench.testing-design.v2` | `dashboard.v2.json`，schema `workbench.testing.dashboard.v2.preview` |

站会记录和聊天导出都由页面读取本机文件后把正文交给回环服务，服务不浏览任意路径。样本里的负责人是角色，不是人名。`autoSend` 仍必须是 `false`。钉钉的 `send` 动作固定返回 `SendDisabled`。测试看板的每条运行都写着人工验收「未验收」。
