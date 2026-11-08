# Native Runtime Follow-up Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make cancellation status tests deterministic and measure the actual UPCAM capture boundary before repairing it, then verify the shipping host and plugins publicly.

**Architecture:** Inject system-default observation time internally without changing actual task ownership. Add a fixed failure-only focus discriminator to existing isolated capture checks; defer a behavior repair until actual Windows evidence identifies the predicate.

**Tech Stack:** .NET 10, xUnit 2, Microsoft.Extensions.TimeProvider.Testing 10.0.0, WinUI 3, existing PowerShell and PUBLIC Windows/Linux Actions.

**Spec:** `docs/superpowers/specs/2026-11-08-native-runtime-followup-design.md`

## Global Constraints

- Keep Windows-native WinUI pages in the same host window; do not embed an independent application window or alter a business repository/DLL.
- Keep SDK 0.4/API 1, Testing 0.2, the production eight-file plugin profile, pinned DingTalk 0.3.2 and UPCAM 0.1 compatibility.
- Preparation keeps a positive finite `StopTimeout`; default production time is `TimeProvider.System`. Do not change the SDK or public constructor signature, actual hook cancellation, participant identity, acknowledgement, Stop, or retained-work ownership.
- Preserve the exact `Canceled` versus `Pending` assertions. Keep the existing 100 ms test configuration; do not accept either status, increase it, sleep to arrange the race, or detach work.
- Preserve gallery receipt schema/reason vocabulary/roster, strict page/runtime/generation/HWND/PID/birth/desktop/focus checks, original limits 32 attempts/4096 dimensions/64 MiB BGRA/16 MiB PNG, same-owner restoration, immutable CreateNew output, and no capture-failure await after PNG publication.
- Diagnostic output is isolated synthetic `business-seed/upcam-preview` only, blocked/focus-detached only, fixed sibling `upcam-preview.focus-diagnostic.json` outside the gallery roster directory, schema 1, at most 8192 UTF-8 bytes and JSON depth 8. CreateNew, best effort, no raw exceptions; it cannot change the primary probe or receipt result.
- Diagnostic content uses fixed enums, booleans, bounded numeric counts/depths, and already validated source/process identity. Never read/export Text, Name, Header, Password, AutomationId, provider/profile values, arbitrary paths, or dynamic type names. No extra successful-path tree walk, layout, event subscription, dispatcher task, await, or retry.
- Preserve source, prior failed evidence, existing artifacts, and business repositories. No real credentials/providers/Figma/Webhook/DingTalk messages or user-PC control. No billing/purchase, artifact deletion, denied-PR retry, merge, deploy, or release.
- Use the existing PUBLIC standard two-job workflow with one-day artifact retention. Verify public visibility and exact source before one dispatch per reviewed source; prevent automatic duplicate runs. Windows Server CI and reference compilation do not constitute Windows 11 human acceptance.

One product writer at a time. Root controls review, commits' publication, and Actions; workers never access GitHub or delegate.

## Review Focus

1. A slow queued hook/settings write must not let real time invalidate a cancellation-first test premise; Task 1 freezes only observation time.
2. Cancellation after timeout must leave the original Pending result unchanged; Task 1 explicitly tests this order.
3. Either caller result must retain exactly one hook/context and delay Stop retirement until release; Task 1 asserts actual held work and eventual join.
4. Revalidation/restoration can move the tree after a failure; Task 2 latches the actual failing predicate before cleanup and cannot overwrite it with later observations.
5. A diagnostic collection/write failure or above-root scroller must not alter capture acceptance or leak UI/provider text; Task 2 executes bounds/failure-only serialization tests and preserves the original decision.

---

### Task 1: Deterministic preparation observation ordering

**Files:**
- Modify: `src/Workbench.Core/PluginManager.cs`
- Modify: `src/Workbench.Core/PluginManager.Projects.cs`
- Modify: `tests/Workbench.Core.Tests/PluginProjectTests.cs`
- Modify: `tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj`
- Modify: `src/Workbench.Core/README.md` only if needed to explain the existing observer race contract.

**Interfaces:**
- Consumes: the existing internal `PluginManager(options, secretProtector, runtimeState)` and actual `PrepareProjectLeaveAsync` retained task.
- Produces: an optional internal `TimeProvider? projectPreparationTimeProvider = null` constructor dependency, stored privately and defaulted to `TimeProvider.System`; preparation uses `WaitAsync(_options.StopTimeout, provider, cancellationToken)`.
- Public constructors/options and SDK are unchanged. No new fixture mode or timer implementation is required: the existing marker can be awaited while fake time stays frozen.

