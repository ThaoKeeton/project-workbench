using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class RunOptionsTests
{
    [Theory]
    [InlineData("seed")]
    [InlineData("restart")]
    [InlineData("update")]
    [InlineData("fault")]
    [InlineData("rollback")]
    [InlineData("remove")]
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
