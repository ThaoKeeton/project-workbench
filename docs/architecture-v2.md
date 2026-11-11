# 架构 v2 预览

这是一次并行预览，不替换现有 WinUI 3 宿主，不修改公开 SDK 0.4 的 API 1 表面，也不改项目目录 schema 1。现有 `Workbench.App` 仍按原来的方式构建和启动。v2 使用独立的用户根 `%LOCALAPPDATA%/ProjectWorkbench-v2-preview`，避免碰到正式的 `%LOCALAPPDATA%/ProjectWorkbench`。

选择这条形状，是因为网页界面更适合由 AI 持续维护，同一套界面可以在 Linux 上用 Playwright 验收，并且以后同一套界面可以连到另一台局域网电脑上的服务。本预览只实现本机回环。

## 形状

```text
Electron 薄壳（窗口最小宽度 1,100）
        │  只承载页面，不实现插件页面
        ▼
本机服务  http://127.0.0.1:<端口>/api/v2
        │  会话令牌；默认拒绝局域网
        ├─ 项目目录：复用 ProjectWorkspaceCatalog（schema 1）
        ├─ v1 插件：复用 PluginManager.DiscoverAsync，只读状态，不启用、不加载
        └─ v2 插件：权限清单 + C# 后端模块 + 嵌在宿主里的网页
                └─ 本预览只迁了「测试用例 · 示例」
```

桌面壳是 Electron，不再是 WinUI 3 / WebView2。现有 `Workbench.App` 仍是 WinUI 3 宿主，预览不替换它。Electron 只打开窗口并拉起已经存在的 `Workbench.Service`：页面仍是 `web/host` 的 React 界面，接口仍是 `http://127.0.0.1:<端口>/api/v2`。窗口默认 1180×800，宽度低于 1,100 或高度低于 640 时 Electron 会拒绝继续缩小。这和原来外壳恢复里「宽度至少 1,100」的语义一致。网页按 S1 启动器排布：左侧图标轨、顶栏项目名和设备切换、居中的首页。1,100 像素宽时图标轨仍在，不改成另一套导航。

Electron 使用系统标题栏，不把页面画进标题栏，避免盖住 S1 顶栏。深色页面底色是 `#0B0C0E`，浅色是 `#F3F5F7`，用来减少窗口刚出现时的闪色。原来 WinUI 壳会把标题栏按钮刷成同一套墨色；Electron 的系统标题栏是否在 Windows 11 上呈现同样的按钮颜色，还要在真机上看。

## 怎么在 Windows 上跑

需要 .NET SDK `10.0.403` 和 Node.js 22。v2 预览壳不再需要 WebView2 运行时。在仓库根目录：

```powershell
dotnet test .\ProjectWorkbench.V2.slnx -c Release
cd .\web\host
npm ci
npm run build
cd ..\..\desktop\shell
npm ci
npm start
```

壳会用 `dotnet run` 启动本机服务，把随机会话令牌交给页面的 `sessionStorage`，并打开 `http://127.0.0.1:<端口>/`。服务按框架依赖方式运行，需要本机已安装的 .NET 10，不是现有自包含主程序的替代发布。用户根是 `%LOCALAPPDATA%/ProjectWorkbench-v2-preview`。Chromium 配置写在这个根下面的 `electron/`，不写到正式的 `%LOCALAPPDATA%/ProjectWorkbench`。

再次启动时加上 `--attach`，会先读取该根目录里的 `v2-session.json`；健康检查通过就附着到已有服务，否则重新启动。正常关闭时，只有这次由壳拉起的服务会被停掉；附着上的服务继续留着。这个文件只留在本机，含本次会话令牌，不要复制出去。壳拒绝 `--allow-lan`，并且不会把这个参数传给服务。

不打开壳、只看服务：

```powershell
dotnet run --project .\src\Workbench.Service -- --user-root $env:TEMP\workbench-v2-preview --ui-dist .\web\host\dist --print-token
```

`--print-token` 会把令牌打到标准输出。`--allow-lan` 会直接失败，预览不会改去监听其他地址。

Linux 上可以跑服务测试、网页测试，以及 Electron 壳能在无头环境里验证的部分。这不是 Windows 11 人工验收：

```sh
dotnet test ProjectWorkbench.V2.slnx -c Release
cd web/host && npm ci && npx playwright install --with-deps chromium && npm run build && npm run test:e2e
cd ../../desktop/shell && npm ci && npm test
npm run build
root="$(mktemp -d)"
npx electron . --smoke --user-root "$root" --ui-dist "$(pwd)/../../web/host/dist" --smoke-report "$root/smoke.json"
```

