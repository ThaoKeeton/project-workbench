# Native Shell and Recovery Integration Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair the requested native titlebar and integrate exportable startup recovery with actual owned Windows validation.

**Architecture:** A focused MainWindow partial owns the stable native TitleBar, icon and backdrop. The independent diagnostic worker's fixed-stage redacted export is integrated unchanged in meaning, then tested in real normal-start Windows cases without touching user computers.

**Tech Stack:** WinUI3, .NET10, WindowsAppSDK1.8.260921001, PowerShell7.5+, existing Core xUnit and public standard Windows Actions.

**Spec:** docs/superpowers/specs/2026-11-09-native-shell-recovery-design.md

## Global Constraints

- WinUI3/.NET10/WindowsAppSDK1.8.260921001; no WebView or embedded independent app.
- SDK packages0.4.0, AssemblyVersion1.0.0.0, services1 and user-data schema1 remain compatible.
- Do not concurrently modify HostFailure.cs, RunOptions.cs, WorkspaceRuntimeCoordinator.cs diagnostic logic, MainWindow initialization/catch or ProjectWorkspacePages recovery before the independent patch is handed over.
- Preserve files/user choices/evidence; no real credentials/providers/Webhooks/DingTalk/Teambition writes, user-PC access, system networking, Testing migration or vendor source changes.
- Default single-project UI hides unrelated multi-project controls; existing multi-project switching remains usable.
- Main/publication paused; no PR retry/force/delete/billing/release. Reviewed public standard Actions only; no duplicate vendor build.

## Review Focus

1. Failure before a runtime exists still has a usable titlebar and an actual default-redacted export path; Task2.
2. Normal duplicate startup, real write refusal and a blocking file cannot erase data or silently claim the project opened; Task2.
3. Native caption/drag behavior at narrow and maximized sizes cannot be replaced by client-only imagery; Tasks1/2.
4. Dark/light/system themes, unsupported Mica and resource packaging retain legible native controls without changing saved settings; Tasks1/2.
5. New host/old SDK compatibility and repaired UPCAM0.1.1 remain distinct from old companion and untested pairing; Task3.

### Task 1: Unified native window shell

**Files:** Create src/Workbench.App/ShellTitleBar.cs and original src/Workbench.App/Assets/Workbench.svg and Workbench.ico. Modify only MainWindow.cs constructor/shell layout, Workbench.App.csproj icon/content properties, focused native smoke shell assertions, and exact new-file provenance entries in scripts/TestingPluginDelivery.psm1/tests/scripts/TestingPluginPackage.Tests.ps1 if required. Diagnostic logic/recovery code is excluded.

**Interfaces:** Consumes existing MainWindow _root/_navigation, root actual theme and normal Window lifetime. Produces a private InitializeShellTitleBar() constructor call, stable AutomationName `工作台标题栏`, static title `Project Workbench`, original bundled icon, native caption/drag behavior and Mica or explicit themed fallback. No SDK/public contract or new CLI mode.

- [ ] Read the existing constructor/theme code and pinned1.8 API metadata; preserve all current child rows, events and runtime ownership.
- [ ] Establish the known reported manual regression (stock blue frame/default icon) and add focused real-native assertions before implementation; record that Linux cannot execute their native RED, without claiming a fake runtime test. Do not add string/source-mirror unit tests for cosmetic changes.
- [ ] Implement the focused partial using stable TitleBar/SetTitleBar/ExtendsContentIntoTitleBar and Window.SystemBackdrop, native caption buttons, stable packaged assets and existing actual theme. Use a narrow constructor hook after Content is attached; do not alter initialization/recovery worker blocks. Preserve initial unavailable-project frame usability.
- [ ] Add the exact asset/source provenance required by existing packaging guards, run their focused meaningful checks, and one full NativeReference rebuild in a fresh preserved task root. Compile0warnings/0errors is required; explicitly no Windows runtime or Mica proof from this build.
- [ ] Inspect the diff, commit the admitted shell changes, and report exact tested source/assets/binary hashes and actual commands. No network/Actions/push/other agent.

### Task 2: Integrate the small diagnostic repair

**Files:** The exact sixteen files in the verified parent patch, plus only required source-provenance guard updates. MainWindow must retain Task1 shell and all newer SourceAcceptance setup. No diagnostic redesign or new feature family.

**Interfaces:** Consumes Task1 shell and parent patch `workbench-safe-error-report.patch`, 50,219bytes/SHA256 `91dbd6b0129fd18b600405d6683f43bf5d27202c2649bca6f7fd35d5d4288ad0`, base655d7b6. Produces integrated fixed-field snapshots, user-chosen FolderPicker export and existing recovery with exact current-source tests. Root's input audit shows only MainWindow startup context does not apply; the patch has not been applied yet.

- [ ] Read the verified patch/docs and parent boundary evidence (61focused, Linux1445pass/2skip, reference0W0E on655, no Windows UI). Confirm current clean shell bytes, run/record apply check, integrate using three-way or deliberate hunk resolution and preserve both current SourceAcceptance and shell plus the parent's complete diagnostic repair. Never overwrite current files with the old655 originals.
- [ ] Add only integration-specific regression evidence where a real semantic merge risk exists; don't mirror implementation or rerun tests merely to recreate the parent's records. Verify no raw text/stack/path/project/config/machine/credential fields enter default export, first cause survives pending rollback, picker/export is owned during close, cancellation/collision does not overwrite files, and existing lock/data behavior stays intact.
- [ ] Run the affected portable tests and one full current Core suite because the patch changes shared storage exceptions/coordinator classification; retain all failures/skips by name. Run one final fresh NativeReference graph for the combined current App. Preserve earlier source/output roots and source/command/hash receipts. No local Windows runtime success claim.
- [ ] Check exact diff/Source248plus-new-input inventory and parent patch identity, commit the integrated repair, report all merge choices and actual outputs, then release to a fresh scoped reviewer. No CI/push/main/release, external provider/user-PC/other-repo work or generic automation framework.

