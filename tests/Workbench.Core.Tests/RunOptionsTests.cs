using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class RunOptionsTests
{
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
