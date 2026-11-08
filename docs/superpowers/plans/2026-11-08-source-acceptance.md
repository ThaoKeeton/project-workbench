# SourceAcceptance Local Source Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an independent synthetic source acceptance entry and prove actual production client/controller/store composition locally.

**Architecture:** Production source-service factory plus the same WinUI entry; a separate fixture assembly supplies two terminal in-memory handlers, bounded ledger/SDK commands and retained teardown. Portable tests compile-link the exact fixture code and real production ownership helpers. Packaging and installed Windows proof remain later separate tasks.

**Tech Stack:** .NET10/C#, WinUI3 reference compiler, xUnit, PowerShell, actual Core JSON/secret/task services.

**Spec:** `docs/superpowers/specs/2026-11-08-source-acceptance-design.md`.

## Global Constraints

- User renewed public standard-runner GitHub Actions authorization. Implementer develops/tests locally; controller coordinates reviewed-source public Actions batches with short report retention and controlled artifact count. No private/paid runners, billing changes or purchases, unrelated billable services or automatic rerun loops.
- Host minimum0.4.0/API1/Testing0.2.0/assembly1.0.0.0 unchanged; production eight-file payload membership and helper validation unchanged. Fixture ID `com.projectworkbench.testing.source-acceptance`, version0.2.0/file0.2.0.0, distinct entry/deps/manifest, never bundled in ordinary app/Plugins.
- SAME production TestingPlugin/page/controller/lifecycle; only internal service composition/friend seam. No copied UI/Domain, generic computer API, source/AI-to-action mapping, production runtime flags, new SDK/Core public contract or native/runner changes.
- Terminal in-memory HTTP only, fixed synthetic credentials/endpoints/replies; no socket/fallback/retry/redirect, raw key/header/body/path/exception logging, real providers/credentials/Webhook/DingTalk messages/userPC.
- Controls and transport are instance-local, bounded and fail closed; no delegate under a ledger/ownership lock. Cap32 accepted scenarios, checked Int64 counters, state schema1≤8192UTF8bytes, request body≤65536bytes. HTTP entry/settlement counters are not provider/store acknowledgments.
- Existing source/owned admission, cancellation/cleanup/UI tails and first-failure semantics stay intact. Fixture teardown joins actual inner Stop before disposing both caller-owned handlers. No abandoned waits, implicit cancellation/disposal/timer gate release, automatic replay or false success.
- Current native27/old9of27abortUNKNOWN/five-round cap, installedWindows/business51+12birth/SDK/production delivery/crash/maintenance/actualDPAPI/rootlease/Windows11human remain OPEN/deferred. Managed ReferenceOnly is unavailable and creates no production helper/ZIP.
- Preserve all prior source/.sdd/reports/failed outputs/data/business repos/old artifacts; no deletion,403 bypass, denied PR retry, merge/release/deploy. Trusted in-process fixture/plugin code is not a sandbox or hot-unload feature.

## Review Focus

- Passive actions and credential namespace: initialization/state/arm/edit/refresh cannot read secrets or enter HTTP, and a different project/plugin cannot supply fixture credentials (T1 PassiveAndIsolation).
- Actual receipt/trace/manual-case bytes: response order2/0/1 must map to original selected criteria, exclude the unselected decoy, acknowledge real receipt, and keep Action=null (T1 HappyTraceAndReopen).
- Partial publication and malformed transport: committed receipt-only state survives case-bound failure without fake case success/retry or secret diagnostics (T1 FailureAndPartial).
- Held IO/cancellation/cleanup/disposal: actual lifetime Stop must remain pending, handlers undisposed and admission closed until the same request is explicitly released (T1 OwnershipAndHold).
- Wrong identity/closed/over-limit controls and stale release: fixed commands refuse after context/fixture closure, do not mutate another generation, and never mistake counters for current store state (T1 ProtocolAndLimits).

---

### Task 1: Production source seam and independent synthetic fixture

**Files:** Create `plugins/Workbench.Testing/TestingSourceServices.cs`; modify `TestingPlugin.cs`, `Workbench.Testing.csproj` (one fixture friend), `plugin.json` (description only). Create `tests/Workbench.Testing.SourceAcceptance/{Workbench.Testing.SourceAcceptance.csproj,plugin.json,SourceAcceptancePlugin.cs,SourceAcceptanceProtocol.cs,SourceAcceptanceScenarioLedger.cs,SourceAcceptanceHttpHandlers.cs,SourceAcceptanceOwnership.cs}`. Create `tests/Workbench.Core.Tests/SourceAcceptanceTests.cs` (split into focused SourceAcceptance*.cs if necessary); modify its csproj for exact pure-source links, and `tests/Workbench.NativeReference/Workbench.NativeReference.csproj` for fixture project reference with ReferenceOutputAssembly=false. Modify `scripts/TestingPluginDelivery.psm1` and `tests/scripts/TestingPluginPackage.Tests.ps1` only for new/changed source/test/fixture/reference provenance and actual negative hash guards. Update `docs/testing-plugin.md`/`docs/windows-verification.md` truthfully, including the deferred gallery M2 artifact reference. Modify `.github/workflows/build.yml` only to add the exact four-property full NativeReference build to its existing public Linux reference step, so the actual new fixture entry compiles on CI. No new job/artifact or packaging/probe/publicSDK/Core/Domain/native/business implementation changes.

