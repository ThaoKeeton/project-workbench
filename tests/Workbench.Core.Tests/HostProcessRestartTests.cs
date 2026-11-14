using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class HostProcessRestartTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_host_process_restarts_restore_only_generic_opt_in_and_keep_disable_revoked(bool requiresPluginStart)
    {
        using var disk = BackgroundTaskTests.Package();
        var policy = requiresPluginStart ? "guarded" : "generic";
        using var seed = await RunAsync(disk, $"host-runtime-{policy}-seed");
        Assert.Equal(1, seed.RootElement.GetProperty("runs").GetInt32());
        using var restarted = await RunAsync(disk, $"host-runtime-{policy}-verify");
        Assert.Equal(requiresPluginStart ? 1 : 2, restarted.RootElement.GetProperty("runs").GetInt32());
        Assert.Equal(requiresPluginStart ? "UserRequest" : "Restore", restarted.RootElement.GetProperty("startReason").GetString());
        Assert.Equal(requiresPluginStart ? "Stopped" : "Running", restarted.RootElement.GetProperty("taskState").GetString());
        using var disabled = await RunAsync(disk, $"host-runtime-{policy}-disabled");
        Assert.False(disabled.RootElement.GetProperty("hasRuntime").GetBoolean());
        Assert.Equal(requiresPluginStart ? 1 : 2, disabled.RootElement.GetProperty("runs").GetInt32());
    }

    private static async Task<JsonDocument> RunAsync(TestDirectory disk, string operation)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "deployment-probe", OperatingSystem.IsWindows() ? "Workbench.DeploymentProbe.exe" : "Workbench.DeploymentProbe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { disk.Plugins, disk.User, "com.example.runtime", operation }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Synthetic host process did not start.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var errors = process.StandardError.ReadToEndAsync(deadline.Token);
        await process.WaitForExitAsync(deadline.Token);
        Assert.True(process.ExitCode == 0, await errors);
        return JsonDocument.Parse(await output);
    }
}
