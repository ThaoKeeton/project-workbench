# Genuine Native Gallery and Public Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox syntax for tracking.

**Goal:** Capture genuine current WinUI content and installed-plugin usage during the next authorized public Windows validation, with bounded evidence retention.
**Architecture:** Passive capture at existing isolated probe checkpoints, exact process/package/image receipt binding, seven-artifact public workflow.
**Tech Stack:** WinUI3 RenderTargetBitmap/Windows PNG codec, .NET10/xUnit, PowerShell, existing two public standard GitHub runners.
**Spec:** docs/superpowers/specs/2026-11-08-native-gallery-design.md

## Global Constraints

- Actual displayed MainWindow.Content only, same runtime/page/own HWND and PID+birth; scope actual-window-content-only, titleBarIncluded=false, popupsIncluded=false. No mockup, normal-use capture, new probe mode, desktop/other-window capture or arbitrary device.
- Synthetic isolated probe data only; no real provider/credentials/Webhook/DingTalk message. Actual installed production business/testing package identities remain distinct from synthetic acceptance identity.
- Preserve original probe actions/failures/Closed/OS exit/root-lease and lifecycle; no false success, implicit held-IO release, dropped render task or native CLR speculative fix/cap reset.
- Schema1/captureKind WinUiContentRender; status captured|blocked|failed|skipped-unavailable|skipped-unsafe. Max32 images/aggregate and process, dimensions1..4096, BGRA<=67108864bytes, PNG<=16777216bytes. Exact bytes/hash, strict scalar identity/path checks; no raw keys/PasswordBox contents/exceptions in metadata.
- Current two public standard runners only, no new job/private/paid runner/billing/purchase/deletion/PR retry/merge/release/deploy. Controller alone handles GitHub writes/public Actions after review.
- At most7 artifacts/run, retention-days1; preserve old artifacts/evidence/business repos. Production eight-file payload/helper validator unchanged. SourceAcceptance2draftdocs remain uncommitted/out of this task.
- Linux code/reference results do not prove genuine images, Windows runtime or Windows11 human acceptance; original native27/older9of27CLR/five-round cap/current business/crash/maintenance/source fixture gates stay independently open.

## Review Focus

- Live identity changes/detached page/no desktop produce bounded blocked/failed receipts without accepted PNG.
- Blank/transparent/over-limit/invalid encoded output and duplicate/escaped paths cannot become captured evidence.
- Independent launcher PID+birth+exit and current EXE/package hashes bind each actual receipt; array values and matching forged native-only identity fail.
- Failed/unreached original probes and nonexistent Docs page remain explicit, while unrelated isolated probes can still attempt capture.
- Short retention/consolidated reports preserve all relevant raw evidence and do not upload duplicate payloads/caches or mark an unverified EXE as successful delivery.

### Task 1: Passive genuine gallery and bounded public workflow

**Files:** Create src/Workbench.App/NativeGalleryCapture.cs (MainWindow partial), NativeGalleryPolicy.cs (pure BCL policy); add passive calls in NativeSmoke.cs, NativeWorkspaceProbe.cs, TestingNativeProbe.Authoring.cs, TestingNativeProbe.Execution.cs, BusinessPluginSmoke.cs, optionally BusinessPluginAcceptanceSmoke.cs. Modify existing MainWindow smoke-navigation seam only for accurate selected-item alignment. Create scripts/NativeGalleryReport.psm1 and collect-native-gallery.ps1; modify native-smoke.ps1, project-workspace-probe.ps1, testing-plugin-probe.ps1, business-plugin-smoke.ps1 for immutable actual launcher binding. Create tests/Workbench.Core.Tests/NativeGalleryTests.cs and link exact policy in its csproj; create tests/scripts/NativeGalleryReport.Tests.ps1. Modify .github/workflows/build.yml, README.md, docs/windows-verification.md, docs/testing-plugin.md. Source audit scripts/TestingPluginDelivery.psm1 and tests/scripts/TestingPluginPackage.Tests.ps1 may change only to cover new inputs and actual new-record hash refusals. No Core/SDK/Domain/native/business repository implementation changes.

**Consumes:** Current actual MainWindow.Content and SmokeNavigateAsync, existing native probe assertions/stores/installed ZIPs/launcher report semantics. Recon note .superpowers/sdd/source-acceptance/native-gallery-recon.md is optional implementation context; this brief/spec own requirements.

