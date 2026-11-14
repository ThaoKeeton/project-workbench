# Project Workspaces Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a native single-project-first workspace foundation with isolated settings/data, safe multi-project switching, retained context/drafts and an optional backward-compatible project SDK.

**Architecture:** Adopt the legacy root in place and derive new managed roots from stable catalog IDs. A controller owns the base lease and all runtime/editor transition work; optional SDK services bind plugins to the current project without changing API1/runtime1. Testing/controlled devices remain explicitly staged in the overall spec.

**Tech Stack:** .NET10.0.403/WinUI3/WindowsAppSDK1.8.260921001/C#/xUnit/PowerShell7/GitHub WindowsServer2025.

**Spec:** `docs/superpowers/specs/2026-11-07-project-workspaces-design.md`

## Global Constraints

- Work only in project-workbench on feat/native-plugin-workbench; baseline a5aac3d498a553508a8f5b20ea1093e10dc9c9d9, original work/main and business repositories preserved.
- Keep .NET SDK10.0.403, net10.0/win-x64, WindowsAppSDK1.8.260921001, unpackaged/self-contained directory; no trim/AOT/single-file/embedded independent GUI/WebView/plugin XAML.
- Final app/SDK preview0.3.0, stable assembly1.0.0.0, API1/runtime services1; old IPluginContext/IPluginRuntimeContext/WinUiView signatures unchanged; optional project services1 only.
- Frozen DingTalk0.3.2/c0c1d33 and UPCAM0.1.0/6370b9d production bytes/provenance remain unchanged; genuine acceptance48e67e8 stays test-only. Real prebuilt old-SDK business compatibility remains required.
- Legacy default root stays in place. New project roots derive from managed IDs only; archive/mode changes never delete/move data or copy credentials/consents/active business ledgers.
- Single-project normal UI has no multiple-project selector/count/manager; normal new settings/context edits auto-apply without an additional activation checkbox/save click.
- All known preparation/service/editor/background/Stop work must actually finish before ownership is released or another project's runtime starts; no forced Stop/hot unload/security sandbox claim.
- Preserve existing regression coverage, native14/management17+theme1/platform7/business51; explain and strengthen intentional sample autosave expectation changes rather than hide removed checks.
- No real credentials/provider HTTP/Webhook/messages/user-computer control, business-repo mutation, denied PR retry, merge or deployment. Default local/synthetic data only.
- Distinguish implemented/callable SDK/design-only/unverified capabilities and Linux/reference/Server automatic/Windows11 manual evidence; human acceptance is deferred, never passed by implication.
- Host failures never evaluate arbitrary exception Message/ToString/GetBaseException or echo provider/settings/evidence values; user-entered project/context content is rendered only in its intended native editor.


## Review Focus

- Existing root already contains data or malformed/newer metadata: adopt without moving files; refuse/reset nothing. Task1 real hash inventory/recovery tests.
- Caller supplies a project ID/root alias or stale revision: refuse traversal/links/lost updates without touching current files. Task1 exact path/conflict tests.
- Old preparation/Stop or failed target cleanup remains alive: retain every session/lease and do not activate another runtime. Task2/3 retained-task and actual-manager tests.
- Settled pre-Stop switch preparation/editor failure: reopen the same live manager/plugin editor/actions after owned work settles; retain attempted-target metadata for explicit retry without losing a created target. Pending work, post-Stop/candidate cleanup and failed Dispose remain sealed. Final fix covers the actual live command and native private-editor correction routes.
- Last accepted native context value: capture synchronously into the plain model with the reload guard, refresh visuals later, refuse fresh post-barrier edits, and exercise same-turn edit-to-switch/close without waiting for asynchronous autosave.
- New project's controls still use original RunOptions.UserRoot or inherit previous theme/secret UI: every operation must use effective root. Task4 actual native A/B isolation and empty-credential checks.
- Single/multiple transitions lose draft/catalog state or show extra UI: flush before transition, archive metadata-only and verify actual conditional native controls. Task3/4 failure/retention/UI tests.

