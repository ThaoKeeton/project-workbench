# Project services v1 (host 0.3.0)

The app and both SDK packages are preview 0.3.0 with file version 0.3.0.0.
Assembly identity remains 1.0.0.0, API remains 1 and runtime services remain 1.
The original `IPluginContext`, `IPluginRuntimeContext` and `WinUiView` signatures
are unchanged. Already-built SDK 0.2 business packages retain their fixed bytes,
provenance and minimumHostVersion 0.2.0. New plugins requiring project services
declare minimumHostVersion 0.3.0 and check for the optional interface:

```csharp
if (context is IPluginProjectContext project)
{
    ProjectIdentity identity = project.Project.Identity;
    ProjectContextSnapshot current = await project.Project.ReadAsync(cancellationToken);
    await project.Project.UpdateAsync("Nonsecret project instructions", current.Revision, cancellationToken);
    project.RegisterBeforeProjectLeave(token => FlushDocumentModelsAsync(token));
}
```

`WorkbenchProjectServices.Version` is 1. `ProjectIdentity(Id, Name)` describes the
current scope. `ProjectContextSnapshot(Revision, Instructions)` maps the actual
context document's text to its SDK name. `IProjectContext` exposes `Identity`,
`ReadAsync(token)` and `UpdateAsync(instructions, expectedRevision, token)`.
Reads of a missing document return revision `missing` and empty instructions
without creating a root or document. Invalid context is an error, never an
editable blank. Writes require the last revision; a stale/null revision yields
RevisionConflict. Schema 1 remains `.workbench-project/context.json` with
projectId/text/updatedUtc; revisions are SHA256 of raw bytes. Context text is
limited to 1 MiB UTF8 and encoded JSON to 8 MiB. No credential draft belongs here.

Core `ProjectRuntimeScope(id, name, userRoot)` fixes its `Id` and `UserRoot`, exposes
`Identity` and actual `Documents`, and supports `Rename(name)` after the host has
persisted the catalog rename. Names use the catalog's trimmed 80 UTF16-character
validation. `PluginManagerOptions.ProjectScope` is optional; legacy callers bind
to their actual UserRoot and identity default/Default. A supplied scope must
match that root. Project roots are chosen by the host from managed IDs; plugins
must not infer another project's root from display names.

Register one `RegisterBeforeProjectLeave(Func<CancellationToken, Task>)` during
InitializeAsync. Duplicate or late registration fails; initialization rollback
never invokes the staged hook. The host calls
`PrepareProjectLeaveAsync(string? pluginId = null, CancellationToken token = default)`
before switching/closing a project or stopping a plugin. Its result is
`ProjectPreparationResult(Succeeded, HasPendingWork, ErrorCode)`; the default
wait is the manager's finite StopTimeout. Hooks run on the thread pool, including
synchronous prefixes. Capture plain document models and join their writes;
never access WinUI controls from the callback. First-party examples autosave
ordinary preferences and flush their nonsecret draft models from this hook and
Stop. Failed writes leave buffers dirty and recoverable; credential editors and
vendor business authority/ledgers are not autosaved or copied.

Only one manager preparation is active. Same-filter/same-runtime-participant
retries wait on its actual task without a duplicate callback. If it settles after
a timeout, the retry receives that retained result once; an acknowledged result
permits a new leave cycle. A different overlapping filter or participant set returns
ProjectPreparationBusy and cannot claim another plugin was prepared. Failure
keeps the current enabled runtime available for correction and retry. A timeout
or caller cancellation does not imply rollback or exactly-once IO; actual work
stays owned. `HasPendingProjectWork` includes preparation and admitted service
calls, including calls a plugin did not await. It becomes false only once that
work settles. Successful preparation covers its captured participants; host
coordination must prevent fresh edits/actions before replacing the runtime.

Close immediately revokes new identity/read/update calls with ProjectContextClosed.
Already admitted calls stay owned until their actual tasks settle. Terminal Stop
joins preparation, project IO, background work, cancellation callbacks and the
single plugin Stop task before disposing the context. A pending Stop retains
the instance, manager, process and user-root lease; shutdown/disposal remain
retryable. ExecuteCommandAsync has not become a generic owned callback tracker.
Same-process trusted plugins are not a security sandbox or force-stop mechanism.

Normal native close calls the coordinator's internal `PrepareCloseAsync(token)`
before terminal `DisposeAsync()`: it synchronously seals new runtime/action
admission, joins admitted actions and the editor, then calls committed hooks.
Success remains sealed through disposal. A pending hook remains owned and a
retry joins its actual task. A settled preparation/editor failure before Stop
reopens the same live manager and editor for correction; no manager restart or
private-buffer replacement occurs. A close that supersedes a queued switch
still prepares the live current manager. A switch already at Stop/candidate
cleanup, or a failed actual Dispose, follows the existing owned cleanup path
without activating a queued target or reviving a retired manager. After a failed
actual Dispose, only the coordinator's confirmed cleanup state can reopen the
document for correction; retry close joins it via Dispose again. The direct
isolated legacy probes retain their distinct Stop-only persistence coverage.

Native close also admits this seal while a shell action, switch, dialog or startup
is still pending. It retains close intent before coordinator publication, and
joins the actual shell/initialization continuations before closing the window.
The overload `PrepareCloseAsync(flushHost, token)` executes the root-bound host
flush callback inside the sealed queue after earlier admitted work and before
document/hooks/Stop. A dispatcher callback returns its actual completion; it
must not call RunCurrent recursively. A failed chosen theme write therefore
cannot be skipped merely because new action admission has already closed.

Host theme writes are root-bound and owned alongside shell actions. A failed
chosen write retains its selected value, dirty state and fixed error category;
switch/normal close must flush it before detachment or retirement. An untouched
corrupt-file system-theme fallback is not a choice and does not write on leave.
Explicit recovery retains the exact damaged bytes before replacement. Context
and ordinary preferences autosave; credential drafts do not.

Every pending theme-repair replacement checks its observed original again, even
when a previous attempt already created a backup. Changed bytes refuse ordinary
flush/selection/close retry and retain the chosen theme. The conditional native
recovery action explicitly calls `ObserveRecoveryAsync()` to observe the current
file while keeping that choice and prior backups; the new bytes require their
own exact backup before replacement. A later change refuses again. This applies
to intervening valid or unknown metadata; it does not claim automatic migration
or downgrade. No existing backup is overwritten or pruned.

`PluginProjectContextException` is sealed, has a fixed message, exposes ErrorCode
and retains no raw inner exception. Its public constructor still allows a plugin
to supply arbitrary strings: hosts allow-list known storage/service codes before
rendering them and never inspect exception Message/ToString/GetBaseException.
Preparation returns fixed Pending/Canceled/Busy/Failed categories or known
storage codes. User instructions are displayed only in their native editor.

The SDK snapshot contains exactly ten listed source files, two 0.3.0 NuGet
packages and both consumer READMEs, with projectServices=1, source/file hashes
and the Windows PRI when native packaging applies. The artifact is
`workbench-sdk-0.3.0`. Linux `-ReferenceOnly` output proves compilation and
integrity only. Current Windows native execution, old prebuilt SDK 0.2 business
compatibility and Windows 11 human acceptance require their own current-source
evidence; historical 0.2 reports do not establish them.