### Task 3: Reviewed full host producer

**Boundary:** The Task1/2 host producer and its source fixes have been reviewed through the existing five-round producer loop. The reviewed producer source is `94cddc356b310b38c88f6976425722f1a2cd8562`; its bounded diagnostic run `42596739897` passed native smoke and collected client-gallery images. The distinct full standard producer run `42598740616` is pending its actual result and an eligible shipping artifact. Keep the producer source, job bodies, native/Source36/business51/owned27 checks and seven existing artifact slots materially unchanged. No sixth producer fix round is admitted by this boundary.

**Interfaces:** Produces the independently verified host source H1, successful full run R1, original app artifact A1 and its archive digest for Task4. Only Root audits and preserves those actual identities. Host0.4.1, SDK0.4.0, AssemblyVersion1.0.0.0, API/services1 and data schema1 remain unchanged; no host DLL is packaged into plugins.

- [ ] Root inspects the actual full jobs, logs, TRX and original app/assets/PE bytes, confirms the artifact's API identity and archive digest, and preserves the original before expiry. A diagnostic run, local compile or client image is not the shipping artifact or a full-window/export result.

### Task 4: Exact-shipping native recovery consumer and deliverable

**Boundary:** This separate consumer and delivery task follows the reviewed producer; it does not reopen or reset the producer's exhausted five-round source loop. The ordinary merge preserves the producer and the previously prepared consumer histories. H1 is bound to the actual producer run/artifact/host metadata; H2 independently identifies the clean consumer-driver checkout and workflow source, whether H1 and H2 differ or independently verify equal. Root performs fresh consumer review before any explicit light-mode dispatch.

**Files:** Add scripts/native-shell-recovery-probe.ps1 and bounded owned native UIA/capture helpers only as needed; .github/workflows/build.yml, exact host provenance/version tests and current delivery/Windows documentation. If host0.4.1 is used, update only App host version, PluginModels logical host default and exact strict host consumers; both SDK package versions remain0.4.0. No business/plugin source changes.

**Interfaces:** Consumes reviewed Task1/2, actual normal published EXE and an exclusive fresh evidence root. Produces CI-only strict full-window/export receipts and a new source-bound host package. Only Root dispatches public Actions after the helper/code gate. Existing UPCAM1.1 binary is an input or independent pairing handoff; never rebuild vendor.

- [ ] Implement the smallest owned Windows harness around existing proven UIA/PrintWindow primitives. Require github-hosted interactive Windows and an absent normal profile before creating test-owned data; verify retained PID/creation/HWND identity, clean child environment and exact app hashes. Do not search/interact with arbitrary windows or existing profiles. Write/test bounded identity/redaction/evidence parsing with meaningful invalid cases before using the helper. No new public CLI mode/device/SDK capability.
- [ ] The first delivery gate prioritizes the user's export need: normal healthy default startup/close; a second controlled process holding/refusing the root lease; damaged catalog; actual error-page FolderPicker cancellation/export, repeat-click serialization, same-name preservation, and main-window close while export is pending. Preserve sentinel bytes and root locks. Safely reproduce blocking-file/write-refusal cases within the same test-owned data where feasible, restore only the owned blocker/ACL and retry; label any unexecuted case explicitly. No new Figma/AI/proxy/Testing migration or complete arbitrary-computer matrix.
- [ ] Verify actual unified titlebar/icon and real light/dark complete-window pixels with native close/maximize/restore/layout checks. Capture only the exact owned HWND, reject blank/partial/missing captures, and retain original full-frame pixels separate from content RenderTargetBitmap. Drag/snap/high-DPI/accessibility and the other backdrop branch require their own actual observation; never infer them from properties. Native export cannot be replaced with a fake picker or unit writer.
- [ ] Run focused local checks and a necessary fresh reference graph for amended code, commit and obtain scoped review before Root's single authorized public standard Windows batch. Check PUBLIC/workflow/idle/branch/rules gates; preserve failures and limit artifact retention; never redispatch a source automatically. No old private feature-history push.
- [ ] Inspect complete jobs/logs/TRX and actual published app/assets/PE icon, full-window and redacted report bytes, original exit/root receipts. Existing native/Source36/business51/owned27 gates must not be silently skipped. Diagnose each independent failure before fixes. Report unit and native results separately.
- [ ] Prioritize the verified error-report executable delivery; don't wait for unrelated larger features. Give the actual new host source/version/EXE/DLL/ZIP hashes and pinned UPCAM production0.1.1/source389ccb1 to its existing pairing owner, or consume its existing package once in this authorized lane. Keep old0.1.0 companion51 separate and don't claim new pairing without an actual result.
- [ ] Document the user's original-machine cause as unknown until a supplied redacted report establishes it. Server proof does not replace userWindows11 acceptance. State scope/blockers and exact package evidence. Final whole-plan review precedes any main/publication integration; preserve all local history/evidence.
