# Installed SourceAcceptance package and native controls

The user authorizes continuing the independent SourceAcceptance package, actual native source-controls acceptance and preparation for real AI/Figma integration. Ordinary technical choices and design questions are opted out; Windows 11 user acceptance remains postponed. This phase continues from functional655d7b6/documentatione646e78 without reopening or discarding their completed runtime plan, evidence or 24 original pictures.

## Intent and boundaries

Install a distinct synthetic source fixture into the actual shipping WinUI host and drive the **same production TestingPlugin, TestingPage, TestingSourcePanel, Domain clients/controller and lifecycle**. Its terminal in-memory HTTP handlers remain the only transport. There is no new alternate page, embedded app, private controller invocation, real provider/key/Webhook/DingTalk traffic, general user-PC authority or change to the production provider default. Generated AI cases remain manual and never start computer automation.

Keep host/SDK0.4.0, API1, Testing0.2.0, assembly1.0.0.0 and current business pins. In-process code is trusted; ALC is not a sandbox or hot-unload facility. Ordinary production delivery remains exactly eight files; fixture installation stays outside ordinary app/Plugins and requires deliberate isolated trust. Public standard Actions are authorized; no paid/private runners, billing changes, artifact deletion, PR retry, merge, release or deployment. At most the existing seven artifacts, retention one day; reuse native-verification-evidence for fixture/source reports. Preserve old work and failed evidence.

## Approaches and decisions

1. A separate fixture UI/controller would test a different product. Rejected.
2. A shared typed post-Stop handle would need new SDK/Core services or private type sharing. Rejected for this bounded fixture.
3. A closed paired package profile, production controls and an opt-in fixture-private file release capability reuse the current boundaries. Selected by the coordinator under the user's autonomy instruction.

Build the fixture graph once with normal Windows WinUI/MSVC properties, then export both profiles from that same fresh build. Do not assume rebuilding common projects into two different output paths produces identical bytes. This explicitly selected paired mode keeps the default production build unchanged and makes seven common payload comparisons literal byte comparisons.

## Closed package contract

`package-testing-plugin.ps1`, `New-TestingPluginDelivery` and `Test-TestingPluginDelivery` accept only `Production|SourceAcceptance`, default Production. Fixed profile descriptors choose project, manifest, identity, archive name and allowlist; callers cannot supply arbitrary manifests/extra files. SourceAcceptance archive is `workbench-testing-source-acceptance-0.2.0.zip`, identity `com.projectworkbench.testing.source-acceptance`, entry `Workbench.Testing.SourceAcceptance.dll` / `Workbench.Testing.SourceAcceptance.SourceAcceptancePlugin`, API1/minimumHost0.4.0. Fixture capabilities are the six production hints plus exactly `source-acceptance-fixture`; hints grant no authority.

Production contains exactly these eight regular case-sensitive ZIP entries, no explicit directories/extras:

```
Fixtures/figma.synthetic.json
Fixtures/requirements.synthetic.json
Workbench.Testing.deps.json
Workbench.Testing.dll
Workbench.Testing.Domain.dll
Workbench.Testing.Windows.dll
Workbench.OwnedLab.exe
plugin.json
```

SourceAcceptance replaces plugin.json and adds exactly `Workbench.Testing.SourceAcceptance.dll` and `Workbench.Testing.SourceAcceptance.deps.json` (ten entries). All seven non-manifest production entries must match the same-run production delivery byte-for-byte. Both outer deliveries remain exactly README.md, SHA256SUMS, provenance.json and their selected ZIP.

