# 平台试用交付完善

## 目的与基线

在 `feat/native-plugin-workbench` 的 `6915b81fb98c133cd601b90251079cd3a22c39a6` 上继续完善用户拿到下载包后的安装、失败处理与实机验收路径。该基线实际 CI42313119980 已通过 Server2025 Windows Core272/272、原生14、管理17、平台重启7、业务12进程51项及原生SDK完整性；Windows11人工验收仍未执行。业务仓库只读，两个已发布独立应用保留。

用户已要求持续推进并自行决定一般技术方案；本设计中的选择由控制器作出，未声称用户逐项批准。草稿PR的明确授权仍待答复，本轮不得重试、合并或部署。

## 选择与取舍

1. 只补上游多层ZIP解压说明：改动最小，但用户仍需下载两个大型上游产物、辨认包装层。
2. 另外提供两个生产插件的配套下载artifact，附安装说明、固定来源、逐文件哈希和实测宿主配对信息：沿用现有独立应用包，用户直接选择根清单ZIP。本轮选择此方案。
3. 把整个宿主与插件放进新的大安装包：重复宿主分发并扩大安装器/启动流程范围，本轮不做。

继续使用 WinUI3/.NET10/WindowsAppSDK1.8.260921001、API1/运行服务1/SDK0.2.0/程序集1.0.0.0。没有SDK或Core维护算法改造。

## 生产插件配套下载

新 `workbench-business-plugins` artifact 只包含：`dingtalk/package.zip`、`dingtalk/provenance.json`、`upcam/package.zip`、`upcam/provenance.json`、`manifest.json` 和 `README.zh-CN.md`。两个ZIP保持已验证字节不变：钉钉0.3.2/c0c1d33/SHA38d7a8f3…、UPCAM生产0.1.0/6370b9d/SHA3e07b7db…；根目录均有plugin.json。不得包括 acceptance 变体、宿主/SDK、独立Desktop程序、实际用户文件或凭据。

导出重用已有完整输入审计，并独立对照仓库固定production来源，不能仅信任可一起被替换的输入ZIP与输入provenance。生产模式核验UPCAM实际plugin-package.json的syntheticAcceptance=false和生产ID。严格ZIP导入不放宽、不猜测包装层、不自动安装/信任/启用。

manifest schemaVersion1包含两个插件的ID/版本/来源提交/上游CI/ZIP SHA256与字节数、输出文件哈希清单和宿主配对信息。宿主sourceCommit从实际Workbench.App.dll的ProductVersion `0.2.0+<40hex>` 读取，另记EXE/DLL SHA256；packagingSourceCommit及dirty状态单独记录，不能用当前Git HEAD冒充旧宿主二进制来源。Windows CI要求宿主与打包源码均匹配当前提交且打包输入干净。说明哈希不等于独立发布者签名。

输出使用本次独有暂存目录，目标非空/源与目标相交/位于app目录/链接时拒绝，失败只清理自己创建的暂存。不得递归清空任意调用者目录。外层GitHub artifact ZIP需要先解压，再在“包维护→导入ZIP”选里面的package.zip；UPCAM原厂包装目录仍可用文件夹安装。

CI在发布宿主后导出并验证该目录，业务探针的生产输入改为这个将要上传的目录；acceptance仍从独立CI固定输入进入另一个测试根，绝不混入用户配套下载。现有12进程、来源/Closed/OS退出/租约断言保持。实际下载artifact后再次核验文件集合、哈希和ZIP根清单。

## 可读且安全的原生提示

首页区分“默认只带两个示例”与“可按需安装钉钉/UPCAM”，移除尚未接入的过时说法。包维护提示按安装/更新/回滚/移除/取消/启动结果给出中文下一步，提示root plugin.json、已装同ID改用更新、更新须同ID且版本更高、Prepared可取消、已开始的journal保留并重启。不能从失败结果推断已成功回退；当前代码版本/历史/诊断是核对入口。

新增UI中性呈现帮助类；不回显任意PluginOperationResult.Message或异常文本。Core维护行为、结果模型、停止/租约策略和cancel条件保持。阶段文字覆盖现有全部枚举，包含Aborted，并保留未知阶段的安全提示。技术诊断仍可查看。

主题读取失败沿用HostFailure安全类别，显示系统主题回退与下一步，不读取Message/ToString/GetBaseException。补真实原生损坏主题文件的恢复检查；保留已有原生14/管理17与所有业务断言。低影响文案不写镜像测试；围绕敏感文本不可泄漏、失败后仍可操作和维护状态语义写有价值的测试。

## 旧版并行保护与Windows11最小验收

独立文档给出实际产物路径和纯模拟首次使用步骤、代码更新/失败回退操作、宿主数据与外部规范存储边界。按固定业务源码核验：UPCAM生产插件与独立版0.4.3均使用 `Local\\UPCAMTaskDigest.Singleton.v1` 的存在性互斥；钉钉规范state.dpapi及其旧版兼容锁由业务Runtime/Core持有，连接仍需要具体授权。宿主UserRoot锁不等于这两种业务保护，不能保证任意旧版本或不遵守协议的进程被排除。记录已核验源码、已有自动证据和未执行的双GUI实测，不能冒充旧EXE联合运行成功。

Windows11指南提供最小模拟通路和剩余完整清单，明确核验操作系统客户端类型、x64与24H2+，同为26100构建号的Server不是Windows11证据。使用专门测试用户/空数据，不输入真实密钥、Webhook或发送消息；不复制出第二份活动账本、不改变实际用户目录。步骤包括完整解压、版本/哈希、ZIP与文件夹选择/取消、信任取消/确认、两插件原生页面、导航/Stop/重启、主题/DPI/键盘/可访问性。更高生产版本迁移、真实旧程序并行、真实接口与多日运行均按实际证据标注待验；最小演示不等于全清单完成。

## 交付判定

两个任务各自提交/审查；最后对本轮 `6915b81..HEAD` 做一次完整增量审查，不重审既有整个平台实现。新当前Windows CI、实际配套artifact与宿主身份核验通过后，工程交付可完成；整体Windows11人工门槛仍开放。没有真实业务接口、Webhook、消息、凭据、PR重试、合并或部署。
