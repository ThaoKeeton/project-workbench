# Owned Windows Pilot A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver fixed-scenario native automation, private evidence and WinUI history.

**Architecture:** Portable runner/store; actual business process-birth evidence; real C++ AUT/broker and managed launcher; installed UI/maintenance proof. No Pilot B mapping.

**Tech Stack:** .NET10/C#, WinUI3, MSVC x64 C++20 /MT, UIA/GDI/jobs, xUnit/PowerShell.

**Spec:** `docs/superpowers/specs/2026-11-08-owned-native-pilot-design.md`.

## Global Constraints

- Host minimum 0.4.0; SDK/API 1; Core case/update/virtual-evidence contracts unchanged.
- Testing production package 0.2.0, x64 only; no same-version replacement.
- Only repository-owned synthetic target `owned.win32.task-form.v1`, protocol 1.
- No AI/source-case execution, mapping overlay, generic device API, real service credentials/messages, or user-PC operation in Pilot A.
- Native plugin UI stays in the host WinUI window; AUT is a separate owned Win32 process.
- Production helper requires Windows MSVC x64, Windows SDK and static CRT; missing/unverified helper fails packaging.
- No SDK/Core/WinUI/.NET copies in plugin payload; no Core real-frame evidence writes.
- No remote deletion, 403 bypass, PR retry, merge, release, deploy, or binary-carrier workaround.
- Actual execution, preparation, accepted storage, cancellation callbacks, processes/job/IO and in-run notifications settle before idle; admission/run/root/project ownership remains held through final idle notification delivery.

## Review Focus

- Delayed launch/throwing callbacks: seal then join actual work (T1 SealDuringPreparation; T4 NativeLeaveWaits).
- Partial/cross-session/malformed wire: reject before action/allocation (T1 WireCorpus; T3 NativeBadFrame).
- Uncertain publication: preserve bytes, no inferred acknowledgment/retry (T1 PublicationCuts).
- UIA/GDI hang/early exit/nested-job or DPI failure: retire owned job/IO, no fallback (T3 NativeFailureMatrix).
- Recycled PID/stale report or missing/reference helper: match actual process birth, reject stale identity/no fake package (T2 BusinessCreation; T3 HelperIdentity; T4 Maintenance).

---

Paths are repo-relative. Root reviews before execution in its authorized isolated workspace; preserve others’ .sdd workspaces. Task boundaries are review gates. After Task1, the observed Windows PID reuse adds bounded Task2 identity proof; original native/UI tasks shift to3/4. No code/Git/tests/actions are executed by this document.

### Task 1: Portable fixed plans, protocol, journal and runner

**Files:** Create `plugins/Workbench.Testing.Domain/OwnedLab/{OwnedModels,FixedOwnedPlans,OwnedWireCodec,OwnedAtomicFiles,OwnedJournal,OwnedJournalFormat,OwnedLaunchGate,OwnedRunner,OwnedPilotController}.cs` (types/catalog/wire/files/prefix/execution/admission); modify `plugins/Workbench.Testing.Domain/Workbench.Testing.Domain.csproj` for test friend Workbench.Core.Tests. Tests: `tests/Workbench.Core.Tests/{OwnedFixedPlansTests,OwnedWireTests,OwnedJournalTests,OwnedRunnerTests,OwnedAdmissionTests,OwnedLabTestRig}.cs`.

