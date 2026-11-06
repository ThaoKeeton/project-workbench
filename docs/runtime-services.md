# Runtime services v1 (host 0.2.0)

`WorkbenchApi.Version` remains 1 and `IPluginContext` is unchanged. Both SDK
packages are 0.2.0 with file version 0.2.0.0 and assembly identity 1.0.0.0. A
plugin that needs these services declares `minimumHostVersion: "0.2.0"` and
casts its initialization context to the optional `IPluginRuntimeContext`.
`WorkbenchRuntimeServices.Version` is 1. The SDK stays UI-neutral; see the exact
types in [RuntimeContracts.cs](../src/Workbench.Sdk/RuntimeContracts.cs).

```csharp
var runtime = context as IPluginRuntimeContext
    ?? throw new NotSupportedException("Runtime services v1 required.");
runtime.Secrets.Register(new PluginSecretDefinition("openai.api-key", "API key"));
runtime.BackgroundTasks.Register(new PluginBackgroundTask("monitor", "Monitor", async execution =>
{
    try { await Task.Delay(Timeout.Infinite, execution.CancellationToken); }
    catch (OperationCanceledException) when (execution.CancellationToken.IsCancellationRequested)
    {
        // Read the dynamic execution.StopReason when deciding what intent to keep.
    }
}));
```

Register tasks and secret definitions only during `InitializeAsync`. Duplicate
or late IDs fail registration. Registration executes no job code. Task operations
are unavailable until initialization succeeds and contributions commit. Failed
initialization revokes staged metadata and sets `InitializationFailed` before
requesting cancellation and Stop.

`IPluginBackgroundTasks` exposes `GetSnapshots()`, `StartAsync(id, token)`,
`StopAsync(id, token)` and `SetResumeOnStartupAsync(id, enabled, token)`. Operations
return SDK `PluginTaskOperationResult(bool Succeeded, string Message)`. Each task
has one observed thread-pool execution; its synchronous prefix does not run on
the caller/dispatcher. Delegates must not access UI. Repeated Start while running
is idempotent; Start during Stopping fails until true completion. Request tokens
control operation waiting, rather than the background execution's lifetime.

`IPluginBackgroundExecution` provides `CancellationToken`, `StartReason`
(`UserRequest` or `Restore`) and dynamic `StopReason` (`None`, `UserRequest`,
`PluginDisabled`, `HostShutdown`, `InitializationFailed`). Reasons update before
cancellation. A later explicit disable upgrades a pending HostShutdown to
PluginDisabled without invoking Stop twice. Explicit stop reasons are preserved
through later shutdown retries. Task snapshots contain ID, title, state
(`Stopped`, `Running`, `Stopping`, `Completed`, `Faulted`), resume preference,
desired running intent, start/finish timestamps and a safe error code. Faults are
observed as `BackgroundTaskFailed`; they do not propagate to UI or retry in a loop.

The existing three-argument `PluginBackgroundTask` constructor is unchanged.
Its optional init property `RequiresPluginStart` defaults to false, and the
snapshot exposes the same flag. Set it true when starting requires a preview or
one-time business confirmation in the plugin's own page. Host
`StartBackgroundTaskAsync` then returns `PluginStartRequired` without executing
the delegate. The plugin's `context.BackgroundTasks.StartAsync` remains available
after its confirmation flow; host Stop remains available. `SetResumeOnStartupAsync`
with true is rejected from both host and plugin contexts, and initialization
clears any saved resume/running intent before commit, so Restore cannot bypass
that confirmation. This is a cooperative host policy and UI hint for trusted
code, not enforcement of business authorization against an in-process plugin.

Resume defaults off. Only opted-in tasks with desired running intent, unchanged
package fingerprint and a trusted/enabled plugin resume at a subsequent
`RestoreEnabledAsync` startup, with `StartReason=Restore`. Manual plugin Enable
does not resume previous work. Explicit task stop or plugin disable clears
desired intent, including disable before plugin restoration. Host shutdown keeps
intent. Discovery of package changes removes the previous task resume consent
and intent; reverting the files later does not revive it. Intent is host-owned,
stored atomically in `UserRoot/background-tasks.json`, separate from settings.
Corruption defaults to no resume; failed persistence prevents a new execution.

Business adapters must interpret HostShutdown as preserving business resume
intent, and UserRequest/PluginDisabled as revocation. Restore must still validate
the business configuration fingerprint, authorization, budget and deduplication.
Trusting or enabling changed code is not authorization to contact services or
send messages. The host does not establish mutual exclusion with independently
running versions; business code needs shared resource ownership and an explicit
requirement to stop older standalone monitoring.

## Cooperative stop ownership

