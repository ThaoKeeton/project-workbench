# Testing contracts 1 — host 0.4.0

The app and both SDK packages are preview **0.4.0**, file version **0.4.0.0**.
Assembly identity **1.0.0.0**, API 1, runtime services 1, project services 1 and
the existing context/WinUiView signatures stay compatible.
`WorkbenchTestingServices.Version` is 1. `IPluginTestingContext` derives from
`IPluginProjectContext` and adds only `IProjectTestWorkspace Testing`.

This intermediate commit implements guarded source and case storage in Core and
defines the complete SDK contract. The host testing context, run/evidence
implementation and independent native testing plugin are **not wired yet**.
The host exposes no placeholder testing service. Existing preset case-list
plugins remain examples. Consumers should check the optional derived interface
and declare `minimumHostVersion: "0.4.0"` when using testing services. Existing
project-only plugins may retain 0.3.0; frozen business packages retain their
original 0.2.0 minimum and bytes.

The exact public declarations are in
[TestingContracts.cs](../src/Workbench.Sdk/TestingContracts.cs). Every workspace
method ends in `Async`, returns a specific snapshot/summary/evidence type, and
has a final optional `CancellationToken`. Source/case writes take drafts or
host-ID-bearing case content, never caller-selected paths, project identities,
producer identities or run/session IDs. The future captured-identity facade
removes the first `producerPluginId` parameter used by Core write methods.
Cancellation does not establish rollback; after an admitted operation settles,
reread its actual durable state.

## Committed text-state contract for later run implementation

`TestRunEventDraft` and `TestRunEvent` each add the nullable init-only property
`string? NativePlainTextState`; their positional constructors and deconstruction
shapes remain unchanged. StepFinished requires the actual
`TestActionResult.NativePlainTextState`: 1–8192 UTF-16 code units, valid Unicode,
preserved exactly, including astral characters, CRLF, tabs and whitespace-only
state. No normalization or truncation applies. Every other event kind requires
null; an unfinished step has no invented text or result.

The future schema1 event field `nativePlainTextState` participates in the exact
stored-event bytes and journal hash, within the existing 256 KiB encoded-event
cap. AppendRunEvent must commit the adapter's observed text, and ReadRun must
return that committed text after reopen without re-executing the adapter or
substituting planned action values. This intended user-visible native text is
separate from allow-listed diagnostic ErrorCode and must not contain raw
exception messages. It remains readable when the simulated BMP glyph renderer
cannot reproduce arbitrary Unicode.

This increment declares that path; it implements no run persistence or rendering.
Task2 must round-trip independently chosen multiline/astral/maximum8192 state
through append, close and reopen, verify exact text/hash, and refuse invalid
Unicode, empty/oversized state or non-null state on other kinds without changing
existing bytes. Task4 must show reopened native detail uses the committed text
even when the BMP omits those glyphs. These are future gates, not passed results.

## Implemented source and case behavior

`ProjectTestStore(userRoot, projectId)` captures a normalized project root and
valid project identity (`default` or a managed 32-character lower-hex identity).
An existing `.workbench-project/workspace.json` must also match that captured
identity before testing reads or initialization. Missing legacy markers are
permitted; the testing store always supplies its own strict project binding.
It implements PublishSource/ListSources/ReadSource,
CreateCaseSet/ListCaseSets/ReadCaseSet/UpdateCaseSet/ReadCaseRevision only.
It does not implement `IProjectTestWorkspace` with unimplemented run methods.

PublishSource creates a new immutable source with host ID/project/producer/UTC,
copied common items and exact original bytes. `OriginalSha256` hashes those exact
bytes, including a UTF-8 BOM when present; `Revision` separately hashes the exact
stored JSON envelope bytes. Titles, document IDs and locators are literal data,
including a locator such as `../../x`. They never form filesystem paths.
Provider metadata declares a mapping; it is not cryptographic proof of any
remote document. Builtin source parsing/generation belongs to the later domain
plugin, not this generic store.

CreateCaseSet mints new case/step IDs. A fork uses drafts and receives new IDs
even when it preserves another producer's exact traces. Every case and step has
1–16 traces; each trace must match an existing source's exact revision, literal
locator and zero-based criterion index. Updates require the actual prior
revision and the original producer, case/step membership, order and traces.
Only metadata and instruction/expected text may change. A text edit requires
that step's action to become null. Metadata alone preserves its exact action;
changing or introducing a binding requires a new generated set.

Allowed typed actions are `SetText(task.title, value)`,
`Click(task.save, null)`, and
`AssertText(task.toast|task.validation|task.createdCount, value)`.
SetText permits empty text for negative examples. Values are bounded to 4096
UTF-16 code units. No arbitrary selector, process, shell, network or OS input is
represented. Manual steps remain manual; this increment runs no steps.

