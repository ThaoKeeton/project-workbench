# Traceable native testing extension — design

Accepted foundation: `63e481c5c97174b51ca7baf41043a8a863508d7a`, exact full Windows/Linux workflow42358163726 and eight actual-download audits complete. Foundation closure and explicit unexecuted gaps are recorded at `/tmp/project-workbench-project-workspaces-completed-63e481c.md`. This document specifies the NEXT implementation; its contents do not claim that testing capabilities already exist. The user explicitly delegates ordinary decisions and asks continuous development without approval questions. Builder check of prior proposal/resolutions found zero required product questions. The technical choices below are controller decisions, not individually user-approved details.

## Outcome and constraints

Install independent plugin com.projectworkbench.testing version0.1.0 into the actual native WinUI host. Native same-window page imports deliberate local requirement/Figma snapshots or bundled synthetic examples, generates traceable rule-based cases, edits/autosaves cases, exports actual UTF8 CSV, and runs mapped steps against named virtual.task-form.v1. Show committed run/step/outcome/error/passive simulated-frame evidence, cancel actual owned execution and explicitly retry interrupted/failed work as a new linked attempt. Projects isolate all test records while default single-project host UI remains simple. Old installed business plugins continue working unchanged.

No embedded independent appGUI, pluginXAML, WebView, liveAI/Figma/provider/HTTP/Webhook/messages/realcredentials or real-user-computer control. Manual/unmapped steps are not automatic passes. All frames visibly SIMULATED, never real desktop screenshots. ALC/trusted inprocess code is not a sandbox. No forcedStop/hotunload/exactlyonce/undo/powerloss transaction or unlimited drafthistory claim. WindowsServer automation and deferred Windows11 human acceptance stay separate.

Approaches considered: private one-plugin storage is fast but not a useful platform boundary; a general workflow/live-provider callback router is too large and needs double-lifetime dispatch. Choose focused optional SDK testing service with host-owned project records and actually used provider/generator/adapter interfaces. Shared data is callable by future plugins; live provider registry/routing remains unimplemented.

## Version and structure

App/mainSDK/WinUISDK preview0.4.0, fileversion0.4.0.0; assembly1.0.0.0/API1/runtime1/project1 unchanged. Add TestingContracts.cs to mainSDK and optional testingServices1. IPluginTestingContext extends IPluginProjectContext with Testing:IProjectTestWorkspace only; do not change existing interface members or WinUiView factorysignature. New testing plugin minimumHost0.4.0; existing project-only examples may retain0.3 and immutable business packages retain0.2. Update all actual SDK sourceallowlist (11sources), NuGet/nuspec/README/PRI/artifact/version/provenance checks coherently.

Host-owned ProjectTestStore under ProjectRuntimeScope, wrapped by captured-plugin testing service. Portable plugin-domain assembly provides builtin parsers/generator/CSV/virtual model/runner; native entry assembly provides WinUI controls and explicit localpickers. Independent package includes its own domain/entry/deps/manifest, never hostSDK/Core/desktop businessDLLs. Existing samples remain honestly marked examples; the new plugin is a real separately installed package. No businessrepo change or frozeninput rewrite.

## Public service behavior

IProjectTestWorkspace supplies PublishSource/ListSources/ReadSource; CreateCaseSet/ListCaseSets/ReadCaseSet/UpdateCaseSet(expectedRevision)/ReadCaseRevision; CreateRun(expectedCaseRevision,selectedCaseIds)/CreateRetryRun(parentRunId)/ListRuns/ReadRun; AppendRunEvent(expectedSequence,expectedHeadHash); AddRunEvidence/ReadRunEvidence. Builder may choose exact record names without changing these behaviors. All async methods accept CancellationToken; cancellation never implies rollback. Identity/project/producer/session/managed recordIDs/timestamps are supplied by host, not accepted as caller authority. Reads can access published active-project records; case updates, run events/evidence/retry are owner-only. Forking another author's set creates a new owned set preserving exact source refs.

