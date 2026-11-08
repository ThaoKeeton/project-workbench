# Native runtime follow-up design

The next shipping validation must distinguish the caller's cancellation from an observation timeout and recover the genuine UPCAM preview image without weakening ownership or capture checks. The user has delegated routine decisions and requested continued development and public Actions verification; user acceptance remains postponed.

The retained `31287b5` Windows run reports one Core failure: `Caller_cancellation_does_not_release_preparation_that_ignores_it` expected `ProjectPreparationCanceled` but observed `ProjectPreparationPending`. That test starts a 100 ms system-time observer before queued preparation performs settings/file I/O and the test notices its marker. Source proves an unconstrained race, not the historical event ordering. The actual hook was still pending at the failed assertion. Production ownership failure has not been established.

The same run records UPCAM preview `blocked/focus-detached`, zero viewport, and no PNG. The surviving boundaries are required-null input, initial owner rejection, and first post-position revalidation. Pinned business source and installed DLL identity are unchanged. An ancestor scroller above the exact content root remains possible because the older successful helper did not stop at that root; neither that possibility nor a static Grid layout is sufficient evidence for a behavior repair.

## Decisions and alternatives

Use the framework `TimeProvider` for the existing preparation observation wait, with an internal constructor dependency defaulting to `TimeProvider.System`; only tests provide a manually advanced clock. Use the Microsoft-maintained `Microsoft.Extensions.TimeProvider.Testing` package, pinned `10.0.0`, in the test project. This preserves production deadlines and avoids hand-written timer emulation. A larger real timeout or accepting either status would leave the causal premise untested.

Freeze observation time while the actual fixture reaches its held hook, then separately order cancellation first and timeout first. Both results must retain exactly one hook, prevent terminal disposal while it is held, and join it after release. Cancellation does not retroactively replace a completed timeout result. Actual Stop and fixture cleanup continue using finite system time.

For UPCAM, first add a failure-only structural discriminator. It records the exact existing failed predicate and checkpoint, measured page/root/owner depths, and bounded supplemental above-root owner information without using that information for ownership. Do not change capture behavior before the next Windows observation identifies the boundary. A later behavior task must cite that actual sidecar and receive a fresh scoped review; do not repeat the prior static guess.

## Binding constraints

- Keep Windows-native WinUI pages in the same host window; do not embed an independent application window or alter a business repository/DLL.
- Keep SDK 0.4/API 1, Testing 0.2, the production eight-file plugin profile, pinned DingTalk 0.3.2 and UPCAM 0.1 compatibility.
- Preparation keeps a positive finite `StopTimeout`; default production time is `TimeProvider.System`. Do not change the SDK or public constructor signature, actual hook cancellation, participant identity, acknowledgement, Stop, or retained-work ownership.
- Preserve the exact `Canceled` versus `Pending` assertions. Keep the existing 100 ms test configuration; do not accept either status, increase it, sleep to arrange the race, or detach work.
- Preserve gallery receipt schema/reason vocabulary/roster, strict page/runtime/generation/HWND/PID/birth/desktop/focus checks, original limits 32 attempts/4096 dimensions/64 MiB BGRA/16 MiB PNG, same-owner restoration, immutable CreateNew output, and no capture-failure await after PNG publication.
- Diagnostic output is isolated synthetic `business-seed/upcam-preview` only, blocked/focus-detached only, fixed sibling `upcam-preview.focus-diagnostic.json` outside the gallery roster directory, schema 1, at most 8192 UTF-8 bytes and JSON depth 8. CreateNew, best effort, no raw exceptions; it cannot change the primary probe or receipt result.
- Diagnostic content uses fixed enums, booleans, bounded numeric counts/depths, and already validated source/process identity. Never read/export Text, Name, Header, Password, AutomationId, provider/profile values, arbitrary paths, or dynamic type names. No extra successful-path tree walk, layout, event subscription, dispatcher task, await, or retry.
- Preserve source, prior failed evidence, existing artifacts, and business repositories. No real credentials/providers/Figma/Webhook/DingTalk messages or user-PC control. No billing/purchase, artifact deletion, denied-PR retry, merge, deploy, or release.
- Use the existing PUBLIC standard two-job workflow with one-day artifact retention. Verify public visibility and exact source before one dispatch per reviewed source; prevent automatic duplicate runs. Windows Server CI and reference compilation do not constitute Windows 11 human acceptance.

## Evidence and scope

Focused deterministic real-manager tests and the complete Core suite validate the clock change. Actual reference compilation and executable diagnostic serialization tests validate the diagnostic source locally. Public Windows CI supplies actual UI-tree evidence, then the original native/runtime/workspace/Testing/business/owned matrix and genuine PNGs supply runtime acceptance. A repeat blocked preview with no usable sidecar is still a missing discriminator, not permission to guess.

The three Testing pictures and older 21 originals remain valid only for their recorded source versions. Never substitute an old PNG into a new-source roster. Real AI/Figma and additional SourceAcceptance package work remain separate outstanding capabilities.

Framework contracts: [Task.WaitAsync](https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.waitasync), [Microsoft testing clock 10.0.0](https://packages.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing/10.0.0). API documentation supports the design; it is not runtime evidence.