**Consumes:** Existing public Domain `TestingController(context,null,FigmaFileReader,AiTestCaseGenerator)`, injected terminal handlers remain caller-owned; real PluginRegistrationContext/ProjectRuntimeScope/settings/secret store/background supervisor/Core testing store; linked production NativeActionOwner/RemoteOperation/activity/lifecycle and the completed owned panel.

**Produces — exact stable ports:**

```csharp
internal sealed record TestingSourceServices(FigmaFileReader Figma, AiTestCaseGenerator Ai)
{
    internal static TestingSourceServices CreateDefault();
    internal TestingController CreateController(IPluginTestingContext context);
}
public TestingPlugin();
internal TestingPlugin(TestingSourceServices sources);

// Portable fixture namespace Workbench.Testing.SourceAcceptance; these are not SDK APIs.
internal enum SourceAcceptanceProvider { None, Figma, Ai }
internal enum SourceAcceptanceScenario { None, Happy, HttpFailure, Malformed, Partial, Hold }
internal sealed class SourceAcceptanceScenarioLedger
{
    internal SourceAcceptanceScenarioLedger(string projectId);
    internal string StateJson();
    internal bool TryArm(SourceAcceptanceProvider provider, SourceAcceptanceScenario scenario);
    internal bool ReleasePending();
    internal void SealCommands();
}
internal static class SourceAcceptanceProtocol
{
    internal const string FixtureId = "com.projectworkbench.testing.source-acceptance";
    internal const string FigmaToken = "source-acceptance-figma-token";
    internal const string OpenAiKey = "source-acceptance-openai-key";
    internal static void Register(IPluginTestingContext context,
        SourceAcceptanceScenarioLedger ledger, Func<bool> canArm);
}
internal sealed class SourceAcceptanceFigmaHandler : HttpMessageHandler;
internal sealed class SourceAcceptanceAiHandler : HttpMessageHandler;
// Both take (SourceAcceptanceScenarioLedger); expose internal bool IsDisposed for actual ownership tests.
internal sealed class SourceAcceptanceOwnership
{
    internal SourceAcceptanceOwnership(SourceAcceptanceScenarioLedger ledger,
        HttpMessageHandler figma, HttpMessageHandler ai, Func<CancellationToken,Task> stopInner);
    internal Task StopAsync(CancellationToken cancellationToken = default);
}
```

Ledger/handler request-ticket internals and test rig details are local routine choices; implement needed held/cancellation/settlement signals without adding public or host dependencies. State JSON fields, command IDs, synthetic inputs/replies and bounds are frozen in the spec. New fixture project uses the existing Windows TFM/x64/WinUI pattern, references production Testing, and emits no extra portable DLL. Its public SDK entry is `Workbench.Testing.SourceAcceptance.SourceAcceptancePlugin`; actual controls remain registered by the delegated production instance. Manifest capabilities are current six production hints plus `source-acceptance-fixture`.

