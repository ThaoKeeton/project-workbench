# Task 5 source-stage report — independent native testing plugin

SOURCE implementation locally verified and released for the controller's fresh task review. Task 5 overall remains OPEN: current-source Windows execution/full CI, current downloads and final downloaded-byte audit are root-owned after task review and ONE aggregate source gate. No push, PR, merge, deployment, helper or reviewer was dispatched by this worker.

Approved starting HEAD: `0979dae961759a5a36caefbd31d3fa1664b4be39`, branch `feat/native-plugin-workbench`, initially clean. Sole source writer: delegated `testing_native_plugin`. Product/source evidence is bound by `/tmp/workbench-testing-task5-20261107/source-manifest-final.json` (38 changed/new product/doc/test files, SHA256 and length). This report is the additional owned plan/report file. The final source commit is the Git commit containing this report; its exact hash and clean-tree result are emitted in the worker handoff/commit receipt. No self-referential package/source-commit claim is made.

## Delivered source

- `plugins/Workbench.Testing/Workbench.Testing.csproj`, `plugin.json`, `TestingPlugin.cs`: independently loadable `com.projectworkbench.testing` 0.1.0, minimumHostVersion 0.4.0, API 1; entry `Workbench.Testing.TestingPlugin`, assembly `Workbench.Testing.dll`. Registers one Chinese `WinUiView("testing", "测试设计", CreatePage)` and ONE before-leave hook. Awaits the real controller initialization before contribution Commit. Optional `IPluginTestingContext` is required; no existing interface or view factory ABI changes.
- `TestingPage.cs`, `.Rendering.cs`, `.Pickers.cs`: code-created WinUI UserControl/ScrollViewer, native lists/editors/actions in the existing host window, committed journal/step/actual text and BMP Image. No plugin XAML, WebView, standalone window or embedded application. No remote/provider/device placeholders.
- `NativeActionOwner.cs`: actual user/picker/import/export/dispatcher callback ownership published before each synchronous prefix; temporary preparation seals, terminal admission, actual drain. `NativeRunAdmission.cs`: remembers the actual returned owned run ID, so selecting terminal history A cannot unlock active B. A terminal journal can offer the next Run; the existing controller Queue/Retry joins retained retirement before reuse. There is NO entry-owned long settlement waiter and NO new wait command blocking the first UI drain.
- `NativeTestingFiles.cs`: same bounded read-only local-file import for bundled/picked files and same atomic CSV writer for native picker/registered local export. Links refused, temporary files owned and cleaned, old destination preserved on failed/cancelled publication. Imported original bytes are not rewritten.
- `src/Workbench.App/TestingNativeProbe.cs`, `.Authoring.cs`, `.Execution.cs`: actual shipping-EXE opt-in `--testing-probe seed|verify`, normal workspace coordinator. Installs the actual separate ZIP through `PluginPackageStore`; operates actual registered page/commands/controls and public Core/SDK store only. The host App project and probe sources have NO static testing entry/domain reference. `RunOptions` adds mode conflict/value/root guards and IsIsolated membership; `MainWindow` adds only normal workspace preparation/dispatch.
- `scripts/package-testing-plugin.ps1`, `TestingPluginDelivery.psm1`: separate installable artifact, exact ZIP allow-list/hash/CRC/manifest/source auditing, honest ReferenceOnly identity and clean native-host/source pairing. Actual Windows package provenance includes real host EXE/DLL SHA256 and PE-declared source/product/file/assembly identity. ReferenceOnly carries host=null and does not claim host pairing.
- `TestingPluginProbe.psm1`, `testing-plugin-probe.ps1`: real EXE seed/verify runner, required checks, native PID/Closed/OS-exit/root-lease/package identity gates, raw evidence receipts and immutable per-phase `.runner.json` sidecars. Native reports stay unmodified. Failure stops successors; a phase cannot be completed twice. Each invocation preserves a fresh `artifacts/native/testing-probes/<UTC-GUID>` output. A timed-out process remains owned/reported, not silently treated as exited or killed as success.
- Tests: Core `TestingNativeOwnershipTests` (4) and `TestingNativeFileTests` (2), CLI additions; actual `Workbench.Testing.PackageProbe` package-store installation utility; package and report/phase script regressions. NativeReference builds the independent plugin through ReferenceOutputAssembly=false; App does not reference it. Solution includes its independent project.
- Build/workflow: Windows native build packages testing outside `app/Plugins` and outside the production companion. New separate artifacts `workbench-testing-plugin-0.1.0` and `testing-plugin-probes`; package/install/script and actual EXE gates added. Existing native14, management17+theme1, restart7, workspace16+3, business27+24 and actual12-process gates remain intact, as does the frozen six-file production companion.
- Docs: `docs/testing-plugin.md`, README, testing-services, plugin-sdk, integrations/plugin-contract-v1 and windows-verification updated. Deferred T3-M1 resolved in BOTH `src/Workbench.Sdk/README.md` and `src/Workbench.WinUI.Sdk/README.md`; stale unwired/future-run text replaced by actual source/case/run/evidence/context/controller/native-entry capabilities and honest pending runtime/provider/device/human gates. SDK source allow-list remains eleven files, two NuGets, two packaged READMEs, testingServices1; Windows native PRI remains required.

