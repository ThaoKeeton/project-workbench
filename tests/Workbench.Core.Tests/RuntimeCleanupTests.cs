using System.Text.Json;
using System.Text.Json.Serialization;
using Workbench.App;
using Workbench.Sdk;
using Xunit;
using Xunit.Abstractions;

namespace Workbench.Core.Tests;

public sealed class RuntimeCleanupTests(ITestOutputHelper output)
{
    private const string Id = "com.example.runtime";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_settings_IO_keeps_original_stop_and_root_owned_until_cleanup_joins(bool runningBackground)
    {
        using var headroom = new CallbackWorkerHeadroom();
        using var disk = BackgroundTaskTests.Package();
        var runtime = new HostRuntime(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User, StopTimeout = TimeSpan.FromMilliseconds(75) });
        var manager = runtime.Manager;
        var temporary = Path.Combine(disk.User, "settings", Id, $".settings.json.{Guid.NewGuid():N}.tmp");
        var held = new HeldSettingWrite(temporary);
        Task? write = null, cleanup = null;
        try
        {
            await runtime.InitializeAsync();
            Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
            var settings = manager.GetSettings(Id);
            if (runningBackground)
            {
                Assert.True((await manager.StartBackgroundTaskAsync(Id, "monitor")).Succeeded);
                await RuntimeTest.WaitAsync(async () => await settings.GetAsync("jobRuns", 0) == 1);
            }
            // The real settings serializer owns this actual FileShare.None stream while holding
            // its ordinary settings gate. Plugin Stop/settings writes must queue behind it.
            write = Task.Run(() => settings.SetAsync("heldWrite", held));
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<PluginShutdownException>(() => runtime.DisposeAsync().AsTask());
            output.WriteLine($"One-shot Dispose: background={runningBackground}, pending={manager.HasPendingStops}, settingWrite={write.Status}, sameManager={ReferenceEquals(manager, runtime.Manager)}");
            Assert.True(manager.HasPendingStops);
            Assert.True(Assert.Single(manager.Plugins).HasRuntime);
            Assert.False(write.IsCompleted);
            Assert.ThrowsAny<IOException>(() => { using var other = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
            Assert.ThrowsAny<IOException>(() => { using var other = HostRuntime.AcquireLease(disk.User); });

            var bodyFailure = new Xunit.Sdk.XunitException("Synthetic body assertion before owned settings cleanup.");
            cleanup = RuntimeTest.WithCleanupAsync(runtime, () => throw bodyFailure);
            await RuntimeTest.WaitAsync(() => Task.FromResult(Assert.Single(manager.Plugins).Diagnostics
                .Count(diagnostic => diagnostic.Code == PluginDiagnosticCode.StopTimedOut) >= 2));
            Assert.False(cleanup.IsCompleted, "A pending Dispose is not completed cleanup and cannot authorize directory deletion.");
            Assert.Same(manager, runtime.Manager);
            Assert.ThrowsAny<IOException>(() => { using var other = HostRuntime.AcquireLease(disk.User); });

            held.Release.TrySetResult();
            await write.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(bodyFailure, await Record.ExceptionAsync(() => cleanup));
            Assert.False(manager.HasPendingStops);
            Assert.False(Assert.Single(manager.Plugins).HasRuntime);
            using var persisted = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(disk.User, "settings", Id, "settings.json")));
            Assert.Equal("held settings write", persisted.RootElement.GetProperty("heldWrite").GetProperty("value").GetString());
            Assert.Equal(1, persisted.RootElement.GetProperty("stopCalls").GetInt32());
            Assert.Equal("HostShutdown", persisted.RootElement.GetProperty("contextStopReason").GetString());
            if (runningBackground) Assert.True(persisted.RootElement.GetProperty("jobFinished").GetBoolean());
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(disk.User, "settings", Id), ".*.tmp"));
            output.WriteLine($"Joined cleanup: pending={manager.HasPendingStops}, settingWrite={write.Status}, stopCalls=1, settingsTemps=0");
            // The helper must retire the HostRuntime too, before any directory is removed.
            using var nextOwner = HostRuntime.AcquireLease(disk.User);
        }
        finally
        {
            held.Release.TrySetResult();
            if (write is not null) await write;
            if (cleanup is not null) _ = await Record.ExceptionAsync(() => cleanup);
            await RuntimeTest.DisposeAsync(manager);
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task Host_cleanup_deadline_preserves_body_release_and_retirement_errors_and_the_live_root()
    {
        var disk = new TestDirectory();
        disk.Package(Id, fields: new() { ["entryType"] = "Workbench.TestPlugin.ControlledStopPlugin" });
        var runtime = new HostRuntime(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User, StopTimeout = TimeSpan.FromMilliseconds(75) });
        var manager = runtime.Manager;
        IPluginSettings? settings = null;
        Task? cleanup = null;
        var bodyFailure = new Xunit.Sdk.XunitException("Synthetic primary assertion before retained host cleanup.");
        var releaseFailure = new IOException("Synthetic release failure; the real Stop gate is still held.");
        await RuntimeTest.WithCleanupAsync(runtime, async () =>
        {
            await runtime.InitializeAsync();
            settings = manager.GetSettings(Id);
            Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
            cleanup = RuntimeTest.WithCleanupAsync(runtime, () => throw bodyFailure, disk,
                beforeCleanup: () => throw releaseFailure);
            var failure = await Record.ExceptionAsync(() => cleanup);
            CheckFailures(failure);
            Assert.True(manager.HasPendingStops);
            Assert.True(Assert.Single(manager.Plugins).HasRuntime);
            Assert.Equal(1, await settings.GetAsync("stopCalls", 0));
            Assert.True(Directory.Exists(disk.User));
            Assert.True(File.Exists(Path.Combine(disk.User, "host.lock")));
            Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(disk.User); });
            output.WriteLine("Actual Stop gate outlived the unchanged cleanup deadline: body/release/retirement/pending errors retained, stopCalls=1, directory and root lease retained.");
        }, disk, beforeCleanup: async () =>
        {
            if (settings is not null) await settings.SetAsync("releaseStop", true);
            if (cleanup is not null) CheckFailures(await Record.ExceptionAsync(() => cleanup));
        });
        Assert.False(Directory.Exists(disk.User));

        void CheckFailures(Exception? failure)
        {
            var aggregate = Assert.IsType<AggregateException>(failure);
            Assert.Collection(aggregate.InnerExceptions,
                error => Assert.Same(bodyFailure, error),
                error => Assert.Same(releaseFailure, error),
                error => Assert.IsAssignableFrom<OperationCanceledException>(error),
                error => Assert.Contains("RuntimeCleanupPending", error.Message));
        }
    }

    [Fact]
    public async Task Directory_cleanup_error_does_not_replace_the_original_body_failure()
    {
        var disk = new TestDirectory();
        Directory.Delete(disk.Root, recursive: true);
        await File.WriteAllTextAsync(disk.Root, "Synthetic non-directory obstruction");
        var bodyFailure = new Xunit.Sdk.XunitException("Synthetic body assertion before directory cleanup.");
        try
        {
            var failure = await Assert.ThrowsAsync<AggregateException>(() => RuntimeTest.WithCleanupAsync([], () => throw bodyFailure, disk));
            Assert.Same(bodyFailure, failure.InnerExceptions[0]);
            Assert.IsAssignableFrom<IOException>(failure.InnerExceptions[1]);
        }
        finally { File.Delete(disk.Root); }
    }

    public sealed class HeldSettingWrite(string temporary)
    {
        [JsonIgnore] public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        [JsonIgnore] public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Value
        {
            get
            {
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.WriteByte(1);
                        Entered.TrySetResult();
                        Release.Task.GetAwaiter().GetResult();
                    }
                    return "held settings write";
                }
                finally { File.Delete(temporary); }
            }
        }
    }
}
