using System.Text.Json;
using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class HostFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shipping_view_and_action_boundaries_capture_failures_and_remain_usable(bool hostile)
    {
        Exception Failure() => hostile ? new HostileException() : new InvalidOperationException(PrivateResponse);
        var view = HostFailure.CreateView<object>(() => throw Failure());
        Assert.Null(view.Value);
        Assert.Equal("NativeViewFailed (PluginFailure).", view.Error);
        var action = await HostFailure.RunAsync(async () => { await Task.Yield(); throw Failure(); });
        Assert.Equal("HostOperationFailed (PluginFailure).", action);
        var next = new object();
        Assert.Same(next, HostFailure.CreateView(() => next).Value);
        var stopped = false;
        Assert.Null(await HostFailure.RunAsync(() => { stopped = true; return Task.CompletedTask; }));
        Assert.True(stopped);
        Assert.Equal("NativeViewFailed (NullView).", HostFailure.CreateView<object>(() => null).Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failure_reports_never_evaluate_plugin_exception_text(bool hostile)
    {
        using var disk = new TestDirectory();
        var path = Path.Combine(disk.Root, "failure.json");
        var options = new RunOptions(true, disk.User, path);
        options.WriteFailure("synthetic-failure", hostile ? new HostileException() : new InvalidOperationException(PrivateResponse), PrivateResponse);
        var report = File.ReadAllText(path);
        Assert.DoesNotContain(PrivateResponse, report);
        using var json = JsonDocument.Parse(report);
        Assert.Contains("HostOperationFailed", json.RootElement.GetProperty("error").GetString());
    }

    internal const string PrivateResponse = "synthetic-private-native-provider-response";
    internal sealed class HostileException : Exception
    {
        public override string Message => throw new InvalidOperationException("Host evaluated plugin Message.");
        public override string ToString() => throw new InvalidOperationException("Host evaluated plugin ToString.");
    }
}