Core, portable domain/controller, existing SDK declarations/version constants and all frozen `tests/integration` payloads/provenance are unchanged. No business repository was edited. No API/binding expansion was needed.

## Native controls, commands and defaults

Page title `测试设计`. Default normal startup still has only the two existing bundled example packages; testing is separately installed/trusted/enabled. Single-project shell remains clean. Sources start empty and new project missing-store reads create no testing storage. No run/resume starts automatically. All execution targets are explicitly `virtual.task-form.v1`, all bitmap panels visibly `SIMULATED`.

Stable automation/control names: `导入本地需求 JSON`, `导入本地 Figma JSON`, `导入打包需求示例`, `导入打包 Figma 示例`, `测试来源列表`, `生成测试用例`, `测试用例集`, `测试用例集名称`, `测试自动保存状态`, `测试用例列表`, `测试用例标题`, `测试前置条件`, `测试步骤选择`, `测试步骤说明`, `测试预期结果`, `测试映射与追溯`, `重试测试草稿`, `保留测试草稿并重新加载`, `导出测试 CSV`, `运行选中测试用例`, `取消测试执行`, `重试测试执行`, `测试执行列表`, `测试执行事件`, `测试执行详情`, `测试实际观察文本`, `SIMULATED 测试帧`, `测试帧状态`.

Useful registered no-argument commands (original ABI unchanged):

| ID | Actual operation |
| --- | --- |
| import-example-requirements | Opens real packaged Fixtures/requirements.synthetic.json through bounded file IO/parser; deliberately includes labeled failure/manual example |
| import-example-figma | Opens real packaged Fixtures/figma.synthetic.json through same path |
| generate-all | Generates from all current local source IDs; selects new draft |
| export-local-csv | Flushes accepted draft; writes data/com.projectworkbench.testing/testing-cases.csv through actual shared writer |
| queue-mapped | Queues current set's wholly mapped cases, preserving actual returned attempt ID |
| start-queued | Explicitly starts existing queued run |
| cancel-run | Requests cancellation and joins actual work/retirement |
| retry-selected | New linked run/session from selected parent's exact frozen cases, starting at step 1 |
| refresh | Reads committed project source/case/run records |

Ordinary native Run queues and starts in ONE click. Cancel uses a separate action path and remains independently usable while Run/IO is owned. Command success means its local operation/start request completed; the native message explicitly directs users to committed journal state for run results.

Native import/export pickers construct `Microsoft.Windows.Storage.Pickers.FileOpenPicker/FileSavePicker` from the loaded page's actual `XamlRoot.ContentIslandEnvironment.AppWindowId`, pinned Windows App SDK 1.8.260921001. Native file selection allows `.json`; export offers `.csv`. The ORIGINAL picker operation is retained until actual completion, with no cancellation wrapper pretending a dialog has settled. Human picker execution is NOT locally validated.

