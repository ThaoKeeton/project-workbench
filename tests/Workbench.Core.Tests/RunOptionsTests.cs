using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class RunOptionsTests
{
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