---

### Task 1: Managed catalog and versioned project-context storage

**Files:** Create `src/Workbench.Core/Projects/ProjectWorkspaceModels.cs`, `ProjectWorkspaceCatalog.cs`, `ProjectContextDocumentStore.cs`, focused storage helper if needed; tests `tests/Workbench.Core.Tests/ProjectWorkspaceCatalogTests.cs`, `ProjectContextDocumentStoreTests.cs`.

**Interfaces:** Namespace `Workbench.Core.Projects`.
- `ProjectWorkspaceInfo(string Id, string Name, bool IsArchived)`; `ProjectWorkspaceSnapshot(string Revision, string ActiveProjectId, IReadOnlyList<ProjectWorkspaceInfo> Projects)`; `ProjectWorkspaceCreation(ProjectWorkspaceInfo Project, ProjectWorkspaceSnapshot Snapshot)`.
- `ProjectWorkspaceCatalog(string baseRoot)`: `OpenAsync(ct)`, `CreateAsync(string name,string expectedRevision,ct)` returning creation; `SetActiveAsync(string id,string expectedRevision,ct)`, `RenameAsync(string id,string name,string expectedRevision,ct)`, `SetArchivedAsync(string id,bool archived,string expectedRevision,ct)` returning snapshot; `GetProjectRoot(string id)` derives only validated ID; `GetRecoverableAsync(ct)` returning `IReadOnlyList<ProjectWorkspaceInfo>` and `RecoverProjectAsync(string id,string expectedRevision,ct)` returning snapshot; `RecoverCatalogAsync(ct)` returns snapshot only for explicit recovery of validated retained snapshots.
- `ProjectContextDocument(string Revision,string Text,bool IsMissing)`; `ProjectContextDocumentStore(string userRoot,string projectId)` with `ReadAsync(ct)` and `WriteAsync(string text,string expectedRevision,ct)` returning document. Domain failures use sealed `ProjectWorkspaceStoreException(string errorCode)` with fixed `ErrorCode`, not raw inner exceptions/messages; UI adapters allow-list codes rather than trusting arbitrary supplied strings.
- Exact paths/limits/schema/IDs/revision are in spec. All async methods accept `CancellationToken cancellationToken=default`. Store operations use cooperating file leases and atomic owned staging; caller still owns whole-runtime base/project lease.

- [ ] Write meaningful failing tests at the storage boundary: `LegacyAdoptionPreservesEveryExistingByte`; `NewProjectDoesNotCopySettingsSecretsTrustOrLedger`; `ArchiveRestoreAndSingleModeRetainRootsAndFiles`; `CorruptUnknownCatalogPreservedAndConditionalRecoveryRetainsOriginal`; `FailedCreateRetainsRecoverableOwnedRoot`; `IdsLinksAliasesAndStaleRevisionsCannotWriteElsewhere`; `ContextMissingReadDoesNotCreateFiles`; `ContextConflictFailedWriteAndInvalidSchemaPreserveOriginal`; `ContextProjectBindingAndUnicodeLimitsRoundTrip`.
- [ ] Run focused tests RED (a minimal callable scaffold may precede; absent symbols alone are not behavioral evidence). Record actual assertions/IO behavior, not only parse failures.
- [ ] Implement the exact catalog/context APIs, snapshot recovery and root/regular-file/revision checks with no directory deletion. Bound metadata and preserve unknown bytes. Successful author edits intentionally replace current text; structural catalog snapshots and corrupt originals remain retained.
- [ ] Run `dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj -c Release --no-restore --nologo --filter 'FullyQualifiedName~ProjectWorkspaceCatalogTests|FullyQualifiedName~ProjectContextDocumentStoreTests'`; expect0fail. No unchanged full suite here.
- [ ] Self-review, stage explicit own paths, commit/report/release writer. App/SDK versioning and UI are later tasks.

### Task 2: Optional project SDK, retained preparation and preview version coherence

