using System.Text.Json;
using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class RunOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Smoke_checkpoint_locked_report_does_not_interrupt_occupied_cleanup_or_replace_failure(int lockBefore)
    {
        using var disk = new TestDirectory();
        var report = Path.Combine(disk.Root, "smoke.json");
        var options = new RunOptions(true, disk.User, report);
        var original = new InvalidOperationException("Synthetic occupied-window body failure");
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeAttempted = false;
        var closedWaitCompleted = false;
        FileStream? locked = null;

        void Checkpoint(string stage, int position)
        {
            if (position == lockBefore)
            {
                locked = new FileStream(report, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var ioFailure = Record.Exception(() => File.WriteAllText(report, "blocked"));
                Assert.True(ioFailure is IOException or UnauthorizedAccessException,
                    "The test must cause an actual report write failure.");
            }
            options.TraceStartup(stage);
        }

        var observed = await Record.ExceptionAsync(async () =>
        {
            try { throw original; }
            finally
            {
                try
                {
                    // A portable stand-in for the WinUI close/Closed sequence; the real
                    // RunOptions report writer and the actual checkpoint names are used.
                    Checkpoint("native-smoke-occupied-close-begin", 0);
                    closeAttempted = true;
                    closed.SetResult();
                    Checkpoint("native-smoke-occupied-close-complete", 1);
                    Checkpoint("native-smoke-occupied-closed-wait-begin", 2);
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(1));
                    closedWaitCompleted = true;
                    Checkpoint("native-smoke-occupied-closed-wait-complete", 3);
                }
                finally { locked?.Dispose(); }
            }
        });

        Assert.True(closeAttempted, $"Close was not attempted; observed {observed?.GetType().Name}.");
        Assert.True(closedWaitCompleted, $"Closed was not awaited; observed {observed?.GetType().Name}.");
        Assert.Same(original, observed);
    }

    [Fact]
    public void Smoke_checkpoint_unwritable_report_does_not_throw()
    {
        using var disk = new TestDirectory();
        var report = Path.Combine(disk.Root, "occupied-report");
        Directory.CreateDirectory(report);
        var ioFailure = Record.Exception(() => File.WriteAllText(report, "blocked"));
        Assert.True(ioFailure is IOException or UnauthorizedAccessException);
        var options = new RunOptions(true, disk.User, report);
        Assert.Null(Record.Exception(() => options.TraceStartup("native-smoke-live-card-begin")));
    }

    [Fact]
    public void Smoke_checkpoint_persists_on_a_writable_report()
    {
        using var disk = new TestDirectory();
        var report = Path.Combine(disk.Root, "smoke.json");
        var options = new RunOptions(true, disk.User, report);
        options.TraceStartup("native-smoke-occupied-close-begin");
        options.TraceStartup("native-smoke-occupied-close-complete");

        using var saved = JsonDocument.Parse(File.ReadAllText(report));
        Assert.Equal("native-smoke-occupied-close-complete", saved.RootElement.GetProperty("stage").GetString());
        var stages = saved.RootElement.GetProperty("startupStages");
        Assert.Equal(2, stages.GetArrayLength());
        Assert.Equal("native-smoke-occupied-close-begin", stages[0].GetProperty("stage").GetString());
        Assert.Equal("native-smoke-occupied-close-complete", stages[1].GetProperty("stage").GetString());
    }

    [Theory]
    [InlineData(false, "native-smoke-occupied-close-begin")]
    [InlineData(true, "host-initialization-completed")]
    public void Smoke_checkpoint_exception_handling_does_not_change_other_startup_writes(bool smokeTest, string stage)
    {
        using var disk = new TestDirectory();
        var report = Path.Combine(disk.Root, "occupied-report");
        Directory.CreateDirectory(report);
        var options = new RunOptions(smokeTest, disk.User, report);
        var observed = Record.Exception(() => options.TraceStartup(stage));
        Assert.True(observed is IOException or UnauthorizedAccessException);
    }

    [Theory]
    [InlineData("seed")]
    [InlineData("verify")]
    [InlineData("stop-figma")]
    [InlineData("stop-ai")]
    public void Source_acceptance_is_explicit_isolated_and_uses_only_its_lowercase_guid_root(string phase)
    {
        var parent = Path.Combine(Path.GetTempPath(), "ProjectWorkbench-source-acceptance-probe");
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        var options = RunOptions.Parse(["--source-acceptance-probe", phase, "--probe-root", root]);
        Assert.True(options.IsIsolated);
        Assert.Equal(phase, options.GetType().GetProperty("SourceAcceptanceProbe")?.GetValue(options));
        Assert.Equal(root, options.UserRoot);
        Assert.Equal(Path.Combine(root, "source-acceptance-result.json"), options.ReportPath);
        Assert.Equal(parent, Path.GetDirectoryName(RunOptions.Parse(["--source-acceptance-probe", phase]).UserRoot));
        foreach (var invalid in new[] { Path.GetTempPath(), Path.Combine(parent, "normal"),
            Path.Combine(parent, "ABCDEF0123456789ABCDEF0123456789"), Path.Combine(root, "nested"),
            Path.Combine(Path.GetTempPath(), "ProjectWorkbench-testing-probe", Guid.NewGuid().ToString("N")) })
            Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--source-acceptance-probe", phase, "--probe-root", invalid]));
        foreach (var other in new[] { new[] { "--smoke-test" }, ["--workspace-probe", "seed"],
            ["--business-probe", "seed"], ["--runtime-probe", "management"], ["--testing-probe", "seed"] })
        {
            Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--source-acceptance-probe", phase, .. other]));
            Assert.Throws<ArgumentException>(() => RunOptions.Parse([.. other, "--source-acceptance-probe", phase]));
        }
    }

    [Theory]
    [InlineData("--source-acceptance-probe")]
    [InlineData("--Source-Acceptance-Probe", "seed")]
    [InlineData("--source-acceptance-prob", "seed")]
    [InlineData("--source-acceptance-probe-extra", "seed")]
    [InlineData("--source-acceptance-probe", "SEED")]
    [InlineData("--source-acceptance-probe", "unknown")]
    [InlineData("--source-acceptance-probe", "")]
    [InlineData("--source-acceptance-probe", "seed", "--source-acceptance-probe", "verify")]
    [InlineData("--source-acceptance-probe", "seed", "--Probe-Root", "unused")]
    [InlineData("--source-acceptance-probe", "seed", "--probe-rot", "unused")]
    [InlineData("--source-acceptance-probe", "seed", "unexpected-positional")]
    public void Ambiguous_or_unknown_source_options_fail_before_normal_startup(params string[] args)
        => Assert.Throws<ArgumentException>(() => RunOptions.Parse(args));

    [Theory]
    [InlineData("--probe-root")]
    [InlineData("--smoke-report")]
    public void Source_probe_refuses_missing_and_duplicate_shared_values(string flag)
    {
        var root = Path.Combine(Path.GetTempPath(), "ProjectWorkbench-source-acceptance-probe", Guid.NewGuid().ToString("N"));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--source-acceptance-probe", "seed", flag]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--source-acceptance-probe", "seed", flag, root, flag, root]));
    }

    [Fact]
    public void Ordinary_startup_stays_nonisolated_without_a_source_option()
    {
        var options = RunOptions.Parse([]);
        Assert.False(options.IsIsolated);
        Assert.Null(options.ReportPath);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectWorkbench"), options.UserRoot);
    }

    [Theory]
    [InlineData("seed")]
    [InlineData("verify")]
    public void Testing_probe_isolated_identity_cannot_fall_through_to_user_or_other_modes(string phase)
    {
        var parent = Path.Combine(Path.GetTempPath(), "ProjectWorkbench-testing-probe");
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        var options = RunOptions.Parse(["--testing-probe", phase, "--probe-root", root]);
        Assert.True(options.IsIsolated);
        Assert.Equal(root, options.UserRoot);
        Assert.Equal(phase, options.GetType().GetProperty("TestingProbe")?.GetValue(options));
        Assert.NotNull(options.ReportPath);
        Assert.Equal(parent, Path.GetDirectoryName(RunOptions.Parse(["--testing-probe", phase]).UserRoot));
        foreach (var invalid in new[] { Path.GetTempPath(), Path.Combine(parent, "normal"),
            Path.Combine(Path.GetTempPath(), "ProjectWorkbench-workspace-probe", Guid.NewGuid().ToString("N")) })
            Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--testing-probe", phase, "--probe-root", invalid]));
        foreach (var other in new[] { new[] { "--smoke-test" }, ["--workspace-probe", "seed"],
            ["--business-probe", "seed"], ["--runtime-probe", "management"] })
            Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--testing-probe", phase, .. other]));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("SEED")]
    [InlineData("")]
    public void Invalid_testing_probe_is_rejected_before_root_selection(string phase)
        => Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--testing-probe", phase]));

    [Theory]
    [InlineData("seed")]
    [InlineData("verify")]
    public void Workspace_probe_never_opens_normal_or_other_probe_roots(string phase)
    {
        var parent = Path.Combine(Path.GetTempPath(), "ProjectWorkbench-workspace-probe");
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        var options = RunOptions.Parse(["--workspace-probe", phase, "--probe-root", root]);
        Assert.True(options.IsIsolated);
        Assert.Equal(root, options.UserRoot);
        Assert.Equal(phase, options.WorkspaceProbe);
        Assert.NotNull(options.ReportPath);
        var generated = RunOptions.Parse(["--workspace-probe", phase]);
        Assert.Equal(parent, Path.GetDirectoryName(generated.UserRoot));
        foreach (var invalid in new[] { Path.GetTempPath(), Path.Combine(parent, "normal"),
            Path.Combine(parent, Guid.NewGuid().ToString("N"), "nested"),
            Path.Combine(Path.GetTempPath(), "ProjectWorkbench-runtime-probe", Guid.NewGuid().ToString("N")),
            Path.Combine(Path.GetTempPath(), "ProjectWorkbench-business-probe", Guid.NewGuid().ToString("N")) })
            Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--workspace-probe", phase, "--probe-root", invalid]));
    }

    [Theory]
    [InlineData("--smoke-test")]
    [InlineData("--runtime-probe", "management")]
    [InlineData("--business-probe", "seed")]
    public void Workspace_probe_conflicts_refuse_before_any_root_is_selected(params string[] other)
    {
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--workspace-probe", "seed", .. other]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse([.. other, "--workspace-probe", "verify"]));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("SEED")]
    [InlineData("")]
    public void Invalid_workspace_phase_cannot_fall_through_to_normal_startup(string phase)
        => Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--workspace-probe", phase]));

    [Theory]
    [InlineData("seed")]
    [InlineData("restart")]
    [InlineData("update")]
    [InlineData("fault")]
    [InlineData("rollback")]
    [InlineData("remove")]
    [InlineData("acceptance-seed")]
    [InlineData("acceptance-restore")]
    [InlineData("acceptance-stopped")]
    [InlineData("acceptance-config")]
    [InlineData("acceptance-missing")]
    [InlineData("acceptance-disabled")]
    public void Business_probe_requires_its_own_isolated_root_and_cannot_mix_probe_modes(string phase)
    {
        var root = Path.Combine(Path.GetTempPath(), "ProjectWorkbench-business-probe", Guid.NewGuid().ToString("N"));
        var options = RunOptions.Parse(["--business-probe", phase, "--probe-root", root]);
        Assert.True(options.IsIsolated);
        Assert.Equal(phase, options.GetType().GetProperty("BusinessProbe")?.GetValue(options));
        Assert.Equal(root, options.UserRoot);
        Assert.NotNull(options.ReportPath);
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--business-probe", phase, "--probe-root", Path.GetTempPath()]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--business-probe", phase, "--runtime-probe", "management"]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--business-probe", phase, "--smoke-test"]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--business-probe", "unknown"]));
    }

    [Theory]
    [InlineData("--business-probe")]
    [InlineData("--probe-root")]
    [InlineData("--runtime-probe")]
    [InlineData("--smoke-report")]
    [InlineData("--workspace-probe")]
    [InlineData("--testing-probe")]
    public void Incomplete_value_options_cannot_fall_through_to_the_normal_user_store(string option)
    {
        Assert.Throws<ArgumentException>(() => RunOptions.Parse([option]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--business-probe", "seed", option]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse([option, "--smoke-test"]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse([option, "--runtime-probe", "management"]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse([option, ""]));
    }

    [Theory]
    [InlineData("management")]
    [InlineData("restart-seed")]
    [InlineData("restart-verify")]
    public void Explicit_runtime_probe_only_accepts_a_dedicated_temporary_guid_root(string phase)
    {
        var root = Path.Combine(Path.GetTempPath(), "ProjectWorkbench-runtime-probe", Guid.NewGuid().ToString("N"));
        var options = RunOptions.Parse(["--runtime-probe", phase, "--probe-root", root]);
        Assert.True(options.IsIsolated);
        Assert.Equal(root, options.UserRoot);
        Assert.Equal(phase, options.RuntimeProbe);
        Assert.NotNull(options.ReportPath);
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--runtime-probe", phase, "--probe-root", Path.GetTempPath()]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--smoke-test", "--runtime-probe", phase]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["--probe-root", root]));
    }
}