`build.ps1 -IncludeSourceAcceptance` is the explicit paired build. It publishes the normal host and samples, then invokes the fixture graph producer once, exporting production to `artifacts/native/testing-plugin` and fixture to `artifacts/native/source-acceptance-fixture`. Default build.ps1 retains the production invocation. Both modes must refuse an existing nonempty native output root rather than recursively deleting earlier outputs. `package-testing-plugin.ps1 -Profile SourceAcceptance` uses the fixture project as full transitive Rebuild root, a fresh reserved `artifacts/testing-source-acceptance-package-build`, and `-PairedProductionDirectory` (default ordinary production directory) for its paired export. Refuse nonempty reserved build/output directories before consuming/overwriting old bytes. Both New/Test delivery functions consume explicit `-ProductionDirectory` when Profile is SourceAcceptance and reject that unrelated argument for Production. Production and ReferenceOnly use distinct existing/reference build roots; ReferenceOnly returns without either delivery and never authorizes export. No fixture publish/pack, disabled dependency builds or global WinUI property suppression in a real Windows build.

Reuse current host/source/helper/CRC/checksum/exact-entry/current-clean-source validators. Native helper stays actual static-CRT x64 **Production**, descriptor available, source/toolchain/image bound. Reject stale, missing, reference-only or fault helpers before output. Check every input and ancestor for links/reparse points. No PDB/runtimeconfig/SDK/Core/WinUI/WinRT/.NET copies, native build sidecars or credentials enter a ZIP.

Extract one private archive reader used by the real delivery validator and structural tests; it must not bypass native/managed/full delivery validation. Strict JSON parsing rejects duplicate (case-insensitive), missing/unknown schema fields where closed, malformed/trailing data and wrong types. Manifest/deps JSON reads are bounded to 2MiB and depth32; existing payload16MiB ceiling remains. Validate exact capability sets.

Add a shared metadata-only managed/dependency audit, without loading/instantiating DLLs. Private DLL names/assembly1.0.0.0/file0.2.0.0/informational0.2.0+currentCommit must match actual PE attributes. Testing/Windows/fixture are x64; Domain is portable. Require code-bearing assemblies, reject ReferenceAssemblyAttribute, validate concrete public IWorkbenchPlugin entry and public zero-argument constructor. Resolve private AssemblyReferences to packaged identities; only actual host-shared SDK/projection identities and its embedded framework catalog can supply approved shared/framework references. Do not expand PluginLoadContext sharing. Both deps roots/libraries/runtime DLL paths and private dependency edges must agree with actual payloads; reject missing/renamed/foreign private assets and conflicting identities. Bind external shared identities to the actual paired host rather than shipping them. Current observed target is `.NETCoreApp,Version=v10.0`; no arbitrary target/RID substitution. Record/recompute audit facts, not trust provenance assertions. New script/audit/test files must be inventoried by Get-TestingSourceFiles and covered by hash-refusal tests.

The package-store probe preserves its one-argument production invocation and accepts explicit `--profile SourceAcceptance` plus optional `--report <new-file>` (refuse overwrite). Install through real PluginPackageStore in fresh isolated data, compare exact installed files/bytes, discover disabled/untrusted/unloaded, refuse duplicate without overwriting, refuse actual original fixture at host0.3.0 and wrong API/above-host minimum mutations; host0.4.0 installs. Save a bounded schema1 install receipt outside the exact four-file delivery. Installation alone is not runnable WinUI evidence.

## Retained explicit release v1

Preserve the ten existing SDK commands and state schema1/8192-byte bound/shared32 accepted-request cap. Read fixture setting `source-acceptance-release-v1` once at init, default empty. Empty disables the bridge with no companion IO/monitor. Nonempty must be lowercase hex32 runId and match the captured data directory exactly under the new dedicated root:

* default: `<temp>/ProjectWorkbench-source-acceptance-probe/<runId>/data/<fixtureId>`;
* project: `<temp>/ProjectWorkbench-source-acceptance-probe/<runId>/.workspaces/projects/<projectId>/data/<fixtureId>`.

No input path, endpoint, command or provider data. Reject wrong layout, links and nonregular targets before access. This is a same-user trusted fixture convention, not protection against arbitrary trusted code or filesystem races.

