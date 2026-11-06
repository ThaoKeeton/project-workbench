using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Workbench.Core;
using Xunit;
using static Workbench.Core.Tests.PackageArchiveTests;

namespace Workbench.Core.Tests;

public sealed class PackageRecoveryTests
{
    private const string Id = "com.example.recovery";
    // Removing phase reconciliation, fingerprint checks, quarantine journaling,
    // durable revocation or per-package error isolation breaks these real layouts.
    [Theory]
    [InlineData("Prepared", false, false)]
    [InlineData("BackedUp", false, false)]
    [InlineData("BackedUp", true, false)]
    [InlineData("OldMoved", true, false)]
    [InlineData("OldMoved", true, true)]
    [InlineData("NewMoved", true, true)]
    [InlineData("Committed", true, true)]
    public async Task Replays_before_and_after_each_move_idempotently(string phase, bool movedOld, bool movedNew)
    {
        using var disk = new TestDirectory();
        var layout = await PrepareAsync(disk, phase, movedOld, movedNew);
        using var marker = disk.WatchActivation();
        var store = Store(disk);
        Assert.True(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        Assert.Contains("2.0.0", File.ReadAllText(Path.Combine(layout.Target, "plugin.json")));
        Assert.Contains("1.0.0", File.ReadAllText(Path.Combine(layout.Old, "plugin.json")));
        Assert.Empty(await store.ApplyPendingAsync());
        Assert.Single(store.History);
        Assert.False(File.Exists(marker.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revalidates_staged_fingerprint_and_recovers_old_when_it_was_moved(bool moved)
    {
        using var disk = new TestDirectory();
        var layout = await PrepareAsync(disk, moved ? "OldMoved" : "Prepared", moved, false);
        File.WriteAllText(Path.Combine(layout.Stage, "added.txt"), "tampered stage");
        var store = Store(disk);
        Assert.False(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        Assert.Contains("1.0.0", File.ReadAllText(Path.Combine(layout.Target, "plugin.json")));
        Assert.True(File.Exists(Path.Combine(layout.Stage, "added.txt")));
        Assert.NotEmpty(store.Diagnostics);
        if (moved) Assert.Empty(await store.ApplyPendingAsync());
    }

    [Theory]
    [InlineData("NewMoved", false, false)]
    [InlineData("NewMoved", true, false)]
    [InlineData("RecoveryPrepared", false, false)]
    [InlineData("RecoveryPrepared", false, true)]
    [InlineData("Quarantined", false, true)]
    public async Task Damaged_or_missing_placed_new_code_recovers_old_and_preserves_quarantine(string phase, bool missing, bool movedToQuarantine)
    {
        using var disk = new TestDirectory();
        var layout = await PrepareAsync(disk, "NewMoved", true, true);
        File.WriteAllText(Path.Combine(layout.Target, "plugin.json"), "damaged-new-manifest");
        var quarantine = Path.Combine(layout.Operation, "quarantine");
        if (missing) Directory.Delete(layout.Target, recursive: true);
        if (movedToQuarantine) Directory.Move(layout.Target, quarantine);
        var json = JsonNode.Parse(File.ReadAllText(layout.Journal))!;
        json["phase"] = phase;
        File.WriteAllText(layout.Journal, json.ToJsonString());
        var store = Store(disk);
        Assert.False(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        Assert.Contains("1.0.0", File.ReadAllText(Path.Combine(layout.Target, "plugin.json")));
        if (!missing) Assert.Equal("damaged-new-manifest", File.ReadAllText(Path.Combine(quarantine, "plugin.json")));
        Assert.Empty(await store.ApplyPendingAsync());
        await using var manager = disk.Manager();
        Assert.False(Assert.Single(await manager.DiscoverAsync()).IsTrusted);
    }

    [Fact]
    public async Task Replays_interruption_after_old_code_was_restored_before_phase_write()
    {
        using var disk = new TestDirectory();
        var layout = await PrepareAsync(disk, "NewMoved", true, true);
        File.WriteAllText(Path.Combine(layout.Target, "damage.txt"), "preserved");
        Directory.Move(layout.Target, Path.Combine(layout.Operation, "quarantine"));
        Directory.Move(layout.Old, layout.Target);
        var json = JsonNode.Parse(File.ReadAllText(layout.Journal))!;
        json["phase"] = "Quarantined";
        File.WriteAllText(layout.Journal, json.ToJsonString());
        var store = Store(disk);
        Assert.False(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        Assert.Contains("1.0.0", File.ReadAllText(Path.Combine(layout.Target, "plugin.json")));
        Assert.Empty(await store.ApplyPendingAsync());
    }

    [Theory]
    [InlineData("old")]
    [InlineData("data")]
    [InlineData("unknown-phase")]
    [InlineData("escape")]
    [InlineData("unexpected-current")]
    public async Task Corruption_is_local_and_does_not_guess_or_destroy_unrelated_code(string damage)
    {
        using var disk = new TestDirectory();
        var layout = await PrepareAsync(disk, "BackedUp", false, false);
        var unrelated = disk.Package("com.example.unrelated");
        if (damage == "old")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(layout.Old)!);
            Directory.Move(layout.Target, layout.Old);
            File.WriteAllText(Path.Combine(layout.Old, "damage.txt"), "unknown old");
        }
        if (damage == "data") File.WriteAllText(Path.Combine(layout.Data, "damage.txt"), "unknown data");
        if (damage == "unexpected-current") File.WriteAllText(Path.Combine(layout.Target, "mine.txt"), "preserve current");
        if (damage is "unknown-phase" or "escape")
        {
            var json = JsonNode.Parse(File.ReadAllText(layout.Journal))!;
            json[damage == "escape" ? "targetDirectory" : "phase"] = damage == "escape" ? "../outside" : "UnknownFuturePhase";
            File.WriteAllText(layout.Journal, json.ToJsonString());
        }
        var store = Store(disk);
        Assert.False(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        Assert.True(File.Exists(layout.Journal));
        Assert.Contains("1.0.0", File.ReadAllText(Path.Combine(unrelated, "plugin.json")));
        Assert.False(Directory.Exists(Path.Combine(disk.Root, "outside")));
        if (damage == "unexpected-current") Assert.Equal("preserve current", File.ReadAllText(Path.Combine(layout.Target, "mine.txt")));
        Assert.NotEmpty(store.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Damaged_new_code_with_unsafe_or_unknown_old_is_preserved_in_place(bool linked)
    {
        using var disk = new TestDirectory();
        var layout = await PrepareAsync(disk, "NewMoved", true, true);
        File.WriteAllText(Path.Combine(layout.Target, "plugin.json"), "damaged-new");
        if (linked) File.CreateSymbolicLink(Path.Combine(layout.Old, "link"), Path.Combine(layout.Target, "plugin.json"));
        else File.WriteAllText(Path.Combine(layout.Old, "unknown.txt"), "unknown old");
        Assert.False(Assert.Single(await Store(disk).ApplyPendingAsync()).Succeeded);
        Assert.Equal("damaged-new", File.ReadAllText(Path.Combine(layout.Target, "plugin.json")));
        Assert.False(Directory.Exists(Path.Combine(layout.Operation, "quarantine")));
        Assert.True(File.Exists(layout.Journal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task An_occupied_metadata_directory_reports_failure_and_blocks_saved_approval(bool pendingDirectory, bool storeInstall)
    {
        using var disk = new TestDirectory();
        var target = disk.Package(Id);
        var package = await PackageValidator.ValidateAsync(target, new(0, 2, 0), default);
        await new PluginStateStore(disk.User).SaveAsync(Id, true, package.Fingerprint, default);
        const string importId = "com.example.new-install";
        var import = disk.Package(importId, external: true);
        var importedPackage = await PackageValidator.ValidateAsync(import, new(0, 2, 0), default);
        await new PluginStateStore(disk.User).SaveAsync(importId, true, importedPackage.Fingerprint, default);
        await new PluginRuntimeState(disk.User).SaveAsync(importId, importedPackage.Fingerprint!, "job", new(true, true), _ => { }, default);
        var approvalPath = Path.Combine(disk.User, "plugin-state.json");
        var intentPath = Path.Combine(disk.User, "background-tasks.json");
        var approval = File.ReadAllBytes(approvalPath);
        var intent = File.ReadAllBytes(intentPath);
        var metadata = Path.Combine(disk.Plugins, ".workbench-packages");
        if (pendingDirectory) Directory.CreateDirectory(metadata);
        var occupied = pendingDirectory ? Path.Combine(metadata, "pending") : metadata;
        File.WriteAllText(occupied, "preserve unknown metadata bytes");
        var store = Store(disk);
        Assert.False(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        Assert.NotEmpty(store.Diagnostics);
        Assert.Equal("preserve unknown metadata bytes", File.ReadAllText(occupied));
        await using var manager = disk.Manager();
        var installed = storeInstall ? await store.InstallAsync(import) : await manager.InstallAsync(import);
        Assert.False(installed.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(disk.Plugins, importId)));
        Assert.False(File.Exists(Path.Combine(disk.Plugins, importId)));
        Assert.Equal(approval, File.ReadAllBytes(approvalPath));
        Assert.Equal(intent, File.ReadAllBytes(intentPath));
        Assert.Equal("preserve unknown metadata bytes", File.ReadAllText(occupied));
        var snapshot = Assert.Single(await manager.DiscoverAsync());
        Assert.Equal(PluginState.Invalid, snapshot.State);
        Assert.False(snapshot.IsTrusted);
        Assert.True(File.Exists(Path.Combine(target, "plugin.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_occupied_id_journal_directory_blocks_install_without_changing_saved_approval_or_intent(bool storeInstall)
    {
        using var disk = new TestDirectory();
        var source = disk.Package(Id, external: true);
        var package = await PackageValidator.ValidateAsync(source, new(0, 2, 0), default);
        await new PluginStateStore(disk.User).SaveAsync(Id, true, package.Fingerprint, default);
        await new PluginRuntimeState(disk.User).SaveAsync(Id, package.Fingerprint!, "job", new(true, true), _ => { }, default);
        var approvalPath = Path.Combine(disk.User, "plugin-state.json");
        var intentPath = Path.Combine(disk.User, "background-tasks.json");
        var approval = File.ReadAllBytes(approvalPath);
        var intent = File.ReadAllBytes(intentPath);
        var occupied = Path.Combine(disk.Plugins, ".workbench-packages", "pending", Id + ".json");
        Directory.CreateDirectory(occupied);
        var unknownPath = Path.Combine(occupied, "unknown.json");
        File.WriteAllText(unknownPath, "preserve unknown journal ownership");
        await using var manager = disk.Manager();
        var installed = storeInstall ? await Store(disk).InstallAsync(source) : await manager.InstallAsync(source);
        Assert.False(installed.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(disk.Plugins, Id)));
        Assert.False(File.Exists(Path.Combine(disk.Plugins, Id)));
        Assert.Equal(approval, File.ReadAllBytes(approvalPath));
        Assert.Equal(intent, File.ReadAllBytes(intentPath));
        Assert.True(Directory.Exists(occupied));
        Assert.Equal("preserve unknown journal ownership", File.ReadAllText(unknownPath));
        Assert.Equal(unknownPath, Assert.Single(Directory.GetFileSystemEntries(occupied)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrupt_managed_metadata_blocks_only_its_root_and_preserves_bundled_activation(bool rediscover)
    {
        using var disk = new TestDirectory();
        const string managedId = "com.example.managed";
        const string bundledId = "com.example.bundled";
        var managed = disk.Package(managedId);
        var package = await PackageValidator.ValidateAsync(managed, new(0, 2, 0), default);
        await new PluginStateStore(disk.User).SaveAsync(managedId, true, package.Fingerprint, default);
        var source = disk.Package(bundledId, external: true);
        var bundled = Path.Combine(disk.Root, "bundled");
        Directory.CreateDirectory(bundled);
        Directory.Move(source, Path.Combine(bundled, bundledId));
        await using var manager = new PluginManager(new PluginManagerOptions
        {
            PluginsRoot = disk.Plugins,
            UserRoot = disk.User,
            AdditionalDiscoveryRoots = [bundled]
        });
        if (rediscover)
            Assert.True((await manager.DiscoverAsync()).Single(plugin => plugin.Manifest?.Id == managedId).IsTrusted);
        File.WriteAllText(Path.Combine(disk.Plugins, ".workbench-packages"), "preserve unreadable managed ownership");
        var snapshots = await manager.DiscoverAsync();
        var blocked = snapshots.Single(plugin => plugin.Manifest?.Id == managedId);
        Assert.Equal(PluginState.Invalid, blocked.State);
        Assert.False(blocked.IsTrusted);
        Assert.Contains(blocked.Diagnostics, diagnostic => diagnostic.Code == PluginDiagnosticCode.PackageMaintenancePending);
        Assert.False((await manager.EnableAsync(managedId, confirmTrust: true)).Succeeded);
        var enabled = await manager.EnableAsync(bundledId, confirmTrust: true);
        Assert.True(enabled.Succeeded, enabled.Message);
        await manager.DiscoverAsync();
        Assert.True((await manager.ExecuteCommandAsync(bundledId, "ping")).Succeeded);
        Assert.True(manager.Plugins.Single(plugin => plugin.Manifest?.Id == bundledId).IsTrusted);
    }

    [Fact]
    public async Task Cancel_refuses_to_discard_a_partially_applied_operation()
    {
        using var disk = new TestDirectory();
        var layout = await PrepareAsync(disk, "BackedUp", true, false);
        Assert.False((await Store(disk).CancelPendingAsync(Id)).Succeeded);
        Assert.True(File.Exists(layout.Journal));
        Assert.True(Directory.Exists(layout.Old));
        Assert.True(Directory.Exists(layout.Stage));
        Assert.True(Assert.Single(await Store(disk).ApplyPendingAsync()).Succeeded);
    }

    [Fact]
    public async Task Failed_approval_revocation_blocks_discovery_and_activation_until_replay()
    {
        using var disk = new TestDirectory();
        var layout = await PrepareAsync(disk, "NewMoved", true, true);
        var package = await PackageValidator.ValidateAsync(layout.Target, new(0, 2, 0), default);
        var preferences = new PluginStateStore(disk.User);
        await preferences.SaveAsync(Id, true, package.Fingerprint, default);
        var statePath = Path.Combine(disk.User, "plugin-state.json");
        File.Move(statePath, statePath + ".saved");
        Directory.CreateDirectory(statePath);
        var store = Store(disk);
        Assert.False(Assert.Single(await store.ApplyPendingAsync()).Succeeded);
        // Restore an actual saved approval before discover: the incomplete
        // journal, rather than a corrupt-state default, must block activation.
        Directory.Delete(statePath);
        File.Move(statePath + ".saved", statePath);
        await using var manager = disk.Manager();
        var plugin = Assert.Single(await manager.DiscoverAsync());
        Assert.Equal(PluginState.Invalid, plugin.State);
        Assert.False(plugin.IsTrusted);
        Assert.False((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
    }

    [Fact]
    public async Task A_real_restart_applies_before_discovery_and_original_fingerprint_rollback_remains_untrusted()
    {
        using var disk = new TestDirectory();
        const string id = "com.example.fixture";
        disk.Package(id);
        var approved = await ProbeAsync(disk, id, null);
        Assert.True(approved.GetProperty("Enabled").GetBoolean());
        var originalState = File.ReadAllText(Path.Combine(disk.User, "plugin-state.json"));
        Assert.Contains("\"enabled\": true", originalState);
        var source = disk.Package(id, external: true, fields: new() { ["version"] = "2.0.0" });
        var store = Store(disk);
        Assert.True((await store.StageUpdateAsync(id, source)).Succeeded);
        var updated = await ProbeAsync(disk, id, "package-apply");
        Assert.True(updated.GetProperty("Applied").GetBoolean());
        Assert.False(updated.GetProperty("Trusted").GetBoolean());
        Assert.False(updated.GetProperty("DesiredEnabled").GetBoolean());
        Assert.False(updated.GetProperty("Loaded").GetBoolean());
        await store.RefreshAsync();
        var history = Assert.Single(store.History);
        Assert.True((await store.StageRollbackAsync(id, history.HistoryId)).Succeeded);
        var restored = await ProbeAsync(disk, id, "package-apply");
        Assert.Equal("1.0.0", restored.GetProperty("Version").GetString());
        Assert.False(restored.GetProperty("Trusted").GetBoolean());
        Assert.False(restored.GetProperty("Loaded").GetBoolean());
    }

    internal static async Task<JsonElement> ProbeAsync(TestDirectory disk, string id, string? operation)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "deployment-probe", OperatingSystem.IsWindows() ? "Workbench.DeploymentProbe.exe" : "Workbench.DeploymentProbe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(disk.Plugins);
        start.ArgumentList.Add(disk.User);
        start.ArgumentList.Add(id);
        if (operation is not null) start.ArgumentList.Add(operation);
        using var process = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.ExitCode == 0, await error);
            using var document = JsonDocument.Parse(await output);
            return document.RootElement.Clone();
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private sealed record Layout(string Target, string Stage, string Old, string Data, string Journal, string Operation);
    private static async Task<Layout> PrepareAsync(TestDirectory disk, string phase, bool movedOld, bool movedNew)
    {
        var target = disk.Package(Id);
        var source = disk.Package(Id, external: true, fields: new() { ["version"] = "2.0.0" });
        Assert.True((await Store(disk).StageUpdateAsync(Id, source)).Succeeded);
        var root = Path.Combine(disk.Plugins, ".workbench-packages");
        var journal = Path.Combine(root, "pending", Id + ".json");
        var json = JsonNode.Parse(File.ReadAllText(journal))!;
        var operationId = json["operationId"]!.GetValue<string>();
        var operation = Path.Combine(root, "operations", Id, operationId);
        var stage = Path.Combine(operation, "new");
        var history = Path.Combine(root, "history", Id, operationId);
        var old = Path.Combine(history, "code");
        var data = Path.Combine(history, "data");
        if (phase != "Prepared")
        {
            Directory.CreateDirectory(data);
            json["dataFingerprint"] = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
        }
        if (movedOld) Directory.Move(target, old);
        if (movedNew) Directory.Move(stage, target);
        json["phase"] = phase;
        File.WriteAllText(journal, json.ToJsonString());
        return new(target, stage, old, data, journal, operation);
    }
}
