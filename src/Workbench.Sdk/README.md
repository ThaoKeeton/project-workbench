# Workbench.Sdk 0.4.0

Contracts for independently built, trusted in-process Project Workbench plugins.
Target .NET 10 with SDK 10.0.403. `WorkbenchApi.Version` stays **1**,
`IPluginContext` stays compatible, and assembly identity stays **1.0.0.0**.

Optional `IPluginRuntimeContext` runtime services version **1** require host
**0.2.0** or newer: registered background work, host settings and declared secret
keys. Check for that optional interface before use. Register contributions during
initialization; stop and join all owned work when the host requests Stop.
Generic Start and Restore refuse tasks requiring plugin confirmation.

Discovery and installation do not activate a plugin. The user must explicitly
trust its exact package fingerprint. Trust, resume preference and business
consent are separate; same-process plugins are not sandboxed. JSON settings and
data files are not a credential vault; secret values must not enter diagnostics.

Native views additionally use `Workbench.WinUI.Sdk` 0.4.0 with Windows App SDK
**1.8.260921001**. Distribute plugin code and private dependencies without host
App/Core/SDK/Desktop binaries. Package updates require a host restart; hot unload
is not supported. A `referenceOnly` source snapshot proves compilation only.

Optional `IPluginProjectContext` project services **1** require host **0.3.0**
or newer. The original context/runtime interfaces remain unchanged. Read/update
current-project instructions with revision checks; register one before-leave
hook during initialization to flush nonsecret document models. Hooks run off the
UI thread and must not access controls or persist credential drafts. Failure
keeps the current runtime available; timeout/cancellation retains actual work
until it settles, including during Stop. See `ProjectContracts.cs` in the
UI-neutral SDK. File version is **0.4.0.0**; assembly identity stays **1.0.0.0**.

Optional testing services **1** use `IPluginTestingContext : IPluginProjectContext`
and require host **0.4.0**. The host captures project identity, retains each
operation before IO, and runs one initialization barrier per context. Old valid
nonterminal runs from that plugin owner become `Interrupted` before reads; other
owners' live runs remain unchanged. Closing revokes new calls and joins actual
admitted work. There is no public recovery command or sandbox.

`TestingContracts.cs` supplies exact-byte source snapshots, traced revisioned
case sets, frozen run plans, committed hash-chained events and passive simulated
evidence. The independent `com.projectworkbench.testing` **0.1.0** entry consumes
these services through a portable controller and a same-window native page.
Local requirements/normalized Figma JSON imports, rule generation, autosave with
conditional conflict recovery, CSV export and explicit virtual Run/Cancel/linked
Retry are implemented. Manual/unmapped steps cannot pass automatically.

`NativePlainTextState` init properties carry the adapter's actual observed
StepFinished text (1–8192 valid-Unicode UTF-16 units); other event kinds require
null. Schema1 hashes exact stored field bytes; reopened runs return persisted
text without adapter replay. Native detail renders it separately from the
320×180 **SIMULATED** BMP, whose bounded glyphs may omit Unicode. The runner
commits StepStarted before a typed action, persists evidence before StepFinished,
and joins actual work and disposal before terminal release. Retry creates a new
linked run/session from step one with the parent's frozen cases.

Source/portable tests and reference compilation have separate evidence from
actual Windows package/PRI/native process verification. Use same-run host/SDK
and the separate testing ZIP/provenance, and consult that run's reports before
claiming Windows acceptance. A `referenceOnly` package is compile evidence only.
Real Figma/provider/AI/device/desktop automation and Windows 11 human
pickers/IME/DPI/accessibility acceptance remain unexecuted. All frames are
simulation, never real desktop screenshots. Existing project-only plugins may
keep minimumHostVersion **0.3.0**, runtime-only plugins **0.2.0**; immutable business
packages retain their original bytes. The original WinUiView factory signature,
API/runtime/project services **1**, and assembly **1.0.0.0** remain unchanged.
