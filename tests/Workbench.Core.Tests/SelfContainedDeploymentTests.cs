using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class SelfContainedDeploymentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_private_dependencies_are_not_framework_assemblies_in_a_self_contained_folder(bool includePrivateDependency)
    {
        using var disk = new TestDirectory();
        const string id = "com.example.fixture";
        var package = disk.Package(id);
        if (!includePrivateDependency)
            File.Delete(Path.Combine(package, "Workbench.TestDependency.dll"));
        var probeDirectory = Path.Combine(AppContext.BaseDirectory, "deployment-probe");
        var executable = Path.Combine(probeDirectory, OperatingSystem.IsWindows() ? "Workbench.DeploymentProbe.exe" : "Workbench.DeploymentProbe");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(disk.Plugins);
        start.ArgumentList.Add(disk.User);
        start.ArgumentList.Add(id);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The deployment probe did not start.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var errors = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.ExitCode == 0, await errors);
            var result = JsonSerializer.Deserialize<ProbeResult>(await output)!;
            // This is an actual published self-contained child process: its
            // CoreLib is colocated with the application's conflicting v2 DLL.
            Assert.Equal(Path.TrimEndingDirectorySeparator(probeDirectory), Path.TrimEndingDirectorySeparator(result.CoreLibraryDirectory));
            Assert.Equal(Path.TrimEndingDirectorySeparator(probeDirectory), Path.TrimEndingDirectorySeparator(result.HostDirectory));
            Assert.Equal("host-private-version", result.HostMessage);
            Assert.Equal("2.0.0.0", result.HostDependencyVersion);
            if (includePrivateDependency)
            {
                Assert.True(result.Enabled, result.Message);
                Assert.Equal("com.example.fixture:dependency-ok:demo draft", result.Ping);
                Assert.StartsWith("Workbench:", result.DependencyContext);
            }
            else
            {
                Assert.False(result.Enabled, "A missing plugin-private DLL must not resolve to the host's v2 assembly.");
                Assert.Contains("Workbench.TestDependency", result.Message);
            }
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private sealed record ProbeResult(
        string HostMessage, string HostDependencyVersion,
        string CoreLibraryDirectory, string HostDirectory,
        bool Enabled, string Message, string? Ping, string? DependencyContext);
}