**Interfaces — Workbench.Testing.Domain.OwnedLab:**
- DTOs use typed PascalCase spec fields: IDs/hashes/text string; nullable parent/error fields string?; protocol ushort; sequence/index int; UTC DateTimeOffset; immutable IReadOnlyList collections. OwnedInputPlan includes TargetId/Protocol/Scenario/Steps/Bytes:ReadOnlyMemory<byte>/Sha256. OwnedStep(Id:string,Action:TestAction). OwnedExecution and OwnedEvent follow exact spec field order; OwnedEventDraft omits generated schema/sequence/priorHash/executionSha/utc/captureKind/syntheticTarget, replaces FrameSha with Bmp:byte[]?. Journal stamps captureKind:string?/syntheticTarget:bool? exactly per spec. Freeze/copy before awaits.
- OwnedHelperIdentity(ImageSha,NativeSourceSha,SourceCommit,Msvc,Sdk,Profile):strings; OwnedGrant(Execution:OwnedExecution,ExecutionBytes:ReadOnlyMemory<byte>,ExecutionSha:string,DeadlineTimestamp:long), timestamp nonpersisted, same injected TimeProvider. Grant also exposes public LaunchGate getter/internal initialization; gate internal once-only binding, public TryResume(Action):bool/Revoke share one lock. Unbound/revoked/expired/consumed denies; only actual fast ResumeThread under gate. Profiles Production/AcceptanceFixture; production factory enforces Production.
- OwnedIdentity(uint BrokerPid,ulong BrokerCreationFileTime,uint TargetPid,ulong TargetCreationFileTime,ulong Hwnd,string ImageSha); OwnedState(string Title,string Toast,string Validation,string CreatedCount); OwnedObservation(OwnedIdentity Identity,OwnedState State,byte[] Bmp); OwnedRetirement(bool WasLaunched,int? BrokerExitCode,int? TargetExitCode,uint ActiveProcesses). Null target exit permitted only if unreported; zero-job proof still required.
- OwnedEventKind=Queued0/Started1/StepStarted2/StepResult3/Terminal4; OwnedOutcome=Passed/Failed/Cancelled. OwnedRunState=Queued/Running/Passed/Failed/Cancelled/Interrupted/RecoveryNeeded; OwnedRetirementReason=Completed/Cancelled/Failed/Expired/Revoked. OwnedRunSnapshot(Input,Execution,ExecutionSha,Events,State,Sequence,HeadSha,RecoveryNeeded,ErrorCode); OwnedRunSummary(Execution:OwnedExecution,Scenario,State,Sequence,HeadSha); OwnedPilotSnapshot(Runs,SelectedRun?,IsBusy:bool,AdmissionClosed:bool,ErrorCode:string?). OwnedLabException.ErrorCode is spec-allowlisted.
- `FixedOwnedPlans.Create(OwnedScenario):OwnedInputPlan`; `ParseFrozen(ReadOnlyMemory<byte>):OwnedInputPlan` validates exact catalog. `OwnedPacket(OwnedWireKind Kind,uint Sequence,string Session,byte[] Body)`; `OwnedWireCodec.Encode(OwnedPacket):byte[]`; `Decode(ReadOnlySpan<byte>,string session,uint sequence):OwnedPacket` validates header AND exact body.
- `IOwnedDriverFactory.PrepareAsync(CancellationToken):ValueTask<IOwnedPreparation>`. Preparation:IAsyncDisposable has Helper, `LaunchAsync(OwnedGrant,CancellationToken revocation):ValueTask<IOwnedSession>`. Session:IAsyncDisposable has Identity/InitialState and `ExecuteAsync(TestAction,CancellationToken):ValueTask<OwnedObservation>`, `RetireAsync(OwnedRetirementReason):ValueTask<OwnedRetirement>`. Retire/Dispose idempotent uncancellable joins. Public OwnedLaunchException : OwnedLabException carries safe ErrorCode+immutable OwnedRetirement only after actual pre-session retirement; validate active0/actual exits, created suspended broker counts WasLaunched=true. Typed clean receipt permits terminal after Queued/no Started; unknown/no valid proof => RecoveryNeeded/no terminal claim, preparation still joined.
- `OwnedScope(string ProjectId,string PluginId,string DataDirectory)`; `OwnedJournal.OpenAsync(OwnedScope,CancellationToken):Task<OwnedJournal>` owns lock through DisposeAsync; `AppendAsync(string,OwnedEventDraft):Task<OwnedRunSnapshot>`. Other journal operations are private to this task. Focused private OwnedJournalFormat owns strict schema and transition validation; journal owns filesystem/prefix. Internal controller overload may inject OwnedAtomicFiles for friend-test barriers only, without public/runtime override. Internal OpenAsync overload takes OwnedAtomicFiles, whose virtual `PublishNewAsync(string path,ReadOnlyMemory<byte>):Task` is the fault/barrier seam. OwnedRunner performs spec prepare/grant/Queued/launch/steps/retire/terminal, no generalized execution engine.
- `OwnedPilotController(OwnedScope,IOwnedDriverFactory,TimeProvider)`: SealAdmission():IDisposable synchronously revokes launch generation; InitializeAsync(ct), CancelAndDrainAsync(), PrepareLeaveAsync(ct), StopAsync(ct)=>Task; RunAsync(OwnedScenario,ct), RetryAsync(string,ct), SelectAsync(string,ct)=>Task<OwnedRunSnapshot>; ReadFrameAsync(string,int,ct)=>Task<byte[]>; Snapshot:OwnedPilotSnapshot; event Action<OwnedPilotSnapshot> Changed. ct=CancellationToken. Run/Retry finish after actual retirement/terminal IO; no BackgroundTasks restore. Event stepIndex zero-based0..3/null per kind; numeric scenario/action/event kinds, exact string outcome, UTC DateTimeOffset O. Final idle projection may be false consistently only after all in-run work/notifications settle; admission/run/root ownership persists through that final callback, all drain methods join, new Run/Retry refuses. No observer-only relabeling of committed outcomes.

