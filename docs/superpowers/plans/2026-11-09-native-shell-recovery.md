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

### Task 2: Diagnostic patch integration and normal-window evidence

**Files:** Independent patch paths and focused linked tests as explicitly handed off; add a dedicated scripts/native-shell-recovery-probe.ps1 and bounded helper modules/native driver only as necessary; modify .github/workflows/build.yml and exact provenance inventories. Preserve existing content gallery behavior and labels.

**Interfaces:** Consumes Task1 shell and parent-provided exact diagnostic patch/commit/tests. Produces a CI-only owned normal-window acceptance entry that takes the actual published app path and an exclusive newly created evidence root, validates process creation identity/HWND ownership, saves original full-window images and strict redacted-export reports/receipts. No arbitrary device or profile path.

- [ ] Wait only for the diagnostic patch handoff; meanwhile inspect existing owned driver/import/launcher mechanisms. Verify patch base655 against current tracked changes and integrate without blind overwrite or user-data migration. Run its focused portable tests and retain all reported failures.
- [ ] Write/test bounded evidence/identity and redacted-export assertions before the corresponding helper. Deny wrong PID/birth/HWND/root/image identity, unsafe paths, partial/missing export, any sensitive/non-schema field and a failed expected recovery. Preserve reports on failure; no duplicate final report overwrites.
- [ ] Exercise actual normal default startup in a newly owned ephemeral Windows CI profile, then normal duplicate launch, a known blocking file and safely applied real write refusal. Preserve sentinel bytes, restore only the owned test blocker/ACL, invoke actual UI retry and export controls, parse exact redacted output, verify real Closed/normal exit/leases. No mocked picker acceptance.
- [ ] Capture actual full owned Workbench window after shell theme/narrow/maximize/restore/drag checks. Keep full-window receipts separate from client RenderTargetBitmap images, bind original pixels and build/PID/birth, and record unsupported/failed capture instead of a placeholder.
- [ ] Run local portable/PowerShell validation appropriate to the changed code, one necessary reference build, commit and obtain scoped review before a public batch. Actual Windows checks are Root's next stage, not a local success claim.

### Task 3: Actual public Windows batch and repaired host delivery

**Files:** App host version/PluginModels host default only if0.4.1 is used; current delivery/Windows verification docs; workflow only required minimal artifact additions. SDK package versions remain0.4.0.

**Interfaces:** Consumes reviewed Task1/2 code and producer. Produces exact shipping host source/version/EXE/DLL/ZIP hashes, actual original native recovery/full-window/export evidence, and explicit external UPCAM0.1.1 pairing handoff.

- [ ] Check current repository public/workflow/idle/branch/rules state. Bind one reviewed source and run one authorized standard public Actions batch; preserve all failures, control artifact retention and never redispatch the same source automatically.
- [ ] Inspect full jobs/logs/TRX and actual package/export/window bytes; existing Source36/business51/owned27 and other native gates must not be silently skipped. Separate each original failure; diagnose before fixes.
- [ ] Deliver current usable-host evidence and exact new package to the UPCAM pairing owner; retain source389ccb1/0.1.1, no vendor rebuild or old0.1.0 substitution. Do not claim new pairing until actual result is returned.
- [ ] Document original-machine root cause as unknown until a user-provided redacted report establishes it. Native Server proof does not replace userWindows11 acceptance. Record code position, tested scope and remaining blockers. Final whole-plan review and any integration require this actual evidence; preserve all branches/evidence.
