# Native Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows native plugin workbench foundation whose independent plugins contribute controls to the host window.

**Architecture:** Cross-platform .NET SDK/Core manage discovery, trusted activation and settings. A WinUI SDK carries native view factories; a WinUI host displays them without statically referencing sample plugin classes.

**Tech Stack:** .NET 10 LTS, WinUI 3, Windows App SDK 1.8.260921001, C#, xUnit, PowerShell, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-06-native-workbench-design.md`

## Global Constraints

- Work only in the independent project-workbench repository; preserve other business repositories and branches.
- No WebView, embedded application windows, live credentials, external business messages, AI calls or actual computer automation.
- Plugins are trusted in-process code. AssemblyLoadContext is not a security sandbox; disabling is not assembly unloading. Update/removal require exit/restart.
- Discovering or installing a package must not execute plugin code; first activation requires explicit user trust and defaults to disabled.
- Pin Windows App SDK 1.8.260921001; target .NET 10, Windows 10.0.19041.0+, x64 unpackaged self-contained folder deployment; no trimming, AOT or single-file publish.
- Plugins provide code-created WinUI FrameworkElement factories; no dynamically loaded XAML/PRI resources in v0.1.
- Clearly label sample data and unfinished business integrations; distinguish Linux code tests, Windows builds and Windows interactive verification.

## Review Focus

- Duplicate/incompatible/malformed packages remain diagnosable without blocking valid ones (Task 1 discovery tests).
- Failed initialization or stop cannot leave active registrations or executable commands behind (Task 1 lifecycle tests).
- Settings persist across new instances and cannot escape the per-plugin directory (Task 1 real filesystem tests).
- Local installation rejects traversal, symlinks and replacement, including after a plugin is loaded (Task 1 install tests).
- Host navigation stops referencing a disabled plugin and command failures are visible; smoke checklist exercises native view creation, theme and shutdown (Task 2 host code and Windows checklist).

---

### Task 1: Testable trusted plugin runtime

**Files:**
- Create: `global.json`, `Directory.Build.props`, `ProjectWorkbench.Core.slnf` or `ProjectWorkbench.Core.slnx`, `src/Workbench.Sdk/*`, `src/Workbench.Core/*`.
- Test: `tests/Workbench.Core.Tests/*`, `tests/Workbench.TestPlugin/*`.

**Interfaces:**
- Produces: public `IWorkbenchPlugin`, `IPluginContext`, `IPluginView` (ID/title), async `PluginCommand`/command result and `IPluginSettings` in `Workbench.Sdk`.
- Produces: `PluginManager` in `Workbench.Core`, with discover/install/enable/disable/command execution methods and read-only plugin status/contribution snapshots. Lifecycle methods are async and accept `CancellationToken`. A single-plugin context owns all its registrations/settings; no WinUI reference in SDK/Core.
- UI-specific plugin views implement `IPluginView`; concrete WinUI factory is added by Task 2. Document exact final signatures in the task report for the UI implementer.

- [ ] Step 1: Create project/test configuration and executable regression tests with xUnit. Use actual temporary directories and a separately built fixture DLL; watch meaningful failures before implementation. The production changes that should fail tests are allowing traversal/duplicate IDs, executing discovery code, omitting rollback, failing to clear commands on stop, or failing to persist settings.
- [ ] Step 2: Implement strict manifest/package validation and deterministic discovery. Preserve useful per-package diagnostics. A malformed package, incompatible API/host or missing assembly does not stop another valid package. Do not activate during scanning. IDs must be safe namespaced identifiers; entry paths must remain inside the package and end in `.dll`. Local directory install copies only regular contained files into a new per-plugin directory, rejects symlinks and duplicate IDs, and cleans partial installs on error.
- [ ] Step 3: Implement a non-collectible load context with AssemblyDependencyResolver and explicit shared-contract/framework assembly handling. Implement trusted enable, staged registration commit/rollback, duplicate contribution checks, lifetime cancellation, stop cleanup and per-package failure status. A fixture registers a view, setting and command that can be asserted without WinUI. Exercise actual dynamic loading and a missing dependency/error path.
- [ ] Step 4: Implement JSON settings and saved enabled/trusted state under a supplied user root, atomic writes, default values, clear corruption diagnostics, restart restoration that never enables an untrusted package. Do not treat stored settings as a secret store or a security boundary.
- [ ] Step 5: Run the full cross-platform test command, record RED/GREEN evidence and any limitations, self-review, then commit task code. Do not add native UI or business integration.

