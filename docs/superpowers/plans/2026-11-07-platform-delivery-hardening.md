# Platform Delivery Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 让试用用户准确安装固定生产业务插件、看懂失败下一步，并能执行诚实的Windows11最小验收。

**Architecture:** 重用独立业务包审计，导出只有两个生产ZIP的配套artifact；以实际宿主PE元数据和文件哈希建立配对。原生呈现层加入静态安全提示，不改变Core/SDK维护与生命周期算法。

**Tech Stack:** .NET10/WinUI3/WindowsAppSDK1.8.260921001/xUnit/PowerShell7/GitHub WindowsServer2025 CI.

**Spec:** `docs/superpowers/specs/2026-11-07-platform-delivery-hardening-design.md`

## Global Constraints

- Work only in project-workbench on feat/native-plugin-workbench; baseline6915b81, business repositories read-only, original main/work preserved.
- Keep .NET10/win-x64/AppSDK1.8.260921001/API1/runtime1/SDK0.2.0/assembly1.0.0.0; no SDK/Core maintenance algorithm or result-model change.
- Trusted same-process native plugins; no embedded GUI/WebView/sandbox/hot unload/arbitrary forced Stop claims.
- Preserve all original14 native,17 management,7 platform restart and51 business checks; any extra coverage is separately recorded.
- Frozen production DingTalk0.3.2/c0c1d33 and UPCAM0.1.0/6370b9d bytes remain unchanged; acceptance48e67e8 remains test-only.
- No real credentials/provider HTTP/Webhook/messages, actual user/profile edits, business-repo mutation, PR retry, merge or deployment.
- Host failure text must not read exception Message/ToString/GetBaseException or echo arbitrary result/settings/native/provider values.
- Distinguish Linux/reference, Server2025 automatic and Windows11 manual evidence; manual cannot be marked done without real observations.

## Review Focus

- A user chooses the outer artifact ZIP: instructions must identify the exact inner ZIP/folder, without loosening host validation; Task1 verifies real root manifests.
- Package and provenance are tampered together or acceptance is mislabeled production: compare with repository fixed authority, not self-consistency alone; Task1 negative tests.
- A stale host is paired with a newer checkout: derive source from actual PE, and fail strict CI verification on mismatch; Task1 tests old-host/expected-source mismatch.
- Existing or linked output path is supplied: refuse and preserve unrelated bytes; Task1 real filesystem regressions.
- Failure hints imply completed rollback or expose private text: Task2 sensitive-result/exception controls, phase/cancel semantics and actual native fallback check.

---

### Task 1: Audited production-plugin download and actual input path

**Files:** Create `scripts/export-business-plugins.ps1`, focused module if needed `scripts/BusinessPluginDelivery.psm1`, `tests/scripts/BusinessPluginDelivery.Tests.ps1`, `docs/install-business-plugins.md`; modify `scripts/business-plugin-smoke.ps1`, `.github/workflows/build.yml`, README/integration README as needed.

**Consumes:** Current strict `scripts/import-business-plugin.ps1 -InputRoot -DestinationRoot -Mode Production`, immutable `tests/integration/{dingtalk,upcam}/{package.zip,provenance.json}`, actual published `Workbench.App.exe/.dll` metadata/hashes.

**Produces:** `export-business-plugins.ps1 -AppDirectory <default artifacts/native/app> -InputRoot <default tests/integration> -DestinationRoot <default artifacts/native/business-plugins>`; the exact six-file output layout in spec. Add optional `[string]$ProductionInputRoot` to business-plugin-smoke with old tests/integration default; only the production importer uses it. Acceptance keeps its existing source/mode/root separation. Artifact name `workbench-business-plugins`.