**Files:** Create `src/Workbench.Sdk/ProjectContracts.cs`, `src/Workbench.Core/Projects/ProjectRuntimeScope.cs`, `PluginProjectContext.cs`, `src/Workbench.Core/PluginManager.Projects.cs`; modify `PluginModels.cs`, `PluginRegistrationContext.cs`, `PluginManager.cs` only required context/known-work integration. Update `Workbench.App.csproj`, both SDK csproj/README, `scripts/export-sdk.ps1`, `tests/scripts/SdkSnapshot.Tests.ps1`, `scripts/BusinessPluginDelivery.psm1`, delivery tests/workflow/active version docs as needed. Own first-party sample draft/preference persistence and affected sample-native expectations only if necessary for these hooks. Add focused SDK/context/preparation tests.

**Consumes:** Task1 catalog/context records and stores.
**Produces:** SDK API1/runtime1 unchanged; optional `WorkbenchProjectServices.Version=1`; `ProjectIdentity(string Id,string Name)`, `ProjectContextSnapshot(string Revision,string Instructions)`; `IProjectContext.Identity`, `ReadAsync(ct)`, `UpdateAsync(string instructions,string expectedRevision,ct)`; `IPluginProjectContext : IPluginRuntimeContext` with `IProjectContext Project` and `RegisterBeforeProjectLeave(Func<CancellationToken,Task> prepare)` (one init-time hook). Sealed `PluginProjectContextException.ErrorCode` has no raw inner exception; host renderers accept only known host codes, never arbitrary plugin-supplied code text.
- Core `ProjectRuntimeScope(string id,string name,string userRoot)` exposes immutable root/ID, current SDK identity, Task1 `Documents`, and `Rename(string name)` after persisted catalog rename.
- `PluginManagerOptions.ProjectScope` optional; default bound to its actual UserRoot/default identity for legacy callers. Scope root must match options root.
- `PluginManager.PrepareProjectLeaveAsync(string? pluginId=null,CancellationToken cancellationToken=default)` returns `ProjectPreparationResult(bool Succeeded,bool HasPendingWork,string? ErrorCode)`; `HasPendingProjectWork` reports actual pending preparation/service work. Retain one active preparation task; do not assume ExecuteCommandAsync tracks it. Terminal stop joins pending tasks/host-service calls before context disposal. Safe failures never read exception virtual text.
- Final app/SDK package0.3.0/file0.3.0.0, assembly1.0.0.0/API1/runtime1; old SDK0.2 business packages/minimumHost0.2 immutable. SDK snapshot actual10 sources including ProjectContracts, two NuGet/README/nativePRI, projectServices1, artifact `workbench-sdk-0.3.0`.

- [ ] Add behavioral RED for current-project service binding and revision conflict, immediate revocation after close, registered-hook commit/rollback, duplicate/late hook refusal, pending sync-prefix/ignored-cancel task retention, once-owned retry/drain before context disposal and hostile exception privacy. Test actual temporary stores/manager where possible, not only mocks.
- [ ] Implement service wrapper/lifetime operation ownership and preparation using retained-task/finite-wait semantics compatible with existing Stop. Keep dispatcher-free callback execution and original Stop reason/consent behavior. Do not block manager gate forever or release leases while service/preparation work lives.
- [ ] Preserve first-party nonsecret draft state before host switch/close/terminal UI operations, and normal sample preferences autosave without redundant activation. Failed preparation must leave data/buffer/current ownership recoverable. Do not autosave/copy credential drafts or change vendor business packages. Update affected old assertions to verify the new intended autosave/retention semantics and report each change.
- [ ] Perform coherent0.3 version/snapshot/delivery parsing changes. Preserve source/dirty/hash guards, frozen production minimumHost0.2 pins and all45 delivery cases including relative-entry fixture anchoring. Old real PE fixtures are metadata-only; no fake current-native claim. Historical evidence stays labeled by its source.
- [ ] Run focused new SDK/preparation/store tests; affected reference build; affected PowerShell delivery/snapshot checks (Linux snapshot must be marked ReferenceOnly). Actual Windows/old-prebuilt business execution is controller final gate. Commit/report exact public signatures/version/output changes and release writer.