- [ ] **1. Write failing catalog/wire tests.** Pin3 exact four-step plans; reject changed frozen input. Golden SetTitle body `04 00 00 00 F0 9F 94 AC` =>60B frame/bodyLength8. WireCorpus includes262145B length, partial/trailing/cross-session/order/unknown/Unicode/title257/control/BMP/identity cases. MaxValue roundtrip is legal; session owns no-wrap progression. Host codec forbids internal TargetReady0.

```csharp
Assert.Equal(4, FixedOwnedPlans.Create(OwnedScenario.Positive).Steps.Count);
Assert.Equal("DELIBERATELY WRONG TOAST", FixedOwnedPlans.Create(OwnedScenario.AssertionFailure).Steps[2].Action.Value);
Assert.Equal(60, encoded.Length);
Assert.Throws<OwnedLabException>(() => OwnedWireCodec.Decode(crossSessionFrame, expectedSession, 1));
```

- [ ] **2. Run red:** `dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj -c Release --filter "FullyQualifiedName~OwnedFixedPlansTests|FullyQualifiedName~OwnedWireTests"`; intended missing behavior, not setup failure.
- [ ] **3. Implement models/catalog/codec interfaces**, exact spec caps/numerics; no configurable actions or virtual adapter.
- [ ] **4. Write failing journal/runner tests.** Scripted transport + actual publication barriers cut before/after frame/event/marker, throw after publication, hold flush/retirement/callback. Test seal after prep and after Queued, clean interruption/retry vs orphan read-only root, valid restored terminal, late committed result after cancellation,4/4/3 results and current-helper retry. Add held suspended-resume/seal/expiry gate tests, typed actual-retirement vs unknown launch failure, held final-idle admission/drain, and real known child testhost crash/lock/prefix recovery; no dumps or uncontrolled PID/name termination.

```csharp
Assert.Equal(0, driver.LaunchCount); // uncertain Queued or sealed preparation
Assert.False(stopTask.IsCompleted); // actual work still held
Assert.Equal(1, driver.ActionCalls); // no automatic reissue
Assert.Equal(OwnedRunState.RecoveryNeeded, uncertain.State);
Assert.Equal(originalInputBytes, retry.Input.Bytes.ToArray());
```

- [ ] **5. Run red:** same command, filter `FullyQualifiedName~OwnedJournalTests|FullyQualifiedName~OwnedRunnerTests|FullyQualifiedName~OwnedAdmissionTests`; intended retention/state failures.
- [ ] **6. Implement journal/runner/controller.** Immutable prefix head; lease/hash before grant/Queued; no launch after ambiguity; accepted writes uncancelled. Seal launch generation before cancel/drain, including delayed prefixes; external seal survives owner drain/UI tails; retain callback/UI bridges. Retry accepts verified Passed/Failed/Cancelled or clean Interrupted; live Queued/Running and every RecoveryNeeded/read-only root refuse; verified restored terminal never implies prior caller acknowledgment.
- [ ] **7. Verify green:** `dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj -c Release --filter FullyQualifiedName~Owned`; `dotnet test ProjectWorkbench.Core.slnx -c Release --nologo`. Scripted IO is not Windows proof.
- [ ] **8. Review/commit T1 paths only**, `feat: add bounded owned pilot plans journal and runner`; root gate.

