# Genuine WinUI gallery and bounded public CI evidence

User requests genuine platform/installed-plugin screenshots and ongoing public standard-runner Actions verification. There are no historical workbench screenshots. Two archived portable test BMPs are not platform images. User opted out routine design questions; the coordinator chooses the following small runtime content-capture path.

## Scope and approach

Capture the actual displayed MainWindow.Content with WinUI RenderTargetBitmap and BitmapEncoder PNG at existing isolated native probe checkpoints. This captures actual host navigation and attached plugin controls, excluding OS title bar and separate dialogs/popups; label that scope in every receipt/index. No mock UI, copied plugin page, new RunOptions mode, generic desktop capture or arbitrary HWND/device control. Linux reference compilation is not screenshot proof. Windows Server2025 CI evidence is not Windows11 human/IME/DPI/picker/accessibility acceptance.

Add one internal MainWindow partial capture helper, one pure policy class linked into Core tests, fixed passive calls in existing smoke/workspace/testing/business probes, and a small strict PowerShell receipt/launcher-binding aggregator. Keep existing probe actions, process exit/Closed/lease verdicts, plugin isolation and lifecycle intact. Capture errors are separate evidence results and cannot turn any failing original probe into a pass.

## Live capture and fixed bounds

Require an existing isolated synthetic probe and report path, UI thread, same attached current page, Loaded/current XamlRoot, no runtime/close transition, own visible HWND with actual current PID and creation FILETIME, positive client/content bounds, Environment.UserInteractive and readable input desktop. Never inspect another user's desktop. Align the existing NavigationView item under the existing refresh guard so the selected sidebar matches the actually attached page without recreating it.

UpdateLayout and await bounded dispatcher/compositor turns; await real rendering/pixel/encoding operations, retain ownership until completion rather than dropping a timed-out task. Existing launcher/process timeout remains the outer failure bound. Recheck page/runtime/window/PID/birth after awaits. No render in normal application use; only explicit existing probe calls.

Max32 shots per capture process and max32 accepted gallery shots per aggregate; dimensions1..4096 each, BGRA <=67108864bytes, PNG <=16777216bytes. checked width*height*4 must equal pixel length. Reject all-transparent and uniform frames. Encode PNG and decode its dimensions using actual WinRT codec before accepting; create files with CreateNew, never overwrite. Preserve diagnostic attempt receipt on blocked/failed capture but do not accept/write a purported screenshot. Pixel checks establish finite/nonblank data, not visual completeness; controller must view downloaded accepted PNGs.

## Protocol and roster

Gallery records use schemaVersion1, captureKind "WinUiContentRender", scope "actual-window-content-only", titleBarIncluded=false, popupsIncluded=false. Status enum is captured, blocked, failed, skipped-unavailable, skipped-unsafe. Reason codes are fixed safe ASCII <=64chars, never raw exception/header/key/provider payload. A captured record has shotId/pageKey/pluginId/viewId, probe kind/phase, current project identity if available, current page/selected-navigation identity, actual HWND/PID/creationFILETIME/captureUTC, executable SHA256/host informational version/source identity, OS/architecture/session/desktop/visibility, width/height/rasterization scale/theme/viewport/scroll offset, fixed safe boolean/count witnesses, relative PNG path/byte count/SHA256. File times and PIDs are scalar integers, not array-coerced. No PasswordBox contents, environment dump, secrets or raw diagnostic exceptions.

Use strict ordinal IDs and safe filename components. Receipt paths and images stay under the dedicated artifacts/native evidence directories beside the relevant report. Reject traversal, alternate streams, rooted paths, symlinks/reparse points, duplicate IDs/orphan images, impossible scalar values, hash/size/header mismatches and identity mismatch. Keep attempt records/reporting bounded, not a general event log.

