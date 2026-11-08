# Owned Execution Panel Source Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the functional same-window owned execution panel and test its actual portable ownership composition without claiming Windows qualification.

**Architecture:** The existing real owned controller/journal and fixed Windows factory feed a native subpanel. A private bridge shares source/virtual admission; a small fixed-dependency production lifecycle helper retains cancellation, controller, notification and UI tails. Tests link these same production helpers with actual Core/supervisor/store and explicitly scripted transport.

**Tech Stack:** .NET10/C#, WinUI3 code controls, xUnit, PowerShell; Linux managed reference compilation only.

**Spec:** `docs/superpowers/specs/2026-11-08-owned-panel-source-design.md`; original `docs/superpowers/specs/2026-11-08-owned-native-pilot-design.md` remains authoritative for native/record/evidence contracts.

## Global Constraints

- Host minimum0.4.0, SDK/API1, Testing package0.2.0, assembly version1.0.0.0, protocol1 and target `owned.win32.task-form.v1` remain unchanged.
- Production ZIP remains exactly eight files; provenance records/real hash-refusal guards change, payload/version/helper-validation/output behavior does not.
- Same production TestingPage, no embedded independent application, generic computer API, selector interpreter, AI/source-to-action mapping, new driver/runner algorithm, production fault flag, sandbox or hot-unload claim.
- Actual methods reserve atomically; presentation busy/admission values never authorize. Capture scenario/retry before awaits; owned CTS and returned completion exist before prefix/handoff.
- Cancellation joins actual work. Retain first operation failure/acknowledgment, immutable history, real notification/UI/bitmap tails and journal ownership until safe retirement. No abandoned cancellation wrappers, self-drain or late status mutation.
- Exact header/retry/RecoveryNeeded Chinese copy and `owned-pilot-{scenario,run,cancel,retry,history,events,state,frame,status}` AutomationIds from the spec.
- All27/current Windows/installed/runtime/business51+12birth instances/SDK/final delivery/production crash/upgrade/rollback/download gates remain OPEN. Historical9of27 abort UNKNOWN; no sixth speculative Task3 fix. Windows11 human acceptance is deferred.
- SourceServices/compiled SourceAcceptance, native probe/workflow/maintenance, SDK/Core public contracts and business repositories are excluded.
- No real providers/credentials/Webhook/DingTalk messages/user-PC access, old artifact deletion, billing change,403 bypass, denied PR retry, merge/release/deploy or binary carrier. No push/rerun without new account-remedy evidence.

## Review Focus

- Pending owned handoff cancellation before Pilot.Run exists must retain prior retirement and prevent every owned prefix (T1 PendingHandoffCancel).
- Final informational idle, post-release render and nested/terminal leave barriers must not authorize or leave controls permanently disabled (T1 AdmissionRendering).
- Throwing cancellation/cleanup and asynchronous pilot-seal disposal notifications must join all branches/UI before journal release (T1 ProductionLifecycle).
- Late frame success/failure across different runs, same sequence, reselection and page generations must never replace current evidence (T1 EvidenceIdentity).
- Partial initialization/Stop/unload and command entry routes must retain actual work without duplicate cancellation, source status writes or loss of drafts (T1 PartialStopAndRouting).

---

### Task 1: Functional owned panel and retained production composition

**Files:** Create `plugins/Workbench.Testing/{NativeOwnedPilot,NativeOwnedEvidence,NativeTestingLifecycle,TestingOwnedPilotPanel}.cs`. Modify `NativeActivityCoordinator.cs`, `NativeRemoteOperation.cs`, `NativeActionOwner.cs`, `TestingPlugin.cs`, `TestingPage.cs`, `TestingPage.Rendering.cs`, `TestingSourcePanel.cs`; change only visibility of `OwnedPilotController.DrainNotificationsAsync` in `plugins/Workbench.Testing.Domain/OwnedLab/OwnedPilotController.cs`. Create `tests/Workbench.Core.Tests/{TestingNativeOwnedPilotTests,TestingNativeOwnedEvidenceTests,TestingNativeLifecycleTests}.cs`; modify the test csproj for source links and existing activity/remote tests only if needed to cover changed contracts. Modify `scripts/TestingPluginDelivery.psm1`, `tests/scripts/TestingPluginPackage.Tests.ps1` for source provenance/refusals; update truthful status in `docs/{testing-plugin,windows-verification}.md`.

