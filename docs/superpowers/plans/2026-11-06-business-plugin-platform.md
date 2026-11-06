# 业务插件平台 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 安装并验证独立钉钉 WinUI 业务插件，完成宿主管理的后台生命周期、凭据边界及重启维护闭环。

**Architecture:** 保留 API 1，通过可选运行上下文扩展受观察任务和 Windows DPAPI 存储；代码包更新暂存到重启前应用，并保留历史和数据备份。宿主 UI 无业务静态依赖；独立业务包作为带来源的集成输入验证。

**Tech Stack:** .NET 10, WinUI 3, Windows App SDK 1.8.260921001, x64 unpackaged self-contained, xUnit/PowerShell/GitHub Windows CI.

**Spec:** `docs/superpowers/specs/2026-11-06-business-plugin-platform-design.md`

## Global Constraints

- Work only in project-workbench; business repositories are read-only references. Keep the feature branch feat/native-plugin-workbench and original branches.
- .NET 10/win-x64/Windows App SDK 1.8.260921001; no trimming, AOT, single-file or plugin XAML/PRI loading; no WebView/embedded independent app/Desktop DLL reuse.
- Trusted in-process plugins; no sandbox/hot-unload/forced task termination claims. Never replace an assembly loaded in this process.
- WorkbenchApi.Version remains 1; preserve IPluginContext. Optional runtime-services version1 requires host0.2.0; SDK assembly identity remains1.0.0.0.
- Discover/install/stage/recovery do not execute plugin code; explicit fingerprint trust precedes activation. Business consent is distinct from trust/resume.
- Only synthetic credentials/data and simulated business providers in tests. No actual DWS/OpenAI/Webhook calls or business messages. Secret values never enter host-originated diagnostics.
- Preserve original13 native checks and distinguish Linux, WindowsServer2025 automation and Windows11 manual acceptance; do not claim unexecuted checks.

## Review Focus

- Stop while a task or plugin Stop ignores cancellation: retain runtime ownership and store lock, prevent a second instance; Task1 owns actual gated regressions.
- Restore after package/config change: no unexpected background work or inherited trust/consent; Task1 binds running intent to package fingerprint, Task4 tests process restart.
- Corrupt/unsupported credential storage and secret-bearing exceptions: fail closed without plaintext or value-bearing diagnostics; Task1 real Windows DPAPI plus portable fake-protector tests.
- Interrupted update and tampered backup: retain a usable old package and untouched current user data; Task2 on-disk recovery tests plus Task4 actual process update/rollback.
- ZIP traversal, collisions and forged lengths: never write outside staging or exceed limits; Task2 actual archive/filesystem tests.

---

### Task 1: Runtime SDK, supervised jobs and credentials

**Files:** Create `src/Workbench.Sdk/RuntimeContracts.cs`; Core `PluginBackgroundTasks.cs`, `PluginSecretStore.cs`, `WindowsSecretProtector.cs`, `PluginRuntimeState.cs`; modify `PluginRegistrationContext.cs`, `PluginManager.cs`, `PluginModels.cs`, SDK projects and SDK docs. Extend real `Workbench.TestPlugin` fixtures and Core tests (`BackgroundTaskTests.cs`, `SecretStoreTests.cs`, `StopOwnershipTests.cs`); extend deployment probe for same-user Windows secret restart as needed.

**Consumes:** Existing Initialize/Stop, staged registration, Lifetime, settings, ALC and user-root contracts.

**Produces:** Exact optional interfaces/types in the spec, default StopTimeout5s, host0.2.0 and stable SDK assembly1.0.0.0; manager task/secret definition snapshots and operations for host UI; retained pending-stop ownership and safe error codes.

- [ ] Write meaningful RED tests for duplicate Start, Start/Stop race, observed fault, auto-resume opt-in/fingerprint binding, no run before commit, explicit-disable versus shutdown intent, timed-out Stop retaining one instance and reusing one Stop Task, late completion cleanup, unsupported/corrupt secret storage, key isolation and no raw secret diagnostics.
- [ ] Run focused Core tests and confirm behavior failure before implementation. Keep each RED log; compile errors alone are not behavioral RED.
- [ ] Implement the optional SDK and staged registration. Task execution uses `IPluginBackgroundExecution` with UserRequest/Restore and observable stop reason. Preserve old API1 fixture behavior and staged initialization rollback.
- [ ] Implement bounded cooperative stop with ownership retained until all known Stop/background Tasks finish. Expose safe state without blocking the dispatcher; do not dispose context/lock while work is alive.
- [ ] Implement scoped Windows DPAPI CurrentUser secret storage and metadata; no plaintext fallback. Fake protector tests are explicitly not real encryption proof. Use only synthetic values for actual Windows provider/restart tests.
- [ ] Run covering tests, full Core Release once after changes settle, affected reference builds and format/whitespace. Return actual outputs and public interface signatures. Controller runs Windows CI and review.
- [ ] Document/register package0.2.0 with stable assembly identity, new runtime-services version1, secret/exception boundaries and background restore/stop semantics; commit.

### Task 2: Transactional packages and restart recovery

**Files:** Create Core `PluginPackageStore.cs`, `PackageArchive.cs`, `PackageJournal.cs`, `LoadedPackageRegistry.cs`; modify only necessary manager integration/models; tests `PackageArchiveTests.cs`, `PackageMaintenanceTests.cs`, `PackageRecoveryTests.cs`; update package SDK docs.

**Consumes:** Task1 safe lifecycle and host version; existing PackageValidator/FileSystemGuard/AtomicJsonFile, current managed PluginsRoot and additional bundled roots.