Fixed descriptor `source-acceptance-release-v1.held.json` and request `source-acceptance-release-v1.release.json`, with fixed same-directory staging files and atomic closed/flushed publication. Strict UTF-8, max2048bytes/depth8, no duplicate/unknown/missing fields/trailing JSON. Descriptor exact fields: protocolVersion=1, fixtureId/pluginId=fixture identity, runId, projectId, instanceId (hex32), generation (positive Int64), provider=figma|ai, scenario=hold, releaseId (fresh per-ticket hex32). Request has identical fields plus action=release. Stale records may remain; identity, not existence, authorizes release. Matching duplicate is idempotent; mismatches cannot release any ticket.

At acceptance of an actual validated Hold, under the same ledger seal barrier, atomically bind the exact ticket/captured release capability/nonce and retain a monitor-completion placeholder before any async prefix or descriptor publication. A Stop winner admits no monitor; an acceptance winner is included in drain even if its monitor has not started. No IO or callbacks under the lock. No bridge monitor for arm/non-Hold/unexpected/closed work. At most32.

One monitor per admitted hold observes only its fixed request and exact release task at25ms cadence (testable TimeProvider). Timer/cancellation/deadline/Dispose/Stop/errors never release the request or cancel that admitted monitor. SDK explicit release while open also retires it. No SDK background-task registration or revoked-facade use. Stop seals admission, retains the single actual inner Stop, allows monitors to deliver explicit release, joins inner Stop, joins all admitted monitors, then attempts both handler disposals and preserves first failure/caller-cancel ordering. Monitor never awaits enclosing Stop. Init failure uses the same join path. Descriptor-publication failure must not strand unobserved work: preserve admitted completion and use explicit open-context release in controller cleanup; after closure, undeliverable signal remains a reported pending failure, never a fabricated successful cleanup.

No separate native per-handler-disposal timestamp/receipt is added. Native acceptance observes actual manager completion, normal Closed/OS exit and lease recovery; portable ownership tests independently pin the internal ordering. Do not claim native measurement of each internal disposal milestone.

## Actual native source lane

Dedicated `--source-acceptance-probe seed|verify|stop-figma|stop-ai`, mutually exclusive with all other probes/smoke; IsIsolated=true and only a lowercase GUID-N direct child of `%TEMP%/ProjectWorkbench-source-acceptance-probe`. Parse missing/duplicate/conflicting/unknown source phase/root values fail-closed before normal startup. Separate marker, result, diagnostic vocabulary and package binding. No unknown-source-option fallback to normal AppData. Prepare before runtime creation, run after normal workspace initialization. Curate only intended isolated packages; no production handlers overridden by flags.

Host uses only public Core/SDK and existing WinUI navigation/control peers. No host reference to fixture/Testing/Domain assemblies, reflection, alternate controller/page or new sharing. Source controls currently have AutomationProperties.Name; use their exact real names rather than inventing AutomationIds. Read/write actual controls, invoke actual enabled button peers, reacquire after row/navigation/render replacement, and read actual public ProjectTestStore records.

Seed covers the twelve flows in native-controls-recon-report.md, with project-switch held Figma **and** AI paths. Inputs are fixed synthetic File1/version/nodes2:3,1:2 and exact three manual criteria from existing fixture. Save synthetic constants only through actual CredentialPage; real CurrentUser DPAPI roundtrip/ciphertext nonplaintext proof and project isolation, editors clear. Do not claim a measured native zero-DPAPI-read counter (none exists). Passive editing/navigation/selection/arm must have zero transport entries. Local requirements decoy is never selected for AI.

