# Native shell and production recovery integration

## User goal and evidence

The delivered Workbench0.4.0/655d7b6 opens WinUI on the user's Windows computer but shows project-not-ready with a generic local-file warning. Its blue stock titlebar/default icon also differs from the requested PowerToys/Windows-native unified shell. This is real user feedback; prior Server content-only PNGs do not certify the full window. No current environment exposes the user's Windows computer; do not connect another machine, infer that it is offline, or inspect its files. The user explicitly prefers exporting a default-redacted error report without computer access.

Source facts: MainWindow has no ExtendsContentIntoTitleBar, SetTitleBar, SystemBackdrop or application icon. Coordinator error conversion loses unrecognized failure categories; normal RunOptions has no ReportPath. Those are confirmed defects, not proof of the user's initialization root cause. Busy store, permissions and blocking files remain isolated reproduction cases.

## Ownership

The independent parent-coordinated diagnostic worker is implementing safe categories, fixed-stage snapshots, default-redacted user-chosen export, and recovery guidance from immutable655d7b6. This workstream must NOT concurrently edit HostFailure.cs, RunOptions.cs or WorkspaceRuntimeCoordinator.cs diagnostic logic. Parent will supply the patch/exact commit and tests. MainWindow initialization/catch and ProjectWorkspacePages recovery are also reserved to that worker until integration. This workstream owns the native shell, assets, and subsequent Windows integration/validation. Keep the old SourceAcceptance plan complete, all its evidence and the clean docs commit4f109994ea5b2ea0125db32bd1eddbc1e882300a. Main/publication stay paused pending this urgent work; no PR retry.

## Constraints

- WinUI3/.NET10/WindowsAppSDK1.8.260921001, native controls in one host window; no WebView or embedded independent app.
- SDK packages0.4.0, AssemblyVersion1.0.0.0, API/runtime/project/testing services1 and user-data schema1 remain compatible. Do not move Testing or modify UPCAM/DingTalk repositories.
- Default single-project UI keeps its existing project header and hides the multi-project selector; multiple projects retain switching. No unrelated navigation redesign.
- No real credentials/provider calls, Webhooks, DingTalk messages, Teambition writes, user-computer access, system proxy/network changes or new engine.
- Preserve files, user choices, all old downloads and failure evidence. No reset/delete/force/billing/release/PR operations. Use public standard Actions only when reviewed code needs Windows validation; one-day/minimal existing outputs.

## Approach

Use the stable WinUI TitleBar control with ExtendsContentIntoTitleBar and SetTitleBar, the native system caption buttons and drag regions, an original bundled Workbench icon, and Window.SystemBackdrop Mica where available. Retain a usable themed fallback if the OS cannot supply Mica. Do not simulate caption buttons or build a custom HWND/window manager. Keep the project header in its existing content row to avoid duplicating single/multi-project controls. A focused ShellTitleBar.cs partial owns this behavior and construction occurs before project initialization, so failed-start recovery retains a usable frame. MainWindow's constructor gets a narrow call; no diagnostic-worker code is changed. Theme changes follow existing actual host theme without changing saved preferences. No WindowsAppSDK2.x APIs from newer docs may be used.

The alternatives are keeping the stock frame (fails the user's explicit requirement) or fully owner-drawing caption buttons/hit-testing (adds unnecessary accessibility, DPI and close/drag risk). Native TitleBar is the selected implementation decision under the user's standing autonomy instruction. Personal-context retrieval is unavailable; the visible user/parent requirement and repository native-design specification are the authority, not an invented earlier mockup.

Primary references: https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/title-bar and https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops . Current docs include newer2.x features; use only APIs present in the pinned1.8 references. Content images exclude system caption overlays; full-window verification remains separate.

## Recovery integration

Integrate the independent diagnostic patch without rewriting its history or overwriting current shell changes. Its fixed stage/code/version/report-ID export must remain free of exception text/stack, paths, project contents, settings, machine names and credentials. Native export is available even when initial project creation fails; destination is explicitly chosen by the user. Failure to export must leave recovery controls and all user data intact. Diagnostics are not a claim that the user's machine issue is fixed.

## Actual acceptance

Portable checks cover amended safety/policy behavior and link the independent diagnostic tests. One preserved reference build verifies pinned WinUI APIs, resources and source provenance; it is not runtime proof. Existing real Windows tests and plugin probes must remain green. New owned CI-only full-window observations verify the normal default project opens, unified titlebar/icon before and after startup, caption close/maximize/restore/drag behavior, and theme contrast at usable narrow/normal widths. Capture only the newly launched Workbench HWND after validating its PID/creation identity; no arbitrary screen/device control or real user root. Save original full-window pixels with explicit scope and process/build identity, separate from the existing content gallery.

In fresh ephemeral Windows CI data, reproduce normal second launch while the first owns its lease, a known blocking file, and actual directory-write refusal where safely supported. Preserve each sentinel's bytes, fix only the test-owned blocker/ACL and retry through the actual UI, verify the root opens and closes normally, and export/parse the redacted report through actual controls. If native picker automation or full-frame capture cannot execute, keep a failing/blocked gate and describe it; no mocked picker or client-only PNG substitutes. Human Windows11/DPI/IME/accessibility, live accounts, active UPCAM diagnostic cancellation and the user's original machine remain unverified.

## Delivery

After code review and actual Windows evidence, a distinct host patch version0.4.1 may be used for the repaired host, with host compatibility default aligned and SDK versions/data schemas unchanged. Use exact source/build hashes; never relabel old0.4.0 bytes. Pair the final shipping host with the already produced UPCAM production0.1.1/source389ccb1 through its existing independent pairing owner; do not duplicate the vendor build or treat old0.1.0 companion51checks as new-pair acceptance. No release, deployment or main update before the urgent validation gate is assessed.