Fixed requested roster (max32, implemented views only):
- native smoke: home; host plugins, commands, background, credentials, packages, settings; summary-page, summary-settings, cases-page, cases-settings.
- workspace seed: workspace-single-context and workspace-multi-context, captured at actual existing single/multiple project checkpoints.
- installed testing: testing-authoring, testing-virtual-evidence, testing-owned-panel. Owned idle/blocked presentation is valid UI capture but never evidence of an executed owned run; explicitly report actual-run proof blocked.
- installed business seed: business-plugins, dingtalk-workspace, dingtalk-settings, upcam-preferences, upcam-preview.
- optional synthetic acceptance history: upcam-acceptance-history, clearly separate com.upcam.task-digest.acceptance/fake-provider identity.
- docs-page: skipped-unavailable, because no separate Docs navigation page exists. Context editor is the available project document. Do not add a placeholder Docs UI.

Each accepted screenshot must follow existing state assertions. Use current actual production/plugin package-store activation, current ZIP/entry hashes and fixed synthetic data. Source/AI fixture and generic device execution are not part of this gallery. Native testing screenshot may show a SIMULATED frame inside a genuine UI image; distinguish those two layers explicitly.

## Launcher and aggregator binding

Each of the four gallery-capable wrappers retains actual process handle/start FILETIME and confirmed exit, and writes an immutable gallery-binding sidecar with actual PID/birth/exit, phase, shipping EXE hash/current source SHA, original report path/hash and package identity available to that probe. Business wrappers already observe birth and must reuse it. Keep all original failure/root-lease semantics and preserve sidecars after failure. Never infer process identity solely from the native receipt.

NativeGalleryReport.psm1 exposes Test-NativeGalleryReceipt and Export-NativeGallery; collect-native-gallery.ps1 aggregates fixed known probe roots into artifacts/native/genuine-gallery/index.json and validated images only. Reuse existing sidecar/report data where possible. Gallery verifies independent launcher binding, native receipt, image length/hash/PNG dimensions, original probe result and package identity. Failed original probes remain failed in the index even if earlier valid images exist. Missing/no-desktop/unreached pages get explicit blocked or skipped rows. Integrity refusal exits1; a valid index reporting unavailable desktop may exit0 but status is blocked and is not screenshot acceptance.

## Workflow and cost policy

Use current ubuntu-latest/windows-2025 jobs; no new jobs, private/paid runners, billing changes/purchases or unrelated billable services. Move OwnedLab gate after gallery-capable host probes. Give existing independent isolated smoke/workspace/testing/business probes explicit successful-build/package prerequisite conditions so an unrelated earlier probe/OwnedLab failure does not silently prevent later requested captures; retain each original step failure and each wrapper's within-probe stop-on-failure. Run collector if always, so missing prerequisites are reported. Do not use continue-on-error to turn validation green.

At most7 new artifacts/run, all retention-days1: core-linux-results; native-verification-evidence (consolidated original Windows reports/probe roots/diagnostics/generated sources/Core TRX); existing four success-only delivery artifacts workbench-win-x64/workbench-testing-plugin-0.2.0/workbench-sdk-0.4.0/workbench-business-plugins; workbench-ui-gallery only when a valid index exists (may explicitly contain blocked rows). No duplicate full app/plugin trees in reports or caches. Do not delete/change old artifacts. Update README/testing-plugin/windows-verification artifact references consistently. Public CI dispatch belongs to controller after source review, not implementer.

## Local verification and remaining gates

Meaningful RED/GREEN for actual pure BGRA policy and receipt/path/identity/hash validation; focused existing script guards and whole final Core suite once (real90second case, no60second cutoff), full four-property NativeReference build, changed PowerShell/YAML/manifest/source provenance checks. Preserve exact argv/env/logs/TRX/source/DLL identities and failed attempts. Update source audit/actual new-record hash refusals only where needed, never weaken production eight-file package validation. No production ZIP from ReferenceOnly.

Actual PNG rendering/desktop/visual quality/installed current packages/business/owned27/crash/maintenance/DPAPI/Windows11 human gates remain explicit until their respective actual results. Trusted in-process plugins and this probe are not a sandbox.