- [ ] **Step 1:** Preserve the current historical Windows TRX/audit. Write the cancellation-first test using a `FakeTimeProvider` and exact existing assertions; add timeout-first coverage requiring `Pending` even after later cancellation. Name the production mutation each test would catch. Run the focused new tests before wiring the clock; retain the expected compile/behavior failure without claiming it proves the old runner's event ordering.
- [ ] **Step 2:** Add only the internal observation provider and test-only package reference `Microsoft.Extensions.TimeProvider.Testing` version `10.0.0`. Keep actual Stop/system time and hook token handling untouched.
- [ ] **Step 3:** Freeze fake time at entry; await the actual held marker under the existing finite wall-clock test budget. In cancellation-first, cancel then require `ProjectPreparationCanceled`, pending work, and stability after advancing 100 ms. In timeout-first, advance 100 ms, await `ProjectPreparationPending`, cancel later, and require the exact original result remains Pending. In both cases retry the same preparation with a separately advanced observation deadline, require one invocation, exercise refused terminal Stop while held, release in failure-safe cleanup, and verify actual terminal joining/lifetime usability.
- [ ] **Step 4:** Run the two focused order tests, then `FullyQualifiedName~Workbench.Core.Tests.PluginProjectTests`, then the complete `ProjectWorkbench.Core.slnx` once. Store logs/TRX/source and producing DLL hashes under this plan workspace. No increasing timeouts or accept-either assertions. Report all failures by name.
- [ ] **Step 5:** Self-review, commit only Task 1 files, write task report, release writer. A fresh reviewer checks spec and quality from frozen diff/evidence without rerunning the suite.

### Task 2: Failure-only UPCAM focus boundary discriminator

**Files:**
- Modify: `src/Workbench.App/NativeGalleryCapture.cs`
- Modify: `src/Workbench.App/NativeGalleryViewport.cs`
- Create: `src/Workbench.App/NativeGalleryFocusDiagnostic.cs` (data-only bounded serializer/writer; link into Core.Tests if meaningful tests need it).
- Create: a focused `tests/Workbench.Core.Tests/NativeGalleryFocusDiagnosticTests.cs` and modify its project link only for executable serialization/side-effect checks.
- Modify: `docs/windows-verification.md` to state diagnostic scope and actual evidence limits.

**Interfaces:**
- Consumes: unchanged strict `GalleryFocusScroller`, `FocusAttached`, capture phase, validated receipt source/PID/birth.
- Produces: schema-1 fixed sibling `upcam-preview.focus-diagnostic.json` only for the existing failed isolated attempt. Existing PNG and receipt consumers do not consume this diagnostic and are unchanged.
- Failed checks use a fixed enum for required-null, unloaded/root mismatch, hidden ancestor, root-page/owner missing, owner invalid, root-not-reached, and owner-changed. Checkpoint values identify initial owner versus position callback/post-position/render/codec/restore/final publication without dynamic strings. Nullable flags mean not evaluated. Cap numeric depths/counts at 255 with -1 for unseen; record no UI content/IDs/type names.
- Latch first and terminal primary focus failure; preserve the terminal observation before finally restoration. Failure-only supplemental traversal above exact root is bounded at 16 nodes and can record nearest framework owner depth/known kind/termination, never provide an accepted owner or change an existing check. Mark supplemental data unavailable/truncated explicitly.

- [ ] **Step 1:** Read the retained real blocked receipt and both recon reports. Specify fixed DTO keys and failure-only admission tests before implementation; test safe scalar serialization, size/depth limits, CreateNew refusal/no overwrite, nonmatching/success suppression and swallowed write failure. No source-string assertions and no Linux WinUI-runtime claims.
- [ ] **Step 2:** Instrument existing predicates without changing their evaluation order, owner selection, return values or layout/event behavior. Add the bounded serializer/writer. Preserve no new successful-path traversal and no await after final PNG publication. Failures in diagnostics cannot alter the primary receipt/probe.
- [ ] **Step 3:** Run only the new diagnostic tests and any changed helper's meaningful existing tests. Run full NativeReference with `OwnedLabReferenceOnly=true`, `UseWinUI=false`, `AppxGeneratePriEnabled=false`, `WindowsAppSDKSelfContained=false`. Do not rerun an unchanged complete Core graph simply to repeat the Task 1 evidence.
- [ ] **Step 4:** Self-review, commit only Task 2 files, freeze report/evidence and release writer. Fresh review spec and quality, including control-flow comparison and safe output, without duplicate suite runs.

### Task 3: Actual public Windows discriminator and shipping verification

**Files:** No product code mutation admitted by this task. Root records evidence in this plan workspace and may record verified run/download/image facts in `docs/windows-verification.md` and `docs/install-business-plugins.md` after actual audits. Documentation-only outcome recording requires no new runtime build; final review includes it.

**Interfaces:** Consumes reviewed Tasks 1/2 exact HEAD; produces verified run/artifact identity, per-test counts and actual sidecar interpretation.