### Task 2: Native shell, two independent plugins and Windows build

**Files:**
- Create: `ProjectWorkbench.slnx`, `src/Workbench.WinUI.Sdk/*`, `src/Workbench.App/*`, `plugins/Workbench.Summary.Sample/*`, `plugins/Workbench.TestCases.Sample/*`, `scripts/build.ps1`, `.github/workflows/build.yml`.
- Modify: `README.md`; create `docs/plugin-sdk.md`, `docs/windows-verification.md`.

**Interfaces:**
- Consumes: Task 1 public SDK/Core signatures and exact fixture/test commands from its report. Read only the relevant source/API as needed.
- Produces: `WinUiView` implementing `IPluginView`, with `Func<FrameworkElement>` factory and view title/ID; settings views use the same strong type. Host and plugins share this SDK assembly.
- Produces: `scripts/build.ps1` building both independent packages into the host's `Plugins` directory and publishing an x64 self-contained app folder. No project/assembly reference from App to concrete sample plugin code; staging build orchestration is separate.

- [ ] Step 1: Add WinUI SDK and programmatic WinUI host using `Application.Start`, a standard XamlControlsResources dictionary and native NavigationView. Implement home, plugin list/details and enable/disable, command list/run results, application theme settings, plus plugin view/settings navigation. Set meaningful automation names and avoid hardcoded window widths in content layouts.
- [ ] Step 2: Add local folder package installation UI and explicit same-process trust confirmation before first enable. Persist desired enabled state; restore trusted enabled packages on next launch. Show discovery/activation/command/install errors. When disabling, navigate away from plugin views and drop related controls/references before calling Core stop; on app closing stop active plugins before final close, without claiming forced cancellation of uncooperative plugins.
- [ ] Step 3: Add two independently compiled plugins. Summary sample: local demonstration data, editable draft generated by a command and a persisted preference. Test-case sample: local demonstration rows, a native editable table/list, a sample CSV export and a persisted preference. Each registers a distinct native page, a native settings page and an async command. All sample actions are local and explicitly labeled as examples.
- [ ] Step 4: Add PowerShell orchestration for restore/build/publish/staging; do not lose plugin assemblies/deps/manifests on publish, and do not duplicate host SDK/WinUI binaries in plugin folders. Add CI Linux Core tests plus Windows build/publish and upload of the complete app directory; no deployment. Ensure scripts terminate on nonzero dotnet exits and use clean output roots to avoid stale plugins. Add an isolated `--smoke-test` mode using a unique temporary user directory and only the two known bundled sample packages. On the real WinUI UI thread it enables those samples explicitly for the test, creates/attaches both native page and settings factories, runs their local commands, disables and re-enables, then stops/closes and writes a JSON result with a nonzero process exit on failure. CI runs it with a bounded timeout and uploads the report; inability to initialize WinUI in a hosted desktop is an explicit failure/blocker, never a silently skipped pass. Do not scan installed user plugins or read actual user settings during this smoke test.
- [ ] Step 5: Run feasible build checks. If Linux cannot run WinUI toolchain, record the exact blocking target/error; perform an honest C# reference compile if useful and add the Windows CI path. Document clean Windows prerequisites, launch/build commands, package layout, trust/update limitations and a manual Windows checklist covering first launch, install, both native views/settings, commands, disable/re-enable, restart persistence, system/light/dark themes, DPI and shutdown. Never mark those interactive checks passed without evidence.
- [ ] Step 6: Run full Core tests again after integration, validate XML/JSON/workflow and script syntax where feasible, self-review and commit. Report actual artifacts and remaining Windows verification gaps.
