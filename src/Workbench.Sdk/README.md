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

Optional testing contracts **1** are defined in `TestingContracts.cs` through
`IPluginTestingContext : IPluginProjectContext`, requiring host **0.4.0**.
This intermediate source/case-store increment defines the complete SDK boundary;
the native testing plugin, run implementation and host testing context are not
wired yet. Check for the derived interface; no placeholder service is exposed.
Existing project-only plugins may keep minimumHostVersion **0.3.0**, runtime-only
plugins **0.2.0**, and immutable business packages retain their original bytes.