## Lifecycle, draft and IO recovery

Every admitted callback is retained before its synchronous prefix. TextChanging synchronously reads complete actual TextBox.Text for the pair of instruction/expected or title/prerequisites, then calls controller Capture. There is no MaxLength clipping, no off-thread control read, no async capture after the input has changed. Programmatic render updates are guarded. Queued notification work rereads the LATEST controller Snapshot, not the old supplied notification, and checks active page generation before applying async results. TextBox instances stay stable; full source text/actual observed text are separate from bounded BMP glyphs.

Entry ONE prepare hook seals new user admission, joins actual admitted UI/picker/file/dispatch callbacks, invokes controller.PrepareLeave while testing services are open, then joins controller-produced actual dispatcher work. Final prospective editable render and dispatcher drain happen WHILE the private seal is still held; final disposal/release is synchronous with no pending async tail. Other global participant failure leaves this same page accurate/editable. No handler waits for its own drain. Unloaded retires subscriptions/generation and removes the active page; it does not terminally stop the project controller or abandon accepted picker/file work. Lifetime cancellation immediately terminal-seals admission. Stop joins entry work, existing controller Stop and actual dispatcher retirement; it tolerates partial initialization and starts no fresh save after context Close.

Autosave coalesces versions through the approved controller/session. Failed or conflicting writes retain exact dirty input/model and block departure/run/export. Retry Draft only appears after an actual failure; preserve-copy/reload only appears for RevisionConflict. Editing step text permanently removes mapping; metadata preserves mapping. No ordinary Save/activation checkbox. The next Run after a durable terminal uses the controller's retained actual retirement, not selected-history state or a synthesized progress label.

Read-only bounded source IO uses existing TestSourceInput.ReadAsync, at most 1 MiB plus detection byte. Names, locators and source IDs never construct storage paths. Bundled native actions read actual packaged JSON; missing/corrupt files are surfaced, never replaced with embedded bytes. The package auditor verifies those fixture hashes against source. CSV picker and registered local export share controller.ExportCsvAsync and NativeTestingFiles.WriteAsync: UTF8 BOM, CRLF, quoted cells/internal escaping, one apostrophe on every text column (including IDs/revisions/locators), only validated sequence columns numeric. Original store/source text remains exact. No spreadsheet resave guarantee or live Excel execution.

Evidence UI reads committed NativePlainTextState and actual evidence receipt/bytes, never reconstructs from planned input or replays an adapter. It clears the prior Image before loading a new event. BitmapImage.SetSourceAsync provides actual decoder completion; native probes compare pixel dimensions and the selected receipt hash, preventing a stale frame from satisfying a new event. State remains fully readable even when the SIMULATED font omits CRLF/tab/astral glyphs.

## Implemented Windows acceptance (not executed locally)

`scripts/testing-plugin-probe.ps1 -TimeoutSeconds 240` runs actual `artifacts/native/app/Workbench.App.exe --testing-probe seed|verify --probe-root <fresh TEMP/ProjectWorkbench-testing-probe/GUID> --smoke-report <phase.json>`. Dedicated root guards reject normal/other probe roots or mixed modes. The ZIP is the sibling `artifacts/native/testing-plugin/workbench-testing-plugin-0.1.0.zip`, paired with actual same-source host metadata before launching. The script records actual Server OS/architecture, PIDs, Closed, exit, reacquired root leases, package hash and evidence receipts; root-owned Windows CI must supply those observations.

Seed has 14 required check names: independent-zip-native-page; packaged-requirements-figma-io; traceable-manual-mapped-generation; immediate-metadata-csv-bytes; immediate-step-mapping-invalidation; actual-file-share-save-failure-close-correction; conflict-preserve-copy-reload-bytes; positive-actual-journal-bmp-native-decoder; negative-empty-title-run; labeled-deliberate-assertion-failure; cancel-real-owned-store-io-before-first-step; native-project-isolation-empty-read; synthetic-inflight-journal-seed-no-action; immediate-final-input-before-leave.

