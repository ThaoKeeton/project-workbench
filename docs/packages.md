# 插件安装与重启维护

用户入口：[生产插件安装](install-business-plugins.md) · [Windows 11 最小人工验收](windows11-manual.md) · [独立版并行与切换边界](standalone-compatibility.md)。

`PluginPackageStore` 导入本地目录或根目录含 `plugin.json` 的 ZIP。安装、暂存、恢复和发现均不执行插件代码，不批准信任，也不启用插件。原有 `PluginManager.InstallAsync(string packageDirectory, ...)` 目录安装接口保留；它同样拒绝替换已有 ID，并使用相同的复制上限和进程内写入串行化。

ZIP 支持经典单卷格式、Stored（不压缩）或 Deflate，文件名须为 ASCII 或明确标记的 UTF-8。ZIP64、加密、分卷及其他压缩方法会被拒绝。导入拒绝穿越、绝对路径、ADS、Windows 设备名、链接/特殊文件、大小写碰撞和重复目标。上限为 **1000 个文件、总计 100 MiB、单文件 50 MiB**，包含清单与 DLL。除了预检元数据，复制时还计算实际字节；ZIP 直接读取有界压缩载荷，校验实际展开长度与 CRC，避免伪造的短长度截断载荷后被误认为有效包。

更新必须匹配已安装 ID，版本严格更高，且 API/最低宿主版本兼容。回滚选择历史快照的 `HistoryId`，允许从已移除插件的代码历史重新安装；它不继承以前对该指纹的批准。bundled 发现根中的 ID 保留给其发布方，不允许就地更新、回滚或移除。

每个 ID 只能有一个待执行操作。普通 `Prepared` 暂存不改变当前代码、批准、后台工作或设置；当前可信实例仍可使用。界面仅在 Prepared 快照提供取消；只有尚未移动代码的暂存才能真正取消。崩溃可令目录已移动而 metadata 仍显示 Prepared，此时 Core 仍拒绝取消，须保留文件与 journal、安全退出并在下次启动恢复。进入后续阶段后界面不提供取消/删除操作。

## 宿主调用顺序与接口

宿主先取得 UserRoot 独占租约，再调用 `ApplyPendingAsync`，之后才 `DiscoverAsync` 和 `RestoreEnabledAsync`。构造管理器或包存储本身不封闭维护窗口。任一管理器开始发现某个根后，该根在当前进程内都不能 Apply；通过额外发现根扫描也会封闭它。任一包路径被加载上下文使用后，路径及其相交的代码目录树在整个进程生命周期中保持受保护，停用和 Dispose 都不会解除。不要用新管理器或重新安装来绕过重启。

```csharp
public PluginPackageStore(PluginManagerOptions options);
public IReadOnlyList<PendingPackageOperation> PendingOperations { get; }
public IReadOnlyList<PackageHistorySnapshot> History { get; }
public IReadOnlyList<PluginDiagnostic> Diagnostics { get; }
public Task RefreshAsync(CancellationToken cancellationToken = default);
public Task<PluginOperationResult> InstallAsync(string path, CancellationToken cancellationToken = default);
public Task<PluginOperationResult> StageUpdateAsync(string pluginId, string path, CancellationToken cancellationToken = default);
public Task<PluginOperationResult> StageRollbackAsync(string pluginId, string historyId, CancellationToken cancellationToken = default);
public Task<PluginOperationResult> StageRemoveAsync(string pluginId, CancellationToken cancellationToken = default);
public Task<PluginOperationResult> CancelPendingAsync(string pluginId, CancellationToken cancellationToken = default);
public Task<IReadOnlyList<PluginOperationResult>> ApplyPendingAsync(CancellationToken cancellationToken = default);

public sealed record PendingPackageOperation(
    string PluginId, string OperationId, PackageOperationKind Kind, PackageOperationPhase Phase,
    string CurrentPath, string? StagedPath, string? OldVersion, string? NewVersion);
public sealed record PackageHistorySnapshot(
    string PluginId, string HistoryId, PackageOperationKind Kind, string Version, string Fingerprint,
    string CodePath, string? DataBackupPath, DateTimeOffset CreatedAt);
public enum PackageOperationKind { Update, Rollback, Remove }
public enum PackageOperationPhase {
    Prepared, BackedUp, OldMoved, NewMoved, Committed,
    RecoveryPrepared, Quarantined, Recovered, Aborted
}
```

快照为不可变的最后一次读取结果。启动时调用 `ApplyPendingAsync` 或 `RefreshAsync` 读取磁盘；成功的 Stage/Cancel/Apply 也刷新快照。Pending 按文件名排序；History 按 ID、时间倒序、HistoryId 排序，HistoryId 是操作生成的 GUID（N 格式），不应从版本号推导。历史中只提供已提交且代码指纹仍有效的代码快照。