### Task 2: Bind existing business launches to actual process birth

**Files:** Modify `src/Workbench.App/BusinessPluginSmoke.cs`, `scripts/business-plugin-smoke.ps1`, `scripts/BusinessPluginReport.psm1`, and `tests/scripts/BusinessPluginReport.Tests.ps1`. Production and acceptance share the existing native report writer; do not alter either upstream plugin repository or package pins. Update `docs/windows-verification.md` only to explain identity evidence and deferred download/Windows11 limits.

**Interfaces/fields:** Native report adds `processCreationFileTimeUtc` as a positive JSON Int64 from its own actual `Process.GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc()` (dispose query object). Extend `Complete-BusinessPluginReport` with `[AllowNull()][object]$ProcessCreationFileTimeUtc` and `$ProcessExitFileTimeUtc`, validated in body so rejection still persists evidence. Preserve incoming `nativeProcessId` and `nativeProcessCreationFileTimeUtc`; persist independently observed `processId`, `processCreationFileTimeUtc`, `processExitFileTimeUtc`, actual `exitCode`/`hasExited`. Accept only actual positive integer FILETIME values (no strings, floats or bools), creation matches native exactly, exit>=creation only after confirmed exit. No fabricated value when retrieval fails. On rejected main reports, preserve separately labeled diagnostic-only nativeFailureEvidence from the expected sibling .evidence.json if available; never use it to authorize success/backfill main fields or erase rejection. Top native fields remain main observations, independent runner fields remain actual captured facts. Add a matching-looking-sidecar refusal regression; no generic host failure hook change. Export focused `Assert-BusinessProcessInstances -Phases <object[]> -ExpectedPhases <string[]>`: exact ordered phases, valid successful PID/birth/exit fields, unique PID+creation tuples; repeated numeric PID with different birth is valid. No generic process/device SDK.

- [ ] **1. Write failing proof regressions.** Cover missing/malformed/mismatched native birth, missing/invalid launcher creation/exit, preserving both observed identities on rejection, valid same numeric PID/different birth, repeated tuple and missing/reordered phase refusal. Existing nonzero/timeout/missing report/Closed/root/provenance cases remain. Compile-compatible parameter/function stubs may precede RED; missing command/parameter errors are setup failures, not behavior evidence.

```powershell
# A stale report from an earlier process with the same numeric PID must fail.
Reject { Complete $path -creation 200 -exitTime 300 } $path
# Different verified births identify two distinct serialized instances.
Assert-BusinessProcessInstances -Phases $reusedPidDifferentBirth -ExpectedPhases @('first','second')
```

- [ ] **2. Run RED:** `pwsh -NoProfile -File tests/scripts/BusinessPluginReport.Tests.ps1`; intended assertions/refusals expose old missing birth validation, not setup failure. Retain log and actual exit.
- [ ] **3. Implement captured native/launcher identity and validator.** After Start-Process retain/query its actual handle and creation before WaitForExit; after actual exit record exit FILETIME and exit code. Capture failure marks failed with known PID/facts, no successor/false success. Keep prior timeout/no-force policy and lease/package/provenance checks. Add fields to completed phase rows, assert exact sequence, print bounded actual tuples/checks. No synthetic birth values in native runs.
- [ ] **4. Verify locally:** `pwsh -NoProfile -File tests/scripts/BusinessPluginReport.Tests.ps1`; compile `tests/Workbench.NativeReference/Workbench.NativeReference.csproj` using documented Linux reference flags. No local fullCore repetition for recording-only change. These prove format/refusal/type checks, not Windows birth observations.
- [ ] **5. Commit and review**, `test: bind business acceptance to process creation identity`. Root normal featureFF CI then validates exact current-source native host/both frozen business variants:12 ordered unique PID+birth tuples,27+24checks/all0, actual exits/leases and unchanged functional gates before uploads. WindowsServer is not Windows11; no download proof if quota remains. Native driverTask3 may be designed while CI runs but cannot use an unmet identity gate as acceptance success.

