# Workbench.Sdk 0.2.0

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

Native views additionally use `Workbench.WinUI.Sdk` 0.2.0 with Windows App SDK
**1.8.260921001**. Distribute plugin code and private dependencies without host
App/Core/SDK/Desktop binaries. Package updates require a host restart; hot unload
is not supported. A `referenceOnly` source snapshot proves compilation only.