The save-failure check holds the real case file with Windows FileShare.Read, preserving reads while denying replace. Native edit fails autosave, normal MainWindow Close refuses before detach/Stop, exact bytes and SAME control/controller/manager generation remain, then releasing the real handle and editing the SAME control recovers and exports. Conflict uses a real external CAS update, preserves external bytes, edits native text to a retained dirty conflict, then invokes normal conditional preserve/reload and verifies the new copy and original bytes.

Positive and empty-title runs actually execute the approved typed virtual adapter; labeled failure must journal AssertionFailed and no Completed. Simulated BMP header/receipt/hash and actual Image decoder are checked. Cancel invokes the real native Cancel button plus the useful cancel command joining the same actual work while the actual testing-store lease is held. It demonstrates cancel-before-first-step during REAL store IO, NOT interrupting a desktop action. Portable Core runner tests separately cover durable StepStarted before gated typed adapter work and actual action/disposal joins.

Actual native New dialog/project selector create B, install the same ZIP under B and prove no copied sources/cases/runs or storage creation on missing reads; switch back retains A. AFTER the last context switch, public Core APIs create an explicitly SYNTHETIC inflight journal seed with RunStarted+StepStarted but NO action or StepFinished. The normal plugin has no owned runner for that synthetic record. Normal Close is not called a crash. Both phase reports carry syntheticInterruptionSeed=true. Final real TextBox accepted input immediately invokes normal Close without yielding or waiting for autosave; state records actual accepted control text. Seed's copied evidence predates that final input by design; verify reads the post-Close actual records.

Verify requires a different actual process ID and has five required check names: separate-process-context-interrupted-synthetic-seed; restart-final-native-input-persisted; reopened-exact-observed-text-bmp-native-decoder; linked-new-session-retry-frozen-step-one; restart-project-roster-preserved. The new actual plugin context initializer marks the seed Interrupted; it reopens exact committed multiline/astral observed text, receipt and BMP, then actual normal native Retry creates new run/session from the parent's frozen revision, beginning with step 1. No adapter replay derives the old result.

Raw project test-store event/head/receipt/BMP bytes and actual CSV are copied only from the owned synthetic root, refusing links before recursive enumeration and bounding files. Per-file SHA256/length are verified by the runner. Separate native phase report and CreateNew runner sidecar preserve original evidence/first rejection, actual native PID and collector/exit/lease observations. Successor phases are never launched after a failed phase. Distinct process/PID gates are separate from assertion counts.

## Actual local verification and RED qualifications

All final commands used:

```sh
DOTNET_ROOT=/workspace/.tools/dotnet
DOTNET_CLI_HOME=/workspace/.cache/dotnet-home
NUGET_PACKAGES=/workspace/.cache/nuget
XDG_CACHE_HOME=/workspace/.cache
XDG_CONFIG_HOME=/workspace/.config
XDG_DATA_HOME=/workspace/.local/share
# Also PATH=/workspace/.tools/dotnet:$PATH for every PowerShell command invoking dotnet.
# Tool additional filesystem write permission /home/agent/.dotnet supplied for every dotnet execution.
```

Actual tools/environment: SDK 10.0.403; Linux 6.18.57 x86_64; workspace PowerShell `/workspace/.tools/powershell/pwsh`. HOME was not changed. No credentials/environment dumps, proxy/TLS bypass or external provider calls. One exploratory PowerShell type-reflection command omitted XDG and failed on read-only `/home/agent/.cache/powershell`; rerunning with the required workspace environment returned the actual ZipArchiveEntry.Crc32/IsEncrypted properties. No data/action ran from that failed exploratory command.

All raw logs/TRX/artifacts are under `/tmp/workbench-testing-task5-20261107`. `raw-log-receipts.json` inventories log hashes; `source-manifest-final.json` captures final source bytes and approved BASE; `delivery-audit-final.json` records actual SDK/testing metadata and independent CRC/extracted-byte auditing.

