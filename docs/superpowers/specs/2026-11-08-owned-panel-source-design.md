# Owned execution panel source stage

This is the functional WinUI source part of the original owned-native Pilot A Task4. The user renewed the request to complete the controlled execution page and continue independent work while CI is blocked, and opted out of ordinary design questions. The choices below are the coordinator's implementation decisions. They do not establish native qualification or permission for user-PC control.

## Goal and boundary

Expose the existing fixed-scenario `OwnedPilotController` in the production `TestingPage`, in the host's current WinUI window. Run, Cancel, Retry, history, events, observed Unicode state and persisted BMP must call the real controller and journal. No placeholder callback, embedded independent application, generic selector, AI-to-action mapping, or production fault option is permitted. The separate repository-owned synthetic Win32 target remains the only controlled process.

Host minimum0.4.0, SDK/API1, Testing package0.2.0, assembly version1.0.0.0, protocol1 and target `owned.win32.task-form.v1` remain unchanged. The production ZIP still has exactly eight files. Source provenance additions and hash-corruption guards are required; payload/version/helper-validation/production-output behavior does not change. Trusted in-process plugins retain host-user authority; this is not a security sandbox or hot-unload feature.

The previous source/virtual coordination stage is complete at producer `9bc28d0fb684fda7a33e250314737c143e75628a`, documentation HEAD `4236bb1dac94366cfe9f38fcd987072fa411e80b`, independent source review PASS/Approved C0I0M0. Consume its real terminal-retirement port and shared activity reservation.

## Approaches and ordering decision

Waiting for all Windows qualification before writing the panel leaves independent controller/UI integration idle during a confirmed account payment/spending startup block. Implementing a generic computer-control engine would expand scope beyond the fixed driver and its evidence contract. The chosen bounded stage implements the functional production panel and portable production ownership composition now, using actual Core/supervisor/controller/journal plus explicitly scripted owned transport, and compiles the whole App/Testing reference source.

This changes source implementation order only. Original Task3's five-round cap and unknown historical CLR abort remain load-bearing runtime gaps. All27 current native checks must pass before any actual installed-panel/runtime/release acceptance. No sixth speculative native fix is admitted. Actual Windows UI, helper/UIA/GDI/job/IO, current business51/12 PID+birth instances, SDK/final delivery, crash/restart, upgrade/rollback and download-byte audits remain open; Windows11 human acceptance stays deferred. Preserve original reports, failed outputs, .sdd workspaces and all four deferred whole-owned-phase review items.

## Production composition and admission

Capture `OwnedScope` once from the actual project identity, plugin ID and DataDirectory. Construct the production `Win32OwnedDriverFactory(scope, TimeProvider.System)` and real pilot, activity bridge and lifetime composition before the first initialization await. ReferenceOnly remains unavailable and refuses before preparation/Queued/launch; it must not produce a fake helper/ZIP. Partial initialization can be stopped without requiring an initialized journal or field.

Keep one actual `NativeActivityCoordinator` reservation across source, remote source, virtual starts and owned starts. A live/queued/cancel-requested virtual attempt refuses an owned start before owned preparation and without altering virtual selection/error/journal. A terminal virtual attempt is joined through actual existing retirement, including finite Stop retries and callback/postfix work, before owned preparation. Selecting old terminal history does not authorize a start. Source/virtual starts similarly refuse throughout owned work and final notification. No cancellation or implicit wait is used to displace live virtual work.

Install a distinct owned `NativeRemoteOperation` entry and its existing single CTS before scenario/retry capture or handoff await; do not reuse the source operation. Capture scenario or parent run ID synchronously and freeze it. Cancel during handoff still joins prior actual retirement, then prevents all owned preparation, Queued bytes, launch and actions. The cancel-and-drain port captures the exact current entry synchronously and joins its actual completion, cancellation callbacks and settlement; a late cancel must never consult/cancel a newer `_active` entry. Existing source request-only cancellation semantics remain unchanged. Original action failures remain with their invocation; cancellation failure is reported only after required joining.

Informational IsBusy=false is not authorization. Controls project actual reservation availability. A fresh, owner-retained render after reservation release restores controls for both page and command paths without writing source/owned status from a late continuation. Temporary leave reopening may ignore only the exact still-held lifecycle seals, when no active reservation/permanent terminal/nested barrier exists; evaluate this on the actual dispatcher. Methods continue to reserve atomically and refuse competing invocations.

## Production lifetime orchestration

The actual plugin hooks and portable tests use the same small fixed-dependency lifecycle composition, not a generic lifecycle engine or a test that recreates plugin ordering. It owns no new runner/journal algorithm. Seal owner, activity, source operation, owned operation and pilot synchronously before requesting cancellation. Begin owned entry and real pilot cancellation before owner-wide drain, so an accepted Run cannot deadlock behind its own uncancelled work. Continue every required cleanup after a throwing callback; preserve the first real failure and observe later faults without duplicated failure/status writes.

