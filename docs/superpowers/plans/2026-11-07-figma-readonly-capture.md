# Read-only Figma Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement bounded genuine Figma REST-node capture plus a retained project import operation, without live credentials or fabricated acceptance criteria.

**Architecture:** Portable first-party domain reader + immutable capture + explicit manual-criteria builder; existing Core facade persists actual records. Existing WinUI entry, SDK/host ABI and fixed virtual execution remain unchanged. Controlled-computer execution is specified for a later separate increment, not implemented by placeholder interfaces.

**Tech Stack:** .NET10.0.403/C#, built-in HttpClient/System.Text.Json, existing Workbench.Sdk/Core and xUnit; no new package dependencies.

**Spec:** docs/superpowers/specs/2026-11-07-figma-readonly-capture-design.md

## Global Constraints

- Dedicated feature branch only; preserve f7 source/evidence and open prior delivery gate. This plan owns only .superpowers/sdd/figma-readonly-capture; do not read/write sibling workspaces.
- User delegates ordinary choices; use fresh SolMax implementer/reviewer, one product writer, no helper subagents from workers. Retain all rulings/reasons/costs outside scratch at closure.
- No actual business HTTP/provider/Figma/AI/Webhook/messages/real credentials/user-PC control. Only in-memory HTTP handlers and synthetic token/data; no OpenAI client/key work before pending credential decision.
- No SDK/host/plugin version/assembly/API/schema/capability changes, native UI/secret-setting placeholders, business-repo/payload changes, new package dependencies or live provider registry.
- Use exact component/controller signatures, fixed endpoint/caps/error rules and manual-criteria provenance from the Spec. Default transport no redirects/cookies/default credentials; no retries or external URLs.
- Actual operation retention precedes caller synchronous prefixes; cooperative cancellation is not retirement. Existing lifecycle/draining/root/dirty-draft retention stays load-bearing.
- Full portable suite once at final combined source; focused meaningful tests while iterating. Windows actual/native/download closure remains required but quota-blocked until capacity restored. Server automation is not Windows11 human acceptance.
- No artifact deletion/settings/billing mutation, denied PR retry, merge/deploy/release/publish; authorized feature FF push only after gates.

## Review Focus

- Caller-owned collections mutated after admission or deliberately blocked synchronous enumeration must not change authority or escape lifecycle retention — Task1 capture tests, Task2 gated actual controller test.
- Redirects/hostile errors/header injection or malformed response must never leak token/body or send a second request — Task1 handler/exception tests.
- Chunked/no-length UTF8 payload exactly at/over cap and duplicate/escaped Unicode ambiguity must fail predictably without truncation — Task1 streaming/parser tests.
- HTTP completes after cancellation/close/preparation: keep original work/root retained and never publish into another project/closed authority — Task2 gated real Core fixture.
- User criteria/raw API version/node association and source traces survive publication/readback/generation/CSV without becoming automatable — Task2 actual Core/Domain composition.

---

### Task 1: Bounded Figma reader, immutable capture and source builder

**Files:** Create focused files under plugins/Workbench.Testing.Domain/Sources/Figma/: FigmaReadRequest.cs, FigmaReadException.cs, FigmaCapture.cs, FigmaFileReader.cs, FigmaResponseParser.cs, FigmaSourceBuilder.cs (split helper only when it owns a responsibility). Create tests/Workbench.Core.Tests/FigmaCaptureTests.cs. Add concise component contract docs docs/figma-readonly-capture.md. No unrelated source/version/workflow changes.

**Interfaces:** Produces exact FigmaReadRequest/FigmaFileReader.ReadAsync/FigmaCapture/FigmaNodeCapture/FigmaReadException enum+RetryAfterSeconds/FigmaSourceBuilder.CreateDraft as Spec; shared internal request/criteria capture helpers consumed by Task2. Existing main SDK unchanged.

