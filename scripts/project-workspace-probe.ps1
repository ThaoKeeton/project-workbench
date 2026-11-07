[CmdletBinding()]
param([ValidateRange(30, 600)][int]$TimeoutSeconds = 180)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Workspace probes require actual Windows WinUI and DPAPI.' }
Import-Module (Join-Path $PSScriptRoot 'WorkspaceProbeReport.psm1') -Force
$repo = Split-Path $PSScriptRoot -Parent
$app = Join-Path $repo 'artifacts/native/app/Workbench.App.exe'
$output = Join-Path $repo 'artifacts/native/workspace-probes'
$fixture = Join-Path $repo 'artifacts/native/fixtures/runtime/Workbench.NativeFixture.dll'
if (-not (Test-Path -LiteralPath $app -PathType Leaf) -or -not (Test-Path -LiteralPath $fixture -PathType Leaf)) {
    throw 'Build the shipping native host and the separate source-only native fixture first.'
}
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item $output -ItemType Directory -Force | Out-Null
$root = Join-Path (Join-Path ([IO.Path]::GetTempPath()) 'ProjectWorkbench-workspace-probe') ([Guid]::NewGuid().ToString('N'))
$completed = @(Invoke-WorkspaceProbePhases -OutputDirectory $output -ExpectedRoot $root -Observe {
    param($phase, $report, $observation)
    $process = $null
    try {
        $arguments = @('--workspace-probe', $phase, '--probe-root', "`"$root`"", '--smoke-report', "`"$report`"")
        $process = Start-Process -FilePath $app -ArgumentList $arguments -PassThru -RedirectStandardOutput (Join-Path $output "$phase.stdout.log") -RedirectStandardError (Join-Path $output "$phase.stderr.log")
        $observation.ProcessId = $process.Id
        $exited = $process.WaitForExit($TimeoutSeconds * 1000)
        $observation.HasExited = $exited
        if ($exited) {
            $observation.ExitCode = $process.ExitCode
            # OS-exit proof is independent of the native Closed event. Inspect only known managed
            # synthetic roots below this runner's fresh GUID, never a user/profile directory.
            $roots = @($root)
            foreach ($part in @($root, (Join-Path $root '.workspaces'), (Join-Path $root '.workspaces/projects'))) {
                if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'WorkspaceOwnedRootInvalid' }
            }
            $projects = Join-Path $root '.workspaces/projects'
            if (Test-Path -LiteralPath $projects -PathType Container) {
                foreach ($project in Get-ChildItem -LiteralPath $projects -Directory) {
                    if ($project.Name -cnotmatch '^[0-9a-f]{32}$' -or ($project.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'WorkspaceOwnedRootInvalid' }
                    $roots += $project.FullName
                }
            }
            $leases = @()
            try {
                foreach ($ownedRoot in $roots) {
                    if (((Get-Item -LiteralPath $ownedRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'WorkspaceOwnedRootInvalid' }
                    $leases += [IO.File]::Open((Join-Path $ownedRoot 'host.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
                }
                $observation.LeaseAvailable = $true
            }
            finally { foreach ($lease in $leases) { $lease.Dispose() } }
        }
    }
    finally { if ($null -ne $process) { $process.Dispose() } }
})
foreach ($result in $completed) {
    Write-Host "Native workspace probe passed: $($result.phase) ($($result.checks) checks)"
}
@{ schemaVersion = 1; succeeded = $true; userRoot = $root; phases = $completed } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'summary.json') -Encoding utf8
Write-Host "Native workspace probes completed: $output"
