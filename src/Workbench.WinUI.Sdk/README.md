# Workbench.WinUI.Sdk 0.4.0

Native WinUI view contracts for Project Workbench plugins. Use with
`Workbench.Sdk` **0.4.0**, .NET SDK **10.0.403** and Windows App SDK
**1.8.260921001**, targeting Windows x64. API **1**, the original `IPluginContext`
and assembly identity **1.0.0.0** remain stable. Optional runtime services version
**1** require host **0.2.0** or newer.

Register `WinUiView` page/settings factories during initialization. The host
creates fresh native controls on its UI thread inside the shared window and
resource/theme environment. Keep factories short, use registered background work
for long operations, and let Stop join owned work. Plugin XAML/PRI loading,
WebView hosting, Desktop application reuse and hot unload are unsupported.

Plugins run in-process after explicit fingerprint trust, without a security
sandbox. Trust and resume do not grant business consent. Use declared secret keys
for credentials and expose only safe error codes to host diagnostics. Ship only
plugin/private dependencies, without host App/Core/SDK/Desktop binaries.

Windows-built packages retain this SDK's generated PRI. A snapshot marked
`referenceOnly` omits native resource generation and proves C# compilation only;
it must not substitute for Windows packaging or actual WinUI acceptance.

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
