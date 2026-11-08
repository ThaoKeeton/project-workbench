# Native Activity Coordination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make existing native plugin source and virtual-start paths mutually exclusive through actual ownership, with safe cancellation and terminal handoff.

**Architecture:** One plugin-private fail-fast coordinator plus an internal Domain terminal-retirement port. Existing NativeRemoteOperation owns its single CTS; NativeActionOwner retains UI/notification/picker work. No owned driver or new execution page is involved.

**Tech Stack:** .NET10/C#, xUnit actual Core/supervisor/filesystem fixtures, managed WinUI reference compilation, PowerShell provenance guards.

**Spec:** `docs/superpowers/specs/2026-11-08-native-activity-coordination-design.md`.

## Global Constraints

- Host minimum0.4.0, SDK/API1, Testing0.2.0, immutable Core/owned formats and public SDK remain unchanged.
- Per-plugin/project coordinator; no global user mutex, automatic queued action, generic engine, source-to-device execution, runtime fault flag or live provider/key/message/user-PC operation.
- Real callbacks/accepted writes/retirement/notification tails settle before admission releases; informational IsBusy is not proof.
- No native code/probe/CI/owned panel/source fixture/package/release change; no remote push/rerun/financial settings, deletion,403 bypass or PR retry by the implementer.
- Native27/current Windows and business compatibility/delivery/Windows11 human gates remain open; Linux evidence is labelled precisely.

## Review Focus

- Terminal journals precede actual supervisor postfix/Stop: TerminalHandoff uses real barriers/shared retirement.
- Selected terminal history may hide a live owned run: CurrentAttemptRefusal tests live/start-before-definition/failed-start/cancel states without data/error mutation.
- Remote cancellation can race synchronous capture or retirement: RemoteHandoffCancel installs the existing CTS first, joins retirement, then makes zero credential/provider calls.
- Same-code backend errors differ from refusal: TypedRefusalAndCapture tests rejected/accepted StoreBusy and mutable captured input independently.
- UI/observer/lifecycle tails outlive operation result: FinalIdleAndLifecycle tests real retained callbacks, delayed prefixes, seals, Stop and original outcome preservation.

---

### Task 1: Independent source/virtual coordinator and complete entry routing

**Required production paths:** create `plugins/Workbench.Testing/NativeActivityCoordinator.cs`; modify `TestingPlugin.cs`, `TestingPage.cs`, `TestingSourcePanel.cs`, `TestingPage.Rendering.cs` only as needed for safe-code/projections; modify `plugins/Workbench.Testing.Domain/TestingController.Runs.cs` and its csproj friend declaration. Do not rewrite NativeActionOwner/NativeRemoteOperation or owned/SDK/Core behavior.

**Tests/provenance:** link coordinator in `tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj`; create `TestingNativeActivityTests.cs`; extend `TestingControllerAdmissionTests.cs`; reuse `TestingDomainFixture.cs`/AsyncTestGate/GatedVirtualAdapter (only targeted fixture extension if necessary). Update `scripts/TestingPluginDelivery.psm1` to audit new/changed test inputs and test project exactly once; extend `tests/scripts/TestingPluginPackage.Tests.ps1` with real recorded-hash corruption refusals for new inputs. No new line-presence test or optional skipped proof.

**Interfaces:**