冒烟报告只记通过与否和检查代码，不记会话令牌。报告通过只说明这台 Linux 上的 Electron 打开了现有 React 界面，并连上了回环 .NET 服务。

## API 版本

所有预览 HTTP 都在 `/api/v2`。契约版本是 `2.0.0-preview.1`。稳定的插件 API 仍是 `WorkbenchApi.Version == 1`。项目目录文件里的 `schemaVersion` 仍是 1。v2 自己的新文件使用单独的 schema 名，并且 `schemaVersion` 为 2：

| 文件 | 位置 | 标记 |
| --- | --- | --- |
| 项目目录 | `<预览根>/.workspaces/catalog.json` | 现有 schema 1，不扩展字段 |
| 示例用例 | `<项目根>/v2-plugins/<插件 id>/cases.v2.json` | `workbench.test-cases.v2.preview` / 2 |
| 会话 | `<预览根>/v2-session.json` | `workbench.v2-session.preview` / 2 |

调用方必须带 `Authorization: Bearer <会话令牌>`，或者先 `POST /api/v2/session` 换一个 `HttpOnly`、`SameSite=Strict`、路径 `/api` 的 cookie。错误体只有 `error` 代码，不返回异常文本、路径或 HRESULT。

| 方法 | 路径 | 作用 |
| --- | --- | --- |
| GET | `/api/v2/health` | 存活、契约版本、`bind: loopback` |
| GET | `/api/v2/info` | 产品、预览标记、设备「本机」、局域网关闭、自动发送关闭、目录 schema 1 |
| GET | `/api/v2/projects` | 列出项目和当前修订 |
| POST | `/api/v2/projects/active` | `{ id, expectedRevision }`，调用现有 `SetActiveAsync` |
| GET | `/api/v2/plugins` | v1 发现状态 + v2 模块状态、权限和嵌入路径 |
| GET | `/api/v2/diagnostics` | 数量和诊断代码；包含本机窗口最小宽度 1100 |
| POST | `/api/v2/session` | 设置 cookie，响应体为空 |
| GET | `/api/v2/events` | WebSocket。先发 `ready`，项目切换后发 `project-changed` |
| GET/PUT | `/api/v2/plugins/com.projectworkbench.test-cases.v2/cases` | 示例用例 |
| POST | `/api/v2/plugins/com.projectworkbench.test-cases.v2/export` | 本地 CSV，不发送 |
| GET/PUT | `/api/v2/ui/theme` | 界面主题 `system` / `light` / `dark`。文件是 `v2-preview/ui-theme.json`，`schemaVersion` 为 2 |

不兼容的变更要提高 `preview` 版本，并继续留在 `/api/v2` 之外的新路径上，直到有意发布稳定的 v2。不要把新字段写进 schema 1 的目录、上下文或测试存储。

## 插件契约 v2

字段级说明在 [插件契约 v2](plugin-contract-v2.md)。摘要：

- 清单文件是 `plugin.v2.json`，和 v1 的 `plugin.json` 分开。v2 包不要放进现有宿主的 `Plugins/` 发现目录。
- 包由三部分组成：权限清单、实现 `IWorkbenchPluginModule` 的 C# 后端、嵌在主窗口里的网页模块。
- 本预览迁的是仓库里的测试用例示例，新 id 是 `com.projectworkbench.test-cases.v2`。原来的 `com.projectworkbench.test-cases` WinUI 插件保持不动。
- 预置三行仍是 EX-001、EX-002、EX-003。数据写在 v2 目录，不写 `.workbench-project/testing`。
- `autoSend` 必须是 `false`。`message.send` 以及任何未列入允许集的权限都会让模块保持未加载。标记 `sensitive: true` 的权限在本预览里也不授予；以后凡是发送，都要在本机单独确认。

## 墨色主题

网页壳使用「墨色」这一套变量，深色和浅色都有，默认跟随系统。设置页可以改成「跟随系统 / 浅色 / 深色」。选择先写入 `localStorage` 键 `workbench.theme`，再交给 `PUT /api/v2/ui/theme`。这个文件不进入 schema 1。

变量名与代码里的自定义属性一一对应：`bg`、`surface-1`、`surface-2`、`surface-3`、`border`、`text-primary`、`text-secondary`、`text-muted`、`accent`、`accent-on`、`success`、`warn`、`danger`、`focus-ring`、`radius-sm`（6px）、`radius-md`（8px）。生效的主题写在 `document.documentElement.dataset.theme`，取值为 `light` 或 `dark`。选择本身写在 `data-theme-setting`。

