using Workbench.App;
using Workbench.Core;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class PackagePresentationTests
{
    // Echoing Message in either the title or body must break this shipping-boundary control.
    [Theory]
    [InlineData((int)PackageAction.Import, false)]
    [InlineData((int)PackageAction.Import, true)]
    [InlineData((int)PackageAction.Update, false)]
    [InlineData((int)PackageAction.Update, true)]
    [InlineData((int)PackageAction.Rollback, false)]
    [InlineData((int)PackageAction.Rollback, true)]
    [InlineData((int)PackageAction.Remove, false)]
    [InlineData((int)PackageAction.Remove, true)]
    [InlineData((int)PackageAction.Cancel, false)]
    [InlineData((int)PackageAction.Cancel, true)]
    [InlineData((int)PackageAction.Startup, false)]
    [InlineData((int)PackageAction.Startup, true)]
    public void Shipping_package_outcomes_do_not_echo_untrusted_result_text(int actionValue, bool succeeded)
    {
        var action = (PackageAction)actionValue;
        var notice = PackagePresentation.ForResult(action, new(succeeded, HostFailureTests.PrivateResponse), hasPending: true);
        Assert.DoesNotContain(HostFailureTests.PrivateResponse, notice.Title + notice.Body);
        Assert.NotEmpty(notice.Title);
        Assert.NotEmpty(notice.Body);
        Assert.Equal(succeeded, notice.IsSuccess);
    }

    // A false result can accompany partial recovery; the UI must retain warning semantics.
    [Theory]
    [InlineData((int)PackageAction.Startup)]
    [InlineData((int)PackageAction.Rollback)]
    public void Failed_startup_or_rollback_is_never_presented_as_completed(int actionValue)
    {
        var action = (PackageAction)actionValue;
        var notice = PackagePresentation.ForResult(action, new(false, "Old code restored successfully."), hasPending: true);
        Assert.False(notice.IsSuccess);
        Assert.DoesNotContain("完成", notice.Title);
        Assert.Contains("诊断", notice.Body);
        Assert.DoesNotContain("Old code restored", notice.Body);
    }

    [Theory]
    [InlineData((int)PackageAction.Update)]
    [InlineData((int)PackageAction.Rollback)]
    [InlineData((int)PackageAction.Remove)]
    public void Staged_success_requires_a_current_pending_snapshot(int actionValue)
    {
        var action = (PackageAction)actionValue;
        var staged = PackagePresentation.ForResult(action, new(true, "arbitrary success"), hasPending: true);
        Assert.True(staged.IsSuccess);
        Assert.Contains("下次启动", staged.Body);
        var missing = PackagePresentation.ForResult(action, new(true, "arbitrary success"), hasPending: false);
        Assert.False(missing.IsSuccess);
        Assert.Contains("刷新", missing.Body);
    }

    [Fact]
    public void Cancel_success_is_not_mislabeled_as_another_packages_pending_stage()
    {
        var notice = PackagePresentation.ForResult(PackageAction.Cancel, new(true, "arbitrary"), hasPending: true);
        Assert.True(notice.IsSuccess);
        Assert.Contains("取消", notice.Title);
        Assert.DoesNotContain("下次启动执行", notice.Title);
    }

    [Theory]
    [InlineData(PackageOperationPhase.Prepared, true)]
    [InlineData(PackageOperationPhase.BackedUp, false)]
    [InlineData(PackageOperationPhase.OldMoved, false)]
    [InlineData(PackageOperationPhase.NewMoved, false)]
    [InlineData(PackageOperationPhase.Committed, false)]
    [InlineData(PackageOperationPhase.RecoveryPrepared, false)]
    [InlineData(PackageOperationPhase.Quarantined, false)]
    [InlineData(PackageOperationPhase.Recovered, false)]
    [InlineData(PackageOperationPhase.Aborted, false)]
    [InlineData((PackageOperationPhase)999, false)]
    public void Only_prepared_metadata_admits_cancel_and_unknown_phases_remain_safe(PackageOperationPhase phase, bool canCancel)
    {
        var hint = PackagePresentation.ForPending(phase);
        Assert.Equal(canCancel, hint.CanCancel);
        Assert.NotEmpty(hint.Label);
        Assert.NotEmpty(hint.Body);
        Assert.DoesNotContain("999", hint.Label + hint.Body);
    }

    [Fact]
    public void Unknown_operation_context_is_a_warning_without_echoing_values()
    {
        var notice = PackagePresentation.ForResult((PackageAction)999, new(true, HostFailureTests.PrivateResponse), hasPending: true);
        Assert.False(notice.IsSuccess);
        Assert.DoesNotContain("999", notice.Title + notice.Body);
        Assert.DoesNotContain(HostFailureTests.PrivateResponse, notice.Title + notice.Body);
    }
}