Leave remains temporary. Retain seals through actual pending handoff/preparation/storage/driver/callback work, virtual preparation, pilot notifications, owner-tracked dispatcher/frame decoding, and the final reopening UI tail. Pilot `SealAdmission.Dispose` can publish Changed asynchronously: expose only its existing notification drain internally, then retain other seals while joining release-generated notifications and their owner UI work. Owner.Drain alone cannot join a callback that has not yet run. No owner/activity self-drain from inside an admitted action.

Stop/lifetime is permanent. The lifetime callback synchronously seals/revokes pilot authority along with other admissions; it never starts detached Stop work. Stop joins both controllers and all accepted work even when one cancellation/cleanup faults. Retire/unsubscribe page bridges and join accepted dispatcher/BMP tails before final pilot Stop releases its journal/owner.lock. Same-project Unloaded only detaches subscriptions and advances page generation; it neither cancels/stops the pilot nor forgets accepted IO. No background registration, automatic resume/replay, rollback, exactly-once or inferred OS retirement is added.

## Native page and evidence

Add `TestingOwnedPilotPanel` inside the current production page. Required primary copy is exact:

- Header: `受控 Windows 试验 · 仅操作自带的合成任务窗口`
- Retry: `使用当前已安装的驱动，从第 1 步开始新执行`
- RecoveryNeeded: `执行记录或资源状态尚未确认，已暂停新增执行并保留原始数据。当前版本暂不支持修复或重试。`

Set AutomationIds `owned-pilot-{scenario,run,cancel,retry,history,events,state,frame,status}`. Offer the three existing fixed scenarios. Fixed owned execution does not flush source drafts or create source/AI mappings. Keep Cancel and owned history usable during active work. Retry only verified Passed/Failed/Cancelled or clean Interrupted using a fresh run/current installed helper; RecoveryNeeded/read-only/live history refuses. Show waiting-for-prior-retirement distinctly from an already-created run, without inventing journal events.

Render actual summaries, selected execution/input/parent identity, events, safe errors and raw state. Retain one coherent displayed run/event projection. Resolve evidence only within that projection, namespace keys by owned/run/sequence/frame hash, invalidate immediately on selection/generation change, and guard both before and after actual `BitmapImage.SetSourceAsync`. Read frames through `OwnedPilotController.ReadFrameAsync`; reuse request caching, not the virtual-only event model. Late A success/failure cannot replace B, a reselection, or a reloaded generation. Retain actual journal read, stream/bitmap decoding and dispatcher tails through NativeActionOwner. Portable tests prove selection/cache policy and actual persisted bytes, never WinUI decoding.

Scope the old all-SIMULATED introduction to the virtual section. OwnedWindowPrintWindow is an actual capture of the bundled synthetic target; it is not generic user-desktop capture. Scripted AcceptanceFixture transport in tests remains explicitly identified as synthetic evidence. Keep raw Unicode state separate from limited screenshot glyphs and optional technical diagnostics separate from the primary UI.

## Verification and remaining work

Meaningful behavioral RED and GREEN use linked production bridge/lifetime/evidence helpers, actual `TestingDomainFixture` Core/supervisor and held retirement, actual `OwnedLabTestRig` controller/journal and scripted driver, deterministic callback/UI gates, and exact persisted frames. Cover live refusal/current-attempt selection, pending cancellation, throwing cancellation callbacks, held preparation/launch/retirement/final idle, release-generated Changed tails, permanent lifetime closure, partial initialization and stale evidence. A compile error or test-only copy of lifecycle ordering is insufficient.

After final code: one full portable Core suite (including the real90second deadline case, no60second hang cutoff), focused old/new ownership suites, whole App/Testing managed ReferenceOnly build, actual source-provenance/hash-refusal guards and PowerShell parser checks. Preserve argv, exits, raw logs, RED corrections, exact tested source hashes and local producers. No redundant full suite after documentation-only changes.

SourceServices injection/compiled SourceAcceptance, actual synthetic Windows DPAPI fixture, installed panel probes, deterministic clean production two-host crash after committed StepStarted, upgrade0.1→0.2/rollback and the final whole-owned-phase review remain separate unfinished tasks. Neither a fixture crash nor a synthetic journal seed substitutes for exact production crash evidence. No actual Windows or final delivery gate closes in this Linux stage.

No real credentials, Figma/OpenAI calls, Webhook, DingTalk messages, third-party messaging, user-PC access, artifact deletion, billing changes, 403 bypass, denied PR retry, merge/deploy/release, or binary carrier is authorized. Confirmed current CI startup error is recent account payments failed OR spending limit; exact account alternative/headroom is unknown. No rerun/push without new remedy evidence; independent local development continues.