Reusable ITestSourceProvider parses bytes into common items; ITestCaseGenerator generates a new draft from immutable snapshots; IControlledTestAdapter opens a specific target/session and executes typed bounded actions. These interfaces are implemented and called by the real plugin. No unused future-reservation-only adapter shape. Initially host validates only virtual.task-form.v1 and its fixed element/action set; unknown/real targets refuse. Future realdevice support needs a new actual scoped policy/adapter and verification, not a capability string pretending permission.

Each service call publishes actual operationownership before scheduling any IO/enumeration/plugin synchronous prefix. TerminalClose revokes new admission immediately, retains/drains admitted tasks before context/root release. Execute callback work offdispatcher and no plugin delegates under Core/coordinator locks. The service's once-per-new-context initialization barrier classifies only that plugin owner's old valid nonterminal runs Interrupted before ANY new public action; all admitted calls await the same retained barrier. Missing store creates nothing on read. Other owners' live runs are never marked. This replaces a dangerous public Recover method that could race a current run. The plugin explicitly awaits initialization/ListRuns before contributing its page/background task.

Use fixed allowlisted sealed errors such as TestingContextClosed, InvalidTestSource, InvalidCaseSet, InvalidRun, InvalidEvidence, RevisionConflict, StoreLimitExceeded, StoreIoFailure, UnsafePath, TestStoreCorrupt, RecoveryRequired, ManualStepUnsupported, TargetUnavailable, AssertionFailed, AdapterFailed. Unknown public errorcodes map to generic TestingFailed; never log raw exception text/provider content. User case/source text is intended native plain text, separate from diagnostics.

## Storage, caps and schema

Root .workbench-project/testing, strict store schema1 with captured projectId. Managed32lowerhexGUID IDs and64lowerhex hashes; names/locators/sourcepaths never form filepaths. Immutable source envelopes; currentCAS case documents plus run-referenced immutable case revision archives; immutable runplans and append-only hashchained events; bounded passive evidence bytes. Reuse guarded regularfile/linkrefusal/owned staging/exclusive lease/flush/atomic publication patterns. Preserve corrupt/unsupported/unknownfield/duplicatefield/invalidUnicode/orphan data and safely refuse; never blank-reset. These are cooperating ownership defenses, not protection from malicious trusted code or all TOCTOU races.

Concrete caps: originalsource1MiB, sourceitems256, criteria16peritem, title200UTF16chars, description/criterion4096chars, originalencoded source envelope4MiB;64source snapshots perproject. Case set200cases/20stepspercase, steptext4096chars, encodedcase envelope4MiB;128sets. Executable run atmost200totalsteps;256runs perproject; event256KiB/max512events; evidence one512KiB/max128MiBperrun/max256receiptsperrun. Limit errors preserve dirty data and all existing referenced records. No auto-pruning. Count/encodedbyte/Unicode limits are validated beforepublication and again onload. No benchmarkclaim for maximum-size UI until measured.

Sources persist originalbytes+SHA, kind/provider/version/documentidentity/title, common item locators and criteria plus exact immutable snapshot revision computed from storedbytes. OriginalUTF8 may have a singleBOM; parse without it but preserve/hash exact originalbytes. Local requirementJSON schema1 has schemaVersion/documentId/title/requirements; each requirement id/title/description/acceptanceCriteria/virtualScenario(null or fixed taskform scenario). Figma snapshotJSON has schemaVersion/documentId/title/nodes; each node id/name/description/acceptanceCriteria/virtualScenario. Builtins reject unknown fields, duplicateIDs/properties, invalidUTF8/Unicode, count/byte violations; no remoteURI interpretation. A trusted thirdparty provider's declared mapping is not cryptographic proof of a remote document.

