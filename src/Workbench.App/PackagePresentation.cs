using Workbench.Core;

namespace Workbench.App;

internal enum PackageAction { Import, Update, Rollback, Remove, Cancel, Startup }
internal sealed record PackageOutcome(string Title, string Body, bool IsSuccess);
internal sealed record PackagePhaseHint(string Label, string Body, bool CanCancel);

/// <summary>The shipping native package text boundary, independent of WinUI.</summary>
internal static class PackagePresentation
{
    private const string Preserve = "请保留当前文件和维护记录，刷新“包维护”，核对当前版本、待重启操作、代码历史与诊断，再安全退出并重启。本提示不确认旧代码已恢复。";
    private const string Restart = "安全退出整个工作台后，由下次启动在发现插件前执行；停用不会卸载程序集。执行后请重新审查并确认信任。";
    private const string DataBoundary = "只切换代码，不恢复、删除或降级当前数据、设置、凭据；外部独立版规范状态和 DWS profile 不在自动备份或恢复范围。";

    internal static PackageOutcome ForResult(PackageAction action, PluginOperationResult result, bool hasPending)
    {
        // Message may contain paths, settings or provider text. Only the success bit is consumed.
        if (!Enum.IsDefined(action)) return new("包维护状态待确认", Preserve, false);
        if (!result.Succeeded) return action switch
        {
            PackageAction.Import => new("导入需要处理", "请核对所选目录或 ZIP 根目录是否直接包含 plugin.json、ID 是否已安装，以及 API/最低宿主版本是否兼容。查看“包维护”的诊断后重试；导入不会执行代码或批准信任。", false),
            PackageAction.Update => new("更新暂存需要处理", "更新需使用同一 ID、严格更高且兼容的版本。" + Preserve, false),
            PackageAction.Rollback => new("代码回滚需要处理", "请核对所选代码历史是否仍有效；回滚不会降级当前业务数据。" + Preserve, false),
            PackageAction.Remove => new("移除暂存需要处理", Preserve, false),
            PackageAction.Cancel => new("取消暂存被拒绝", "只有尚未移动代码的暂存可以取消；即使显示 Prepared，崩溃前也可能已经移动目录。不要删除暂存、历史或 journal。" + Preserve, false),
            _ => new("启动维护需要处理", "未受影响的插件仍可使用。" + Preserve + DataBoundary, false)
        };
        if (action is PackageAction.Update or PackageAction.Rollback or PackageAction.Remove && !hasPending)
            return new("请确认维护状态", "未在当前快照中发现待重启操作。请刷新“包维护”并查看诊断，再决定下一步；不能据此认定已暂存或已切换代码。", false);
        return action switch
        {
            PackageAction.Import => new("已导入，尚未启用", "请到“插件”核对 ID、版本、能力与诊断，再明确确认信任并启用。取消信任不会运行插件；同进程可信插件拥有当前用户权限。", true),
            PackageAction.Update => new("已暂存更新", Restart + DataBoundary, true),
            PackageAction.Rollback => new("已暂存代码回滚", Restart + DataBoundary, true),
            PackageAction.Remove => new("已暂存移除", "安全退出整个工作台后，由下次启动移除代码；保留当前数据、设置、凭据和代码历史。外部业务状态不在自动维护范围。", true),
            PackageAction.Cancel => new("已取消暂存", "已取消未开始的暂存，当前代码未改变。其他待重启操作仍请在“包维护”逐项查看。", true),
            _ => new("本项启动维护已完成", "请核对当前版本和代码历史；需要使用时重新审查并确认信任。" + (hasPending ? "列表中仍有待重启操作，请逐项查看阶段与诊断。" : "") + DataBoundary, true)
        };
    }

    internal static string KindLabel(PackageOperationKind kind) => kind switch
    {
        PackageOperationKind.Update => "更新",
        PackageOperationKind.Rollback => "代码回滚",
        PackageOperationKind.Remove => "移除",
        _ => "未知维护操作"
    };

    internal static PackagePhaseHint ForPending(PackageOperationPhase phase)
    {
        var (label, detail) = phase switch
        {
            PackageOperationPhase.Prepared => ("已暂存（Prepared）", "只有尚未移动代码的暂存可取消。若取消被拒绝，请保留文件和维护记录，查看诊断，再安全退出并重启；停用不会卸载程序集。"),
            PackageOperationPhase.BackedUp => ("已备份宿主管理状态（BackedUp）", "状态快照已记录，目录切换尚待完成。"),
            PackageOperationPhase.OldMoved => ("旧代码已移入历史（OldMoved）", "旧目录已移动，代码切换尚未完成。"),
            PackageOperationPhase.NewMoved => ("目标代码目录已切换（NewMoved）", "仍需完成信任与后台意图撤销及维护记录。"),
            PackageOperationPhase.Committed => ("已记录提交（Committed）", "仍需完成历史记录与待执行记录整理。"),
            PackageOperationPhase.RecoveryPrepared => ("已准备恢复（RecoveryPrepared）", "正在恢复经过验证的旧代码，尚不能确认完成。"),
            PackageOperationPhase.Quarantined => ("已隔离损坏代码（Quarantined）", "本次操作确认的损坏代码已保留隔离，恢复仍待完成。"),
            PackageOperationPhase.Recovered => ("已记录旧代码恢复（Recovered）", "请核对当前版本和诊断；仍需重新确认信任，当前业务数据没有恢复到旧版本。"),
            PackageOperationPhase.Aborted => ("已中止切换（Aborted）", "目录切换前已中止维护，保留当前代码和维护字节；请核对版本、信任状态与诊断。"),
            _ => ("未知维护阶段", "无法确认当前维护阶段；请查看诊断。")
        };
        var canCancel = phase == PackageOperationPhase.Prepared;
        return new(label, canCancel ? detail : detail + "不能取消或丢弃维护记录。请保留 journal 与现有目录，安全退出并在下次启动继续处理；不要手动删除或移动文件。", canCancel);
    }
}