**Consumes:** `OwnedPilotController(scope,factory,time)` and its Initialize/Run/Retry/Select/ReadFrame/SealAdmission/CancelAndDrain/PrepareLeave/Stop/Snapshot/Changed; public production `Win32OwnedDriverFactory(scope,time)`; `TestingController.TryJoinPriorTerminalRunAsync():Task<bool>` and actual shared retirement; existing NativeActionOwner/RemoteOperation/EvidenceCache. Domain already friends Testing and Core.Tests. No new public SDK/friend/constructor contract is needed.

**Produces (internal, exact):**

```csharp
// On owner, coordinator and remote-operation; discount only this component's own live exact lease.
bool CanAdmitForRender(IDisposable? temporarySeal = null);
// On coordinator: projection only; existing final notification stays inside actual reservation.
bool CanAdmit { get; }
Task<TResult> OwnedAsync<TInput,TResult>(NativeRemoteOperation operation,
    Func<TInput> capture, Func<Task> admitted,
    Func<TInput,CancellationToken,Task<TResult>> action,
    Func<Exception?,Task> operationSettled, Func<Task> finalNotify);
// On existing remote operation; old request-only method retains its contract.
Task RequestCancellationAndDrainAsync();
// On actual Domain pilot: expose existing implementation internally, no algorithm change.
internal Task DrainNotificationsAsync();

NativeOwnedPilot(OwnedPilotController pilot, NativeActivityCoordinator activity,
    NativeRemoteOperation operation);
bool IsBusy { get; }
bool IsPendingHandoff { get; }
Task<OwnedRunSnapshot> RunAsync(Func<OwnedScenario> capture, Func<Task> notify);
Task<OwnedRunSnapshot> RetryAsync(Func<string> capture, Func<Task> notify);
Task CancelAsync();

NativeTestingLifecycle(NativeActionOwner owner, NativeActivityCoordinator? activity,
    NativeRemoteOperation sourceOperation, NativeRemoteOperation ownedOperation,
    TestingController? virtualController, OwnedPilotController? pilot,
    Func<NativeReopening?,Task> renderLatest, Func<Task> retireUi);
void SealLifetime();
Task PrepareLeaveAsync(CancellationToken cancellationToken = default);
Task StopAsync(CancellationToken cancellationToken = default);
Task RetainActivityAsync(Func<Task> transition);
NativeTestingAdmission ReadAdmission(NativeReopening? reopening = null);
// Lifecycle alone creates/invalidate tickets holding its exact phase leases.
record NativeTestingAdmission(bool UserActions, bool ActivityStarts);

record NativeOwnedEventId(string RunId, int Sequence);
record NativeOwnedSelection(string RunId, OwnedEvent Event, string Key);
static NativeOwnedSelection? NativeOwnedEvidence.Resolve(OwnedRunSnapshot? displayedRun,
    string? displayedRunId, NativeOwnedEventId? selected);
```

`TestingPage.NotifyAsync(NativeReopening? reopening = null, bool retryEvidence = false)` passes the ticket into a current dispatcher render; both subpanels consume the same freshly computed admission. UI-private helper details may follow existing patterns without changing these portable ports. One production writer; raise a genuinely conflicting interface before implementing an alternative.

