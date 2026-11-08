using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Workbench.Core;
using Workbench.Core.Projects;
using Workbench.Core.Testing;
using Workbench.Sdk;
using static Workbench.App.NativeRuntimeProbe;
using static Workbench.App.NativeManagementProbe;

namespace Workbench.App;

/// <summary>Opt-in actual shipping EXE acceptance. Uses only installed contributions and public Core/SDK storage.
/// No reference to the private testing entry/domain, provider, adapter or controller assembly.</summary>
internal static partial class TestingNativeProbe
{
    private const string Id = "com.projectworkbench.testing";
    private const string Marker = "ProjectWorkbench synthetic testing probe schema1";
    private static string PackageZip => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "testing-plugin", "workbench-testing-plugin-0.2.0.zip"));
    private static string MarkerPath(string root) => Path.Combine(root, "isolated-testing-probe.txt");
    private static string StatePath(string root) => Path.Combine(root, "testing-probe-state.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private sealed record ProbeState(int SchemaVersion, int ProcessId, string SetId, string PassedRunId, long ObservedSequence,
        string EvidenceId, string ObservedText, string FrameSha256, string InterruptedSeedId, string FrozenRevision, string SecondaryId, string ImmediateLeaveText);

    internal static async Task PrepareAsync(RunOptions options)
    {
        var parsed = RunOptions.Parse(["--testing-probe", options.TestingProbe!, "--probe-root", options.UserRoot]);
        Require(parsed.UserRoot == options.UserRoot, "Testing isolated root changed.");
        HostRuntime.RejectLinks(options.UserRoot);
        Require(File.Exists(PackageZip), "Installable testing ZIP is missing beside the shipping app.");
        if (options.TestingProbe == "verify")
        {
            Require(File.Exists(MarkerPath(options.UserRoot)) && await File.ReadAllTextAsync(MarkerPath(options.UserRoot)) == Marker,
                "Testing verify requires the exact synthetic seeded root.");
            return;
        }
        Require(!Directory.Exists(options.UserRoot) || Directory.EnumerateFileSystemEntries(options.UserRoot).All(path => path == options.ReportPath),
            "Testing seed requires a fresh dedicated root.");
        using var lease = HostRuntime.AcquireLease(options.UserRoot);
        var packages = new PluginPackageStore(new() { UserRoot = options.UserRoot, PluginsRoot = Path.Combine(options.UserRoot, "Plugins") });
        Require((await packages.InstallAsync(PackageZip)).Succeeded, "Actual testing ZIP package-store install failed.");
        await File.WriteAllTextAsync(MarkerPath(options.UserRoot), Marker);
    }
    internal static async Task RunAsync(MainWindow window, RunOptions options)
    {
        List<string> checks = [];
        var evidence = new List<object>();
        var progress = new ExecutionProgress();
        try
        {
            window.AssertUiThread();
            Require(window.Workspace.ActiveProject?.Id == "default", "Testing probe must use the normal default workspace.");
            if (options.TestingProbe == "seed") await SeedAsync(window, options, checks);
            else await VerifyAsync(window, options, checks, progress);
            var output = Path.Combine(Path.GetDirectoryName(options.ReportPath!)!, options.TestingProbe + "-evidence");
            await CopyEvidenceAsync(window.SmokeRuntime.UserRoot, output, evidence);
            var roots = window.Workspace.Catalog!.Projects.Select(p => new ProjectWorkspaceCatalog(options.UserRoot).GetProjectRoot(p.Id)).ToArray();
            var packageHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(PackageZip))).ToLowerInvariant();
            window.Closed += (_, _) =>
            {
                try
                {
                    foreach (var root in roots) { using var next = HostRuntime.AcquireLease(root); }
                    options.WriteReport(new { schemaVersion = 1, succeeded = true, probe = options.TestingProbe, userRoot = options.UserRoot,
                        processId = Environment.ProcessId, actualWindowClosed = true, rootsRetired = true, checks, evidence,
                        packageSha256 = packageHash, osDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                        osVersion = Environment.OSVersion.VersionString, processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                        syntheticInterruptionSeed = true, completedUtc = DateTimeOffset.UtcNow,
                        limits = "Actual Windows Server native process/controls/package/IO/decoder. Virtual execution and all BMP frames SIMULATED. Synthetic inflight journal seed, not an OS crash. Human pickers/IME/DPI/accessibility/real devices/providers deferred. OS exit observed separately by runner." });
                }
                catch (Exception error) { Environment.ExitCode = 1; options.WriteFailure("testing-closed-lease", error); }
            };
            if (options.TestingProbe == "seed")
            {
                var state = JsonSerializer.Deserialize<ProbeState>(await File.ReadAllBytesAsync(StatePath(options.UserRoot)), Json)!;
                var page = await PageAsync(window);
                // Re-navigation creates controls from the same controller model; it must retain the selection.
                await WaitReadyAsync(page);
                var editor = Find<TextBox>(page, "测试用例集名称");
                editor.Text = "Synthetic final accepted input immediately before Closed 🧪";
                var accepted = editor.Text;
                checks.Add("immediate-final-input-before-leave");
                var closing = window.FinishSmokeAsync(); // No asynchronous yield/save wait between input and normal leave.
                File.WriteAllBytes(StatePath(options.UserRoot), JsonSerializer.SerializeToUtf8Bytes(state with { ImmediateLeaveText = accepted }, Json));
                await closing;
            }
            else await window.FinishSmokeAsync();
        }
        catch (Exception error)
        {
            // A sidecar survives the host's generic failure report; CreateNew protects prior evidence.
            try
            {
                var sidecar = options.ReportPath + ".evidence.json";
                await WriteNewAsync(sidecar, JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, succeeded = false,
                    processId = Environment.ProcessId, phase = options.TestingProbe, checks, evidence,
                    safeError = HostFailure.Describe("TestingNativeProbeFailed", error), errorHResult = error.HResult,
                    failure = DescribeFailure(error, progress) }, Json));
            }
            catch { /* Diagnostic IO/serialization must preserve the original native failure and any prior sidecar. */ }
            throw;
        }
    }
    private static async Task CommandAsync(MainWindow window, string command)
        => Require((await window.SmokeManager.ExecuteCommandAsync(Id, command)).Succeeded, "Installed testing command failed: " + command);
    private static async Task<FrameworkElement> PageAsync(MainWindow window) => await window.SmokeNavigateAsync("view", Id, "testing");
    private static ProjectTestStore Store(MainWindow window) => window.Workspace.Scope!.Testing;
    private static async Task<FrameworkElement> EnableAsync(MainWindow window)
    {
        await window.WorkspaceActionAsync(async runtime => Require((await runtime.Manager.EnableAsync(Id, confirmTrust: true)).Succeeded, "Independent testing enable failed."));
        return await PageAsync(window);
    }
    private static void SelectCase(FrameworkElement page, string id)
    {
        var list = Find<ListView>(page, "测试用例列表");
        list.SelectedItem = list.Items.OfType<ListViewItem>().Single(item => (string)item.Tag == id);
    }
    private static void SelectRun(FrameworkElement page, string id)
    {
        var list = Find<ComboBox>(page, "测试执行列表");
        list.SelectedItem = list.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == id);
    }
    private static async Task<TestCaseSetSnapshot> CurrentSetAsync(MainWindow window, FrameworkElement page)
    {
        var item = (ComboBoxItem)Find<ComboBox>(page, "测试用例集").SelectedItem;
        var setId = (string)item.Tag;
        return await ReadStoreAsync(Store(window), store => store.ReadCaseSetAsync(setId));
    }
    private static async Task<T> ReadStoreAsync<T>(ProjectTestStore store, Func<ProjectTestStore, Task<T>> read)
    {
        NativeStoreObservation.Result<T> observation = default;
        await WaitAsync(async () =>
        {
            observation = await NativeStoreObservation.ReadAsync(read(store));
            return observation.IsReady;
        });
        return observation.Value;
    }
    private static async Task WaitReadyAsync(FrameworkElement page)
        => await WaitAsync(() => Task.FromResult(Find<TextBox>(page, "测试用例集名称").IsEnabled));
    private static async Task<TestRunSnapshot> WaitRunAsync(MainWindow window, ProjectTestStore store, string id, TestRunState expected)
    {
        var manager = window.SmokeManager;
        TestRunSnapshot? current = null;
        await WaitAsync(async () =>
        {
            var observation = await NativeStoreObservation.ReadAsync(store.ReadRunAsync(id));
            if (!observation.IsReady) return false;
            current = observation.Value;
            return current.State == expected && manager.GetBackgroundTaskSnapshots(Id).All(t => t.State is not (PluginBackgroundTaskState.Running or PluginBackgroundTaskState.Stopping));
        });
        Require(current!.State == expected, "Committed virtual run state differed."); return current;
    }
    private static async Task WriteNewAsync(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
        await file.WriteAsync(bytes);
    }
    private static async Task CopyEvidenceAsync(string root, string output, List<object> receipts)
    {
        Require(!Directory.Exists(output), "Testing evidence output already exists.");
        var store = Path.Combine(root, ".workbench-project", "testing");
        HostRuntime.RejectLinks(store);
        var files = new List<string>();
        void Inspect(string directory)
        {
            HostRuntime.RejectLinks(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                HostRuntime.RejectLinks(entry);
                if (Directory.Exists(entry)) Inspect(entry);
                else { Require(files.Count < 10000, "Synthetic evidence file bound exceeded."); files.Add(entry); }
            }
        }
        Inspect(store);
        foreach (var path in files.Order(StringComparer.Ordinal))
        {
            HostRuntime.RejectLinks(path);
            var relative = Path.GetRelativePath(store, path).Replace('\\', '/');
            Require(new FileInfo(path).Length <= 8 * 1024 * 1024, "Synthetic evidence file size bound exceeded.");
            var bytes = await File.ReadAllBytesAsync(path);
            await WriteNewAsync(Path.Combine(output, "store", relative), bytes);
            receipts.Add(new { path = "store/" + relative, length = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        }
        var csv = Path.Combine(root, "data", Id, "testing-cases.csv");
        if (File.Exists(csv))
        {
            var bytes = await File.ReadAllBytesAsync(csv); await WriteNewAsync(Path.Combine(output, "testing-cases.csv"), bytes);
            receipts.Add(new { path = "testing-cases.csv", length = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        }
    }
}