| Actual command (environment above) | Exit / observed result | Raw evidence |
| --- | --- | --- |
| dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj -c Release --nologo --filter FullyQualifiedName~RunOptionsTests (before CLI implementation) | RED: 6 failures, 28 passed, 34 total; unknown/empty testing modes fell through and testing root was not isolated | cli-red.log |
| Focused CLI+owner tests after implementation | GREEN: 37/37; later with terminal-owned-ID admission regression 38/38 | cli-owner-green.log; native-helper-focused.log |
| Focused FullyQualifiedName~TestingNative (ownership/admission/file IO) | GREEN: 6/6 | native-files-focused.log |
| dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj -c Release --nologo --logger trx;LogFileName=task5-core-final.trx --results-directory /tmp/workbench-testing-task5-20261107/trx | EXIT 0, full completed Task5 combined source ONCE: 677 passed, 2 explicit Windows-only DPAPI skips, 0 failed, 679 total; 1m3s | core-final.log; trx/task5-core-final.trx |
| dotnet build tests/Workbench.NativeReference/Workbench.NativeReference.csproj -c Release --nologo -p:UseWinUI=false -p:AppxGeneratePriEnabled=false -p:WindowsAppSDKSelfContained=false | EXIT 0; actual independent entry/page/pickers and actual shipping App probe API compile; 0 warnings, 0 errors | native-reference-final.log |
| pwsh -NoProfile -File scripts/package-testing-plugin.ps1 -ReferenceOnly -OutputDirectory /tmp/workbench-testing-task5-20261107/delivery-source-final | EXIT 0; actual ZIP/source/manifest/hash/CRC checks, 0 build warnings/errors | package-final.log; delivery-source-final/ |
| pwsh -NoProfile -File tests/scripts/TestingPluginPackage.Tests.ps1 -Directory /tmp/workbench-testing-task5-20261107/delivery-source-final -AllowReferenceOnly | EXIT 0; 8 negative tamper/allow-list/identity/ReferenceOnly cases plus actual delivery positive | package-script-final.log |
| dotnet run --project tests/Workbench.Testing.PackageProbe/Workbench.Testing.PackageProbe.csproj -c Release -- /tmp/workbench-testing-task5-20261107/delivery-source-final/workbench-testing-plugin-0.1.0.zip | EXIT 0; real package-store install, all six extracted files byte-identical, disabled/untrusted/no load discovery, duplicate overwrite refused | package-install-final.log |
| pwsh -NoProfile -File tests/scripts/TestingPluginProbe.Tests.ps1 | EXIT 0; six initial failure modes, accepted two-phase synthetic metadata/hashed files, corrupt receipt, duplicate check, reused PID and once-only/no native-report overwrite | probe-script-final.log |
| pwsh -NoProfile -File scripts/export-sdk.ps1 -ReferenceOnly | EXIT 0; two actual 0.4.0 NuGets from final eleven-source snapshot | sdk-export-final.log |
| pwsh -NoProfile -File tests/scripts/SdkSnapshot.Tests.ps1 -SnapshotRoot artifacts/sdk-reference-snapshot | EXIT 0; eleven allow-listed source hashes, two NuGet hashes, both packaged READMEs, Testing services1, stable ABI/file versions and source tamper rejection; native PRI explicitly inapplicable to ReferenceOnly | sdk-snapshot-final.log |
| PowerShell Parser.ParseFile on six new testing scripts/modules/test files | EXIT 0; no parse errors | script-parse-final.log |
| Independent Python ZIP testzip/extracted SHA/length/sourceFiles audit; YAML parse; App dependency/frozen-input source checks; git diff --check | EXIT 0; actual local ZIP CRC/extracted bytes and all recorded source-file hashes match; App has no static domain/entry reference; Core/domain/frozen inputs unchanged; workflow jobs preserved; whitespace clean | delivery-audit-final.json; source-manifest-final.json; final worker tool/commit receipts |

