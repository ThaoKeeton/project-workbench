# Independent business package inputs

These are immutable binaries built by the business repositories, used only by opt-in integration tests. They are never copied into the shipping app's bundled `Plugins` directory, never referenced by a production project, and never rebuilt or patched by this repository.

| Input | Source commit | Upstream successful workflow | ZIP SHA256 |
| --- | --- | --- | --- |
| DingTalk 0.3.1 | `b50586ff321f9932a35235de75f8c9b4996a5a61` | [42284239767](https://github.com/ThaoKeeton/dingtalk-insights/actions/runs/42284239767) | `1e744a83ff85287de7acd43509b7df4822d99a9781e9af98dd3e0a448b4cad0f` |
| UPCAM production 0.1.0, transport repack | `6370b9df46af564fde022924d1dbeace7d575c6a` | [42268731102](https://github.com/ThaoKeeton/upcam-task-digest/actions/runs/42268731102) | `3e07b7db0e3f17871b88ce1e45ac09d3b8724e6628a35fd5e0cdc1eb9ad95dbe` |

Each `provenance.json` pins the original artifact, publisher ZIP, manifest and SHA256/length of every file. The UPCAM publisher ZIP has exactly one known `com.upcam.task-digest/` wrapper. Its pinned transport repack removes only that prefix; all 714 file contents were compared with the publisher ZIP and are byte-identical. It is not a new publisher release. The strict host ZIP importer still requires a root manifest and does not guess arbitrary wrappers. The vendor-supported folder install remains available.

`scripts/import-business-plugin.ps1` audits both archives before copying them to `artifacts/native/fixtures/business`, outside the shipping app. It checks the full ZIP and every expanded file against these pins and rejects duplicate host SDK/Core/App, WinUI/WinRT/Desktop/PRI/XAML, credential payloads and independent GUI executables. UPCAM's private `worker/UPCAMPluginWorker.exe` and its Python runtime are the explicitly permitted headless business worker. DingTalk has only its private plugin/bridge/Runtime/Core dependencies.

The acceptance driver uses DingTalk's actual local demo source/Core/MonitorSupervisor and UPCAM's actual production `preview-sample` worker/Core with fictional input. It does not enter real credentials, open external standalone/DWS profiles, invoke DWS/Teambition/OpenAI/Webhook providers or perform sends. Send integration remains unexecuted. The upstream UPCAM workflow builds but does not publish its different acceptance-ID synthetic package, so production automatic provider success is also unexecuted.

Host credential coverage is exact: DingTalk declares three definitions but defers actual host value consumption upstream; UPCAM uses the host vault only for a generated resume-binding key, not business credentials. With no saved automatic authority in this test, no such key is created. Business credentials retain their upstream canonical DPAPI routes.

To change these inputs, obtain a separately built successful upstream artifact, review its real source/registrations/inventory, and replace the package and all provenance pins together. Do not manufacture a business fixture or copy a vendor source-linked host into the shipping app.