- [ ] **1. Write behavioral failing tests and compiling minimal ports.** `PendingHandoffCancel` checks real live/current virtual refusal, held real terminal Stop/postfix, CTS-before-capture (ordinary/reentrant), zero owned prepare/Queued/launch/action after cancellation, and no late cancellation of a later entry. `AdmissionRendering` calls production RetainActivity for command-style suffix, holds initial notification with Cancel available, final callback and exact/foreign/disposed/nested/terminal seals. `ProductionLifecycle` calls the actual production helper with real controllers/journals plus held prep/retirement/callback/notification/decode delegates; asserts Leave/Stop pending and owner.lock unavailable until real joins, virtual cleanup runs despite another error, pilot seal release Changed tail stays retained. `PartialStopAndRouting` covers uninitialized/partial controller Stop, lifetime revocation and no new prefix. `EvidenceIdentity` uses real persisted frame SHA and cache requests across A/B/reselection/generation; it asserts no fake bitmap decoding. Positive/empty/assertion bridge runs preserve4/4/3 results, frozen retry identity/current helper, corrupt/read-only refusal and original committed outcome despite observer failure.
- [ ] **2. Run meaningful RED.** `dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj -c Release --nologo --filter 'FullyQualifiedName~TestingNativeOwned|FullyQualifiedName~TestingNativeLifecycle'`. Retain failing behavior output and exact source; missing API/compile/setup failures alone are not RED. Correct a flawed assertion transparently and rerun the actual intended missing behavior.
- [ ] **3. Implement bridge/admission/cancellation.** Reuse shared cancellable composition; only owned supplies initial notification. Reserve/install owned entry, synchronously capture/mark pending, start actual terminal-handoff task, safely await retained admitted notification, ALWAYS join that same handoff, reject false, check token, clear pending then run actual pilot. Await settlement/final notifications. Entry completion is installed before prefix; cancel-and-drain captures exact entry/cancellation/completion under lock and joins both even on errors, never retargeting `_active`. Existing source cancellation/refusal/error-origin semantics remain.
- [ ] **4. Implement the fixed production lifecycle.** Follow the spec and frozen recon supplement: construct before init awaits; temporary seals before all cancellation; start source/owned/pilot cancellation before owner drain; continue both controller preparation/cleanup after failures; pilot temporary seal release→pilot notifications→owner UI→exact-ticket reopening→synchronous ticket invalidation/seal disposal. Stop synchronously installs permanent seals before waits, joins in-progress Leave, retires/unsubscribes every UI producer and accepted bitmap work BEFORE pilot.Stop releases journal. Preserve first failure and join later faults. Partial initialization uses the same helper. Unloaded is detach-only; host hooks never run inside owner/activity actions.
- [ ] **5. Implement actual controls/evidence/routing.** Compose fixed production scope/factory/controller/bridge/lifecycle. Use RetainActivity for EVERY source/virtual/owned start wrapper reached by pages and commands. Keep existing picker/original IO ownership; keep draft/history/cancel separate. Own Run/Retry uses a non-author-busy handler; pending Cancel works before journal run exists. Exact scenario copy/IDs/retry/recovery, real history/events/raw state/frame reads, safe errors and current-helper retry. Scope SIMULATED copy to virtual section. Retain actual decode/dispatcher tails and guard identity both sides of decoding; never inject native/fixture modes or source mappings.
- [ ] **6. Verify focused GREEN and provenance.** Rerun new tests plus existing `TestingNativeActivityTests`, `TestingNativeRemoteTests`, `OwnedAdmissionTests` and controller/source ownership suites affected by the change. Add new/reused claimed tests and test project to Get-TestingSourceFiles; require actual single length/hash records and corrupt each relevant record to exercise production hash refusal. Run `pwsh ./tests/scripts/TestingPluginPackage.Tests.ps1 -HelperGuardsOnly` and parse changed ps1/psm1. Preserve errors/corrections, no production ZIP.
- [ ] **7. Verify final code once.** `dotnet test ProjectWorkbench.Core.slnx -c Release --nologo` (includes real90second deadline case; no60second hang/blame limit). `dotnet build tests/Workbench.NativeReference/Workbench.NativeReference.csproj -c Release --nologo -p:OwnedLabReferenceOnly=true -p:UseWinUI=false -p:AppxGeneratePriEnabled=false -p:WindowsAppSDKSelfContained=false`. Expect exit0/full portable counts with named Windows-only skips, reference0warnings/errors. Save exact argv/env/source hashes/raw logs/elapsed/results/local DLL identities. These do not execute WinUI/MSVC/native. Do not repeat full suites for documentation-only changes.
- [ ] **8. Commit and release writer.** Local feature commit only, `feat: expose retained owned execution panel`. Full task report with failures/concerns, evidence/source receipts and writer release. Root verifies report/evidence/actual producer bytes, generates exact BASE..HEAD log/stat/U10 packet and requests fresh independent task spec+quality review. Actual runtime/integration/release remain open. Original entire-owned-phase review with four deferred findings follows later tasks, not this source-stage approval.

## Self-review and continuation

Source-only ordering correction is explicit in the binding stage spec and original plan/spec. Five review-focus classes map to concrete T1 tests. Constructor/delegate/notification ports are fixed; no SourceServices/fixture/native/maintenance feature is implied. Source acceptance is actual production code plus portable controllers/store/supervisor and whole managed compilation; actual WinUI/native/release acceptance cannot be inferred. Existing dedicated feature checkout is isolated from the business repos; retain it, all .sdd logs and all failed evidence. User opted out ordinary design approvals and selected Sol Max development; continue with that recorded method.