- [ ] Add meaningful behavioral RED for missing/tampered/wrong-mode inputs, replaced ZIP+provenance vs repo pins, unsafe/nonempty output and stale host source. Use real source ZIPs/files; local packaging positives may use the already-verified published6915 host metadata, explicitly not a new native run. A minimal no-op scaffold may precede RED; missing-symbol/parse failures alone are not behavioral RED.
- [ ] Implement byte-preserving production-only export, complete hashes/source/version/mode validation, safe staged output and actual PE-derived host stamp. Metadata-only component controls must be labeled as such; do not claim fake host files as an actual WinUI build.
- [ ] Test original output bytes survive refusal, unknown/missing/extra input modes are rejected, and actual copied ZIP SHA/source/provenance/manifest remain exact. Schema1 inventory covers all payloads except manifest itself; paths relative and case-unique. No opaque user secrets or absolute build paths in user manifest.
- [ ] Integrate export/verification after native publish and before existing business matrix. Feed the export directory into `-ProductionInputRoot`; upload artifact only after required validation. Keep all12 business/OS gates and strict original importer.
- [ ] Write user-facing install instructions for this artifact and the already-issued upstream links: outer versus inner ZIPs, FilePicker/FolderPicker navigation, trust, default simulation, new data root, separate approvals and rollback limits. Update relevant entry links; do not claim installer/signature/live tests.
- [ ] Run focused PowerShell packaging/import/report checks and parser validation, not an unchanged full local suite. Commit/report exact signatures, output schema, commands, real RED/GREEN and limitations. Controller obtains current Windows and actual downloaded artifact proof after settled tasks.

### Task 2: Native actionable failure hints and explicit manual/parallel boundaries

**Files:** Create `src/Workbench.App/PackagePresentation.cs` (UI-neutral), relevant focused test file linked in `tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj`; modify `ShellPages.cs`, `PackageMaintenancePage.cs`, `HostFailure.cs` only as needed, `NativeFailureProbe.cs`/`NativeManagementProbe.cs` for actual fallback coverage. Create `docs/windows11-manual.md` and `docs/standalone-compatibility.md`; update docs/packages.md enum and user entry links.

**Consumes:** Core PluginOperationResult.Succeeded/Message (message is not trusted UI output), existing PackageOperationKind/Phase snapshots, HostFailure safe category helper, Task1 concrete download layout/install instructions.

**Produces:** Internal static UI-neutral package presentation keyed by explicit operation context and current result/pending state. Return title/body/success-vs-warning semantics without raw Message. No new Core/SDK enum/property/ABI. Phase labels cover Prepared/BackedUp/OldMoved/NewMoved/Committed/RecoveryPrepared/Quarantined/Recovered/Aborted and safe unknown fallback. Existing Prepared-only Cancel condition stays unchanged.

- [ ] Write actual sensitive-result and hostile-exception RED controls at the shipping presentation boundary, plus meaningful semantic assertions that failed startup/rollback is not labeled successful and started maintenance never offers deletion/cancel. Do not add mirror tests for ordinary copy edits.
- [ ] Implement Chinese next steps for import/update/rollback/removal/cancel/startup outcomes and phase states; leave technical diagnostics queryable. Failed operations must not imply old code was restored. Correct stale homepage copy and use safe theme fallback without raw exception members.
- [ ] Add a real isolated native corrupted-theme startup check showing fallback/native navigation remain usable and stable safe code appears. Preserve all original checks/close ownership. Local reference is explicitly non-runnable.
- [ ] Inspect fixed primary business source for UPCAM shared named-mutex existence protocol and DingTalk canonical state/legacy lock; record exact pinned files and evidence levels. Do not launch old Desktop or touch real canonical data to manufacture proof. Explain supported versions/session/profile limits and remaining actual two-GUI/manual gap.
- [ ] Write Windows11 minimal test sequence and result template/checklist: actual client OS type/x64/24H2+, full unpacking and hashes, chooser cancellation/trust, native pages, Stop/restart, theme/DPI/keyboard/accessibility; no real provider/credentials/send. Explicitly separate smallest smoke from full18-item target and remaining higher-version/legacy GUI tests. Server build26100 alone is not Windows11 proof.
- [ ] Run focused portable boundary tests and affected reference build; commit/report. Controller performs current full Windows workflow, downloads export/app/reports, verifies identities and actual new native check. Task-scoped and final follow-up reviews required; no repeated whole-foundation review.
