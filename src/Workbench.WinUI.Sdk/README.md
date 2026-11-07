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

Optional testing contracts **1** are defined in `TestingContracts.cs` through
`IPluginTestingContext : IPluginProjectContext`, requiring host **0.4.0**.
This intermediate source/case-store increment defines the complete SDK boundary;
the native testing plugin, run implementation and host testing context are not
wired yet. Check for the derived interface; no placeholder service is exposed.
Existing project-only plugins may keep minimumHostVersion **0.3.0**, runtime-only
plugins **0.2.0**, and immutable business packages retain their original bytes.
