# Native source connectors implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose native Figma import and traceable AI test authoring without live API testing or granting automated actions.

**Architecture:** A bounded portable AI client produces an immutable capture. The retained project controller publishes provenance and manual cases; WinUI controls call these services through project-bound secret callbacks.

**Tech Stack:** .NET 10, WinUI 3, System.Net.Http, System.Text.Json, xUnit, existing SDK1/Core testing stores.

**Spec:** `docs/superpowers/specs/2026-11-07-native-source-connectors-design.md`

## Global Constraints

- WinUI native plugin controls in the host window; no embedded independent app or WebView.
- Existing SDK/host/plugin versions and ABI remain unchanged; preserve both old TestingController constructors.
- In-process plugins are trusted code, not a sandbox; no hot-unload claim.
- All transport verification uses synthetic credentials and in-memory handlers. No live API, real Webhook, DingTalk message or user-PC control.
- No business repository, main branch, release, draft PR, credential provisioning, remote deletion or storage-setting mutation.
- No cap increases, silent truncation, automatic network retry or success inferred from an unacknowledged store call.
- Every AI-generated step has Action null and exact original plus acknowledged receipt provenance.
- Retain actual work and UI continuations through cancellation, project leave and Stop; no abandoned timeout task.
- Native inputs survive page unload/reopen within the project session and failures; successful stores are durable. No normal-save checkbox/button.

## Review Focus

- A mutable caller changes source criteria in the credential callback: request and capture must retain the original values (Task 1).
- A successful HTTP body contains refusal/tool output, duplicates, invalid Unicode or invented traces: refuse it before publication (Task 1).
- Receipt commits but case creation fails/cancels or has uncertain IO: retain receipt, report partial state and never auto-retry/delete (Task 2).
- Cancellation arrives after an acknowledged receipt/case write: preserve the authoritative acknowledgment and correct state (Task 2).
- A user navigates away or cancels while credential/HTTP ignores cancellation: the actual task stays owned and inputs remain recoverable (Task 3).

### Task 1: Bounded real AI client with synthetic verification

**Files:** Create focused files under `plugins/Workbench.Testing.Domain/Generation/Ai/`; create `tests/Workbench.Core.Tests/AiGenerationClientTests.cs` and test helpers as needed. Add `docs/ai-test-authoring.md` client/API/limits section.

**Interfaces:** Produce `AiTestCaseGenerator(HttpMessageHandler? handler = null)`, `Task<AiGenerationCapture> GenerateAsync(IReadOnlyList<TestSourceSnapshot> sources, Func<CancellationToken,Task<string?>> getKey, CancellationToken cancellationToken = default)` and immutable capture/case/step types in `Workbench.Testing.Domain.Generation.Ai`. Capture exposes exact input/response bytes, SHA-256, Model, ResponseId, original traces and generated values. Controller/publication/native UI remain untouched.

- [ ] Write focused failing tests for exact fixed endpoint/model/store/schema/request and bounds in the spec; capture mutation before key evaluation, all-criteria coverage and all output limits. Invalid caller inputs must not evaluate credentials or HTTP.
- [ ] Run focused RED and save output. Implement the strict bounded client/parser/capture/error enum in focused files, following the spec's exact values. Credential, transport and stream tasks are actually awaited under a cooperative 90-second deadline.
- [ ] Add meaningful tests for HTTP status mappings, absent/invalid key, unexpected callback/handler/stream errors with private text, 429 Retry-After, content-type/encoding/length/stream overflow, strict UTF-8/Unicode/duplicate JSON, incomplete/refusal/tool output, missing coverage, immutable returned collections/bytes and ignored cancellation at each async boundary.
- [ ] Run focused GREEN plus the existing Figma reader regression filter, write bounded report with actual logs/results, self-review and commit. Do not run the full suite on each task; the combined final suite is the aggregate gate.

### Task 2: Retained project publication and AI provenance

**Files:** Add `TestingController.Ai.cs` and AI receipt/publication types under the Domain project; modify only constructor wiring in `TestingController.cs`; extend `TestingDomainFixture.cs` with optional injected AI client; add `AiGenerationControllerTests.cs`; extend AI documentation.

**Interfaces:** Consume Task 1's client/capture. Preserve old constructors and produce four-argument `TestingController(IPluginTestingContext, IControlledTestAdapter?, FigmaFileReader, AiTestCaseGenerator)`, `Task<AiGenerationPublication> GenerateWithAiAsync(IReadOnlyList<string> sourceIds, Func<CancellationToken,Task<string?>> getKey, CancellationToken cancellationToken=default)` and publication State Created/Cancelled/Closed/Failed with Receipt, nullable CaseSet and safe nullable ErrorCode.

- [ ] Write RED using the real Core testing facade and injected in-memory HTTP. Verify original/receipt traces, raw/input hashes, manual steps, CSV and two-project isolation.
- [ ] Implement receipt builder and retained controller flow exactly as the spec: selected draft flush; captured IDs; actual read/client work; separate lock admission tickets; acknowledged summaries; receipt-first/case-second; explicit partial results; no reread after acknowledgment or automatic cleanup/retry.
- [ ] Test closed admission does not enumerate inputs, held credential/HTTP/body work blocks preparation/Stop until actual settlement, both sides of each publication barrier, failure before receipt, partial failures/cancel/closure after receipt, late cancellation after case acknowledgment, Core cap/IO failures, and uncertain commits retaining bytes with explicit refresh recovery.
- [ ] Run focused AI/controller/Figma regressions, retain RED/GREEN evidence, self-review and commit. Preserve existing native entry and SDK facade behavior.

### Task 3: Native Figma and AI authoring entry

**Files:** Modify `TestingPlugin.cs`, `TestingPage.cs` and rendering only where necessary. Add focused `TestingSourcePanel.cs` and portable `NativeSourceDraft.cs` / `NativeRemoteOperation.cs` if needed; link portable helpers into Core tests. Modify testing manifest/delivery/readme text and focused packaging tests where exact copy is asserted. No workflow or host probe edits in this task.

**Interfaces:** Consume both controller source operations. Register `figma-token` and `openai-api-key` secret definitions during initialization; read them only through captured project secret callbacks on explicit native actions. Existing local command IDs remain unchanged and no remote command is added.

- [ ] Write meaningful RED for session-local native source draft capture/reopen/error retention, per-node criteria mapping and captured input independence, remote cancellation ownership and completion/partial status. Use portable actual tasks rather than mirroring getters.
- [ ] Implement WinUI file-key/version and per-node controls, add/remove rows, explicit Figma import and selected-only AI send actions with the spec's data/cost/manual-review disclosure; no background network. Use synchronous owned control capture and current project secret store. Reject unsupported secret storage without calling a client.
- [ ] Retain request CTS and actual operation until settlement; cancel action requests cancellation, busy remains through actual ignored-cancellation work. Preserve current session inputs across page unload/reopen/failure. Make partial receipt result and errors readable without success overwrite. Keep deterministic local generation intact, update remote/SIMULATED capability copy accurately.
- [ ] Run focused portable/native helper and packaging tests; run full portable suite once for final combined source; run effective reference compile with `-p:UseWinUI=false -p:AppxGeneratePriEnabled=false -p:WindowsAppSDKSelfContained=false`; parse touched PowerShell. Retain logs/TRX/build evidence and commit.
- [ ] Controller then runs independent aggregate review, current-source Windows CI (execution evidence even if artifact quota fails), capacity report and next controlled-automation design. Do not describe new native remote controls or live providers as exercised unless separate evidence exists.