Every case and step has stable ID plus at least one trace(snapshotId,exactSnapshotRevision,locator,criterionIndex), validated against existing sourceitems. Case set has name/author/currentrevision/cases; eachcase title/prerequisites/ordered instruction+expected+trace+optional typedbinding. Every save uses actual prior revisionCAS. Coalesced version-aware autosave flushes final accepted input and loops if a newer edit arrives while saving. Failed/conflicting writes keep the same dirty model and blockdeparture. Explicit conditional conflict-copy-as-new preserves draft before reload. Generation creates a NEWset, not overwrite. No per-keystroke unlimitedhistory: keep current draft and every run-referenced immutable revision. CreateRun freezes expectedcurrentcasebytes into immutable archive BEFORE publishing a referencingplan; older runs never drift with edits.

## Generation, mapping and CSV

Builtin rule generator creates clearly labeled template/rule cases from explicit acceptancecriteria; semantic completeness/AI is not claimed. Unmapped criteria produce manual instruction/expected steps with exact traces. Synthetic requirement and Figma examples provide taskform positive(nonemptytitle->Save->Saved toast) and negative(emptytitle->Save->Title required->createdCount0), and a clearly labeled synthetic assertion-failure example(nonemptytitle->Save->deliberatelywrongtoast) mappings. Typed actions are SetText(task.title,value), Click(task.save), AssertText(task.toast/validation/createdCount,expected); allowed exact elementIDs, strings and scope validated on creation/reload. Adapter resets in-memory fixture at eachcase boundary with explicit targetpolicy so cases do not depend on runorder. Unknown selectors/actions refuse.

User can edit case title/prerequisites/instructions/expected in native controls. Editing step instruction/expected invalidates its old automationbinding to manual, visibly; do not silently execute stale typedmeaning. Metadata edits alone preserve binding. Run refuses any selected manual/unmapped step; never silently treat omission as pass. Regenerate to a newset retains edited manualset. No redundant activationcheckbox for normal saves.

CSV is a real file with case/step/instruction/expected/sourceID/revision/locator/criterion/automation columns, UTF8BOM, CRLF, all fields quoted and internal quotes doubled. For every step, emit every case-trace entry in stored order, then every step-trace entry in stored order, preserving duplicates. traceScope is case or step; traceOrdinal is one-based within that scope. The caseId/stepId/scope/ordinal combination distinguishes repeated references; repeated case traces on another step are deliberate. Prefix every text column, including managed IDs/revisions/locators, with one apostrophe for spreadsheet viewing; only validated stepOrdinal, criterionIndex and traceOrdinal omit the prefix. This also prevents spreadsheet number/date coercion from silently altering text identifiers. Originalstoretext stays exact. This changes CSVcellvalues and does not promise safety after every spreadsheet's resave/reopen. Tests assert bytes and parsed cells; no launchingExcel or evaluating formulas. Guidance verified2026-11-07: https://www.rfc-editor.org/rfc/rfc4180.html and https://community.owasp.org/attacks/CSV_Injection . CSV is not XLSX.

## Runs and evidence

Hostgenerates run/sessionID and captures project/producer, exact case-set revision/source refs, selected ordered cases, target virtual.task-form.v1 and fixed action scope. Creation publishes valid plan+initialQueued+matching authoritative head together through owned staging then same-root directorypublication; incomplete staged/orphan data stays diagnostic and cannot start execution. Immutable casearchive publication may leave an unreferenced safe orphan on interruption. State transitions use sequence/hash, not filemtime or wallclock ordering.