ReadCaseRevision returns the matching current revision or that exact immutable
archive. It never falls back to another revision. The internal freeze helper
copies current raw envelope bytes under the already-held lease before a future
referencing run is published. Ordinary autosaves do not create history.

## Storage schema 1

All files are under `<userRoot>/.workbench-project/testing/`:

```text
store.json                              {schemaVersion, projectId}
sources/<sourceId>.json                  immutable SourceEnvelope
cases/<setId>.json                       current CAS CaseEnvelope
case-revisions/<setId>/<revision>.json    exact immutable CaseEnvelope bytes
```

The cooperative exclusive lease is the adjacent
`.workbench-project/testing.lock` regular empty file. Missing reads create no
root, metadata directory, testing directory, lease or document. Writers acquire
the lease before initialization. An existing partial root without its complete
schema is refused and preserved, including after interrupted initialization.
An empty archive directory is likewise incomplete and refused. Valid complete
unreferenced archives are retained because run-plan publication may follow
archive publication. No automatic pruning occurs.

JSON is UTF-8, camelCase, exact required fields, maximum depth 32. Enums use exact
case-sensitive SDK member names, never numeric values. SourceEnvelope fields:
`schemaVersion,id,projectId,producerPluginId,createdUtc,kind,providerId,providerVersion,documentId,title,originalBytes,originalSha256,items`.
`originalBytes` is Base64. CaseEnvelope fields:
`schemaVersion,id,projectId,producerPluginId,updatedUtc,name,cases`.
Nested item/case/step/trace/action objects use the corresponding SDK constructor
property names in camelCase; nullable scenario/action/value fields are explicit.
No envelope embeds its own revision. UTC metadata has zero offset.

Every operation validates the existing root, all records and archives under its
one lease. It rejects links/reparse points, nonregular files, unknown entries,
orphans, unsupported schemas, wrong project identity, invalid UTF-8/Unicode,
unknown/duplicate/missing fields, invalid enums, malformed IDs/hashes and broken
trace references. Refusal preserves existing bytes. Publication uses exclusively
owned adjacent staging, flush and atomic rename; cleanup removes only staging
owned by that operation. These are cooperating-store defenses, not a sandbox or
a guarantee against malicious trusted code, every TOCTOU race or power loss.

| Boundary | Limit |
| --- | --- |
| Original source bytes / encoded source envelope | 1 MiB / 4 MiB |
| Sources per project / items per source / criteria per item | 64 / 256 / 16 |
| Title, document ID / locator / provider ID, version | 200 / 512 / 100, 40 UTF-16 units |
| Description, criterion, prerequisites, instruction, expected | 4096 UTF-16 units |
| Case sets / cases per set / steps per case / traces per case or step | 128 / 200 / 20 / 16 |
| Encoded case envelope / immutable run-bound archives | 4 MiB / 256 |

Required text/collections are nonempty; required text cannot be whitespace-only.
Description and prerequisites may be empty. Valid astral Unicode, tabs and line
breaks are preserved. Limits are checked before publication and again on load.
The later executable-run limit is 200 total steps; a stored case set can contain
more. Maximum-size native UI performance has not been measured.

Core captures actual deep copies of bytes and collection elements synchronously
before scheduling IO, and returns detached snapshots. The future facade must
first retain operation admission, then call the reusable `TestValidation.Copy*`
helpers outside Core/coordinator locks before returning or scheduling. Calling
the store with caller arrays only after a facade's `Task.Run` is too late.
No plugin delegate or caller collection is enumerated under the store lease.

Failures use sealed `PluginTestingException` with a fixed generic message, safe
`ErrorCode` and no raw inner exception. Allowed codes are TestingContextClosed,
InvalidTestSource, InvalidCaseSet, InvalidRun, InvalidEvidence, RevisionConflict,
StoreLimitExceeded, StoreIoFailure, UnsafePath, TestStoreCorrupt, RecoveryRequired,
ManualStepUnsupported, TargetUnavailable, AssertionFailed, AdapterFailed,
TestRecordNotFound, StoreBusy, InvalidId, InvalidProducer and TestingFailed.
Facades must map unknown public exception codes to TestingFailed and must never
display raw exception/provider text. Native source/case text is separate from
diagnostics.

The SDK snapshot contains eleven allow-listed source files, two 0.4.0 NuGets,
both consumer READMEs, source/package hashes and `testingServices=1`.
`workbench-sdk-0.4.0` requires Windows packaging for the actual WinUI PRI.
Linux `-ReferenceOnly` and native reference compilation establish no native
runtime, installed-plugin, picker, Windows 11, IME, DPI or accessibility result.