- [ ] **1. Write behavioral tests and compiling minimal ports.** PassiveAndIsolation uses actual distinct fixture context/secret services with synthetic protector, state/arm/refresh/import through production factory, zero secret reads/HTTP before explicit actions, wrong context rejection. HappyTraceAndReopen uses fixed Figma source, real Core IDs/revision/bytes and unselected requirements decoy; AI indices2/0/1, actual receipt input/response bytes/hashes/traces, reopened store and Action=null/ManualStepUnsupported with no automatic supervisor start. ProtocolAndLimits verifies fixed registered SDK commands/schema/caps/actual-entry counters/no raw secrets/stale release and noarm while active/closed. Tests call the actual fixture protocol and handler source, not a separate test fake.
- [ ] **2. Run meaningful RED:** `dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj -c Release --nologo --filter FullyQualifiedName~SourceAcceptance`; preserve compiling behavioral failures and source. Missing APIs/setup alone are not RED.
- [ ] **3. Implement seam/fixture/ownership.** Production defaults use real readers; explicit internal constructor captures services before init awaits and calls the pure factory at the current controller creation point. Preserve current owner/pilot/lifetime ordering. Fixture validates context before initialization, uses real inner entry, registers only closed diagnostic controls after init, and uses portable ownership helper for Stop/partial failure. Control commands are bounded synchronous prefixes independent of production Register and check lifetime/closed state. Explicit release captures the current gate before completing outside locks. Both handlers are terminal, validate fixed synthetic requests and never retain secret/header/body values. Tests and actual fixture share source.
- [ ] **4. Add failure/held ownership tests.** Real malformed/HTTP errors preserve previous bytes and selection; real partial prerequisite-overcap commits receipt but no case set and safe StoreLimitExceeded. Hold both provider SendAsync calls ignoring cancellation; actual remote/activity/lifecycle plus SourceAcceptanceOwnership must join real IO/notifications while Stop/disposal remain pending. Release the same instance in finally, join then verify both handlers disposed; repeated Stop stable, first cleanup failure preserved and second disposal attempted. Exercise the factory's default real virtual adapter through actual supervisor using mapped bundled synthetic cases; manual AI cases remain refused. Do not copy WinUI RemoteAsync/Stop into tests or claim wrapper execution.
- [ ] **5. Run focused GREEN and source guards.** New source fixture plus existing Figma/AI controller/remote/activity/lifecycle/owned admission tests as affected. Audit all fixture/pure seam/test/reference/manifest/build inputs; production source guard includes new directories and exact new records without weakening production eight-file validator. Each new relevant record hash corruption must produce actual refusal. Parse changed PowerShell and both manifests; manifest identity and the production copy correction exact/versions unchanged. Correct docs/windows-verification.md deferred gallery M2: `project-workspace-probes` is now under `native-verification-evidence` at `artifacts/native/workspace-probes/`. Add the exact four-property full NativeReference build command to the existing public Linux reference step, check its exit explicitly, and audit YAML/two-job/seven-artifact/one-day constraints unchanged.
- [ ] **6. Verify final local code once:** `dotnet test ProjectWorkbench.Core.slnx -c Release --nologo` with normal real90second deadline/no60second hang cutoff; full `dotnet build tests/Workbench.NativeReference/Workbench.NativeReference.csproj -c Release --nologo -p:OwnedLabReferenceOnly=true -p:UseWinUI=false -p:AppxGeneratePriEnabled=false -p:WindowsAppSDKSelfContained=false` now compiles actual fixture entry too. Require exit0/reference0warnings0errors, actual named Windows-only skips retained. Save exact argv/environment/raw logs/TRX/source snapshots/actual DLL metadata; no production ZIP. Use existing local caches/tools, no dependency/service purchase or network provider call.
- [ ] **7. Local commit/release:** `feat: add independent synthetic source acceptance fixture`. Complete task-1-report.md and task1-checks with failures/concerns/source provenance/actual producer bytes/clean state/writer release. Root verifies full report/evidence, exact BASE..HEAD U10 packet and fresh independent task spec+quality review. Actual entry/DPAPI/WinUI/package/installed gates remain open. Implementer performs no GitHub writes/Actions/uploads or reactivation; controller handles public CI after task review.

## Later bounded tasks and self-review

Independent package profile must validate exactly ten case-sensitive files: the existing production eight with manifest replaced plus fixture DLL/deps. Require real Production MSVC helper/descriptor/source/host and strict cross-profile rejection; current eight-file validator and its package probe remain unchanged. Linux cannot create a production fixture ZIP from ReferenceOnly outputs. Packaging needs a separate frozen task after Task1 review.

Actual installed source probe later uses public Core/SDK/registered controls, separate option/phase/root/marker and PID+birth/exit identities, no static private Domain/fixture reference or reflection. Command-only release works while enabled/preparing, not after Stop revokes commands; freeze a separately reviewed native release protocol before that proof. Clean production StepStarted crash/maintenance are separate. These gates may require a local Windows environment; Only standard public Actions are authorized; no unrelated fees, private/paid runners or billing changes. Preserve source/identity proof and avoid automatic retry loops.

Self-review: the renewed public-CI requirement needs a narrow full NativeReference compile in the existing Linux step; the older no-CI-change scope is superseded only for this command. No production fixture package or installed proof is added. Deferred gallery M2 is a documentation correction in the existing permitted doc. All five failure classes have Task1 tests, interfaces/IDs/JSON/copy/bounds are fixed, no source fixture claims later packaging/runtime. User opts out ordinary design approvals; choices and source-before-runtime order are coordinator rulings, not user promises. Retain existing dedicated feature checkout and all old ledgers; final whole-owned-phase review remains later, never merge/release this stage.
