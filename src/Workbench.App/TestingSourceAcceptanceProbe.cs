using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Workbench.Core;
using Workbench.Core.Projects;
using Workbench.Core.Testing;
using Workbench.Sdk;
using static Workbench.App.NativeRuntimeProbe;

namespace Workbench.App;

/// <summary>Explicit installed synthetic lane. Only public Core/SDK, normal contributions and real control peers.
/// No reference to the private Testing, Domain or fixture assemblies; transport authority stays inside the package.</summary>
internal static partial class TestingSourceAcceptanceProbe
{
    private const string Id = "com.projectworkbench.testing.source-acceptance";
    private const string Setting = "source-acceptance-release-v1";
    private const string Marker = "ProjectWorkbench installed synthetic source acceptance schema1";
    private const string Model = "gpt-5.4-mini-2026-03-17";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string Delivery => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "source-acceptance-fixture"));
    private static string PackageZip => Path.Combine(Delivery, "workbench-testing-source-acceptance-0.2.0.zip");
    private static string MarkerPath(string root) => Path.Combine(root, "isolated-source-acceptance.json");
    private static string StatePath(string root) => Path.Combine(root, "source-acceptance-state.json");
    private sealed record Binding(int SchemaVersion, string Marker, string RunId, string PackageSha256, string SourceCommit, string HostExeSha256, string HostDllSha256);
    private sealed record Counters(long Entered, long Accepted, long Settled, long Unexpected, bool LimitExceeded, bool CancellationObserved);
    private sealed record FixtureState(int SchemaVersion, string FixtureId, string PluginId, string InstanceId, string ProjectId, long Generation,
        string Provider, string Scenario, string Phase, string Outcome, Counters Figma, Counters Ai);
    private sealed record Observation(string Check, string ProjectId, string InstanceId, long Generation, long RuntimeGeneration,
        Counters Figma, Counters Ai, string[] SourceIds, string[] CaseSetIds, int RunCount);
    private sealed record Records(string? FigmaId, string? HappyReceiptId, string? PartialReceiptId, string? CaseSetId, string SeedInstanceId, string? SecondaryId);
    private sealed record Evidence(string ProjectId, string Area, string Id, string Path, long Length, string Sha256);
    private sealed record DurableState(int SchemaVersion, int ProcessId, long ProcessCreationFileTimeUtc, Binding Binding,
        Records Records, Evidence[] Files, string[] ProjectIds);
    private sealed class Session(MainWindow window, RunOptions options, Binding binding)
    {
        internal MainWindow Window { get; } = window;
        internal RunOptions Options { get; } = options;
        internal Binding Binding { get; } = binding;
        internal List<string> Checks { get; } = [];
        internal List<Observation> Observations { get; } = [];
        internal List<HeldEvidence> Holds { get; } = [];
        internal SourceOperation Operation { get; set; }
        internal SourceOperationResult OperationResult { get; set; }
        internal SourceOperationReason OperationReason { get; set; }
        internal PluginDiagnosticCode? OperationDiagnosticCode { get; set; }
        internal Records Records { get; set; } = new(null, null, null, null, "", null);
        internal ProjectTestStore Store => Window.Workspace.Scope!.Testing;
        internal string Root => Window.SmokeRuntime.UserRoot;
        internal string ProjectId => Window.Workspace.ActiveProject!.Id;
        internal async Task RecordAsync(string check, FixtureState? saved = null)
        {
            var state = saved ?? await StateAsync(Window.SmokeManager);
            var inventory = await InventoryAsync(Store);
            Require(!Checks.Contains(check, StringComparer.Ordinal), "SourceCheckDuplicate");
            Checks.Add(check);
            Observations.Add(new(check, state.ProjectId, state.InstanceId, state.Generation, Window.Workspace.RuntimeGeneration,
                state.Figma, state.Ai, inventory.Sources.Select(s => s.Id).ToArray(), inventory.Cases.Select(c => c.Id).ToArray(), inventory.Runs.Count));
        }
    }

    internal static async Task PrepareAsync(RunOptions options)
    {
        var parsed = RunOptions.Parse(["--source-acceptance-probe", options.SourceAcceptanceProbe!, "--probe-root", options.UserRoot]);
        Require(parsed.UserRoot == options.UserRoot, "SourceRootChanged");
        HostRuntime.RejectLinks(options.UserRoot);
        var binding = await CurrentBindingAsync(options.UserRoot);
        if (options.SourceAcceptanceProbe == "verify")
        {
            Require(await ReadJsonAsync<Binding>(MarkerPath(options.UserRoot), 4096) == binding, "SourceSeedBindingChanged");
            _ = await ReadJsonAsync<DurableState>(StatePath(options.UserRoot), 65536);
            return;
        }
        Require(!Directory.Exists(options.UserRoot) || Directory.EnumerateFileSystemEntries(options.UserRoot).All(path => path == options.ReportPath), "SourceRootNotFresh");
        using var lease = HostRuntime.AcquireLease(options.UserRoot);
        var packages = new PluginPackageStore(new() { UserRoot = options.UserRoot, PluginsRoot = Path.Combine(options.UserRoot, "Plugins") });
        Require((await packages.InstallAsync(PackageZip)).Succeeded, "SourcePackageInstallFailed");
        await WriteNewAsync(MarkerPath(options.UserRoot), JsonSerializer.SerializeToUtf8Bytes(binding, Json));
    }

    internal static async Task RunAsync(MainWindow window, RunOptions options)
    {
        window.AssertUiThread();
        var binding = await CurrentBindingAsync(options.UserRoot);
        Require(await ReadJsonAsync<Binding>(MarkerPath(options.UserRoot), 4096) == binding, "SourceMarkerChanged");
        var session = new Session(window, options, binding);
        using var process = Process.GetCurrentProcess();
        var birth = process.StartTime.ToUniversalTime().ToFileTimeUtc(); // Native fact, never projected from launcher.
        try
        {
            Require(window.Workspace.ActiveProject?.Id == "default", "SourceDefaultProjectRequired");
            if (options.SourceAcceptanceProbe == "seed") await SeedAsync(session);
            else if (options.SourceAcceptanceProbe == "verify") await VerifyAsync(session, birth);
            else await StopAsync(session, options.SourceAcceptanceProbe == "stop-figma" ? "figma" : "ai");
            var evidence = await ExportEvidenceAsync(session);
            var roots = window.Workspace.Catalog!.Projects.Select(p => new ProjectWorkspaceCatalog(options.UserRoot).GetProjectRoot(p.Id)).Order(StringComparer.Ordinal).ToArray();
            Require(roots.Contains(options.UserRoot, StringComparer.Ordinal), "SourceBaseRootMissing");
            // Closed is observed in the native process. Normal close first drains the same managers/dispatchers.
            window.Closed += (_, _) =>
            {
                try
                {
                    var leases = new List<FileStream>();
                    try { foreach (var root in roots) leases.Add(HostRuntime.AcquireLease(root)); }
                    finally { foreach (var lease in leases) lease.Dispose(); }
                    options.WriteReport(new { schemaVersion = 1, kind = "SourceAcceptanceNative", succeeded = true, phase = options.SourceAcceptanceProbe,
                        userRoot = options.UserRoot, binding.SourceCommit, binding.PackageSha256, binding.HostExeSha256, binding.HostDllSha256,
                        processId = Environment.ProcessId, processCreationFileTimeUtc = birth, actualWindowClosed = true, rootsRetired = true, roots,
                        osDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription, processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                        checks = session.Checks, observations = session.Observations, holds = session.Holds, records = session.Records, evidence, completedUtc = DateTimeOffset.UtcNow });
                }
                catch (Exception) { Environment.ExitCode = 1; WriteFailure(options, session, birth, "SourceClosedLeaseFailed"); }
            };
            await window.FinishSmokeAsync();
        }
        catch (Exception error)
        {
            // Closed never gets synthesized on a failed action; bounded sidecar preserves safe phase progress.
            try { await WriteNewAsync(options.ReportPath + ".evidence.json", JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1,
                kind = "SourceAcceptanceFailure", phase = options.SourceAcceptanceProbe, processId = Environment.ProcessId,
                processCreationFileTimeUtc = birth, checks = session.Checks, failure = FailureCode(error), diagnostic = FailureIdentity(error),
                operation = OperationIdentity(session.Operation, session.OperationResult, session.OperationReason, session.OperationDiagnosticCode) }, Json)); }
            catch (Exception) { /* Preserve the original failure and prior evidence. */ }
            throw;
        }
    }
    private static void WriteFailure(RunOptions options, Session session, long birth, string code)
        => options.WriteReport(new { schemaVersion = 1, kind = "SourceAcceptanceFailure", succeeded = false, phase = options.SourceAcceptanceProbe,
            processId = Environment.ProcessId, processCreationFileTimeUtc = birth, checks = session.Checks, failure = code });
    private static string FailureCode(Exception error) => error switch
    {
        TimeoutException => "SourceObservationExpired", OperationCanceledException => "SourceObservationCanceled",
        IOException or UnauthorizedAccessException => "SourceStorageFailed", PluginTestingException => "SourceStoreFailed", _ => "SourceAssertionFailed"
    };
    private enum SourceProbeFailureKind { Assertion, ControlLookup }
    private sealed record SourceProbeFailureIdentity(string Kind, string? SourceFile = null, int? SourceLine = null);
    // Only this sealed first-party exception carries a compiled caller location. Never expose exception/control text or absolute paths.
    private sealed class SourceProbeFailure : InvalidOperationException
    {
        internal SourceProbeFailureIdentity Diagnostic { get; }
        internal SourceProbeFailure(SourceProbeFailureKind kind, string file, int line) : base("Source native probe failed.")
        {
            var category = kind switch { SourceProbeFailureKind.Assertion => "Assertion", SourceProbeFailureKind.ControlLookup => "ControlLookup", _ => "Unknown" };
            var name = Path.GetFileName(file) switch
            {
                "TestingSourceAcceptanceProbe.cs" => "TestingSourceAcceptanceProbe.cs",
                "TestingSourceAcceptanceProbe.Controls.cs" => "TestingSourceAcceptanceProbe.Controls.cs",
                "TestingSourceAcceptanceProbe.Records.cs" => "TestingSourceAcceptanceProbe.Records.cs",
                "TestingSourceAcceptanceProbe.Lifecycle.cs" => "TestingSourceAcceptanceProbe.Lifecycle.cs",
                _ => null
            };
            Diagnostic = category != "Unknown" && name is not null && line is > 0 and <= 100000
                ? new(category, name, line) : new(category);
        }
    }
    private static SourceProbeFailureIdentity FailureIdentity(Exception error)
        => error is SourceProbeFailure own ? own.Diagnostic : new("Unknown");
    private static void Require(bool condition, string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        // Preserve failure admission, but never let even a supplied assertion message become diagnostic data.
        if (!condition) throw new SourceProbeFailure(SourceProbeFailureKind.Assertion, file, line);
    }
    private enum SourceOperation { None, SecondaryDialog, SecondaryInstall, SecondaryDiscover, FixtureDiscovery, FixtureSetting, FixtureEnable, FixtureContribution, SecondaryIsolation, ReturnPrimary }
    private enum SourceOperationResult { NotStarted, CallbackNotEntered, Started, Completed, Refused, CallbackFaulted, RuntimeChanged, InvalidDiscovery }
    private enum SourceOperationReason { None, Unknown, MaintenanceOwnershipUnreadable, InstalledOrPending, PathExistsOrLoaded, ImportFailed }
    private sealed record SourceOperationIdentity(string Stage, string Result, string Reason, string? DiagnosticCode);
    private static SourceOperationIdentity OperationIdentity(SourceOperation operation, SourceOperationResult result,
        SourceOperationReason reason = SourceOperationReason.None, PluginDiagnosticCode? diagnosticCode = null) => new(operation switch
    {
        SourceOperation.None => "None", SourceOperation.SecondaryDialog => "SecondaryDialog", SourceOperation.SecondaryInstall => "SecondaryInstall",
        SourceOperation.SecondaryDiscover => "SecondaryDiscover", SourceOperation.FixtureDiscovery => "FixtureDiscovery", SourceOperation.FixtureSetting => "FixtureSetting",
        SourceOperation.FixtureEnable => "FixtureEnable", SourceOperation.FixtureContribution => "FixtureContribution", SourceOperation.SecondaryIsolation => "SecondaryIsolation",
        SourceOperation.ReturnPrimary => "ReturnPrimary", _ => "Unknown"
    }, result switch
    {
        SourceOperationResult.NotStarted => "NotStarted", SourceOperationResult.CallbackNotEntered => "CallbackNotEntered", SourceOperationResult.Started => "Started",
        SourceOperationResult.Completed => "Completed", SourceOperationResult.Refused => "Refused", SourceOperationResult.CallbackFaulted => "CallbackFaulted",
        SourceOperationResult.RuntimeChanged => "RuntimeChanged", SourceOperationResult.InvalidDiscovery => "InvalidDiscovery", _ => "Unknown"
    }, reason switch
    {
        SourceOperationReason.None => "None", SourceOperationReason.MaintenanceOwnershipUnreadable => "MaintenanceOwnershipUnreadable",
        SourceOperationReason.InstalledOrPending => "InstalledOrPending", SourceOperationReason.PathExistsOrLoaded => "PathExistsOrLoaded",
        SourceOperationReason.ImportFailed => "ImportFailed", _ => "Unknown"
    }, diagnosticCode switch
    {
        null => null, PluginDiagnosticCode.InvalidManifest => "InvalidManifest", PluginDiagnosticCode.InvalidPackage => "InvalidPackage",
        PluginDiagnosticCode.IncompatibleApi => "IncompatibleApi", PluginDiagnosticCode.IncompatibleHost => "IncompatibleHost", PluginDiagnosticCode.MissingAssembly => "MissingAssembly",
        PluginDiagnosticCode.DuplicatePluginId => "DuplicatePluginId", PluginDiagnosticCode.Untrusted => "Untrusted", PluginDiagnosticCode.PackageChanged => "PackageChanged",
        PluginDiagnosticCode.LoadFailed => "LoadFailed", PluginDiagnosticCode.InitializationFailed => "InitializationFailed", PluginDiagnosticCode.StopFailed => "StopFailed",
        PluginDiagnosticCode.CommandFailed => "CommandFailed", PluginDiagnosticCode.SettingsCorrupt => "SettingsCorrupt", PluginDiagnosticCode.StateCorrupt => "StateCorrupt",
        PluginDiagnosticCode.StatePersistenceFailed => "StatePersistenceFailed", PluginDiagnosticCode.InstallationFailed => "InstallationFailed", PluginDiagnosticCode.StopTimedOut => "StopTimedOut",
        PluginDiagnosticCode.BackgroundTaskFailed => "BackgroundTaskFailed", PluginDiagnosticCode.RuntimeStateCorrupt => "RuntimeStateCorrupt",
        PluginDiagnosticCode.RuntimeStatePersistenceFailed => "RuntimeStatePersistenceFailed", PluginDiagnosticCode.SecretStorageFailed => "SecretStorageFailed",
        PluginDiagnosticCode.PackageMaintenanceFailed => "PackageMaintenanceFailed", PluginDiagnosticCode.PackageMaintenancePending => "PackageMaintenancePending", _ => "Unknown"
    });
    private static void SetOperation(Session s, SourceOperation operation, SourceOperationResult result)
    { s.Operation = operation; s.OperationResult = result; s.OperationReason = SourceOperationReason.None; s.OperationDiagnosticCode = null; }
    private static void RequireJoinedAction(bool entered, bool runtimeMatched, bool succeeded, bool stillCurrent,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Require(entered && runtimeMatched && succeeded && stillCurrent, "SourceJoinedActionIncomplete", file, line);
    private static async Task<Binding> CurrentBindingAsync(string root)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "Workbench.App.dll");
        var exe = Path.Combine(AppContext.BaseDirectory, "Workbench.App.exe");
        var version = FileVersionInfo.GetVersionInfo(dll).ProductVersion ?? "";
        var source = version.Split('+').Last();
        Require(Hex(source, 40), "SourceHostCommitMissing");
        using var metadata = JsonDocument.Parse(await ReadBytesAsync(Path.Combine(Delivery, "provenance.json"), 2 * 1024 * 1024));
        Require(metadata.RootElement.GetProperty("sourceCommit").GetString() == source && !metadata.RootElement.GetProperty("sourceTreeDirty").GetBoolean(), "SourcePackageCommitChanged");
        return new(1, Marker, Path.GetFileName(root), await HashFileAsync(PackageZip, 64 * 1024 * 1024), source,
            await HashFileAsync(exe, 64 * 1024 * 1024), await HashFileAsync(dll, 64 * 1024 * 1024));
    }
    private static async Task<FixtureState> StateAsync(PluginManager manager)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await manager.ExecuteCommandAsync(Id, "source-acceptance-state", cts.Token);
        Require(!cts.IsCancellationRequested && result.Succeeded && Encoding.UTF8.GetByteCount(result.Message) <= 8192, "SourceStateUnavailable");
        var state = JsonSerializer.Deserialize<FixtureState>(result.Message, Json) ?? throw new InvalidDataException("SourceStateInvalid");
        Require(state.SchemaVersion == 1 && state.FixtureId == Id && state.PluginId == Id && Hex(state.InstanceId, 32)
            && (state.ProjectId == "default" || Hex(state.ProjectId, 32)) && state.Generation is >= 0 and <= 32, "SourceStateIdentityInvalid");
        foreach (var counter in new[] { state.Figma, state.Ai })
            Require(counter.Entered >= counter.Accepted && counter.Accepted >= counter.Settled && counter.Unexpected == 0 && !counter.LimitExceeded, "SourceUnexpectedTransport");
        Require(state.Figma.Accepted + state.Ai.Accepted <= 32, "SourceTransportLimit");
        return state;
    }
    private static async Task CommandAsync(PluginManager manager, string command)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Require((await manager.ExecuteCommandAsync(Id, command, cts.Token)).Succeeded && !cts.IsCancellationRequested, "SourceCommandRefused");
    }
    private static async Task PollAsync(Func<Task<bool>> condition, int seconds = 20)
    {
        var budget = Stopwatch.StartNew();
        while (true)
        {
            var satisfied = await condition(); // Every observation owns/joins its actual task before another is started.
            if (budget.Elapsed > TimeSpan.FromSeconds(seconds)) throw new TimeoutException("SourceObservationExpired");
            if (satisfied) return;
            await Task.Delay(25);
        }
    }
    private static async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> read)
    {
        var budget = Stopwatch.StartNew();
        while (true)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var result = await read(cts.Token); // Cancellation bounds supported store IO; no WaitAsync detaches the inner read.
                cts.Token.ThrowIfCancellationRequested();
                if (budget.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("SourceStoreObservationExpired");
                return result;
            }
            catch (PluginTestingException error) when (error.ErrorCode == "StoreBusy" && budget.Elapsed < TimeSpan.FromSeconds(20)) { await Task.Delay(25); }
        }
    }
    private static bool Hex(string? text, int length) => text?.Length == length && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static async Task<byte[]> ReadBytesAsync(string path, int maximum)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await SharingViolationRetry.RunAsync(() => ReadBytesOnceAsync(path, maximum, cts.Token), cts.Token);
    }
    private static async Task<byte[]> ReadBytesOnceAsync(string path, int maximum, CancellationToken cancellationToken)
    {
        HostRuntime.RejectLinks(path);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        Require(file.Length is > 0 && file.Length <= maximum, "SourceFileBound");
        var bytes = new byte[(int)file.Length]; await file.ReadExactlyAsync(bytes, cancellationToken);
        Require(file.ReadByte() == -1, "SourceFileChanged");
        return bytes;
    }
    private static async Task<T> ReadJsonAsync<T>(string path, int maximum) => JsonSerializer.Deserialize<T>(await ReadBytesAsync(path, maximum), Json)
        ?? throw new InvalidDataException("SourceJsonInvalid");
    private static async Task<string> HashFileAsync(string path, int maximum) => Hash(await ReadBytesAsync(path, maximum));
    private static async Task WriteNewAsync(string path, byte[] bytes)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await SharingViolationRetry.RunAsync(() => WriteNewOnceAsync(path, bytes, cts.Token), cts.Token);
    }
    private static async Task WriteNewOnceAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        HostRuntime.RejectLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileStream? file = null;
        try
        {
            file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
            await file.WriteAsync(bytes, cancellationToken); await file.FlushAsync(cancellationToken); file.Flush(true);
        }
        catch (Exception error) when (SharingViolationRetry.IsSharingViolation(error))
        {
            if (file is not null)
            {
                try { await file.DisposeAsync(); } catch (Exception) { /* Keep the sharing violation for the limited retry. */ }
                file = null;
            }
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { /* Keep the sharing violation for the limited retry. */ }
            throw;
        }
        finally { if (file is not null) await file.DisposeAsync(); }
    }
}