- Internal `TestingController.TryJoinPriorTerminalRunAsync():Task<bool>` is a non-serialized retained Domain operation. False live ownership is not an exception/error update. True requires actual release; use existing join/retirement, no caller cancellation wrapper or new Stop loop. Check current attempt rather than selected history; do not claim atomicity for arbitrary independent Domain callers outside the plugin reservation.
- `NativeActivityCoordinator(TestingController, NativeRemoteOperation)`; `IsBusy:bool`, `Seal():IDisposable`, `Terminal():void`, `DrainAsync():Task`.
- `SourceAsync<TInput,TResult>(Func<TInput> capture, Func<TInput,Task<TResult>> action, Func<Task> settled):Task<TResult>`.
- `RemoteSourceAsync<TInput,TResult>(Func<TInput> capture, Func<TInput,CancellationToken,Task<TResult>> action, Func<Exception?,Task> remoteSettled, Func<Task> finalNotify):Task<TResult>` with reservation -> existing remote CTS -> synchronous capture -> terminal join -> token checks -> action -> remote cancellation/settlement -> final notification -> actual release.
- `VirtualAsync<T>(Func<Task<T>> action,Func<Task> settled):Task<T>`; a Task convenience adapter may use a private bool result. Private `NativeActivityAdmissionException` carries `NativeActivityRefusal { Busy, Closed }`, safe `ErrorCode`, and `ToTestingException()`. Only own reservation, explicit false handoff, or proven pre-delegate remote refusal gets that tag.
- Plugin wrappers cover Run/Retry/queue/start, local file/bundled imports, rule generation and remote Figma/AI. Remote methods accept synchronous capture callbacks; copy mutable node/criteria/selection input exactly before awaits. Preserve public parameterless plugin activation. No source-services injection in Task1.

- [ ] **1. Write meaningful failing ownership tests.** Actual Domain/supervisor barriers establish terminal-but-still-owned retirement; direct linked coordinator tests prove no backend prefix for conflict/live ownership. Missing-method compile errors are setup only: use minimal compiling stubs/baseline extraction for behavior RED, then retain the exact failing assertions.
- [ ] **2. Run targeted RED.** `dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~TestingNativeActivityTests|FullyQualifiedName~TestingControllerAdmissionTests"`; retain argv/actual output/exits and source identity. No test may infer retirement from a timeout or replace the real supervisor.
- [ ] **3. Implement the Domain port/coordinator.** Preserve first action failure and acknowledged state through presentation failure; record/observe notification errors safely. Do not classify backend errors by global busy/code alone. Source cancellation during terminal handoff can return after request/callback completion while real work remains pending, but subsequent provider/credential counts must be zero after actual join. All real pending tasks remain owned; no abandoned WaitAsync/WhenAny.
- [ ] **4. Route every actual entry and lifecycle path.** Frozen map in the task brief/recon covers page and commands. Include local picker-import backend, bundled imports, rule generation, Figma/selected-only AI and all four virtual transitions; avoid nested acquisition. Keep Cancel/history/refresh/export/draft correction outside arbitration. Seal coordinator/owner/remote before cancellation; retain both final notification/drain stages. Preserve SourceDraft status on rejected competing invocations; safe-copy before asynchronous handoff.
- [ ] **5. Verify GREEN and preserved boundaries.** Targeted new+existing NativeRemote/Ownership/Source/Controller/Figma/AI tests; then `dotnet test ProjectWorkbench.Core.slnx -c Release --nologo` once after final code. Full suite contains an actual90second deadline test: do not apply a60second hang timeout. Build actual `tests/Workbench.NativeReference` with `-p:OwnedLabReferenceOnly=true -p:UseWinUI=false -p:AppxGeneratePriEnabled=false -p:WindowsAppSDKSelfContained=false`; require no compiler errors, report warnings exactly. Run helper/provenance guards and parser/source checks for amended PowerShell. No fake production ZIP or native success. Do not rerun fullCore for later script/doc-only fixes without a relevant concern.
- [ ] **6. Freeze source/report/receipts, commit local only and release writer.** Record exact covering files, commands/output/failed attempts, native limitations and full entry map. Root independently verifies, generates BASE..HEAD log/stat/U10 packet and dispatches fresh spec/quality review. Actual Windows verification resumes only with supported availability/remedy evidence. No user human testing is requested.

Self-review: the design's five failure classes each have a named test above; exact method/closed-code/count contracts match; all source and virtual starts are covered; excluded native/source-fixture work remains in the original owned-pilot plan. This plan can finish source/portable verification independently and cannot close the application release gate.