### Task 3: Real native driver and audited0.2 package

**Files:** Create `native/Workbench.OwnedLab/{Main,Broker,Target,TargetProcess,Automation,Capture,Protocol}.cpp`/matching headers/BuildInfo.rc; `plugins/Workbench.Testing.Windows/{Workbench.Testing.Windows.csproj,Win32OwnedDriverFactory.cs,OwnedProcessSession.cs,OwnedNativeMethods.cs,OwnedDriverDescriptor.cs}`; `scripts/{build-owned-lab,owned-lab-probe}.ps1`; `tests/Workbench.OwnedLab.WindowsProbe/{Workbench.OwnedLab.WindowsProbe.csproj,Program.cs,NativeChecks.cs}`; `tests/Workbench.OwnedLab.NativeFixtures/{HangBroker,CrashBroker}.cpp`. Modify `plugins/Workbench.Testing/Workbench.Testing.csproj`, `plugins/Workbench.Testing.Domain/Workbench.Testing.Domain.csproj`, `plugins/Workbench.Testing/plugin.json`, `scripts/{build.ps1,package-testing-plugin.ps1,TestingPluginDelivery.psm1,testing-plugin-probe.ps1,TestingPluginProbe.psm1}`, `.github/workflows/build.yml`, `tests/scripts/{TestingPluginPackage,TestingPluginProbe}.Tests.ps1`, `tests/Workbench.Testing.PackageProbe/Program.cs`: ALL existing version/ZIP/artifact/probe guards move coherently to0.2 in T3.

**Interfaces:** Consumes T1 ports/codec. `Win32OwnedDriverFactory(OwnedScope,TimeProvider):IOwnedDriverFactory`, no path/args input. Windows project TFM=net10.0-windows10.0.19041.0, EnableWindowsTargeting=true; no WinUI/WindowsDesktop/UIA managed dependency. Generated descriptor: Available:bool, ImageSha/NativeSourceSha/SourceCommit/Msvc/Sdk/Profile:string?. Production requires verified values. Compile-time OwnedLabReferenceOnly=true => unavailable/Profile=ReferenceOnly/other values null; Prepare throws UnavailableHelper before queue. Native `RunBroker():int`, `RunTarget(std::wstring_view):int`; fixture replaces RunBroker at link time, no production flags. `build-owned-lab.ps1 -Configuration Release -OutputDirectory <owned dir>` discovers actual MSVC/SDK, uses cl/rc/link C++20 /MT x64. `owned-lab-probe.ps1 -DeliveryDirectory <audited delivery>` stages verified payload beside probe; bounded report binds actual PIDs/FILETIMEs/exits/job/IO/lease/frame/helper/ZIP.

- [ ] **1. Write failing Windows/package checks:** HelperIdentity rejects missing/fake PE/SHA/reparse before process/queue. PositiveAndNegativeFrames proves4/4/3 + raw state/actual identities/BMP. NativeFailureMatrix: real UIA/GDI-block/hang/crash/early-exit/cancel/nested-job/DPI fixtures; NativeBadFrame/ChangedIdentity fail closed.

```csharp
Assert.Equal(172854, frame.Length);
Assert.NotEqual(0u, identity.TargetPid);
Assert.Equal(0u, retirement.ActiveProcesses);
Assert.True(brokerExited && reportedTargetExited && allIoJoined && leaseAvailableAfterProbeExit);
```

