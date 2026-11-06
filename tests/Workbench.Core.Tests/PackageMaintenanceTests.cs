using System.Text.Json;
using Workbench.Core;
using Xunit;
using static Workbench.Core.Tests.PackageArchiveTests;

namespace Workbench.Core.Tests;

public sealed class PackageMaintenanceTests
{
    // These observe real code/data/preferences. Missing stage/apply separation,
    // process-wide ownership, bounded backup or durable revocation breaks them.
    [Fact]
    public async Task Stores_and_legacy_install_serialize_mutations_of_the_same_root()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.maintain");
        var source = disk.Package("com.example.maintain", external: true, fields: new() { ["version"] = "2.0.0" });
        var staged = await Task.WhenAll(Store(disk).StageUpdateAsync("com.example.maintain", source),
            Store(disk).StageRemoveAsync("com.example.maintain"));
        Assert.Single(staged, result => result.Succeeded);
        Assert.Single(Directory.GetFiles(Path.Combine(disk.Plugins, ".workbench-packages", "pending")));
        var installSource = disk.Package("com.example.install", external: true);
        await using var manager = disk.Manager();
        var installed = await Task.WhenAll(Store(disk).InstallAsync(installSource), manager.InstallAsync(installSource));
        Assert.Single(installed, result => result.Succeeded);
        Assert.Single(Directory.GetDirectories(disk.Plugins, "com.example.install"));
    }

    [Fact]
    public async Task Ordinary_staging_and_rediscovery_leave_trusted_runtime_usable()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.fixture");
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync("com.example.fixture", confirmTrust: true)).Succeeded);
        var source = disk.Package("com.example.fixture", external: true, fields: new() { ["version"] = "2.0.0" });
        Assert.True((await Store(disk).StageUpdateAsync("com.example.fixture", source)).Succeeded);
        await Store(disk).RefreshAsync();
        var plugin = Assert.Single(await manager.DiscoverAsync());
        Assert.Equal(PluginState.Enabled, plugin.State);
        Assert.True(plugin.IsTrusted);
        Assert.True((await manager.ExecuteCommandAsync("com.example.fixture", "ping")).Succeeded);
    }

    [Fact]
    public async Task Store_install_cannot_reacquire_approval_or_consent_from_a_long_lived_manager_cache()
    {
        using var disk = new TestDirectory();
        const string id = "com.example.reinstall";
        var source = disk.Package(id, external: true);
        var package = await PackageValidator.ValidateAsync(source, new(0, 2, 0), default);
        var preferences = new PluginStateStore(disk.User);
        await preferences.SaveAsync(id, true, package.Fingerprint, default);
        var runtime = new PluginRuntimeState(disk.User);
        await runtime.SaveAsync(id, package.Fingerprint!, "job", new(true, true), _ => { }, default);
        disk.Package("com.example.other");
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync("com.example.other", confirmTrust: true)).Succeeded);
        Assert.True((await Store(disk).InstallAsync(source)).Succeeded);
        // Another preference write must not resurrect the cached approval.
        Assert.True((await manager.DisableAsync("com.example.other")).Succeeded);
        var plugin = (await manager.DiscoverAsync()).Single(plugin => plugin.Manifest?.Id == id);
        Assert.False(plugin.IsTrusted);
        Assert.False(plugin.DesiredEnabled);
        // Runtime persistence by another long-lived state owner must merge the
        // actual document, rather than rewriting previously removed consent.
        await runtime.SaveAsync("com.example.other", package.Fingerprint!, "other", new(false, false), _ => { }, default);
        using var tasks = JsonDocument.Parse(File.ReadAllText(Path.Combine(disk.User, "background-tasks.json")));
        Assert.False(tasks.RootElement.GetProperty("plugins").TryGetProperty(id, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Identical_reinstall_revokes_an_existing_unloaded_catalog_entry(bool rediscover)
    {
        using var disk = new TestDirectory();
        const string id = "com.example.reinstall";
        var current = disk.Package(id);
        var source = disk.Package(id, external: true);
        var package = await PackageValidator.ValidateAsync(current, new(0, 2, 0), default);
        await new PluginStateStore(disk.User).SaveAsync(id, true, package.Fingerprint, default);
        await using var manager = disk.Manager();
        Assert.True(Assert.Single(await manager.DiscoverAsync()).IsTrusted);
        Directory.Delete(current, recursive: true); // Never loaded, so removal is portable.
        Assert.True((await Store(disk).InstallAsync(source)).Succeeded);
        using var marker = disk.WatchActivation();
        if (rediscover)
        {
            var snapshot = Assert.Single(await manager.DiscoverAsync());
            Assert.False(snapshot.IsTrusted);
            Assert.False(snapshot.DesiredEnabled);
        }
        Assert.False((await manager.EnableAsync(id)).Succeeded);
        Assert.Empty(await manager.RestoreEnabledAsync());
        Assert.False(File.Exists(marker.Path));
    }

    [Fact]
    public async Task A_stale_runtime_writer_cannot_resurrect_consent_removed_by_a_second_store()
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.reinstall", external: true);
        var package = await PackageValidator.ValidateAsync(source, new(0, 2, 0), default);
        var runtime = new PluginRuntimeState(disk.User);
        await runtime.SaveAsync("com.example.reinstall", package.Fingerprint!, "job", new(true, true), _ => { }, default);
        Assert.True((await Store(disk).InstallAsync(source)).Succeeded);
        await runtime.SaveAsync("com.example.other", package.Fingerprint!, "other", new(false, false), _ => { }, default);
        using var tasks = JsonDocument.Parse(File.ReadAllText(Path.Combine(disk.User, "background-tasks.json")));
        Assert.False(tasks.RootElement.GetProperty("plugins").TryGetProperty("com.example.reinstall", out _));
    }

    [Fact]
    public async Task A_changed_data_tree_during_backup_rejects_before_moving_code()
    {
        using var disk = new TestDirectory();
        var target = disk.Package("com.example.maintain");
        var data = Path.Combine(disk.User, "data", "com.example.maintain");
        Directory.CreateDirectory(data);
        for (var i = 0; i < 1000; i++) File.WriteAllText(Path.Combine(data, $"f{i:D4}"), "original");
        var source = disk.Package("com.example.maintain", external: true, fields: new() { ["version"] = "2.0.0" });
        var store = Store(disk);
        Assert.True((await store.StageUpdateAsync("com.example.maintain", source)).Succeeded);
        var paused = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var apply = ApplyWithBackupCheckpointAsync(disk, store, 1000, paused, release);
        try
        {
            Assert.Same(paused.Task, await Task.WhenAny(paused.Task, apply).WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.InRange(await paused.Task, 1, 999);
            Assert.False(apply.IsCompleted);
            File.WriteAllText(Path.Combine(data, "added-during-copy"), "current business data");
        }
        finally { release.Set(); }
        Assert.False(Assert.Single(await apply).Succeeded);
        Assert.Contains("1.0.0", File.ReadAllText(Path.Combine(target, "plugin.json")));
        Assert.Equal("current business data", File.ReadAllText(Path.Combine(data, "added-during-copy")));
    }

    [Fact]
    public async Task A_data_change_after_backup_and_apply_complete_is_legal_and_does_not_rewrite_the_backup()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.maintain");
        var data = Path.Combine(disk.User, "data", "com.example.maintain");
        Directory.CreateDirectory(data);
        for (var i = 0; i < 1000; i++) File.WriteAllText(Path.Combine(data, $"f{i:D4}"), "original");
        var source = disk.Package("com.example.maintain", external: true, fields: new() { ["version"] = "2.0.0" });
        var store = Store(disk);
        Assert.True((await store.StageUpdateAsync("com.example.maintain", source)).Succeeded);
        var paused = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var apply = ApplyWithBackupCheckpointAsync(disk, store, 1000, paused, release);
        try
        {
            Assert.Same(paused.Task, await Task.WhenAny(paused.Task, apply).WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.InRange(await paused.Task, 1, 999);
            Assert.False(apply.IsCompleted);
        }
        finally { release.Set(); }
        Assert.True(Assert.Single(await apply).Succeeded);
        File.WriteAllText(Path.Combine(data, "added-after-copy"), "later business data");
        var backup = Assert.Single(store.History).DataBackupPath!;
        Assert.False(File.Exists(Path.Combine(backup, "data", "com.example.maintain", "added-after-copy")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(backup, "data", "com.example.maintain", "f0000")));
        Assert.Equal("later business data", File.ReadAllText(Path.Combine(data, "added-after-copy")));
    }

    private static Task<IReadOnlyList<PluginOperationResult>> ApplyWithBackupCheckpointAsync(TestDirectory disk,
        PluginPackageStore store, int sourceCount, TaskCompletionSource<int> paused, ManualResetEventSlim release)
    {
        var history = Path.Combine(disk.Plugins, ".workbench-packages", "history");
        return TestDispatcher.RunAsync(() => store.ApplyPendingAsync(), () =>
        {
            if (paused.Task.IsCompleted || !Directory.Exists(history)) return;
            var temporary = Directory.EnumerateDirectories(history, "data.preparing-*", SearchOption.AllDirectories).SingleOrDefault();
            if (temporary is null) return;
            var copied = Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories).Count();
            if (copied == 0 || copied >= sourceCount) return;
            // Hold the actual backup continuation, with a strict partial copy.
            // Revalidation and the commit rename cannot race the test writer.
            paused.TrySetResult(copied);
            Assert.True(release.Wait(TimeSpan.FromSeconds(20)), "The test did not release its backup checkpoint.");
        });
    }

    [Fact]
    public async Task Stages_one_update_without_changing_active_files_and_can_cancel()
    {
        using var disk = new TestDirectory();
        var current = disk.Package("com.example.maintain", folder: "original-folder");
        var source = disk.Package("com.example.maintain", external: true, fields: new() { ["version"] = "2.0.0" });
        var old = File.ReadAllText(Path.Combine(current, "plugin.json"));
        var store = Store(disk);
        Assert.True((await store.StageUpdateAsync("com.example.maintain", source)).Succeeded);
        Assert.Equal(old, File.ReadAllText(Path.Combine(current, "plugin.json")));
        Assert.Single(store.PendingOperations);
        Assert.False((await store.StageRemoveAsync("com.example.maintain")).Succeeded);
        Assert.True((await store.CancelPendingAsync("com.example.maintain")).Succeeded);
        Assert.Empty(store.PendingOperations);
        Assert.Empty(Store(disk).History);
        Assert.Equal(old, File.ReadAllText(Path.Combine(current, "plugin.json")));
    }

    [Theory]
    [InlineData("id", "com.example.other")]
    [InlineData("version", "1.0.0")]
    [InlineData("version", "0.9.0")]
    [InlineData("apiVersion", 2)]
    [InlineData("minimumHostVersion", "99.0.0")]
    public async Task Refuses_wrong_id_version_api_and_host(string field, object value)
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.maintain");
        var source = disk.Package("com.example.maintain", external: true,
            fields: new() { ["version"] = "2.0.0", [field] = value });
        Assert.False((await Store(disk).StageUpdateAsync("com.example.maintain", source)).Succeeded);
    }

    [Fact]
    public async Task Bundled_ownership_reserves_ids_and_refuses_maintenance()
    {
        using var disk = new TestDirectory();
        var bundled = Path.Combine(disk.Root, "bundled");
        Directory.CreateDirectory(bundled);
        var package = disk.Package("com.example.maintain", external: true);
        Directory.Move(package, Path.Combine(bundled, "sample"));
        var store = Store(disk, bundled);
        var source = disk.Package("com.example.maintain", external: true, fields: new() { ["version"] = "2.0.0" });
        Assert.False((await store.InstallAsync(source)).Succeeded);
        Assert.False((await store.StageUpdateAsync("com.example.maintain", source)).Succeeded);
        Assert.False((await store.StageRemoveAsync("com.example.maintain")).Succeeded);
    }

    [Fact]
    public async Task Apply_is_rejected_after_discovery_even_without_loading()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.maintain");
        var store = Store(disk);
        Assert.True((await store.StageRemoveAsync("com.example.maintain")).Succeeded);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.False(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        Assert.True(Directory.Exists(Path.Combine(disk.Plugins, "com.example.maintain")));
    }

    [Fact]
    public async Task Root_spellings_with_a_trailing_separator_share_ownership_and_startup_seals()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.maintain");
        var store = new PluginPackageStore(new PluginManagerOptions
        {
            PluginsRoot = disk.Plugins + Path.DirectorySeparatorChar,
            UserRoot = disk.User
        });
        var staged = await store.StageRemoveAsync("com.example.maintain");
        Assert.True(staged.Succeeded, staged.Message);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        Assert.False(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
    }

    [Fact]
    public async Task Discovery_through_an_additional_root_also_closes_its_apply_window()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.maintain");
        var store = Store(disk);
        Assert.True((await store.StageRemoveAsync("com.example.maintain")).Succeeded);
        await using var manager = new PluginManager(new PluginManagerOptions
        {
            PluginsRoot = Path.Combine(disk.Root, "another-managed-root"),
            UserRoot = disk.User,
            AdditionalDiscoveryRoots = [disk.Plugins]
        });
        await manager.DiscoverAsync();
        Assert.False(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
    }

    [Fact]
    public async Task Loaded_path_blocks_apply_without_discovery_and_install_after_directory_was_moved()
    {
        using var disk = new TestDirectory();
        var current = disk.Package("com.example.fixture");
        var package = await PackageValidator.ValidateAsync(current, new(0, 2, 0), default);
        var context = new PluginLoadContext(package, [], []);
        _ = context.LoadFromAssemblyPath(Path.Combine(current, "Workbench.TestPlugin.dll"));
        var store = Store(disk);
        Assert.True((await store.StageRemoveAsync("com.example.fixture")).Succeeded);
        var refusal = Assert.Single(await store.ApplyPendingAsync());
        Assert.False(refusal.Succeeded);
        Assert.Contains("loaded", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True((await store.CancelPendingAsync("com.example.fixture")).Succeeded);
        var source = disk.Package("com.example.fixture", external: true);
        var retained = current + ".retained";
        Directory.Move(current, retained);
        try
        {
            Assert.False((await store.InstallAsync(source)).Succeeded);
            await using var manager = disk.Manager();
            Assert.False((await manager.InstallAsync(source)).Succeeded);
            Assert.False(Directory.Exists(current));
        }
        finally { Directory.Move(retained, current); }
    }

    [Fact]
    public async Task Loaded_descendants_protect_a_parent_package_from_directory_moves()
    {
        using var disk = new TestDirectory();
        var parent = disk.Package("com.example.parent");
        var child = disk.Package("com.example.child", folder: Path.Combine("com.example.parent", "nested"));
        var package = await PackageValidator.ValidateAsync(child, new(0, 2, 0), default);
        var context = new PluginLoadContext(package, [], []);
        _ = context.LoadFromAssemblyPath(Path.Combine(child, "Workbench.TestPlugin.dll"));
        var store = Store(disk);
        Assert.True((await store.StageRemoveAsync("com.example.parent")).Succeeded);
        var result = Assert.Single(await store.ApplyPendingAsync());
        Assert.False(result.Succeeded);
        Assert.Contains("loaded", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(parent));
    }

    [Fact]
    public async Task Ever_loaded_path_is_protected_across_manager_disposal_and_new_store()
    {
        using var disk = new TestDirectory();
        var current = disk.Package("com.example.fixture");
        await using (var manager = disk.Manager())
        {
            await manager.DiscoverAsync();
            Assert.True((await manager.EnableAsync("com.example.fixture", confirmTrust: true)).Succeeded);
            Assert.True((await manager.DisableAsync("com.example.fixture")).Succeeded);
        }
        var store = Store(disk);
        Assert.True((await store.StageRemoveAsync("com.example.fixture")).Succeeded);
        Assert.False(Assert.Single(await Store(disk).ApplyPendingAsync()).Succeeded);
        Assert.True(File.Exists(Path.Combine(current, "Workbench.TestPlugin.dll")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_and_rollback_revoke_original_approval_and_task_consent(bool rollback)
    {
        using var disk = new TestDirectory();
        var current = disk.Package("com.example.maintain");
        var old = await PackageValidator.ValidateAsync(current, new(0, 2, 0), default);
        var preferences = new PluginStateStore(disk.User);
        await preferences.SaveAsync("com.example.maintain", true, old.Fingerprint, default);
        var runtime = new PluginRuntimeState(disk.User);
        await runtime.SaveAsync("com.example.maintain", old.Fingerprint!, "job", new(true, true), _ => { }, default);
        var source = disk.Package("com.example.maintain", external: true, fields: new() { ["version"] = "2.0.0" });
        var store = Store(disk);
        Assert.True((await store.StageUpdateAsync("com.example.maintain", source)).Succeeded);
        Assert.True(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        if (rollback)
        {
            // The old fingerprint was approved in the ORIGINAL preference file.
            var history = Assert.Single(store.History);
            Assert.True((await store.StageRollbackAsync("com.example.maintain", history.HistoryId)).Succeeded);
            Assert.True(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        }
        await using var restarted = disk.Manager();
        var plugin = Assert.Single(await restarted.DiscoverAsync());
        Assert.False(plugin.IsTrusted);
        Assert.False(plugin.DesiredEnabled);
        Assert.Empty(await restarted.RestoreEnabledAsync());
        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(disk.User, "plugin-state.json")));
        Assert.False(state.RootElement.GetProperty("plugins").GetProperty("com.example.maintain").GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, state.RootElement.GetProperty("plugins").GetProperty("com.example.maintain").GetProperty("trustedFingerprint").ValueKind);
        using var tasks = JsonDocument.Parse(File.ReadAllText(Path.Combine(disk.User, "background-tasks.json")));
        Assert.False(tasks.RootElement.GetProperty("plugins").TryGetProperty("com.example.maintain", out _));
    }

    [Fact]
    public async Task Backup_copies_only_host_managed_state_and_remove_preserves_it()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.maintain");
        foreach (var name in new[] { "data", "settings", "secrets" })
        {
            var directory = Path.Combine(disk.User, name, "com.example.maintain");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "state.bin"), $"synthetic-{name}");
        }
        var external = Path.Combine(disk.Root, "canonical-external.json");
        File.WriteAllText(external, "external-current");
        var store = Store(disk);
        Assert.True((await store.StageRemoveAsync("com.example.maintain")).Succeeded);
        Assert.True(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        var history = Assert.Single(store.History);
        Assert.True(Directory.Exists(history.CodePath));
        Assert.DoesNotContain(Directory.GetFiles(history.DataBackupPath!, "*", SearchOption.AllDirectories), p => p.Contains("canonical-external", StringComparison.Ordinal));
        foreach (var name in new[] { "data", "settings", "secrets" })
        {
            Assert.Equal($"synthetic-{name}", File.ReadAllText(Path.Combine(disk.User, name, "com.example.maintain", "state.bin")));
            Assert.Equal($"synthetic-{name}", File.ReadAllText(Path.Combine(history.DataBackupPath!, name, "com.example.maintain", "state.bin")));
        }
        Assert.Equal("external-current", File.ReadAllText(external));
        Assert.True((await store.StageRollbackAsync("com.example.maintain", history.HistoryId)).Succeeded);
        File.WriteAllText(Path.Combine(disk.User, "data", "com.example.maintain", "state.bin"), "new-current-data");
        Assert.True(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        Assert.Equal("new-current-data", File.ReadAllText(Path.Combine(disk.User, "data", "com.example.maintain", "state.bin")));
    }

    [Theory]
    [InlineData("link")]
    [InlineData("size")]
    [InlineData("count")]
    public async Task Unsafe_or_unbounded_data_backup_leaves_old_code_untouched(string kind)
    {
        using var disk = new TestDirectory();
        var current = disk.Package("com.example.maintain");
        var data = Path.Combine(disk.User, "data", "com.example.maintain");
        Directory.CreateDirectory(data);
        if (kind == "link") File.CreateSymbolicLink(Path.Combine(data, "link"), Path.Combine(current, "plugin.json"));
        if (kind == "size") using (var file = File.Create(Path.Combine(data, "large"))) file.SetLength(1024L * 1024 * 1024 + 1);
        if (kind == "count") for (var i = 0; i <= 10000; i++) File.WriteAllText(Path.Combine(data, $"f{i}"), "x");
        var source = disk.Package("com.example.maintain", external: true, fields: new() { ["version"] = "2.0.0" });
        var store = Store(disk);
        Assert.True((await store.StageUpdateAsync("com.example.maintain", source)).Succeeded);
        Assert.False(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        Assert.Contains("1.0.0", File.ReadAllText(Path.Combine(current, "plugin.json")));
    }
}