- [ ] **Step 1:** Verify clean exact HEAD/remote fast-forward, PUBLIC repository, disabled workflow and no active runs. Publish while paused, enable, dispatch one standard workflow at exact branch HEAD, verify unique workflow_dispatch/source/attempt, disable after admission to prevent automatic duplication.
- [ ] **Step 2:** Inspect complete job results and original TRX; download immutable artifacts using authorized tools, verify API SHA-256 and bounded safe extraction. Preserve failed outputs.
- [ ] **Step 3:** Bind UPCAM diagnostic to the same source/PID/birth/original receipt and installed vendor package. Check exact failed branch, original blocked result, no rejected PNG or extra gallery file, and original business result integrity. If the failure is still unmeasured, report this actual gap and add a new bounded discriminator task; no guessed behavior repair. If measured, append a concrete, scoped repair task to this plan before code, carrying previous caps and untouched constraints.
- [ ] **Step 4:** After the measured repair and fresh review, run one full PUBLIC native/runtime/workspace/Testing/business/owned verification for that source. Audit downloaded shipping app/SDK/plugin bytes and each current gallery image; personally view intended deliverables. Report native matrices, actual process/stop/restart outcomes and exact missing scopes. Never call Server acceptance Windows 11 user acceptance.
- [ ] **Step 5:** Final whole-branch review and accurate handoff. No merge/deploy/release/denied PR retry; keep all evidence and scratch work per user requirement.

### Task 4: Measured fixed-layout viewport support

Task3 steps1–3 supplied the actual b6b006e discriminator; its full shipping/24-picture goal stays open until this task is reviewed and Task3 step4 runs the new source. This is not another guess about old ancestry and does not reset prior fix caps.

**Files:**
- Modify `src/Workbench.App/NativeGalleryCapture.cs`
- Modify `src/Workbench.App/NativeGalleryViewport.cs`
- Modify `src/Workbench.App/NativeGalleryPolicy.cs`
- Modify `tests/Workbench.Core.Tests/NativeGalleryTests.cs`
- Modify `docs/windows-verification.md`

**Interfaces:**
- Rename the private resolver to `GalleryFocusViewportFor(FrameworkElement root, FrameworkElement page, FrameworkElement focus, GalleryFocusTrace? trace = null) : FrameworkElement?`; return the nearest existing valid ScrollViewer or exact root for a proved fixed chain, null for invalid attachment. Adapt the private observed-result helper to FrameworkElement. No SDK/public contract change.
- Keep a distinct `FrameworkElement?` captured anchor and optional ScrollViewer for movement; compare exact anchor identity on revalidation. Fixed anchor is exactly `_root` and never a farther framework scroller.
- Add portable `internal readonly record struct NativeGalleryBounds(double X, double Y, double Width, double Height)` and `NativeGalleryPolicy.TryIntersect(NativeGalleryBounds first, NativeGalleryBounds second, out NativeGalleryBounds intersection) : bool`. Reject nonfinite/empty/overflowed edges, return default on failure; positive strict intersection only. It is a real geometry policy used by fixed UI measurement, not test-only logic.
- UI-only `GalleryFixedFocusInViewport(FrameworkElement root, FrameworkElement page, FrameworkElement focus) : bool` measures actual root/page/focus and rectangular clips in root coordinates, using the portable intersection function. Stop at exact root and require exact page/root again; no parent above root is read for eligibility. Failed/unsupported transforms fail closed through the existing capture error/block path.

- [ ] **Step 1:** Read `ci-42514189444/upcam-focus-boundary-audit.json`, the actual sidecar/original receipt and SDK WinUiView contract. Preserve actual Windows RED23/24 and its identities. Write pure intersection tests first: positive and partially visible overlap, disjoint rectangles and each edge, touching-only/zero/negative sizes, NaN/infinity, finite-coordinate addition overflow, chained root/page/clip intersection including a clip that completely removes focus. Explain the production mutation each family catches. Run and retain missing-interface RED without claiming a Linux UI reproduction.
- [ ] **Step 2:** Implement the finite intersection policy and measured fixed-layout resolver/capture branch. Keep normal scroll helpers, deadlines, observers and same-owner restore unchanged; no fixed-path ChangeView/events/awaits/retries. Revalidate the same root anchor at every existing await and enforce fixed visibility even at final publication. Preserve all liveness identities, original receipt and final immutable PNG publication. No ordinary page/caller/business/SDK/Core/lifecycle/workflow/diagnostic DTO changes. Remove now-unreachable above-root sampling only if needed for a clean implementation; preserve serialized legacy meanings and old evidence.
- [ ] **Step 3:** Run all NativeGalleryTests (including existing pixel-policy guards), all existing NativeGalleryFocusDiagnosticTests because the UI resolver semantics changed but serialized legacy evidence must remain supported, and the full four-property NativeReference build. No complete Core repeat locally unless a relevant failure/new shared change justifies it; Task3 public two-job run supplies complete tests. Record exact logs/TRX/source/DLL provenance, including any failed attempt.
- [ ] **Step 4:** Update Windows verification documentation around the actual b6b006e cause, fixed-versus-scroll behavior and remaining new-source runtime/visual gate. Self-review, commit only these five files, report and release writer. Fresh task review must check spec and quality from frozen diff/evidence; it does not repeat the suites.
- [ ] **Step 5:** Return to Task3 step4: one reviewed-source PUBLIC standard workflow, full native/plugin/owned/delivery audit and24 genuine available images, current image bytes personally inspected. A green workflow with23 images remains partial. Any new failure requires its own actual boundary evidence before another behavior change.