`ApplyPendingAsync` 按待执行目录中文件的序数顺序处理，每个文件返回一个结果；整个目录不可读时返回一个失败结果。无待执行文件时返回空列表，已完成操作重放后不会再次执行。一个包失败不阻止其他包。`Succeeded=false` 可能伴随部分恢复，不能据此认定旧代码已恢复或操作成功；读取 Diagnostics、当前版本和快照核对具体状态。原生提示只消费成功位和明确操作上下文，不直接回显任意 `Message`；技术诊断仍可在“包维护”查询。存储错误返回本地诊断，不猜测或删除未知 journal；请求取消则抛出取消异常，已开始的维护仍由 journal 持有，重启后继续恢复。

成功维护会在移除待执行 journal 前，原子持久化该 ID 的 `Enabled=false`、`TrustedFingerprint=null` 并清除后台恢复授权/运行意图。回滚到最初的指纹也必须重新确认信任。每个发现根分别检查自己的维护 journal；托管根的元数据整体不可读时，该根的批准会被阻止，其他只读根及其已有运行实例保持可用。若撤销持久化失败，journal 保留，受影响 ID 的发现/启用会被阻止；不能把一次 Apply 失败视为可以继续加载该包。不同存储实例与旧目录安装按 PluginsRoot 串行化，两个宿主状态文件分别按完整路径串行化并合并最新磁盘文档，避免旧缓存恢复已撤销的授权。跨进程互斥由宿主持有的租约负责。

## 磁盘布局与恢复阶段

所有代码目录移动都位于同一个 PluginsRoot；只排除精确的宿主元数据目录 `.workbench-packages`，其他未知目录仍按原有发现规则显示诊断。

```text
PluginsRoot/<原安装目录>/                         当前代码
PluginsRoot/.workbench-packages/
  pending/<plugin-id>.json                        原子 journal
  imports/<guid>/                                私有安装暂存
  operations/<plugin-id>/<operation-id>/new/       私有更新/回滚暂存
  operations/<plugin-id>/<operation-id>/quarantine/ 损坏的新代码（保留）
  history/<plugin-id>/<operation-id>/
    code/                                        旧代码
    data/                                        宿主管理状态的历史快照
    record.json                                  已提交/恢复记录
```

journal 只存受校验的 ID、操作 GUID、一个相对当前目录名、版本、指纹、时间和阶段，不接受任意绝对路径。顺序为：`Prepared` 已复验新代码 → `BackedUp` 已完成并记录状态快照 → `OldMoved` 旧目录移入历史 → `NewMoved` 新目录已落位（移除时当前目录为空）→ 撤销批准/意图 → `Committed` → 写历史记录并移除 pending。目录移动完成而下一次 journal 写入未完成时，根据已复验的实际布局幂等继续。

若新暂存损坏、旧代码已移走且当前路径为空，则恢复经过完整指纹校验的旧代码。若 journal 已确认 `NewMoved`，落位的新代码损坏/消失且旧代码仍完整，则先记录 `RecoveryPrepared`，将损坏目录移到唯一操作的 quarantine，再记录 `Quarantined`，恢复旧代码并记录 `Recovered`；这些移动之间的中断也可重放。隔离字节不会被覆盖或删除。若尚未确认新代码落位就出现未知当前指纹，或旧代码/journal/路径不能验证，则保留现状并报告，阻止受影响 ID 激活。

`Aborted` 表示旧代码移动前已中止维护：保留验证过的当前代码与暂存/备份字节，并撤销批准/意图。它不是“已取消暂存”，也不提供删除恢复记录的入口；请核对当前版本、重新信任要求和诊断。

## 数据与凭据的明确边界

切换前只快照 UserRoot 的 `data/<id>/`、`settings/<id>/`、`secrets/<id>/` 和宿主 `background-tasks.json`。后台文档是宿主共用文件，因此快照可能包含其他 ID 的意图；平台从不自动恢复它。凭据只复制已有密文，不解密。快照仅接受普通文件，拒绝链接、复制期间的内容/文件清单变化，以及超过 **1 GiB / 10000 文件** 的状态；失败发生在移动旧代码之前。

**代码回滚不会自动回写、降级迁移或删除当前业务数据、设置或凭据。** 移除也保留这些当前目录和代码历史。历史快照作为供人工审阅的记录，宿主不自动清理历史、不把它当作自动恢复命令。

**用户明确连接的独立版规范状态文件、外部 DWS profile 以及其他外部存储不在自动备份或恢复范围内。** 保存的路径引用不会被跟随复制。平台代码回滚不代表这些外部文件已备份或可以降级；它们的共享资源租约、格式演进和发送回执保护仍由业务运行时负责。
