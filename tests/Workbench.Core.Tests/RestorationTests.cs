using System.Diagnostics;
using System.Text.Json;
using Workbench.Core;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class RestorationTests
{
    private const string Id = "com.example.fixture";

    [Fact]
    public async Task Shutdown_preserves_approval_and_enabled_intent_but_discovery_never_restores_code()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        using var marker = disk.WatchActivation();
        await using (var manager = disk.Manager())
        {
            await manager.DiscoverAsync();
            Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
            await manager.GetSettings(Id).SetAsync("draft", "restart draft");
            await manager.ShutdownAsync();
            Assert.True(Assert.Single(manager.Plugins).DesiredEnabled);
            Assert.Empty(manager.Contributions.Views);
        }
        File.Delete(marker.Path);
        await using var restarted = disk.Manager();

        var package = Assert.Single(await restarted.DiscoverAsync());

        Assert.True(package.IsTrusted);
        Assert.True(package.DesiredEnabled);
        Assert.False(package.IsLoaded);
        Assert.False(File.Exists(marker.Path));
        var restored = Assert.Single(await restarted.RestoreEnabledAsync());
        Assert.True(restored.Succeeded, restored.Message);
        Assert.True(File.Exists(marker.Path));
        Assert.Equal("com.example.fixture:dependency-ok:restart draft", (await restarted.ExecuteCommandAsync(Id, "ping")).Message);
    }

    [Fact]
    public async Task Explicit_disable_preserves_trust_and_stays_disabled_on_restart()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        using var marker = disk.WatchActivation();
        await using (var manager = disk.Manager())
        {
            await manager.DiscoverAsync();
            Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
            Assert.True((await manager.DisableAsync(Id)).Succeeded);
        }
        File.Delete(marker.Path);
        await using var restarted = disk.Manager();
        var package = Assert.Single(await restarted.DiscoverAsync());

        Assert.True(package.IsTrusted);
        Assert.False(package.DesiredEnabled);
        Assert.Empty(await restarted.RestoreEnabledAsync());
        Assert.False(File.Exists(marker.Path));
        Assert.True((await restarted.EnableAsync(Id)).Succeeded);
    }

    [Theory]
    [InlineData("plugin.json")]
    [InlineData("Workbench.TestPlugin.dll")]
    [InlineData("Workbench.TestDependency.dll")]
    [InlineData("extra.txt")]
    public async Task Changed_package_contents_cannot_inherit_approval_on_restart(string changedFile)
    {
        using var disk = new TestDirectory();
        var path = disk.Package(Id);
        await File.WriteAllTextAsync(Path.Combine(path, "extra.txt"), "original data");
        using var marker = disk.WatchActivation();
        // Disposing a noncollectible load context does not release Windows DLL mappings.
        // Finish an actual host process before replacing package bytes, as users must do.
        var executable = Path.Combine(AppContext.BaseDirectory, "deployment-probe",
            OperatingSystem.IsWindows() ? "Workbench.DeploymentProbe.exe" : "Workbench.DeploymentProbe");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(disk.Plugins);
        start.ArgumentList.Add(disk.User);
        start.ArgumentList.Add(Id);
        using (var process = Process.Start(start) ?? throw new InvalidOperationException("Fixture host did not start."))
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var errors = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                await process.WaitForExitAsync(deadline.Token);
                Assert.True(process.ExitCode == 0, await errors);
                using var result = JsonDocument.Parse(await output);
                Assert.True(result.RootElement.GetProperty("Enabled").GetBoolean(), result.RootElement.GetProperty("Message").GetString());
                Assert.True(File.Exists(marker.Path));
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }
        File.Delete(marker.Path);
        if (changedFile == "plugin.json")
        {
            var manifest = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await File.ReadAllTextAsync(Path.Combine(path, changedFile)))!;
            manifest["version"] = JsonSerializer.SerializeToElement("2.0.0");
            await File.WriteAllTextAsync(Path.Combine(path, changedFile), JsonSerializer.Serialize(manifest));
        }
        else
            await File.AppendAllTextAsync(Path.Combine(path, changedFile), "changed");
        await using var restarted = disk.Manager();

        var package = Assert.Single(await restarted.DiscoverAsync());

        Assert.False(package.IsTrusted);
        Assert.False(package.DesiredEnabled);
        Assert.Contains(package.Diagnostics, d => d.Code == PluginDiagnosticCode.PackageChanged);
        Assert.Empty(await restarted.RestoreEnabledAsync());
        Assert.False((await restarted.EnableAsync(Id)).Succeeded);
        Assert.False(File.Exists(marker.Path));
    }

    [Fact]
    public async Task Changing_files_after_discovery_requires_fresh_review_before_explicit_trust()
    {
        using var disk = new TestDirectory();
        var path = disk.Package(Id);
        using var marker = disk.WatchActivation();
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        await File.WriteAllTextAsync(Path.Combine(path, "new-file.txt"), "changed package");

        Assert.False((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.False(File.Exists(marker.Path));
        Assert.False(Assert.Single(manager.Plugins).IsLoaded);
        Assert.Contains(Assert.Single(manager.Plugins).Diagnostics, d => d.Code == PluginDiagnosticCode.PackageChanged);
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
    }

    [Fact]
    public async Task A_loaded_package_cannot_accept_replacement_bytes_until_process_restart()
    {
        using var disk = new TestDirectory();
        var path = disk.Package(Id);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        await manager.DisableAsync(Id);
        await File.WriteAllTextAsync(Path.Combine(path, "changed.txt"), "new package contents");

        await manager.DiscoverAsync();
        Assert.False((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.False(Assert.Single(manager.Plugins).IsTrusted);
        Assert.True(Assert.Single(manager.Plugins).IsLoaded);
        Assert.Empty(manager.Contributions.Commands);
    }

    [Fact]
    public async Task Saved_enabled_state_without_trust_never_activates_code()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        Directory.CreateDirectory(disk.User);
        await File.WriteAllTextAsync(Path.Combine(disk.User, "plugin-state.json"),
            "{\"version\":1,\"plugins\":{\"com.example.fixture\":{\"enabled\":true,\"trustedFingerprint\":null}}}");
        using var marker = disk.WatchActivation();
        await using var manager = disk.Manager();

        var package = Assert.Single(await manager.DiscoverAsync());

        Assert.False(package.IsTrusted);
        Assert.False(package.DesiredEnabled);
        Assert.Empty(await manager.RestoreEnabledAsync());
        Assert.False(File.Exists(marker.Path));
        Assert.Contains(package.Diagnostics, d => d.Code == PluginDiagnosticCode.Untrusted);
    }

    [Fact]
    public async Task Corrupt_state_uses_disabled_defaults_with_a_visible_diagnostic()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        Directory.CreateDirectory(disk.User);
        await File.WriteAllTextAsync(Path.Combine(disk.User, "plugin-state.json"), "{broken");
        using var marker = disk.WatchActivation();
        await using var manager = disk.Manager();

        var package = Assert.Single(await manager.DiscoverAsync());

        Assert.False(package.IsTrusted);
        Assert.False(package.DesiredEnabled);
        Assert.Contains(manager.Diagnostics, d => d.Code == PluginDiagnosticCode.StateCorrupt);
        Assert.Empty(await manager.RestoreEnabledAsync());
        Assert.False(File.Exists(marker.Path));
    }

    [Fact]
    public async Task A_failure_to_save_new_trust_prevents_activation_and_cleans_temporary_files()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        Directory.CreateDirectory(Path.Combine(disk.User, "plugin-state.json"));
        using var marker = disk.WatchActivation();
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();

        var result = await manager.EnableAsync(Id, confirmTrust: true);

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(marker.Path));
        Assert.False(Assert.Single(manager.Plugins).IsLoaded);
        Assert.False(Assert.Single(manager.Plugins).IsTrusted);
        Assert.Contains(Assert.Single(manager.Plugins).Diagnostics, d => d.Code == PluginDiagnosticCode.StatePersistenceFailed);
        Assert.Empty(Directory.EnumerateFiles(disk.User, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Shutdown_continues_after_stop_failure_and_keeps_each_plugins_enabled_intent()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        disk.Package("com.example.second");
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        await manager.GetSettings(Id).SetAsync("mode", "stop-fail");
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.True((await manager.EnableAsync("com.example.second", confirmTrust: true)).Succeeded);

        await manager.ShutdownAsync();

        Assert.Empty(manager.Contributions.Views);
        Assert.Empty(manager.Contributions.Commands);
        Assert.All(manager.Plugins, p => Assert.True(p.DesiredEnabled));
        Assert.True(await manager.GetSettings("com.example.second").GetAsync("stopped", false));
        Assert.False((await manager.ExecuteCommandAsync(Id, "ping")).Succeeded);
    }

    [Fact]
    public async Task Disabling_a_malformed_id_cannot_corrupt_saved_approvals_for_other_packages()
    {
        using var disk = new TestDirectory();
        disk.Package(Id);
        disk.Package("../escape", folder: "malformed");
        PluginOperationResult disabled;
        await using (var manager = disk.Manager())
        {
            await manager.DiscoverAsync();
            Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
            disabled = await manager.DisableAsync("../escape");
        }
        await using var restarted = disk.Manager();

        var package = Assert.Single(await restarted.DiscoverAsync(), p => p.Manifest?.Id == Id);

        Assert.True(package.IsTrusted, "An invalid ID must not enter the state store and invalidate other approvals.");
        Assert.False(disabled.Succeeded);
        Assert.Empty(restarted.Diagnostics);
    }
}