### Task 3: Safe base-root ownership, document autosave and runtime switching

**Files:** Create UI-neutral `src/Workbench.App/WorkspaceRootLease.cs`, `WorkspaceRuntimeCoordinator.cs`, `ProjectDocumentSession.cs`, transition models in a focused file if needed; modify `HostRuntime.cs`/`HostRuntimeOptions.cs` only integration; link new neutral app sources into `tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj`, add coordinator/document/lease tests.

**Consumes:** Task1 catalog/document APIs; Task2 scope and preparation.
**Produces:**
- `WorkspaceRootLease.Acquire(string baseRoot)` and validated matching-root borrow tokens; existing `HostRuntime.AcquireLease` and constructor stay valid for direct/isolated callers. A normal app keeps base host.lock even with secondary root active. A borrowed default-runtime lease cannot dispose its controller owner; secondary runtime owns its own lease.
- `HostRuntimeOptions.Create(RunOptions options,ProjectRuntimeScope? projectScope=null)` supplies effective root/scope; optional HostRuntime overload accepts validated base owner.
- `ProjectDocumentSession(ProjectContextDocumentStore store) : IAsyncDisposable` with `InitializeAsync(ct)`, `Text`, `IsDirty`, `IsEditable`, `ErrorCode`, `Edit(string text)`, `FlushAsync(ct)`, `PreserveConflictAndReloadAsync(ct)` which durably keeps the dirty copy before reload. Serialized responsive autosave; no UI dependencies. Missing is editable empty; malformed is preserved/read-error, not silently editable default.
- `WorkspaceRuntimeCoordinator(RunOptions options,Func<string,ProjectRuntimeScope,WorkspaceRootLease,HostRuntime> factory)` owns catalog/base lease/current and failed candidate runtimes/document. Expose nullable-until-ready `Catalog`, `ActiveProject`, `Runtime`, `Scope`, `Document`, plus `Transition`; `InitializeAsync(ct)`, `SwitchAsync(string id,ct)`, `CreateAndSwitchAsync(string name,ct)`, `RenameAsync(string name,ct)`, `SetArchivedAsync(string id,bool archived,ct)`, `GetRecoverableAsync(ct)` returning project info list, `RecoverProjectAsync(string id,ct)` and `RecoverCatalogAsync(ct)` returning switch result, `RetryAsync(ct)`, `DisposeAsync()`.
- `ProjectSwitchResult(bool Succeeded,bool Pending,string? ErrorCode)`; `ProjectTransitionState` has `Idle, Preparing, StoppingCurrent, StartingTarget, PersistingSelection, Recovering, Blocked`; `ProjectTransitionSnapshot(ProjectTransitionState State,string? CurrentProjectId,string? TargetProjectId,string? ErrorCode)` identifies ownership without arbitrary provider values. Bodies/class splits are builder choices; contract and ownership order are binding.

- [ ] Write RED real-IO/integration tests: `BaseLeaseExcludesLegacyWhileSecondaryActive`; `BorrowDoesNotReleaseBaseAndAllRuntimesRetireBeforeOwner`; `SwitchFlushesLatestTextBeforeStoppingAndStartsOnlyOneRuntime`; `PendingOldStopOrPreparationDoesNotOpenTarget`; `CandidateFailureDoesNotRestoreOldBeforeCandidateActuallyRetires`; `CatalogCommitFailureKeepsOldSelectionAndBothDataRoots`; `CloseJoinsFailedCandidateAndEditorTask`; `ThemeSettingsSecretsAndContextRemainProjectBound`; `AutosaveConflictPreservesBufferAndBlocksUnsafeLeave`. Use actual PluginManager/TestPlugin for core lifecycle cases, deterministic gates rather than sleeps/repeated loops.
- [ ] Implement the spec transaction/failure order; retain old stopped manager/root until candidate initialization and catalog active commit succeed, restore only after candidate safe retirement. Retain references/leases to all unresolved work, no forcekill. Candidate HasPendingStops after Initialize also blocks successful selection even when Initialize returned normally; ordinary failed/invalid plugins without live pending work may remain visible as managed diagnostics. Resume only same-project existing consent rules. The injected factory receives the validated base owner; the neutral coordinator must not reference WinUI HostRuntimeOptions directly.
- [ ] Implement owned editor queue/flush/retry and error state; dirty failed/conflicting edits survive, never silently discard. Scope renames update in-memory identity only after catalog persistence.
- [ ] Run focused tests and neutral reference compilation; no broad foundation rerun. Commit/report explicit interfaces, real failure/RED/GREEN and constraints; release writer.