- [ ] **2. Record the native RED boundary:** write checks before native implementation. On Windows run `./scripts/owned-lab-probe.ps1 -DeliveryDirectory ./artifacts/native/testing-plugin`; missing native behavior must fail. In the selected Linux-only environment, record this command as unavailable, not skipped/passed or a verified RED. Execute the actual current-source Windows CI gate as soon as the implementation can be compiled; any failure is retained and corrected before Task4. Portable RED tests cannot substitute for this gate.
- [ ] **3. Implement C++ broker/target/UIA/capture**, exact spec. Controls y<144, calibration y152..164 unobstructed; DPI keeps320x180 physical client. No generic selector/expected assertion input.
- [ ] **4. Implement managed ownership:** lease/hash before Queued, suspended creation JOB_LIST/HANDLE_LIST. Broker stdin=RX/stdout=TX/stderr=fixed NUL write; exactly3 inherited handles. Target stdout=private Ready/stdin=NUL read/stderr=NUL write, only these handles; --target/session only. No handle/path argument/free-form protocol stdout. OwnedGrant.LaunchGate.TryResume arbitrates actual ResumeThread with synchronous seal; never a token-check-only substitute. Return typed OwnedLaunchException only after actual pre-session retirement. Kill owned job/CancelIoEx/join actual work before release.
- [ ] **5. Implement build/package:** helper first -> generated expected hash Windows DLL ->0.2 entry/domain/exact8-file ZIP/provenance. AssemblyVersion1.0.0.0/host0.4/SDK1 remain. Reference compilation unavailable, no production ZIP even -ReferenceOnly; missing helper always fails production packaging. Source manifest includes plugins/Workbench.Testing.Windows, native/Workbench.OwnedLab and new build/probe scripts. T3 workflow updates existing ZIP/artifact/probe names and runs standalone owned-lab proof before uploads; existing virtual/native/business probes must remain green now, not deferred to T4.
- [ ] **6. Verify WINDOWS:** `./scripts/build.ps1`; `./scripts/owned-lab-probe.ps1 -DeliveryDirectory ./artifacts/native/testing-plugin`; `./tests/scripts/TestingPluginPackage.Tests.ps1 -Directory ./artifacts/native/testing-plugin -RequireCurrentSource`; `dotnet run --project tests/Workbench.Testing.PackageProbe -c Release -- artifacts/native/testing-plugin/workbench-testing-plugin-0.2.0.zip`. Run `./scripts/testing-plugin-probe.ps1 -TimeoutSeconds 240` and existing business/native/SDK gates from build.yml at T3, with ALL new0.2 identities; add standalone owned-lab step before uploads. Pin reference compilation/missing-helper failures. Standalone proves no new host UI. Emit bounded ordinary text/JSON observations (actual identities/exits/job0/IO+callback joins/released lease/helper/ZIP/input/execution/frame lengths+hashes/calibration); retain full files. No binary/base64 tunneling and no claim console replaces downloaded audits.
- [ ] **7. Review/commit T3 paths only**, `feat: ship owned Win32 pilot driver and audited package`; native failure stops progression, root gate.

### Task 4: WinUI/history, compiled source fixture and maintenance proof

**Files:** Create `plugins/Workbench.Testing/{TestingOwnedPilotPanel,TestingSourceServices}.cs`; modify TestingPlugin.cs/TestingPage.cs/Workbench.Testing.csproj. Create `tests/Workbench.Testing.SourceAcceptance/{Workbench.Testing.SourceAcceptance.csproj,SourceAcceptancePlugin.cs,FixtureHandlers.cs,plugin.json}`, `scripts/{package-testing-source-acceptance,owned-pilot-native-probe}.ps1`, `src/Workbench.App/{OwnedPilotNativeProbe,TestingSourceAcceptanceProbe,TestingMaintenanceProbe}.cs`, `tests/scripts/OwnedPilotProbe.Tests.ps1`. Modify `src/Workbench.App/{RunOptions,MainWindow}.cs`, `scripts/{testing-plugin-probe.ps1,TestingPluginProbe.psm1}`, `.github/workflows/build.yml` ONLY for added UI/source/maintenance phases (T3 already owns coherent0.2 identities), `docs/{testing-plugin,windows-verification}.md`.

