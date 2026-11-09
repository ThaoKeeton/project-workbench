# Windows 构建与原生验证

证据分层记录。Linux Core 行为测试、跨目标 C# 参考编译、Windows 发布、真实 WinUI 自动冒烟和 Windows 人工验收各有不同范围，不互相替代。

## 2026-11-09 当前已核对批次

[PUBLIC Actions42559172825](https://github.com/ThaoKeeton/project-workbench/actions/runs/42559172825)绑定功能源码 `b1a3e9c9963c2d75798a6068f99a6270d81bd074`，两个标准job全部成功。Windows Core1561通过/0跳过；Linux1558通过/4项Windows专用检查明确跳过（两项DPAPI、两项扩展路径实际加载）。Windows实际执行并通过259/261字符边界的私有依赖初始化、扩展路径/重解析拒绝测试；项目B初始化成功。两项真实manager取消/期限顺序测试在两端通过。

独立SourceAcceptance安装在当前shipping EXE中完成seed/verify/stop-figma/stop-ai四个不同实际进程，共36项原生检查；原生PID与Int64出生身份、真实Closed、独立启动器正常exit0及全部根租约匹配。7个先捕获的held释放能力、13份实际来源/回执/人工用例记录、重启前后字节与记录身份均已核对。现有严格验证器另重放实际记录正文，确认选择/trace、回执先行、人工步骤和无取消后发布。它使用真实控件/Core/Windows DPAPI，但凭据、HTTP和数据均是合成替身；没有真实provider或Webhooks/钉钉消息。

其他原生启动、管理/重启、项目空间/恢复、已安装Testing本地流程、DingTalk+UPCAM51项/12个实际进程、owned27项均通过；owned另审计23份journal、13份原生构建、11张可解码校准BMP，GdiBlock合成失败为0个StepResult并完成退役。完整owned面板逐项点击、任意用户电脑、真实账号、Windows11人工主题/IME/DPI/选择器/无障碍仍待验证。真实Figma/OpenAI账户与订阅代理未执行。

七份原始下载均匹配GitHub API SHA256，且已安全解压、交叉核对实际宿主与载荷：

| 产物 | artifact ID | 原始下载SHA256 |
| --- | --- | --- |
| workbench-win-x64 |13649448205|`fad691b874f66bf1ef522f5be38e9aa12edb81b69a0000da63609351409a1139`|
| workbench-business-plugins |13649572382|`6399a0f3919baf2c31277900926e5da9b4440d30e91e04b54871166bf3f609f7`|
| workbench-testing-plugin-0.2.0 |13649496784|`2e405454e52c08c6d9f163e2af72c2258ef6ff80fecf5d63e58ae57442c82abe`|
| native-verification-evidence |13649680974|`2534059f6c3414b37baf343fbef7d61ab06cf2a33a40dd142225849142936a6a`|
| workbench-sdk-0.4.0 |13648499828|`d1373f8c7c8e298c443b8cc54bacb318558d56c44363f00a76f1a614498efd5c`|
| workbench-ui-gallery |13648599630|`6a337f0287bb14a48dff37656a5dc3ecc67120bb281c4d55e9dd9e78b2b7490a`|
| core-linux-results |13648508800|`d4781265c7920c92f7842bee98b22251a094571d6dd340e014afbf200af1333f`|

[完整宿主](https://github.com/ThaoKeeton/project-workbench/actions/runs/42559172825/artifacts/13649448205)90,666,891bytes；实际EXE `9ce9b9c2d9d112edcf41e0487e9200bf8d54b2f33abde45f32833d928661efc7`、DLL `34b57b9cdbfa7d616c5eed6bbc4234349b0947643d3c836333f8f5e3c65a94f5`，ProductVersion0.4.0+b1a3e9c、FileVersion0.4.0.0、AssemblyVersion1.0.0.0。目录实际含.NET10.0.14运行时。SDK0.4.0的两份NuGet/11份源码、Testing生产8/夹具10及共同7项字节/245份Git源码记录、实际PE/依赖/Production helper/当前宿主配套均核对通过。测试内层ZIP `c12a7945629c558138e3ee46642edaa37254e771571888f9850c0082de53f103`；来源夹具内层ZIP `eaad414132ccee5eb83d76c6077d27048746801be005d7caa4014c57a7ccf702`。

[业务配套](https://github.com/ThaoKeeton/project-workbench/actions/runs/42559172825/artifacts/13649572382)仍固定DingTalk0.3.2/UPCAM生产0.1.0（共享core0.4.3），不含正在另行修复的UPCAM0.1.1诊断进度/取消问题。旧包的51项不能充当新包验收；后者须单独核对新版本/源码/哈希并在实际宿主检查。安装与数据路径见[当前操作说明](install-business-plugins.md)。

当前24张原始PNG已逐张解码并核对哈希/尺寸/当前EXE/进程绑定；协调助手仅查看home与upcam-preview两图，其余没有声称逐图查看。25行清单的唯一不可用项仍是独立Docs页，替代为项目上下文编辑器。这些是实际WinUI内容渲染，保留透明图层、裁剪和上轮所列限制，不包含OS背景、标题栏、弹窗，也不构成人工外观验收。UPCAM图为虚构预览，合成acceptance的sent不是真实送达。

保留42539771658、42544171178、42549570595、42555126213各次失败原件：由早期来源断言、B操作原因不可见、明确初始化失败，推进到加载器修复后重试控件表面错误。b1批次是后续实际通过，未改写早期失败。协调器下载后审计另保留PowerShell缓存启动失败和图集误把总25行当24行的检查错误；仅修正本地审计环境/索引分类，无产品测试或Windows批次重复。

产物保留一天，Linux到2026-11-10约04:03UTC、其余约04:21UTC，以Actions页面为准。功能程序始终指b1a3e9c；后续文档提交不被当作新二进制。没有创建release、部署、修改账单或删除产物。Windows11用户人工验收继续单列。

## 历史655d7b6批次及后续过程记录

下列“本轮/当前/待验”均属于相应标注的历史版本；最新结果以上方b1批次为准。

2026-11-08 已核对 [PUBLIC Actions42518602297](https://github.com/ThaoKeeton/project-workbench/actions/runs/42518602297)，功能源码 `655d7b6496067daad7c5e4cefb1f1da1b8379536`：两个 job 成功，Windows Core1423通过/0失败/0跳过；Linux1422通过/0失败，另2项真实 Windows DPAPI 测试明确跳过。取消先于期限与期限先于取消的两项真实 manager 测试在两端通过。原生启动、运行管理/实际重启、项目工作空间/恢复、已安装 Testing pass/failure/cancel/恢复、DingTalk+UPCAM51项/12个不同实际PID+出生身份、owned驱动27项均通过。后者另核对23份实际journal、13个原生构建镜像和11个可独立解码校准的真实BMP结果；GdiBlock是明确合成的捕获阶段挂起，0个StepResult，进程/IO/callback回收及租约检查通过，不声称真实OS死锁。

[24张原始内容图](https://github.com/ThaoKeeton/project-workbench/actions/runs/42518602297/artifacts/13633043095)已下载核对并由协调助手逐张查看：UPCAM preview与三个Testing页面均属本轮同一源码；25行清单中唯一 unavailable 是没有独立导航页的Docs，其替代为项目上下文编辑器。图集原ZIP1,520,907bytes、SHA256 `85927523773e0ccc8f88b444df0bf999c9e66336f064c1212626fc3124edf501`，保留原透明通道，未拼接/修图。不含OS背景材质/标题栏/独立弹窗；透明导航和部分选中项文字可读性有限，部分页面只显示当前视口。虚拟证据内BMP明确SIMULATED且不支持的字形为问号，顶部保存/关闭警告为故意失败场景；owned面板图为空闲状态，不证明面板已执行。UPCAM acceptance的sent/acknowledged为本地fake，不是真实投递。协调助手查看不改写原index的reviewedByHuman=false，更不等于用户Windows11人工验收。

七份下载产物均匹配GitHubAPI声明SHA256。当前[完整宿主](https://github.com/ThaoKeeton/project-workbench/actions/runs/42518602297/artifacts/13633291911)ZIP90,592,610bytes、SHA256 `eee78758d3f38e94abe81a6c13f3252b76ce02c00f13375dd6d925e8dd6a9db4`；实际EXE SHA256 `48598e37f84e1f0ca60b0fadaa33a540876d5b83b6db08da593d8b48e33c35ac`、App DLL `25aa56d72caf6b137250b7be75d4e5c5f1e49a3ed4339081f50a7e209dbfd69b`，与图像/启动器/插件报告匹配。SDK0.4.0两个NuGet、11份SDK源文件、正式Testing0.2.0八文件包及232份当前源码记录、两个业务生产ZIP和安装快照已分别核对。[原生证据](https://github.com/ThaoKeeton/project-workbench/actions/runs/42518602297/artifacts/13632564836)保留原始报告/身份/失败场景。[业务插件配套](https://github.com/ThaoKeeton/project-workbench/actions/runs/42518602297/artifacts/13632778983)仍是钉钉0.3.2/UPCAM0.1.0，安装方式见安装说明。产物只保留一天，以Actions页面有效期为准；没有创建release或部署。

本轮全部业务验证使用隔离合成数据与本地替身，没有真实Webhook、钉钉发送或provider请求。在该655d7b6历史批次，SourceAcceptance独立十文件包及其激活/源控件/post-Stop释放通道尚未完成；通用用户电脑控制、真实provider、完整owned面板点跑和Windows11键盘/IME/DPI/选择器/无障碍等仍分别待验。下方历史段落的待验状态属于各自标注版本，不覆盖以上已核对范围；用户人工集中验收继续后置。

当前 gallery 源码只在既有隔离合成探针的断言之后，被动渲染实际显示的 `MainWindow.Content`（宿主导航、页面和插件视图）。它不含 OS 标题栏、独立对话框、桌面或其他窗口。每张候选图由真实 WinUI RenderTargetBitmap、WinRT PNG 编码/解码产生；收集器核对启动器独立观察的 PID/出生与退出 FILETIME、当前 EXE/源码/安装插件哈希、原探针报告、PNG 字节/尺寸/非空像素及固定页面清单。无桌面、失效页面、失败原探针和不存在的 Docs 独立导航页在索引中明确列出；Docs 的可用替代是项目上下文编辑器。Testing 虚拟 SIMULATED BMP 即使出现在真实宿主 PNG 中，仍不成为实际设备截图。Linux 参考编译本身不产生 WinUI 图像；本次图像来自上述同源码 Windows 2025 运行，协调助手已检查当前内容与裁剪限制，用户人工外观验收仍独立待办。Windows 11 的键盘/IME/DPI/选择器/无障碍与真实 provider 仍单独待验。

业务 seed/acceptance-seed 的启动器在确认该进程退出时，将当时实际安装的两个插件各自的 `plugin.json` 与入口 DLL 保存到报告旁固定的 `*.gallery-packages/`，另附绑定 phase、PID/出生/退出、源码、宿主 EXE/DLL、原报告及 publisher ZIP 哈希的 `identity.json`。仅这四个有大小上限的身份文件被复制，不含设置、数据、密钥或完整插件树，也不会被加载；文件以 CreateNew 写入并整目录发布。收集器核验全部快照文件、独立启动器绑定、原报告、实际宿主与原 publisher ZIP，拒绝缺失、改写、重复、额外文件及 reparse/path escape。后续更新/移除不再令有效 seed 图像依赖当前可变安装目录；任何快照/发布失败均保留原始证据与原探针失败结果。现有 business 原始证据路径同时保留这些有界身份文件，无需新增 artifact。

PNG 编码使用 RenderTargetBitmap 的 premultiplied BGRA8 输入约定，保留实际内容透明度；[UWP GetPixelsAsync 文档](https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.media.imaging.rendertargetbitmap.getpixelsasync)明确该 alpha 约定，[WinUI 文档](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.imaging.rendertargetbitmap.getpixelsasync)说明 BGRA8 返回值。OS/窗口 backdrop 不属于此内容渲染，透明区域不会被人工填背景。非聚焦路径的 Task.Yield 只让出 dispatcher，不保证 compositor 已完成一帧；本次逐图查看仍见导航透明区和部分选中项文字可读性限制；是否涉及 compositor/材质仍未归因，需完整窗口/Windows11人工外观验收。alpha 修正不构成完整窗口/导航视觉验收。探针直接启用贡献后，会在选择真实菜单目标前刷新当前贡献，再通过原导航路径创建一次实际视图；普通 UI 导航行为不变。

聚焦截图先沿实际控件、当前页面直到精确 MainWindow._root 的完整可见祖先链确认归属，保留 loaded/XamlRoot 检查；存在根内 ScrollViewer 时仍选择最近的实际 scroller。原非动画 ChangeView、实测偏移/视口判据、ViewChanged/LayoutUpdated 观察者及每次定位/恢复最多 2 秒的期限不变。只有完整链到达精确 page/root 且没有根内 scroller 时，才返回非空的精确 root 作为固定视口锚点；无效链仍返回 null，不读取根上方父级作为 owner，不包装或重设页面父级。SDK `WinUiView.CreateView` 接受 `FrameworkElement`，未要求插件提供 ScrollViewer。

固定视口以实际 root 尺寸、focus/page 在 root 坐标中的变换后边界，以及同一 focus→root 链上每个 UIElement.Clip 的矩形边界逐次取严格正面积交集。clip 的自身 Transform 先作用于矩形四角，再按元素到 root 的变换求边界，避免中间轴对齐框放大旋转 clip；非有限数、空范围、边缘溢出、完全裁剪/离屏、脱离或变换测量失败均阻挡发布。正面积的部分重叠可接受，单纯边缘接触不接受；这只证明内容渲染的变换边界重叠，不证明 compositor/OS 可见性或每个字形可见。固定路径不调用 ChangeView、不增加定位/恢复等待、事件订阅或重试。

渲染/取像素/编码的各既有等待检查点都重新解析并要求同一个锚点，固定↔滚动或 scroller 替换均失效；窗口、PID/出生、runtime/代次、desktop 与当前页面检查继续执行。编码完成后，滚动路径仍先在同一 live scroller 恢复原偏移，再核验身份并同步 CreateNew 发布 PNG；固定路径不移动恢复，最终 post-restore/pre-publish 仍必须重新证明视口重叠。收据格式、原因词汇、原有字节/尺寸限制及渲染时视口/偏移字段不变，PNG 发布后没有捕获失败 await。捕获范围仍为当时真实内容视口，不是整页拼接或完整窗口。

保留 run42493381663 的三项 focus-outside-viewport RED、run42502994608 源码8c03的三张 Testing PNG，以及 [run42514189444](https://github.com/ThaoKeeton/project-workbench/actions/runs/42514189444) 源码 `b6b006e0fb1733ce20032e5e359612a794c59c56` 的实际 23/24 可用图片结果。后者的原 UPCAM blocked/focus-detached 收据与 sidecar 同属 PID3112/出生 FILETIME134386397624357478：完整可见链到 pageDepth7/rootDepth21，根内最近 scroller 为 -1，首个根外 scroller 还需向上 5 层且始终不具资格。业务探针已成功并关闭，拒绝的 UPCAM PNG 不存在；这给出原 resolver 错误要求 scroller 的实际边界。固定视口支持已在上述源码655d7b6/PUBLIC run42518602297完成此门槛：同轮UPCAM及三个Testing原图、实际宿主/插件字节和PID/出生/原报告绑定均匹配，24张已逐一查看；旧b6b006e的绿色workflow加23张仍保留为当时部分完成。Linux 几何测试和完整四参数 NativeReference 编译不证明原生 UI 已修复或产生了图片，Windows 11 人工验收仍延期。

focus 诊断仍仅限既有隔离 `BusinessProbe=seed`、`upcam-preview`、`view/com.upcam.task-digest/digest`，且最终原收据为 `blocked/focus-detached` 时，尽力以 CreateNew 写入 `ReportPath` 旁固定的 `upcam-preview.focus-diagnostic.json`；它位于 `gallery/business-seed` 之外，现有 business-probes artifact 已包含此目录。成功的固定/滚动截图均不写 sidecar，也不增加诊断遍历；固定几何遍历是视口行为检查。写入失败不会改变原探针或收据结果。schema 1 的固定标量 JSON 最多 8192 UTF-8 bytes、深度 8，只含固定枚举/布尔值、已验证的 40 位小写源码提交、正 PID/有效出生 FILETIME，以及首次和最终 focus 失败的 phase/site/check、nullable sawPage/sawRoot、pageDepth/rootDepth/nearestOwnerDepth/hiddenDepth/depthCapped。深度 -1 表示未见，0 表示 focus，超过 255 饱和并置 depthCapped；未执行比较的 saw 标记为 null。firstFailure 可空，terminalFailure 必需；positionEntered/positionReturned 记录定位调用边界，回调的暂时失败不会自动成为最终失败。最终快照在 finally 恢复前冻结，诊断不读取控件文本、名称、Header、Password、AutomationId、动态类型名或 provider/profile 值。

旧 `root-reached-owner-missing` 分支的根上方补充采样已随固定视口支持移除，新诊断的 aboveRootResult/VisitedCount/OwnerDistance 为 not-sampled/0/null。schema 1 与下列枚举保留，仍可解读 b6b006e 原 sidecar 的 found/5/5（旧上限 16、未找到距离为 null、capped/unavailable 保留未测部分）；旧 check9 仍表示当时已到 root 却缺 scroller 的拒绝，不改写历史含义。sidecar 只补充同次源码/PID/出生上下文，未扩展 PNG 收据真实性契约。run42507465993 的原 blocked 收据及 recon 也保留；Linux 标量序列化/IO 测试和完整参考编译不能证明运行时树或新增 PNG，Windows Server CI 也不等于 Windows 11 人工验收。

sidecar 数字枚举的固定解码表（不接受其他数值）：

| 字段 | 数值 → 含义 |
| --- | --- |
| phase | 0 required-input；1 initial-owner；2 position-live；3 post-position；4 pre-render；5 post-render；6 post-pixels；7 post-encoder；8 post-flush；9 post-decoder；10 post-read；11 restore-live；12 post-restore；13 pre-publish |
| site | 0 required-null；1 initial-owner-null；2 focus-attached-helper-null；3 focus-attached-owner-changed |
| check | 0 not-called；1 root-unloaded；2 page-unloaded；3 focus-unloaded；4 root-xamlroot-null；5 page-xamlroot-mismatch；6 focus-xamlroot-mismatch；7 ancestor-hidden；8 root-reached-page-missing；9 root-reached-owner-missing；10 owner-unloaded；11 owner-xamlroot-mismatch；12 root-not-reached；13 owner-returned；14 unavailable（仅诊断收集异常） |
| aboveRootResult | 0 not-sampled；1 found；2 chain-ended；3 capped；4 unavailable |

受控执行页面已分阶段加入生产 TestingPage 源码，使用真实 OwnedPilotController/私有 journal 和固定生产 Win32 工厂。新增可移植测试链接实际 admission/bridge/lifecycle/evidence 实现，组合真实 Core/supervisor 与明确标识的 scripted owned transport，并保留源码、原始 RED/GREEN、参考编译和长度/哈希审计。它们不实例化 Windows/XAML/PRI/dispatcher/BitmapImage，不证明实际 UIA/GDI/job 或宿主根租约重新取得。页面晚到 Loaded/已捕获 Changed 的永久关闭路径已在源码中封闭并经完整参考编译；真实 WinUI 到达顺序仍待 Windows 验收。

当前仓库已公开，用户已授权标准 public runner Actions；历史付款/支出注解不再表述为当前 CI 启动阻塞，也没有改动账单。协调器读回的 [42488628270](https://github.com/ThaoKeeton/project-workbench/actions/runs/42488628270)（源码 `53ae30b`）Windows Core 1305 通过、业务 51 项/12 个实际进程通过，但 native/gallery/owned 仍失败，15 张原始主图并非收集器接受的 gallery。后续 [42493381663](https://github.com/ThaoKeeton/project-workbench/actions/runs/42493381663) 使用独立固定源码 `93be542`：Linux 成功（1304 通过、2 项明确 Windows DPAPI 跳过、0 失败），Windows Core 1305 通过/0 跳过/0 失败；发布、包存储与交付、smoke、runtime/真实重启、workspace/恢复、已安装 Testing pass/failure/cancel/恢复、业务 DingTalk+UPCAM 51 项/12 个实际 OS 进程及 strict gallery 步骤均成功。Windows job 仍因第17步 OwnedFailureMatrix 的 GdiBlock 断言失败；之后的 SDK 与仅成功时执行的最终交付复核被跳过。协调器已下载并重算该轮 gallery/native-evidence ZIP 字节哈希；gallery index 接受21张 PNG/共25行，另3项 testing-* 因 focus-outside-viewport 阻挡、docs-page 为 skipped-unavailable。PNG 保留透明内容范围，不能宣称全窗口/OS 背景/标题栏/弹窗、完整 Light 外观或 Windows11 人工通过；完整绑定与图像审计另由协调器保留。此结果不验证正在开发的 SourceAcceptance 夹具。all27 standalone、历史 9/27 CLR abort UNKNOWN、完整已安装 owned 面板、当前最终交付/SDK复核、干净生产 StepStarted 崩溃、维护和独立 SourceAcceptance/实际合成 DPAPI 仍分别待验；Windows11 人工验收延期。

0.3 项目工作区新增 `scripts/project-workspace-probe.ps1`：源码构建后在两个实际 shipping EXE 进程中依次运行 `--workspace-probe seed|verify`，根只允许 `%TEMP%/ProjectWorkbench-workspace-probe/<GUID>/`。原生报告由实际 Closed 事件写入；启动器独立核对 PID/phase/root、OS exit 0、实际根租约重新取得及完整检查列表，再允许第二进程。检查使用真实项目对话框/选择器、文本框、主题与凭据控件和文件，包含旧准备挂起、候选停止挂起、回滚新 manager 绑定、草稿、归档和重开。固定 NativeFixture 位于 `artifacts/native/fixtures/runtime`，不进入用户主程序或业务伴随包；普通下载没有此源码测试依赖。新增源码的 Linux 聚焦行为/参考编译不构成 Windows 原生通过，需控制器核验同源码实际 CI；当前 workspace-probe 证据位于 artifact `native-verification-evidence` 的 `artifacts/native/workspace-probes/`，旧独立名称 `project-workspace-probes` 仅为历史。原有 native14、management17+theme1、platform7、business12进程51项继续保留。Windows 11 人工、真实独立 GUI/更高版本迁移/provider 验收仍延期，当前不要求用户测试。

Task4 round1 增加实际 WM_SYSCOMMAND/SC_CLOSE：真实切换在旧 hook 挂起时关闭、已接纳原生 action 挂起且后排目标待运行时关闭、Initialize 前关闭后再调用初始化。前两项核对真实 factory 启动阶段和目标插件实例文件不增加、修正/原生重试关闭、实际 Closed 与租约释放；action 用例还保留一次真实主题写入故障，防止提前封闭准入绕过失败偏好。seed 报告现在必须包含原9项加这3项共12项，verify 仍3项；辅助 startup JSON 只记录实际启动观察，不是成功判据。新增 native RED/GREEN 在 Linux 均未执行，不能把参考编译或 portable callback 测试当作原生调用方通过。最终仍需 Closed、OS exit0、重获租约的共同证据。

| 检查 | 已有证据 | 范围与限制 |
| --- | --- | --- |
| Task 3 Linux Core | 一次完整运行 221 通过、2 项明确 Windows DPAPI 跳过，共 223 项；随后新增 OS 进程用例的最终聚焦运行 12/12 通过 | 聚焦运行包含 6 个实际子进程，验证通用 opt-in Restore、RequiresPluginStart 不恢复、停用后不复活；另含 HostRuntime 持锁/Stop 重试、Apply 顺序、坏元数据独立根及只读凭据状态。未把更新后的完整套件宣称为已重新运行；TRX 在 Task 3 报告 |
| Task 3 Linux C# reference | App 与独立 NativeFixture 参考编译 0 warnings/errors | `tests/Workbench.NativeReference` 仅为不可运行的类库；InitializeComponent 占位直接抛异常，没有生产入口/资源替代；不验证 SDK 生成 Main、XAML、主 PRI 或实际 WinUI |
| Task 3 Windows/SDK native（Task 4 BASE） | [42285023349](https://github.com/ThaoKeeton/project-workbench/actions/runs/42285023349)，提交 `27154465c08d132798c8ea7b637f137f7340a90b`：Windows Core 226 通过、原生 14 项、management 10 项、六个连续实际进程 7 项、默认 Windows SDK source/NuGet export 均通过 | 控制器已检查实际报告；该结果只证明 Task 4 之前的 BASE，不是下述新增独立业务包探针的 Windows 执行证据 |
| Task 4 历史 Linux Core/旧业务包 | [42295396164](https://github.com/ThaoKeeton/project-workbench/actions/runs/42295396164)，源码 `ccb6ef2`：237 通过、2 项 Windows DPAPI 明确跳过，共 239 项；16 项脚本检查及两个真实生产 ZIP 的逐文件审计通过 | Linux 不执行 WinUI 或业务程序集；参考编译仅为不可运行的类库 |
| Task 4 历史 Windows/旧业务包 | 同轮源码 `ccb6ef2`：Windows Core 239/239（含两项真实 DPAPI），发布、原生 14 项、management 10 项、六个连续平台重启进程 7 项、SDK 默认 Windows export，以及真实生产业务包六进程 26 项均通过 | 当时使用旧钉钉 0.3.1，且只有生产 UPCAM 缺授权拒绝路径；其结果不代替下列最新源码正向验收 |
| 历史 `a54a668` Linux Core | [42307917231](https://github.com/ThaoKeeton/project-workbench/actions/runs/42307917231)，源码 `a54a668`：265 通过、2 项明确 Windows DPAPI 跳过，共 267 项；脚本检查通过 | 不执行 WinUI 或 Windows DPAPI |
| 历史 `a54a668` Windows 平台/SDK | 同轮 `a54a668`：267/267（含两项真实合成值 DPAPI）、发布、原生 14 项、management 17 项、六阶段平台重启 7 项；默认 Windows SDK 导出/完整性通过 | Server 2025 实际 EXE；9 份源码、2 包 README/哈希、真实 SDK PRI 704 bytes；保留独立 Windows 11 人工门槛 |
| 历史 `a54a668` 独立业务包/正向验收 | 同轮当前发布目录 EXE：钉钉 0.3.2/UPCAM 0.1.0 的六生产进程 27 项，另一个隔离根的独立 UPCAM acceptance-ID 后六进程 24 项，共 51 项 | 全部实际 WinUI Closed、OS exit0、退出后重获 lease、来源/包哈希/PID 一致；定时 Core 和本地 fake providers 通过，真实送达未测试 |
| Linux Core Release | 86/86，通过；包括真实临时目录、独立插件 DLL、自包含子进程 | 不覆盖 Windows 文件系统分支或原生 UI |
| Linux C# 检查（历史基线） | 早期手动主程序版本参考编译及 7 个 C# 文件语法/XML 检查通过 | 后续不可运行的 NativeReference 语义编译已单列；生产 XAML/PRI 工具仍必须在 Windows 执行 |
| Linux 正常原生构建 | 按预期受阻：MSB6003，`MakePri.exe` 的 Exec format error | Windows 原生工具无法在此 Linux 环境运行，未修改生产目标绕过 |
| Windows Core 测试/发布（历史基线） | [第八轮分支 CI](https://github.com/ThaoKeeton/project-workbench/actions/runs/42209929309)：Windows Core 86/86，自包含主程序与两个独立插件包构建通过；发布的 `Workbench.App.pri` 为 1,352,680 bytes | 提交 `d615fa064c8962ed849b494b8b42d1ca54e67b0a`，使用 `windows-2025` runner |
| Windows 原生自动冒烟（历史基线） | 同轮 `succeeded=true`、`stage=completed`，原有 11 项检查全部通过：原生启动、占用根失败窗口、实时主题、4 个页面/设置控件、文件命令、停用/重新启用及关闭 | 没有编辑/导航/重复生成回归，不能证明该行为安全；自动检查不代替人工验收 |
| Windows 编辑回归 RED | [第九轮分支 CI](https://github.com/ThaoKeeton/project-workbench/actions/runs/42213219634)，提交 `9e97dc6b596d3821803189b0c55d112f2be9ba48`：实际原生进程 exit 1，`Summary unsaved draft was lost after navigating through home.` | Windows Core 86/86、主程序与独立插件编译发布成功后，在真实 TextBox 编辑/新工厂导航断言失败；不是编译或初始化故障 |
| Windows 本轮构建 GREEN | [第十二轮分支 CI](https://github.com/ThaoKeeton/project-workbench/actions/runs/42216264601)：Windows Core 86/86，主程序与两个独立插件发布通过，主 PRI 1,352,680 bytes；两个 job 均成功 | 实测代码提交 `a5d9825a9662710ff10dd833e0f9d53cfeaf8cdd`，使用 `windows-2025` runner |
| Windows 本轮编辑回归 GREEN | 同轮原生进程 exit 0，`succeeded=true`、`stage=completed`、UI 线程 2，全部 13 项通过；最终摘要 100 bytes、CSV 293 bytes | 包含新工厂导航/真实编辑/保存/拒绝覆盖/停用恢复；原有 11 项检查保留 |
| Windows 人工验收 | 尚未执行；下方清单全部待验 | 主题外观、DPI、文件选择器、可访问性及真实重启仍需实机 |

历史 [CI 42307917231](https://github.com/ThaoKeeton/project-workbench/actions/runs/42307917231) 的两个 job 均成功，实际运行源码为 `a54a668d3f2c98fda7ab826ed3cf72b9c64a2b06`。生产阶段 seed/restart/update/fault/rollback/remove 的应用 PID 为 3532/6348/1404/8644/6564/9052，检查 6/5/5/3/5/3，共 27 项；独立 acceptance-seed/restore/stopped/config/missing/disabled 的 PID 为 2664/9196/8804/6100/1792/6836，检查 5/5/3/4/4/3，共 24 项。12 个进程均已实际退出，OS exitCode 0、WinUI Closed、启动器重新取得 host lease、来源与哈希核验全部通过。两组使用不同全新 GUID 根，前组及其 worker 全退役后才启动后组。

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

使用受支持的 Windows 11 24H2+ x64、PowerShell 7、仓库 `global.json` 所需的 .NET 10 SDK，以及可由 `vswhere` 发现的 MSVC x64 C++ 工具和 Windows SDK。IDE 可选；若使用，需 Visual Studio 2026 18.0+；CLI 可使用带上述组件的 Build Tools。Windows App SDK 的 NuGet 构建依赖不替代自带 C++ helper 所需的实际 `cl`、`rc`、`link`。API 目标 19041 只是调用 API 下限，不承诺 Windows 10 已获支持或验收。

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
| `core-linux-results` | `artifacts/tests/` | Linux TRX |
| `native-verification-evidence` | `artifacts/native/` 下的 smoke、runtime、workspace、testing、business、owned、debug 与 gallery 原始报告/日志/收据，以及 Windows TRX 和生成源码 | 保留原探针失败与诊断；不含完整应用或插件交付树 |
| `workbench-ui-gallery` | `artifacts/native/genuine-gallery/` | 仅索引有效时上传；固定 roster 的已核验 PNG 与 blocked/skipped/unreached 行，待人工看图 |
| `workbench-win-x64` | `artifacts/native/app/` | 仅成功运行时的完整自包含目录 |
| `workbench-testing-plugin-0.2.0` | `artifacts/native/testing-plugin/` | 仅成功运行时的独立安装包 |
| `workbench-sdk-0.4.0` | `artifacts/sdk-snapshot.zip` | 仅成功运行时的固定 SDK 快照 |
| `workbench-business-plugins` | `artifacts/native/business-plugins/` | 仅成功运行时的固定业务包 |

如果 Core、发布或原生启动失败，job 保持失败状态；上传已有产物供排查，缺少必需报告不会被解释为通过。

原生冒烟失败后才运行 `scripts/native-debug.ps1`，默认最多采集 240 秒；如需安装官方调试器，仅安装调试工具组件并验证 Microsoft 签名。采集只发布相关文本，不生成或上传进程内存 dump，不执行堆遍历或导出完整环境。调试器及被调试程序启动前清空继承环境，仅保留明确允许的 OS、运行时和代理配置，不继承 CI token。调试采集不修改原始失败判定。第六轮通过这些文本定位了 InfoBar 默认样式资源加载失败；第七轮补齐主 PRI 发布后完整冒烟通过。当前成功运行会跳过调试采集；尝试后产生的部分诊断统一进入 `native-verification-evidence`。应用目录仅在全任务成功时作为交付上传，失败报告仍在验证证据中。

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

报告 `artifacts/native/runtime-probes/<phase>.json` 包括该进程 PID、临时根、检查列表和真实启动顺序；stdout/stderr 与 summary.json 同目录。超时视为失败，runner 保留尚在运行的隔离进程并报告 PID，不以强杀证明停止。当前 CI 收进 `native-verification-evidence`。后台/凭据原生行为、Windows 文件锁分支与 DPAPI 只有实际 Windows 运行成功后才算通过。

SDK artifact `workbench-sdk-0.2.0` 另行导出 manifest、两个 0.2.0 nupkg 和固定版本源码；完整重建/消费步骤与 Linux ReferenceOnly 限制见 [独立插件契约](integrations/plugin-contract-v1.md)。Task 3 不导入真实 UPCAM/钉钉包；真实 RequiresPluginStart 监控仍须遵守其业务自己的新预览/停用撤销策略，不能用通用夹具 Restore 结果冒充业务授权。

Linux 参考命令（只编译 C#，不运行应用）：

```sh
dotnet build tests/Workbench.NativeReference/Workbench.NativeReference.csproj -c Release -p:OwnedLabReferenceOnly=true \
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

业务探针的 `artifacts/native/business-probes/<phase>.json` 和 `summary.json` 由当前 CI artifact `native-verification-evidence` 收集，包含实际 PID、**实际 OS exitCode**、来源提交/ZIP SHA256、合成更新 ZIP SHA256、当前文件/备份/停止状态、已观察 worker 的实际 PID/exitCode、实际 WinUI Closed 事件和启动器退出后重新获取租约的证据。缺失报告、非零退出、PID/来源不匹配或超时均失败；已有原生成功报告也不能覆盖进程失败。超时保留已知进程所有权并停止后续阶段，不强杀来证明正常停止。

当前源码还要求每个业务宿主实例的实际出生身份：原生宿主从自身 `Process.StartTime.ToUniversalTime().ToFileTimeUtc()` 写入正整数 Int64 `processCreationFileTimeUtc`；启动器在等待前保留刚启动的同一 `Process` 的实际句柄并独立读取创建 FILETIME，仅在确认 OS 已退出后读取实际退出 FILETIME/exitCode。完成报告将原生值分别保留为 `nativeProcessId`/`nativeProcessCreationFileTimeUtc`，并写入启动器的 `processId`/`processCreationFileTimeUtc`/`processExitFileTimeUtc`/`hasExited`/`exitCode`。缺失、字符串/浮点/布尔时间、出生不匹配、退出早于创建、非零退出及重复 PID+创建 FILETIME 均失败；已经退出后复用同一数字 PID、但实际出生值不同的实例有效。启动器核对固定顺序的 12 阶段与原有 27+24 项检查、Closed、包来源/哈希及租约门槛，在上传前输出有界的实际 phase/PID/创建/退出/检查数。查询失败保留已知事实及 null，不以 `completedUtc` 或当前时钟代替出生/退出。主报告失败被通用宿主诊断覆盖时，可将原生 `.evidence.json` 中的 PID/出生/phase/root 保留在标记 `diagnosticOnly` 的 `nativeFailureEvidence` 下；该旁证不补填主报告身份，也不能使失败变为成功。

历史 run `42438682555`/`c9b6241` 记录了 12 次串行成功启动、11 个数字 PID，PID1672 出现在 fault 和后来的 acceptance-config；该轮没有出生字段，不能追认为 12 个 PID+出生元组。上述新增源码的实际 Windows 身份观察仍须本轮 CI 验证；Linux 报告回归与 NativeReference 编译只证明格式/拒绝行为和编译。配额未解决时，真实产物下载/字节审计仍延期；Windows Server 自动化也不等于 Windows 11 人工验收。

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

## Testing plugin 0.2.0 development gate

The host0.4/API1 source builds a separately installable Testing0.2 ZIP with exactly eight files, including the private Windows driver and Production synthetic-target helper. It requires actual MSVC/Windows SDK compilation; ReferenceOnly does not produce a ZIP. Existing shipping-EXE `--testing-probe seed|verify` acceptance binds real ZIP SHA256, separate process instances, native Closed, actual OS exit and root leases; `native-verification-evidence` retains original journal/receipt/CSV/SIMULATED BMP bytes and immutable runner sidecars. The synthetic interrupted journal seed is explicitly not an OS crash or executed device action. The standalone owned-lab gate still requires 27 real owned-window checks, separate from virtual evidence and the present native panel’s idle screenshot. See [installation, controls and exact verification limits](testing-plugin.md).

At diagnostic source58f56ef, run42457380425/1 failed without observed runner/steps; ordinary job logs returned404. A later supported commit-check lookup followed by read-only check-annotation retrieval succeeded: both jobs state recent account payments failed OR the spending limit needs to increase. The exact alternative, account amount and available headroom remain unknown; no billing change or rerun was performed. This startup error differs from the five artifact-quota errors retained in historical8692. At that historical run, Core/toolchain/package/native/downstream execution was unavailable and artifacts0 was confirmed. The later public-run authorization and current coordinator observations are recorded above; the old account note is retained as historical evidence, not a current blocker. Historical8692/run42454518257 passed Windows build, freshCore1238 and eight-file installation, then completed9of27 native checks before actual probe exit-532462766; underlying exception remained unavailable. None of those historical counts or hashes is current58 acceptance. The bounded diagnostic source repair passed local real-child regressions and scoped source review only. Those old results cannot close later native27, host/runtime/workspace/virtual/business51checks+12birth-aware instances/SDK/final-delivery or downloaded-byte gates; use the source-bound coordinator results above for current observations. Linux reference checks cannot establish native runtime/PRI success. Windows11 human pickers, IME, DPI, accessibility and real-user-device/provider work remain deferred and unexecuted. Earlier tables and downloaded artifacts on this page retain their historical identities.

以下为第一阶段历史说明；其待打包/待原生验收状态已由本页首段b1结果更新。SourceAcceptance 第一阶段另外编译 `tests/Workbench.Testing.SourceAcceptance`，通过内部 services/friend 委托同一生产 TestingPlugin；固定合成凭据与终端内存 HTTP 不访问 provider。可移植测试链接同一协议/handler/ownership 与真实 Core/store/supervisor，独立入口由完整 NativeReference 的四项 ReferenceOnly 参数编译。它不进入普通 app/Plugins，也未打包/安装；十文件 fixture profile、真实 WinUI 控件/DPAPI 和 Stop 后仍可释放 IO 的原生协议属于后续验收。HTTP 计数仅为夹具进入/返回，不能证明 provider 送达或 store 提交。凭据范围是 runtime userRoot + pluginId；项目工作区以独立 runtime root 隔离，不能把同根/同插件但不同 projectId 元数据的上下文称为独立凭据库。

独立已安装源码控件检查现已接入 `build.ps1 -IncludeSourceAcceptance` 的同轮八/十文件成对构建。Windows 标准 job 的预算上限为35分钟；实际包 profile/公共 package-store 安装通过后才运行 `scripts/source-acceptance-probe.ps1` 的 seed、verify、stop-figma、stop-ai 四个真实宿主进程。原 production Testing、business、owned、SDK 和24图 gallery 门槛均保留；源码通道不生成图片或自动执行生成用例，也不替代这些门槛。上传前重新验证当前 clean source、实际 host 和8/10包的七项共同字节。仍使用两个标准 jobs、原七种 artifact 名称、保留一天；夹具和源码收据复用两条 native-verification-evidence 上传分支。

源码报告必须包含15+5+8+8个封闭检查名及对应项目/实例/代际/计数/记录观察。原生自己的 PID/创建 FILETIME、实际 Closed 和根退役断言，必须与保留 Process 句柄独立取得的 PID/创建/退出 FILETIME、exit0 和所有 base/project host.lock 重获一致，不能由 launcher/gallery 补写。JSON、合成 source/receipt/case 导出有界并逐项校验路径、链接、哈希、精确响应、追溯和手动行为；secret/ciphertext 不进入证据。原生失败 sidecar 与 runner receipt 保留首次失败，观测超时后的 owned CI 进程终止始终计失败。

本地新增 RunOptions、报告/回执/字节拒绝和源码 provenance 回归，以及完整四属性 NativeReference 编译，是可移植/编译证据。Linux full Core 包含 retained bridge/生命周期测试，并显式检查两个 ReferenceOnly profile 不导出交付；这些不能证明 WinUI、实际 CurrentUser DPAPI、Windows 文件共享或正常进程退出已通过。Task3 实现完成前的旧 Windows 运行也不能补作源码通道证据。新的实际 public batch、原始产物下载审计由后续独立验证记录结论；Windows11 人工验收和真实 provider/凭据/权限/模型可用性继续延期。