Figma happy: actual raw response/hash/revision, node criteria, producer/project identity. AI happy: selected-only request and independent exact response/receipt hashes, trace order0,1,2 with returned case order2,0,1, all Action/VirtualScenario null, actual manual mapping and disabled run/no new run. HTTP503 and malformed inputs preserve old records/selection and safe errors. Partial AI acknowledges exactly one receipt/no case set with actual StoreLimitExceeded receipt-only UI. Held navigation preserves same session and later acknowledged result. Real Figma/AI cancel keeps actual work/admission owned until explicit release, then publishes no new records. Project switching retains original manager/root/generation until actual preparation/release/retry, isolates B and restores exact durable A with a new instance/reset session. Do not wait on transient/sticky state flags as per-generation evidence.

Verify is a second actual process on the same seed root: new fixture instance/gen0/counts0/reset form, exact durable source/receipt/case bytes/traces, manual UI and synthetic DPAPI persistence, no passive request/publication. Stop-figma/stop-ai use separate fresh roots and real control-originated hold: save exact descriptor, then begin actual Disable/Stop, observe revoked SDK commands, pending retained work and refused new instance, explicitly signal saved gate in finally, await/retry original Stop, verify no canceled publication and normal closure. Do not reacquire the release capability after revocation or switch its root. Wrong/stale signal adversarial behavior is measured by the real parser/monitor's portable tests; do not infer that Windows consumed a bad file merely from its presence and a delay.

The closed required check IDs are:

* seed (15): source-installed-same-page, source-passive-form, source-synthetic-dpapi, source-figma-happy, source-ai-selected-manual, source-figma-http-failure, source-figma-malformed, source-ai-malformed, source-ai-partial-receipt, source-held-navigation, source-figma-cancel-retained, source-ai-cancel-retained, source-project-switch-figma, source-project-switch-ai, source-durable-seed.
* verify (5): source-restart-instance-reset, source-restart-records, source-restart-dpapi, source-restart-manual-ui, source-no-passive-publication.
* each stop phase (8): source-held-entered, source-release-descriptor-bound, source-stop-revokes-commands, source-stop-retains-work, source-stop-refuses-new-instance, source-explicit-release, source-stop-joined, source-canceled-no-publication.

Normal Closed/actual exit0/all-root lease recovery are separate mandatory fields, not optional extra checks. There are36 phase-qualified observations across four processes; negative/cancel checks are not successful provider publications.

Use bounded public-store observations without accumulating unfinished reads, and separately join owned reads/operations after observation expiry. Default20s poll does not bound an inner await. Failures keep fixed safe codes and captured identities, not raw exception/provider/key data. A failed/timeout owned process can be terminated only through the retained launcher handle after recording failure; never score that as graceful retirement.

Each result binds schema1, phase/root/source/package/actual host hashes, actual native PID+creationFILETIME, unique named checks, relevant instance/project/generation/store identities and bounded source evidence. Independent launcher retains process handle, independently observes birth/exit/code and reacquires **all** captured host.lock roots only after exit0; tuple PID+birth is unique (numeric PID reuse alone is allowed). Source-specific report parsing rejects missing/extra/duplicate checks, tampered files/hashes, wrong identities, traversal/links/oversize and invented exit/Closed. Existing production virtual/BMP/CSV validator remains unchanged. Save synthetic source/receipt/case evidence only, never secret/ciphertext files. No source-image claim is required; standard same-source24 gallery remains verified separately.

## Execution and remaining boundaries

Sequential tasks: closed paired packaging/managed audit; retained explicit bridge; native lane/report/workflow wiring; one reviewed-source combined public batch and actual downloaded artifact audits. Local TDD/script/reference evidence is distinct from Windows execution. Preserve argv/exits/raw outputs/TRX/current source snapshots/actual DLL identities and failed attempts. No arbitrary test repeats after passing absent new changes/doubt.

This prepares real provider control/receipt/lifecycle boundaries using existing real clients and sealed synthetic transports. Live Figma permissions/model availability/API credentials/charges/provider responses remain unverified. No real key inspection/reuse/provisioning or new live-provider code is admitted without the appropriate credential decision. Continue independent work and report that boundary. Win11 human theme/IME/DPI/pickers/accessibility and generic user-PC control remain deferred.