**Produces:** internal Task-based passive window capture, strict pure policy, immutable per-shot JSON/PNG, independently bound index, updated public workflow/docs. Exact data/roster/bounds are frozen in spec; local routine record/helper names may be chosen consistently. Suggested stable functions:
- internal NativeGalleryPolicy.IsValidFrame(ReadOnlySpan<byte> bgra, int width, int height) used by actual capture and linked tests.
- internal MainWindow.SmokeCaptureGalleryAsync(RunOptions options, string shotId, string pageKey, string? pluginId, string? viewId, FrameworkElement displayedPage, safe witnesses/viewport as needed).
- Test-NativeGalleryReceipt validates a receipt against independently supplied expected process/EXE/phase binding; Export-NativeGallery aggregates bounded known roots; collect-native-gallery.ps1 is workflow entry.

- [ ] 1. Read spec and needed existing seams, then write meaningful portable policy and actual receipt-validator behavior tests before implementation. RED must fail on a real semantic guard, not missing APIs/setup. Test valid nonuniform opaque frame vs uniform/transparent/size/dimension/overflow; actual files and valid minimal image fixtures vs traversal/reparse/duplicates/orphans/hash/size/dimension/array-ID/wrong-birth/wrong-source/failed-original-probe. Preserve RED sources/output.
- [ ] 2. Implement capture helper/pure policy and exact JSON/index contracts. Validate actual isolated page/runtime/own HWND/desktop/layout/identity before and after rendering. Use real displayed root, actual BGRA/PNG encode+decode, exact CreateNew/limits and retained awaits. Fixed safe reason codes and witnesses only, no secret reads. Record blocked/failed attempts; don't accept invalid/blank/stale PNG. No generic capture framework.
- [ ] 3. Add passive calls at real asserted checkpoints for fixed roster. Align navigation selection without factory recreation; bring long-page control into visible viewport and wait actual layout/scroll, record it. Capture single/multiple context distinction, actual installed testing editor/SIMULATED-evidence/owned idle-or-real state and actual business demo/preview. Optional fake-provider history explicitly synthetic. Missing Docs is a skipped row, not new UI. Keep exact existing functional checks and no provider/send actions.
- [ ] 4. Bind launchers and implement strict collector. Preserve actual process handles/birth/confirmed exit and original report verdicts; validate all per-shot fields/files against independent binding and package/EXE/source hashes. Never overwrite existing receipt/sidecar or weaken root lease checks. Aggregate present images plus explicit blocked/skipped/unreached roster, keep failed original probe failure separate. Implement focused PowerShell GREEN and guard regressions.
- [ ] 5. Update workflow and docs exactly to spec. Two existing public standard jobs only; source checks include new focused PS test. Independent gallery probes guarded by actual successful build/package prerequisites, OwnedLab after those probes, collector always; no suppressed failing step or within-probe phase bypass. Consolidate original Windows evidence paths, separate gallery and four success-only deliveries, all7or-fewer artifacts retention-days1. Update names/path consumers/docs and source provenance/hash guards as needed.
- [ ] 6. Final local checks once on final code: focused new policy/receipt/affected script tests; complete feasible CI PS smoke/debug/business/report set including new validator; dotnet test ProjectWorkbench.Core.slnx -c Release --nologo (real90second/no60second cutoff); dotnet build tests/Workbench.NativeReference/Workbench.NativeReference.csproj -c Release --nologo -p:OwnedLabReferenceOnly=true -p:UseWinUI=false -p:AppxGeneratePriEnabled=false -p:WindowsAppSDKSelfContained=false (0warnings/errors); parse all changed PS/YAML and assert workflow prerequisite/artifact budget/retention; actual source guard/negative hash tests. No Windows/helper production ZIP claim from Linux. Save raw logs/TRX/argv/env/sources/actual DLL metadata and all failed attempts.
- [ ] 7. Commit only task files locally "test: capture genuine native workbench gallery"; self-review BASE..HEAD, finish .superpowers/sdd/native-gallery/task-1-report.md, task1-checks and writer-release. No subagents/GitHub actions. Report status/commit/one-line checks/concerns/path. Controller audits evidence and fresh task-scoped reviewer before public CI.

## Self-review and task boundary

One independently rejectable deliverable: a genuine gallery is not delivered without capture calls, exact evidence identity, workflow attempt order and limited uploads. SourceAcceptance source fixture remains a separate next task. Gallery does not claim later installed source or owned-run correctness; those need actual independent results.