- [ ] **Step 1: Write behavioral RED tests.** In-memory handler records one actual GET to fixedapi host/path/escaped requestedids/depth2/optionalversion and synthetic perrequest header; no default header leakage. Invalid inputs/caps must not call credential or handler. Response rawbytes/nodeorder/version and returned collections/bytes defensively preserved. Test explicit exact manual criteria mapping and missing/extra/invalid criteria refusal;200 totalcriteria succeeds,201 refuses InvalidRequest before token/HTTP. Map builder input limits consistently.
- [ ] **Step 2: Run focused new tests and save actual RED command/log/exit.** Expected new production types missing or concrete behavior failing; do not count collector/build-environment errors as causal RED.
- [ ] **Step 3: Implement transport/parser/builder per Spec.** Add finite cooperative deadline, actual-await retention, safe errors, no redirects/retry/image fetch and bounded UTF8 duplicate-aware JSON. Unknown API fields allowed as inert bounded data; requested identities/version strict. All derived test actions remain null.
- [ ] **Step 4: Add and run meaningful failure tests.** 401/403/404/429, bounded RetryAfter, redirects/requestcount1, transport/credential/errorbody privacy, malformed duplicate/surrogate JSON, wrong/missing/null/extra nodes/version, overlimit length/chunkedstream and ignored-cancel reader task. Use deterministic gates; no arbitrary sleep/deadline stress.
- [ ] **Step 5: Verify/commit/report.** One focused filter FigmaCaptureTests, git diff --check, sourceonly explicit git add/commit. Save commands/rawlogs/TRX/executed-bin manifest/limitations; no suite/native repeats or push. Full concise task-1-report.md <=8KB; releasewriter with commit/status. Root independently hashes/reads and dispatches fresh task review using recorded BASE..HEAD package.

### Task 2: Retained controller import and real project composition

**Files:** Create plugins/Workbench.Testing.Domain/TestingController.Figma.cs; modify TestingController.cs constructor storage only; create tests/Workbench.Core.Tests/FigmaControllerTests.cs; minimally extend TestingDomainFixture only if required for injected constructor/test composition; update docs/figma-readonly-capture.md and README.md with accurate backend-only/manual-criteria/live-unverified status. Do not change SDK, native UI/entry, fixed run target/evidence, source allowlist definitions or workflows.

**Interfaces:** Consumes Task1 exact component/internal capture helpers. Preserve TestingController(context,adapter=null). Add TestingController(context,adapter,FigmaFileReader reader) and exact ImportFigmaAsync(request,acceptanceCriteria,getToken,CT) -> Task<TestSourceSnapshot>. Uses existing AdmitCaptured and captured project service, as Spec; remote errors sanitize to existing codes only.

- [ ] **Step 1: RED actual lifecycle/composition.** Use real Core registration/facade/supervisor in existing fixture style with injected in-memory reader, synthetic token callback and raw Figma JSON. Test published raw-byte/hash/version/manual-criteria exact readback, generated cases remain manual and CSV traces correct. A distinct project remains empty; no global settings/credential writes.
- [ ] **Step 2: Run new focused tests to establish RED.** Actual missing behavior, preserved raw log/exit; no infrastructure error counted as RED.
- [ ] **Step 3: Implement one admitted operation.** Capture input before callbacks/await but after ownership admission; link caller/context-lifetime cancellation, await actual reader, create explicit-criteria draft, admit publication atomically with _sync close (ticket first may settle after Stop, Stop first refuses) and call facade outside lock. Update local source summary only from acknowledged returned snapshot, no postcommit ListSources IO. Preserve actual bytes/acknowledgment across later cancellation; never delete/retry uncertain commits. Wrap sync capture AND async Figma errors into existing safe SDK codes. Preserve constructor ABI. No callbacks evaluated on closed admission; no new secret registration, UI or default network action.
- [ ] **Step 4: Lifecycle/privacy GREEN.** Gate actual handler/credential callback that ignores cancellation; PrepareLeave/Stop remain pending until true release; fixture proves real facade/controller ownership, not a HostRuntime root lease. Test standalone Stop with still-open context before publication admission (no write) versus actual already-admitted blocked store publication (drains/maycommit). After closure no input enumeration/token/HTTP/publication. Capture/HTTP/parse/criteria/IO failures preserve old source bytes and dirty draft;4MiB envelope rejection is safe, no transaction/absence promise; acknowledged write is not relabeled failure by a later cancellation/list refresh, safe current codes only, no provider-text leak. Preserve existing lifecycle finite-budget semantics without raising timeouts.
- [ ] **Step 5: Verify/commit/report.** Focused FigmaControllerTests plus affected existing controller/lifecycle tests once; one final full dotnet test ProjectWorkbench.Core.slnx -c Release --nologo with TRX after combined code. No NativeReference repeat for portable-only change. git diff --check, commit explicit source/docs/tests; report task-2-report.md <=8KB with raw paths/full exits/counts/concerns. Do not push. Root owns task review and one broad final new-plan review, then authorized feature FF/actual CI if useful; quota is not solved by re-running uploads.

## Controller self-review

Spec coverage: Task1 implements actual read/capture/manual-criteria components; Task2 actual project owner/lifecycle path. Computer execution is explicitly design-only in Spec, OpenAI credential-gated and native Figma UX/live verification deferred. Exact signatures/caps/error mapping agree across tasks; source-level changes keep existing SDK and independent plugin package layout. Five Review Focus conditions each have concrete tests above. No implementations are prescribed line-for-line; builders choose focused private helper structure.