### Task 4: Native single/multi shell, project context and actual isolated probes

**Files:** Create `src/Workbench.App/ProjectWorkspacePages.cs`, `NativeWorkspaceProbe.cs`, `scripts/project-workspace-probe.ps1`, relevant report helper/tests if needed; modify `MainWindow.cs`, `ShellPages.cs`, `RunOptions.cs`, startup/close integration, CI workflow and user docs. Update first-party sample/native assertions only for new intended persistence if Task2 did not need them; preserve all original coverage. Add RunOptions/privacy/probe-report tests.

**Consumes:** Coordinator/scope/document; SDK0.3 and existing native UI helpers.
**Produces:** Normal one-project shell directly opens current content/title with no multi selector/count/manager. Application File menu exposes deliberate New/Open; actual selector only when>1 nonarchived, archive/restore metadata-only. Context editor autosaves, appropriate error recovery conditional; project switching is same-window and requires no second Apply click. All theme/settings/labels/data use effective root, not original base RunOptions.UserRoot. Reset system theme/recovery state before loading another project's preferences; no secret field reuse.
- Extend CLI with isolated `--workspace-probe seed|verify` and matching `--probe-root` GUID below dedicated temporary `ProjectWorkbench-workspace-probe`; conflicts with smoke/runtime/business or normal roots refuse. Existing modes retain their curated roots and original behavior. Probe data is synthetic only, no normal user/profile enumeration.
- Runner executes actual published EXE for seed/verify with safe Closed/OS-exit/root-lease evidence and new `project-workspace-probes` artifact. Keep14 native/17 management+theme1/7restart/all12business51 gates. Changed sample autosave assertions are identified, not silently dropped.
- Final fix requires seed16/verify3: the original seed12 plus immediate-context-switch, immediate-context-close, settled-switch-private-correction and actual-dispose-context-correction. Finalize each runner phase once with the first native identity/rejection and actual exit/lease observations; production-orchestration metadata fixtures are not native evidence.

- [ ] Write focused behavioral RED for workspace CLI root leakage/conflicts and report identity/exit/ownership rejection; new UI ordinary copy needs no mirror tests. Source-only/native-compile limits remain explicit until actual Windows.
- [ ] Integrate coordinator normal start/switch/close including all failed candidates/owned saves; keep safe startup management/retry, unknown metadata preservation and HostFailure text privacy. Show no placeholder testing/device capabilities.
- [ ] Implement native controls/dialogs for create/open/selector/archive/context. New context/preferences auto-save; conditional recovery only for actual fault. Never mark switched/rolled back before state proves it.
- [ ] Native seed/verify must observe legacy in-place adoption, exact single-UI suppression, multi selector, actual A/B context/theme/settings/empty credential separation, first-party draft retention, safe old/candidate pending-work refusal/retry, archive-to-single retention and native reopen/actual process restore. Reuse real controls/supervisors and known-owned synthetic fixtures; no fake desktop/provider tests.
- [ ] Run changed portable checks/reference build/scripts; self-review/commit/report. Controller then performs one whole-increment review froma5, one final fix wave, fresh full Windows/Linux workflow, actual0.3 host/SDK/companion/probe downloads and real prebuilt old-SDK business51 compatibility. Human Windows11/legacy2GUI/higher-vendor/live-provider acceptance remains deferred/unexecuted.