Journal files contain contiguous sequence, prior-event digest, planSHA, hostUTC, kind and typed payload in `events/<D10>.json`. The implementer's delegated wire choice uses SHA256 of exact event bytes with only the root hash string's64 ASCII digits replaced by zeros, then stores that digest in the fixed-width slot; it is NOT straight SHA256 of the final self-containing file. See [the exact wire and head schema](../../testing-services.md#event-wire-and-fixed-replay-policy). A required per-run head.json commits sequence/digest; replay must validate it before accepting history. InitialQueued then RunStarted; StepStarted BEFORE adapter action; StepFinished only after actualresult and evidencepersist. Terminal Completed only after every plannedstep has a passingfinished record; failedassertion produces Failed. CancelRequested differs from settledCancelled; projectleave uses Interrupted when appropriate. A last StepStarted without result is uncertain, never success. Validate complete anchored chain/plan/schema/order/step/result/evidencereferences; missing/tampered/divergent/futurerecords refuse rather than fallingback to an older success.

Evidence service accepts bounded passiveBMP bytes, writes+flushes owned record before return and returns managedid/MIME/length/SHA/simulated receipt. StepFinished can reference only valid owned receipts for same run. Reopen checks exact length/hash/passiveformat. Unreferenced evidence after interruption is retained, never a fabricated stepresult. Virtual frames are exact320x180,24bpp,uncompressed bottom-up BMP (54-byte header,172854 totalbytes) state renderings visibly containing SIMULATED; native Image displays actual bytes. Full actual observed usertext is committed as StepFinished nativePlainTextState and remains readable in native event/state detail after reopen without re-executing the adapter or substituting planned action values; glyphrendering need not reproduce arbitrary Unicode. This intended usertext is separate from diagnostic ErrorCode and never raw exception messages. No activeHTML/SVG/URI or real desktop capture.

Run delegate is a real hostsupervised backgroundtask RequiresPluginStart=true, no startupresume. One active run perplugin/project; createqueuedplan then explicitStart, do not reportRunning on failedstart. Actual adapter/state work and committed events drive progress, not invented timerresults. Cancellation requests+joins original delegate/adapter before finalstate/ownershiprelease. ContextClose forbids freshwrites; normal before-leave must join UIactions, request/join runner and persist cancellation/interruption while servicesactive, then flushdraft. Pending work blocksleave and remainsowned. UnexpectedClose/crash keeps lastdurable running/inflight data for safe nextcontext initialization. Retry always new linked run fromstep1/resetfixture, originalattempt preserved; not checkpointcontinuation or exactlyonce.

## Native page and validation

Native source list/import, explicit syntheticexample action, Generate, case list+selecteddetail editor, conditional save/conflict errors, ExportCSV, explicit virtualRun/Cancel, committed run/step/evidence detail and Retry. No unused futureAI/device panels. Keep controlcount bounded with selecteddetail/list rather than building allpossible textboxes. Use loadedpage XamlRoot.ContentIslandEnvironment.AppWindowId with Microsoft.Windows.Storage.Pickers, preserving WinUiView factory ABI. ExplicitCI isolatedpaths may exercise shared import/export core without pretending an actualhuman picker was validated.

Own each actual async UI callback BEFORE firstawait, including picker/read/parse/export/statuscontinuation. Before-leave temporarily seals privateactionadmission, joins callbacks, stops runner/persistscheckpoint, flushes drafts, then releases temporaryseal once hooksettles: another participant may fail and SAMEcurrentpage mustremain usable. Host rootcontent is disabled duringtransition; terminalLifetime revokesgeneration. TextChanging only synchronously captures plainmodel; marshal otherUIwork separately and retain/join actualdispatchtasks. Stop subscriptions/timers onUnloaded and prevent stalegeneration updates.

Meaningful portable tests: strict actualpayloadparser/caps/locators, sourceimmutability, caseCAS/conflict/dirtyfinalinput, frozenoldrevisionruns, actualjournalorder/tamper/inflightinterruption, evidencebytes/hash/path/passiveformat, virtualstate pass/fail, realcancellation/leave/serviceCloseownership and realCSVbytes. Deterministic gates; no assertion-only implementation mirrors/fabricated passreports. Windows actualshippingEXE installs independentpackage and exercises import->generate->edit/autosave->CSV->virtualpass/fail/evidence/cancel->restart/interruptedretry/projectisolation. Report actualPIDs/Closed/OSexit/leases. Preserve every existing native/management/restart/workspace/businesscheck, currentSDK/sourcehash/NuGet/PRI/downloadstrictpairaudit. FullcurrentLinux/Windows CI required. Win11manual/DPI/IME/accessibility and realdevice integration remain explicitunexecuted gaps.


## Concrete boundary decisions for this stage

Namespace Workbench.Sdk, optional WorkbenchTestingServices.Version=1; IPluginTestingContext:IPluginProjectContext exposes IProjectTestWorkspace Testing. SDK records are UI-neutral immutable record shapes; stores defensively copy mutable byte arrays/collections before admitting async work and on return. Original caller buffers cannot mutate persisted authority. Schema1 JSON uses explicit camelCase fields, rejects unknown/duplicate properties and invalid Unicode, and validates enum values (unknown numbers/names are never accepted). Any wire representation is documented with the storage implementation.

The public service methods all end Async and take CancellationToken as last optional argument: PublishSource(TestSourceDraft); ListSources(); ReadSource(sourceId); CreateCaseSet(TestCaseSetDraft); ListCaseSets(); ReadCaseSet(setId); UpdateCaseSet(setId,TestCaseSetContent,expectedRevision); ReadCaseRevision(setId,revision); CreateRun(setId,expectedCaseRevision,selectedCaseIds); CreateRetryRun(parentRunId); ListRuns(); ReadRun(runId); AppendRunEvent(runId,expectedSequence,expectedHeadHash,TestRunEventDraft); AddRunEvidence(runId,TestEvidenceDraft); ReadRunEvidence(runId,evidenceId). Reads return specific snapshots/summaries/receipts; no arbitrary paths, dictionaries of workflow actions or delegates enter host persistence. CreateRun fixes target virtual.task-form.v1 rather than accepting unrestricted target input. Core ProjectTestStore has constructor(userRoot,projectId); write methods additionally take a validated producerPluginId first. The context facade captures real caller identity. ReadCaseRevision reads current revision if matching, otherwise exact immutable archive, never another revision fallback.

Suggested names are binding for inter-task coordination: TestSourceKind(Requirements,Figma); TestVirtualScenario(TaskFormPositive,TaskFormEmptyTitle,TaskFormAssertionFailure); TestSourceDraft/Item/Snapshot/Summary; TestTrace(SnapshotId,SnapshotRevision,Locator,CriterionIndex); TestCaseSetDraft/Content/Snapshot/Summary, TestCaseDraft/TestStepDraft without IDs, TestCase/TestStep with host-minted stable IDs; TestAction(Kind,ElementId,Value), TestActionKind(SetText,Click,AssertText). Creation mints case/step IDs. Update preserves existing case/step IDs and order, validates ownership/membership, can edit metadata and step text but cannot substitute silently changed mappings; changing step Instruction/Expected requires Action=null. Creating a NEWset from an owned or other-author source set uses drafts and mints fresh IDs. Arbitrary structural insert/delete/reorder editing is deferred, so update refuses changed membership/order. New set generation still supports any bounded case/step shape.

Run records: TestRunPlan/RunSnapshot/RunSummary, TestRunEventDraft/TestRunEvent, TestRunState(Queued,Running,CancelRequested,Completed,Failed,Cancelled,Interrupted), TestRunEventKind(Queued,RunStarted,StepStarted,StepFinished,CancelRequested,Completed,Failed,Cancelled,Interrupted); initialQueued is host-created, not a caller event. TestStepOutcome(Passed,Failed). Plan stores host ID/session/project/producer/time/parent/set/revision/selected frozen cases/target/plan hash. Snapshot stores plan, complete ordered events, derived state, sequence and head hash. Event draft contains kind, optional caseId/stepId/outcome/errorCode/evidenceIds and the non-positional nullable init property NativePlainTextState; event adds sequence/prior hash/plan hash/hostUTC/hash and the same non-positional property. Existing positional constructors/deconstruction shapes stay unchanged. The schema1 nativePlainTextState field participates in exact stored-event bytes/hash within the existing256KiB encoded-event cap. StepFinished requires actual observed TestActionResult.NativePlainTextState of1..8192 UTF16chars, valid Unicode, preserved exactly including astral characters/CRLF/tabs/whitespace-only state, without normalization or truncation; every other event kind requires null. Unfinished steps have no invented text/result. Persist and replay one fixed allowed transition/state machine, no free-form event body. Retry accepts only terminal failed/cancelled/interrupted original and copies its exact frozen cases into a new linked plan/session. It never rereads newer editable cases. Nonterminal or completed retries refuse.

Evidence types: TestEvidenceDraft(MimeType,Bytes,IsSimulated); TestEvidenceReceipt(Id,RunId,MimeType,Length,Sha256,IsSimulated); TestEvidence(Receipt,Bytes). Only exact passive BMP format above and IsSimulated=true accepted; fixed target has no external capture. Host validates byte header/dimensions/compression/length, not OCR; domain renderer visibly draws SIMULATED with bounded builtin glyphs. Evidence receipt is persisted under same run before returning and reverified when used/read. Bounds also limit total receipts and bytes including retained orphans.

Actually used portable extension interfaces: ITestSourceProvider has Id,Version,Kind and Parse(ReadOnlyMemory<byte>) -> TestSourceDraft; ITestCaseGenerator.Generate(IReadOnlyList<TestSourceSnapshot>)->TestCaseSetDraft; IControlledTestAdapter.TargetId/OpenAsync(sessionId,CT)->IControlledTestSession; session.ResetCaseAsync(caseId,CT), ExecuteAsync(TestAction,CT)->TestActionResult and IAsyncDisposable. Result supplies Passed,allowlistedErrorCode,nativePlainTextState,passiveFrame bytes. Fixed action selectors: SetText task.title; Click task.save with null value; AssertText task.toast,task.validation,task.createdCount. Values bounded4096 characters. Each case resets state before its first StepStarted. Positive taskcount1, negative count0. Deliberate assertion-failure example expected text is clearly labeled synthetic, drives the real adapter Failed path and never real infrastructure.

No public service exposes host-owned recovery as an arbitrary caller command. Core has an owner recovery entry used only by the once-per-context initialization barrier. The plugin initializes by awaiting a real service list operation before registering views/tasks. Initialization failures surface safe codes, never fake empty lists. Case/action edits and corrupted old events are validated on both writes and reads. Run Completed is derived from actual full passing step sequence; step/action uncertainty and cancellation never become success.


Additional exact validation choices: collections are nonempty unless explicitly optional: sourceitems1..256/criteria1..16,sets1..200cases/steps1..20,case and step traces1..16,selectedcaseIDs1..200 with totalsteps<=200. Set/case/source/item title1..200,documentId1..200,locator1..512 unique ordinal,description/prerequisites0..4096,instruction/expected/criterion1..4096; required strings cannot be whitespace-only. All strings must be valid Unicode; preserve legitimate astral characters, line breaks and tabs. ProviderId1..100/providerVersion1..40 plain bounded metadata, never paths or remote authority. Safe error allow-list also includes TestRecordNotFound,StoreBusy,InvalidId,InvalidProducer. Host store owner plugin IDs use existing validated plugin-ID rules.

Controller boundary: portable TestingController.InitializeAsync registers its supervised background task but does not register an independent before-leave hook. It exposes PrepareLeaveAsync; native entry registers ONE actual hook that seals/joins owned UI callbacks, then calls controller preparation while services remain active, then releases temporary admission on settled completion. Domain lifecycle tests register the same preparation explicitly. This avoids duplicate hook registration and ensures asynchronous pickers/exports are included. Terminal Stop/Lifetime revocation is separate.

## Task2 implementation clarifications — controller rulings, 2026-11-07

Shared reads permit creating a NEW executor-owned run from another producer's
case set. The source, canonical case set, archived revision and trace author stay
unchanged. Append, evidence and retry remain run-owner-only. This resolves the
shared-read/own-write interpretation; it is not an end-user-approved per-record
sharing policy and grants no real-device authority.

Run publication freezes exact current expected-revision bytes under the SAME
outer store lease, before publishing plan+host Queued sequence0 together by a
same-root staged-directory rename. The fixed target is virtual.task-form.v1.
Complete unreferenced case archives and complete evidence receipts remain valid,
retained and counted against caps. Recognized generated partial run/evidence
stages and event temporary files cause RecoveryRequired and remain untouched;
unknown/partial published data still preserve/refuse. Only the actual operation's
own stage may be cleaned after a caught ordinary error/cancellation and guarded
expected-entry checks. Manual repair of interrupted partial staging remains a
boundary; there is no public reset/arbitrary recovery or all-crash/power-loss claim.

A failed StepFinished requires the caller's next event to be Failed. The narrow
controller-authorized exception is internal once-per-new-owner initialization:
it can append Interrupted to ANY valid old nonterminal run, including one whose
last durable result failed. Private required schema1 `isRecovery` is true only on
that host initialization Interrupted (with RecoveryRequired), false on genesis
and every caller event, and included in the exact-byte journal hash. It is absent
from SDK drafts. Replay rejects the flag on a different kind or after terminal;
foreign-owner initialization cannot write it. Original failed result, observed
text and evidence bytes remain unchanged. This reconciles the caller failed-step
rule with owner interruption; it never fabricates success or settles recovery as
Failed. The future captured facade supplies the retained initialization barrier.

The implemented wire, hash masking rule, every allowed transition, BMP header,
caps/orphans and remaining integration gates are specified in
[testing-services.md](../../testing-services.md). The event self-hash covers all
exact file bytes with only its 64 ASCII hash digits replaced by zeros. With the
fixed 172854-byte BMP and 256 receipts, a valid run reaches 44,250,624 bytes, below
the conservative 128 MiB cap; no artificial valid 128 MiB test boundary is claimed.

The controller additionally approved a private run-plan envelope limit of
4 MiB +4096 bytes: valid selected case payload can occupy the full case-envelope
cap before bounded host session/ancestry/revision/target metadata is added. The
source/case limit stays4MiB and events256KiB; plan read/write share the private
limit with no public/test tunable. Real canonical near-limit payload tests cover
creation/retry/reopen and oversized-plan refusal. Full Core passed before this
narrow size-bound adjustment; affected store regressions cover the final change,
and final current full CI remains required at the integration stage.

## Task2 fix1 — authoritative head and partial publication

Review T2-I1 demonstrated that removing the final event left an accepted shorter
prefix and allowed initialization to replace terminal history. A required strict
schema1 per-run head.json now commits runId, planHash, sequence and eventHash
(4096-byte envelope cap). The initial head is staged/flushed with plan+Queued.
Append under the SAME lease durably publishes its immutable event, then atomically
publishes the matching head; only then may it return success/advance its cache.
All read/append/owner-initialization admission validates the required anchor and
whole chain before recovery writes. Missing/malformed/future heads refuse;
headbehind/ahead/missingtail/extra-unanchoredtail/head temporaries preserve/refuse
without deriving an older prefix or guessing a new committed tail. Complete
orphan evidence/archives keep their existing retain/count policy.

The controller's fix ruling retains the existing delegated D10 filename and
masked-slot digest representation to limit unrelated changes; it does not
retroactively claim separate controller preapproval for that original choice.
Updating the original run paragraph above reconciles the stale hash-bearing
filename sentence (T2-M1) during this required schema update. Raw-file SHA256
artifact receipts are separate. No signature/sandbox, hostile chain+anchor
rewrite defense, automatic reset/repair, rollback or all-crash/power-loss promise
is introduced. Current full integration CI/native/human gates remain deferred.

Task4 controller ruling: mapping removal is monotonic. An unchanged instruction/expected pair may retain the exact prior action or remove it to null; updates never introduce/substitute a non-null binding. This allows an accepted changed/invalid edit restored to its original text to autosave as manual, without synthetic intermediate writes. Metadata-only editor methods continue preserving the exact action.