嵌在同一窗口里的插件直接使用这些变量，并读取 `data-theme`。不要另做一套颜色，也不要在运行时请求字体 CDN。界面字体是随包分发的 Geist Sans、Geist Mono 和 Noto Sans SC，许可证都是 SIL OFL。

点缀色只用于主按钮、进度和焦点。深色模式下主按钮文字用 `accent-on`（`#0B0C0E`），不用白色。状态除了颜色，还有文字。没有霓虹、大渐变或背景模糊。

Electron 壳把窗口底色设成当前系统主题对应的页面底色。标题栏按钮沿用操作系统自己的标题栏，不再由 WinUI 刷色。本机窗口宽度低于 1,100、高度低于 640 时，Electron 的最小尺寸会把它拦住。诊断接口里的 `nativeWindow.minimumWidth` 仍是 1100，字段名没改，避免网页客户端另接一套。

## 局域网安全（现在只设计，只实现本机）

预览服务只调用 Kestrel 绑定 `127.0.0.1`，并拒绝非回环的对端地址和 Host。`--allow-lan` 直接失败。后面的基线按这个顺序做，没有一项完成之前不要打开局域网：

1. 默认仍只绑定回环。局域网是显式开关，开关状态写进诊断。
2. 设备配对：每台电脑有自己的证书。第一次连接要在两台机器上核对指纹，然后签发短期令牌。令牌和证书私钥不出本机。
3. 传输加密。局域网不再使用本预览这种明文 HTTP。cookie 改为 `Secure`。
4. 凭据继续只放在产生它们的那台电脑上，用现有 DPAPI CurrentUser 保护。远程请求不能读取、导出或代理这些值。
5. 远程可执行的动作要落在权限清单里，并且写入审计（谁、哪台已配对设备、哪个权限、结果代码）。审计不记录凭据、正文或路径。
6. 任何发送（消息、webhook、外部 HTTP 写）都是敏感动作，必须在存放数据的那台电脑上由人确认。自动发送保持关闭。配对和局域网开关都不等于允许发送。

本预览没有配对、没有局域网套接字、没有发送路径。钉钉、UPCAM 和测试插件里的远程来源都不接到这条新服务上。

## 迁移顺序

现有 WinUI 宿主继续可用，直到对应页面在 v2 里达到同样的行为。

1. 已在本预览：本机服务、网页壳、Electron 薄壳、一个示例插件、服务测试、Linux Playwright，以及 Linux 上的 Electron 冒烟。Windows 11 上的窗口、DPI 和标题栏仍要人工看。
2. 项目上下文、主题和插件设置的只读/保存，仍走现有存储，不改 schema 1。
3. 迁工作摘要示例。命令保持本地，不覆盖已有草稿。
4. 测试设计插件先迁只读页面和本地用例。远程 Figma 与模型创作继续保持手动，不在 v2 里默认打开。
5. 凭据页只显示是否已保存。值不进入网页，也不进入日志。
6. 完成上面的局域网基线之后，才允许有人显式打开局域网。发送仍要本机确认。

每一项都保持自动发送关闭。不要为了预览去打开真实 webhook 或消息发送。

## 还要在 Windows 11 上人工看的部分

GitHub 托管的 Linux 任务可以编译 Electron 壳、跑它的单元测试、拉起真实的 .NET 服务，并在虚拟显示里打开现有 React 页面。这些都不能当作 Windows 11 人工验收。真机至少还要看：

- 窗口默认约 1180×800，拖到宽度 1,100 或高度 640 以下时会被系统拦住；高 DPI 下不要比这个更窄。
- S1 布局还在：左侧图标轨、居中的命令条、今日事项和最近插件，插件打开后标签在上方。不要变成顶栏加导航列表加卡片墙。
- 墨色还在：深色底 `#0B0C0E` / `#111317` / `#16191E`，点缀色 `#5B9FFF`。系统标题栏按钮是操作系统自己画的，不再由 WinUI 刷成墨色。
- 用户数据只出现在 `%LOCALAPPDATA%/ProjectWorkbench-v2-preview`，正式的 `%LOCALAPPDATA%/ProjectWorkbench` 没有新文件。
- 关闭由壳拉起的窗口后，本机服务退出；用 `--attach` 打开的窗口关闭后，原来的服务还在。
- `--allow-lan` 让壳直接退出，服务仍然只听 `127.0.0.1`。
- 市场、设备、诊断、设置仍是宿主页面。UPCAM 和钉钉仍只是界面上的合成入口，没有接到这条服务。