**Produces:** Public Core `PluginPackageStore` with archive/folder install, `StageUpdateAsync`, `StageRollbackAsync`, `StageRemoveAsync`, `CancelPendingAsync`, `ApplyPendingAsync`, pending/history/diagnostic snapshots. Task report fixes exact signatures for Task3. User-root paths remain contained; apply is startup-only and rejects any package path recorded as loaded in this process.

- [ ] Write RED tests using actual ZIPs/files: traversal/absolute/ADS/devices/symlinks/collisions/duplicate files, 1000-file/100MiB total/50MiB file limits, and revalidated staged fingerprints.
- [ ] Implement archive import to a private stage, counting actual copied bytes; preserve folder install semantics and never execute/approve packages.
- [ ] Write RED maintenance tests: new-ID install, one pending operation per ID, version/API/host checks, bundled ownership refusal, cancellation, no active file mutation before restart, loaded-in-process apply rejection.
- [ ] Implement atomic journal and same-volume moves with verified old/new fingerprints. Before update, copy bounded regular-file snapshots of data/settings/background/secrets (1GiB/10000 files); never automatically restore/delete current business data or credential state.
- [ ] Write RED recovery tests for interruption before/after each directory move, corrupted stage/backup/journal, unexpected current changes and idempotent replay. Reconstruct real on-disk states; avoid production-only fault-injection switches.
- [ ] Implement deterministic roll-forward or safe old-code recovery; keep diagnostic failures local and preserve unrelated installed packages. Rollback code history requires trust again; clear changed-package background run intent via Task1 binding.
- [ ] Run covering tests and full Core once after settling, document concrete paths/phase semantics/data rollback limits, commit and report public signatures.

### Task 3: Native management and startup integration

**Files:** Modify App `HostRuntime.cs`, `MainWindow.cs`, `ShellPages.cs`, `NativeSmoke.cs`, `RunOptions.cs`; split focused `BackgroundPage.cs`, `CredentialPage.cs`, `PackageMaintenancePage.cs` when useful; scripts/build/workflow/test scripts as necessary; README and Windows verification docs.

**Consumes:** Task1 task/secret snapshots and operations; Task2 package-store report signatures.

**Produces:** Native task/credential/package management; startup recovery under the lease before discovery/load, retained lease on stop failure; reproducible isolated native checks and SDK distribution snapshot for independent business builds.

- [ ] Add focused regressions for runtime/store lease retained after timed-out close and safe retry, initialized/failed navigation, default-empty credential editor, safe metadata and actual synthetic DPAPI save/read/delete, background UI responsiveness and task stop/fault states.
- [ ] Integrate ApplyPending before discovery/load while holding the existing store lease. Dispose releases lease only after known work truly ends; failed close stays usable and does not spawn/re-enable another runtime.
- [ ] Add native Background/Credentials navigation and contextual package maintenance actions. Metadata only displays configured status; PasswordBox values are not prefilled/logged. Expose restart-required states and code-only rollback/data limits clearly.
- [ ] Preserve original13 checks; add focused runtime/package checks, real Windows provider and actual OS-process restart probes. Test helpers use isolated roots and known synthetic packages; no implicit approval during normal app startup.
- [ ] Build a reproducible SDK source/package snapshot with pinned API/assembly/WinUI versions for private independent CI, no credentials or Desktop binaries. Document it at `docs/integrations/plugin-contract-v1.md` and integration build instructions.
- [ ] Run feasible local checks; controller obtains Windows native GREEN. Record Server versus Windows11 manual boundaries and commit.

### Task 4: Install and verify independent DingTalk plugin

**Files:** Create `tests/integration/dingtalk/provenance.json` and pinned external test package input or import script; `scripts/import-business-plugin.ps1`, `scripts/business-plugin-smoke.ps1`; App focused `BusinessPluginSmoke.cs` and test-only options; update CI/verification docs. No DingTalk business implementation goes into host production projects.

**Consumes:** The actual dingtalk-insights adapter branch/commit and successful package artifact produced independently, aligned to platform SDK/runtime contract. Never use Desktop DLLs. Pin origin, commit, upstream workflow, package SHA256 and manifest; test input contains no real secrets.

**Produces:** Reproducible actual installation/page/settings/business-Core/background/restart/failure/update/rollback/remove report for the independent plugin, with simulated inputs and zero real business sends. Full package has no host static business references.

- [ ] Inspect current upstream branches, source contract and real CI before selecting package; if adapter is still being built, continue independent platform work and record exact dependency rather than invent a plugin.
- [ ] Import a verified, provenance-pinned external package as integration input. Use a test fixture only to avoid cross-private-repository PAT requirements; keep it out of production bundled Plugins. Inspect/package-exclude Desktop/runtime/duplicate SDK/credential material.
- [ ] On actual Windows, install the ZIP through the production store, prove disabled/untrusted first, explicitly approve only this known test fingerprint, create page/settings controls and configure synthetic/demo mode.
- [ ] Execute true business Core read/search/organize paths with simulated inputs; start managed background work, navigate away, observe state changes, stop and verify no subsequent writes. Keep all external-network/send providers disabled.
- [ ] Exit and wait for the OS process, relaunch the same isolated root and verify consent-bound restore, no duplicate worker, checkpoint/credentials/settings persistence and clean stop. Probe missing configuration and managed task failure without corrupting existing state.
- [ ] Exercise staged version update, failure and rollback across separate processes; explicitly label synthetic manifest/version fault variants if upstream supplies only one real release. Verify package identity, backups and current business data preservation; do not claim arbitrary schema rollback or live Webhook dedup proof.
- [ ] Produce JSON reports with actual PID/exit codes/source provenance and all checks. Run current Windows CI and read its results/artifacts. Keep Windows11 manual acceptance pending unless actually executed. Commit and obtain scoped then final review.