Stop first revokes contributions and records the reason. Lifetime cancellation
uses `CancellationTokenSource.CancelAsync`; its callback-completion Task is known
work alongside the single plugin Stop Task, registered executions and their
cancellation callbacks. Core retains the instance and context until all this
work truly ends. The default `PluginManagerOptions.StopTimeout` is five seconds.

Timed-out Disable returns failure and leaves `PluginState.Stopping`,
`PluginSnapshot.HasRuntime` and `HasPendingStop` true. It does not report the
plugin disabled or allow a second instance. A retry waits on the same Stop Task;
late completion cleans up automatically. Shutdown and Dispose throw safe
`PluginShutdownException` while work remains. Failed disposal leaves the manager
queryable and retryable. The host must keep its process and user-root lease until
shutdown/disposal succeeds; it must not release the lease unconditionally in a
finally block. Native close/retry UI and lease integration belong to the host.

Stop should use Lifetime to request cleanup and quickly return an awaitable Task.
Core does not terminate tasks or unload assemblies. Trusted uncooperative code
can still damage or starve the process; Core cannot discover unregistered threads,
tasks, subprocesses or activity in independent applications. View factories keep
their existing UI-thread contract.

## Declared secrets and safe diagnostics

`IPluginSecretStore` exposes `IsSupported`, `Register(PluginSecretDefinition)`,
`GetDefinitions()`, `ReadAsync(id, token)`, `WriteAsync(id, value, token)` and
`DeleteAsync(id, token)`. Definitions contain only ID, title and description.
Only declared keys can be read, written or deleted. An absent value reads as null.
Default storage uses Windows DPAPI **CurrentUser**; ciphertext lives under
`UserRoot/secrets/<plugin-id>/<SHA256(key)>.bin`, and additional entropy binds the
plugin ID and key. Non-Windows storage reports unsupported with no plaintext
fallback. Writes use adjacent temporary ciphertext files and atomic replacement.
Existing links/reparse points and special files are rejected.

Errors use `PluginSecretStoreException.ErrorCode`: `SecretNotDeclared`,
`SecretStorageUnsupported`, `SecretReadFailed`, `SecretWriteFailed`,
`SecretDeleteFailed`, `SecretStoreClosed` or manager `SecretStoreUnavailable`.
No provider message or inner exception is retained. Temporary byte buffers are
cleared where possible; managed strings cannot be reliably erased. This is a
same-Windows-user storage convention, not isolation from other trusted plugins.

Suggested business keys are `openai.api-key`, `dingtalk.webhook-url` and
`dingtalk.webhook-secret`. Webhook URLs may themselves contain keys and must not
be placed in JSON preferences, metadata, returned messages or logs. DWS profiles
store references; they do not automatically copy standalone credentials.

Host-caught plugin exceptions yield only constant categories/error codes, not
raw exception messages, objects or stacks. Plugins remain responsible for their
own returned result messages and logs. Tests use only synthetic values in fresh
temporary roots. Windows-only tests cover real DPAPI and same-user child-process
restart; Linux fake-protector tests exercise storage boundaries and are not
encryption evidence. The provider uses Microsoft's
[ProtectedData package](https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData/10.0.0)
and its documented Windows CurrentUser protection.

## Host integration API

`PluginManager` supplies these UI-neutral operations (all tokens default):

```csharp
IReadOnlyList<PluginBackgroundTaskSnapshot> GetBackgroundTaskSnapshots(string pluginId);
Task<PluginTaskOperationResult> StartBackgroundTaskAsync(string pluginId, string id, CancellationToken cancellationToken = default);
Task<PluginTaskOperationResult> StopBackgroundTaskAsync(string pluginId, string id, CancellationToken cancellationToken = default);
Task<PluginTaskOperationResult> SetBackgroundTaskResumeOnStartupAsync(string pluginId, string id, bool enabled, CancellationToken cancellationToken = default);
IReadOnlyList<PluginSecretDefinition> GetSecretDefinitions(string pluginId);
bool IsSecretStorageSupported(string pluginId);
Task<string?> ReadSecretAsync(string pluginId, string id, CancellationToken cancellationToken = default);
Task WriteSecretAsync(string pluginId, string id, string value, CancellationToken cancellationToken = default);
Task DeleteSecretAsync(string pluginId, string id, CancellationToken cancellationToken = default);
bool HasPendingStops { get; }
```

Task and secret metadata snapshots are published only after commit. Task
snapshots remain visible while an owned runtime stops, then disappear on cleanup.
Start/resume and host credential operations require an enabled/trusted plugin.
The existing `DisableAsync`, `ShutdownAsync`, `DisposeAsync`, `Plugins` and
`Contributions` remain the lifecycle entry points. `IsLoaded` continues to mean
an assembly has been loaded; `HasRuntime` describes instance/context ownership.