Only CLI RED above is a new observed behavioral RED. `owner-missing-red.log`, `package-missing-red.log` and `probe-script-missing-red.log` are missing-feature/source/module compilation/loading failures before implementation, NOT claimed as behavioral regressions. Focused helper/script tests thereafter exercise real ownership/file/package/runner behavior. Native reference iterations also caught and corrected sealed ScrollViewer inheritance, a discard-identifier collision, local name shadowing and C# Span/await ordering. Final corrected compile is the sole completion claim. No Windows process, native picker, native runtime/PRI, real device or provider was executed locally.

## Local artifact identity (limited reference build)

Actual final local ZIP: `/tmp/workbench-testing-task5-20261107/delivery-source-final/workbench-testing-plugin-0.1.0.zip`, **85016 bytes**, SHA256 **198109a7a990cca66e4d55aa16719263796cdc9137e9024d49a34391e358037e**. Metadata explicitly records sourceCommit=approved BASE, sourceTreeDirty=true, referenceOnly=true, host=null, reflecting the pre-commit edited source under test. This is local source evidence, NOT a current clean-HEAD Windows delivery or a downloaded CI artifact. Root must regenerate current clean-HEAD outputs after source approval; the scripts enforce that for non-reference builds.

| ZIP path | Bytes | SHA256 |
| --- | ---: | --- |
| Workbench.Testing.dll | 69120 | 8e696e366a63fba3af6892be3312a9cca0f052dd9f0af91f463d29d0683dcef8 |
| Workbench.Testing.Domain.dll | 120832 | c9b59f734a016e0239b6457fe528dc62756950a91507462c2ae5990b28b69b90 |
| Workbench.Testing.deps.json | 15854 | 9e219d254acd1d2ee97fc9c352a72cdd1e6fcbda678c24f85a8488839892bd7f |
| plugin.json | 487 | d8f8e50feadb1e3bb6713983f7fac67edc558513853e7eacd757b29576303621 |
| Fixtures/requirements.synthetic.json | 1186 | 599ac17bb284df9fc30a6d35e6d582a62377fe39fefa2699e5ad5bf0a74a5be2 |
| Fixtures/figma.synthetic.json | 680 | 73076a1461d63d505d8a49fb5831614f9d7344da780a4fbf7b664cd6c7aa3663 |

Final local SDK `artifacts/sdk-reference-snapshot` records 0.4.0, file0.4.0.0, assembly1.0.0.0/API1/runtime1/project1/testing1, eleven source records/two NuGets/two consumer READMEs, sourceCommit=BASE, sdkSourcesDirty=true, referenceOnly=true. It has no Windows PRI claim. Both consumer README payloads were actually exported and validated.

## Self-review and remaining gates

Self-review checked complete brief and actual bindings, owner admission/synchronous prefix/dispatcher/error postfixes, no UI self-drain, final temporary seal, terminal lifetime and partial initialization, history-vs-owned attempt, terminal-before-supervisor retirement, stale image generation/hash, source bounded IO, original/picker/export ownership, error/conflict same-model preservation, synthetic seed after last context switch, immediate native Close ordering, phase exactly-once/native-report preservation, CRC/path/ZIP file allow-list, source/host pairing, both SDK README packaging and frozen input/domain/Core nonmutation. No new Core/SDK/lifecycle/security boundary was invented. No fake adapter delays, shipping test mode/getContext or arbitrary action/OS control was added.

Remaining controlled gates: fresh Task5 source review, ONE aggregate source review/fixwave, authorized normal feature-branch push by root, current Linux/Windows full CI and all old+new actual EXE gates, exact-current-source host/SDK/testing/production-companion download pairing, API digest/length/CRC/safe extracted bytes audit and final receipts. None is reported complete here. Windows11 human picker/IME/DPI/accessibility/real-device validation and live remote Figma/AI/provider/HTTP/Webhook/message/business dispatch remain deferred/unexecuted. ALC trusted in-process execution is not a sandbox. CSV text values intentionally change; no exactly-once/recovery-from-checkpoint or real desktop screenshot claim.

Writer release: after this committed source-stage handoff and clean status receipt, this worker releases the product writer. No outstanding source implementation blocker; actual Windows/remote/download status remains pending root-owned execution under the approved phase contract.
