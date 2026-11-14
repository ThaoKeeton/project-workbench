using System.Text.Json;
using Workbench.App;
using Workbench.Core;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class NativeRestartProtocolTests
{
    [Fact]
    public async Task Count_only_readiness_can_observe_the_previous_reason_between_real_atomic_writes()
    {
        using var disk = new TestDirectory();
        var directory = Path.Combine(disk.User, "settings", "com.projectworkbench.native-fixture");
        var settings = new JsonPluginSettings(directory, _ => { });
        await settings.SetAsync("worker-runs", 1);
        await settings.SetAsync("worker-start", "UserRequest");
        var countPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReason = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task PublishAsync()
        {
            // The native fixture uses these two independent settings operations.
            await settings.SetAsync("worker-runs", await settings.GetAsync("worker-runs", 0) + 1);
            countPublished.TrySetResult();
            await releaseReason.Task;
            await settings.SetAsync("worker-start", "Restore");
        }
        var producer = PublishAsync();
        try
        {
            await countPublished.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.False(producer.IsCompleted);
            Assert.Equal(2, await settings.GetAsync("worker-runs", 0));
            using var stored = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "settings.json")));
            Assert.Equal(2, stored.RootElement.GetProperty("worker-runs").GetInt32());
            Assert.Equal("UserRequest", stored.RootElement.GetProperty("worker-start").GetString());
            Assert.Equal("UserRequest", await settings.GetAsync("worker-start", ""));
            var partial = await NativeWorkerPublication.ReadAsync(settings);
            Assert.Equal(2, partial.Runs);
            Assert.Equal(NativeProbeReason.UserRequest, partial.StartReason);
            Assert.False(partial.IsRestored);
        }
        finally { releaseReason.TrySetResult(); await producer; }
        Assert.Equal(2, await settings.GetAsync("worker-runs", 0));
        Assert.Equal("Restore", await settings.GetAsync("worker-start", ""));
        Assert.True((await NativeWorkerPublication.ReadAsync(settings)).IsRestored);
    }
}
