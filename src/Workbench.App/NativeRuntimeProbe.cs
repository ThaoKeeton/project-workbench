using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Workbench.Core;

namespace Workbench.App;

/// <summary>Explicit opt-in acceptance fixtures, never used by ordinary startup or installed as bundled plugins.</summary>
internal static class NativeRuntimeProbe
{
    internal const string Id = "com.projectworkbench.native-fixture";
    internal const string SecretId = "synthetic.credential";
    internal const string SyntheticSecret = "synthetic-native-dpapi-roundtrip-only";
    internal static string FixtureSource => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "fixtures", "runtime"));

    public static async Task PrepareAsync(HostRuntime runtime, RunOptions options)
    {
        var marker = Path.Combine(options.UserRoot, "isolated-runtime-probe.txt");
        HostRuntime.RejectLinks(marker);
        if (options.RuntimeProbe is "management" or "restart-seed")
        {
            Require(!File.Exists(marker) && !Directory.EnumerateFileSystemEntries(Path.Combine(options.UserRoot, "Plugins")).Any(), "Probe seed requires a fresh isolated root.");
            Require((await runtime.Packages.InstallAsync(FixtureSource)).Succeeded, "Synthetic fixture installation failed.");
            await File.WriteAllTextAsync(marker, "ProjectWorkbench synthetic native probe v1");
        }
        else Require(File.Exists(marker) && await File.ReadAllTextAsync(marker) == "ProjectWorkbench synthetic native probe v1", "Restart requires its previously seeded isolated root.");
    }

    public static async Task RunAsync(MainWindow window, RunOptions options)
    {
        List<string> checks = [];
        List<string> additionalChecks = [];
        var evidence = new NativeProbeEvidence(options.RuntimeProbe, checks);
        void WriteProgress() => options.WriteReport(new { succeeded = false, stage = options.RuntimeProbe, userRoot = options.UserRoot, checks, additionalChecks, startupStages = options.StartupStages });
        void Record(string check)
        {
            checks.Add(check);
            WriteProgress();
        }
        void RecordAdditional(string check)
        {
            additionalChecks.Add(check);
            WriteProgress();
        }
        await evidence.RunAsync(options.ReportPath, async () =>
        {
            evidence.At(NativeProbeCheckpoint.UiThread);
            window.AssertUiThread();
            if (options.RuntimeProbe == "management")
            {
                evidence.At(NativeProbeCheckpoint.Management);
                await NativeManagementProbe.RunAsync(window, options, Record, RecordAdditional);
            }
            else await NativeRestartProbe.RunAsync(window, options, Record, evidence);
            evidence.At(NativeProbeCheckpoint.FinalStop);
            await window.SmokeStopAsync();
            evidence.At(NativeProbeCheckpoint.FinalStopCompleted);
            evidence.At(NativeProbeCheckpoint.FinalReport);
            options.WriteReport(new { succeeded = true, stage = "completed", probe = options.RuntimeProbe, userRoot = options.UserRoot,
                processId = Environment.ProcessId, checks, additionalChecks, startupStages = options.StartupStages, completedUtc = DateTimeOffset.UtcNow });
            evidence.At(NativeProbeCheckpoint.FinalClose);
            await window.FinishSmokeAsync();
            evidence.At(NativeProbeCheckpoint.Completed);
        });
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await condition()) await Task.Delay(20, deadline.Token);
    }

    internal static T Find<T>(DependencyObject root, string name) where T : DependencyObject =>
        Descendants<T>(root).SingleOrDefault(element => AutomationProperties.GetName(element) == name)
        ?? throw new InvalidOperationException($"Native fixture control missing: {name}");

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}

internal sealed partial class MainWindow
{
    internal HostRuntime SmokeRuntime => _runtime ?? throw new InvalidOperationException("Host runtime is unavailable.");
    internal bool SmokeActionBusy => _busy;
    internal Task SmokeRunOwnedActionAsync(Func<Task> action) => RunAsync(_ => action());
}
