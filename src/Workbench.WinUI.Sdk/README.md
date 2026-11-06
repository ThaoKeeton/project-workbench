# Workbench.WinUI.Sdk 0.2.0

Native WinUI view contracts for Project Workbench plugins. Use with
`Workbench.Sdk` **0.2.0**, .NET SDK **10.0.403** and Windows App SDK
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