**Interfaces:** `TestingOwnedPilotPanel(TestingPlugin)` consumes T1 controller/snapshots and spec AutomationIds. Plugin internal Pilot:OwnedPilotController; hold Pilot.SealAdmission() alongside NativeActionOwner.Seal BEFORE cancellation; request pilot cancellation BEFORE awaiting owner.DrainAsync; retain both through owner drain, controller preparation, notifications and final UI drain, then reopen. Controller preparation returning never releases the external seal. Stop permanent/unload UI-only. `TestingSourceServices(FigmaFileReader Figma,AiTestCaseGenerator Ai)` internal record; public TestingPlugin() production readers, internal TestingPlugin(TestingSourceServices) existing controller ctor; friend only Workbench.Testing.SourceAcceptance. Fixture delegates SAME production instance, ID com.projectworkbench.testing.source-acceptance; production payload + fixture DLL/deps/replaced manifest, distinct proof. Host probes `PrepareAsync(RunOptions):Task`, `RunAsync(MainWindow,RunOptions):Task`, public Core/SDK/controls only. Extend isolated --testing-probe phases owned-seed/owned-verify/source-acceptance/maintenance; preserve seed/verify. Flags never swap production handlers or enable faults.

- [ ] **1. Write failing native/report checks:** IDs/copy/stored evidence/retry; NativeLeaveWaits holds actual retirement/callback. Source fixture synthetic DPAPI:0 init/edit/navigation requests, explicit Figma/selected-only AI, exact traces/manual actions/no network/secret logs. Maintenance rejects wrong phase/package/source.

```csharp
Assert.False(projectLeave.IsCompleted);
Assert.Equal(0, requestsBeforeExplicitActions);
Assert.All(generatedSteps, s => Assert.Null(s.Action));
Assert.Equal(selectedSourceIds, observedSourceIds);
```

- [ ] **2. Run red:** `./tests/scripts/OwnedPilotProbe.Tests.ps1`; host probe rejects missing controls/fixture proof, never skip.
- [ ] **3. Implement UI/lifecycle/fixture.** Capture scenario before await, no source flush/mapping; all dispatcher/callback tails retained. RecoveryNeeded copy: “Storage root is read-only; Pilot A cannot repair or retry it.” Retry admits verified Passed/Failed/Cancelled or clean Interrupted only; live Queued/Running and every RecoveryNeeded/read-only root refuse. Fixture uses in-memory handlers + actual host DPAPI; production default unchanged.
- [ ] **4. Implement Windows production/maintenance probes:** exact0.2 install/native controls/cancel/history/isolation; kill real hostA after committed StepStarted, hostB projects Interrupted without launch, explicit fresh retry. Record actual process/job/IO/lease identities; fault fixture proof separate. HistoricalRebuild0.1 from `8e022a3d93588edead537936051ef101cc2806c5` ->0.2 -> rollback/restart preserves Core/owned bytes; old plugin ignores private history, no mapping migration.
- [ ] **5. Integrate CI before uploads:** preserve current native/runtime/workspace/testing/SDK and frozen business51 checks/12 distinct host PIDs; add compiled fixture/standalone/installed/maintenance audits. Quota cannot skip proof or imply delivery. Docs report actual evidence only.
- [ ] **6. Verify WINDOWS:** `dotnet test ProjectWorkbench.Core.slnx -c Release --nologo`; `./tests/scripts/OwnedPilotProbe.Tests.ps1`; `./scripts/build.ps1`; `./scripts/package-testing-source-acceptance.ps1`; `./scripts/owned-pilot-native-probe.ps1`; run unchanged current testing/business/SDK delivery gates from build.yml, finally Test-TestingPluginDelivery -RequireCurrentSource. All reports pass; actual download/eleven audits remain separate blocked gates if unavailable.
- [ ] **7. Review/commit T4 paths only**, `feat: expose owned pilot with native acceptance and maintenance proof`; root whole-phase review, no merge/release/deploy/Pilot B claim.

## Self-review/handoff

Check spec coverage, step granularity, type consistency, all5 Review Focus tests and proportion. Gates isolate portable policy, accurate business process identity, actual Windows viability, installed UI/maintenance. If constrained, stop at a completed gate with accurate evidence; never remove helper/weaken proof/build mapping first. Root reviews these files before committing/dispatching; no further ordinary user confirmation or external action follows.
