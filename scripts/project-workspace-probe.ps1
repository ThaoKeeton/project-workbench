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
$completed = @()
foreach ($phase in @('seed', 'verify')) {
    $report = Join-Path $output "$phase.json"
    $process = $null
    $exited = $false
    $exitCode = $null
    $leaseAvailable = $false
    try {
        $arguments = @('--workspace-probe', $phase, '--probe-root', "`"$root`"", '--smoke-report', "`"$report`"")
        $process = Start-Process -FilePath $app -ArgumentList $arguments -PassThru -RedirectStandardOutput (Join-Path $output "$phase.stdout.log") -RedirectStandardError (Join-Path $output "$phase.stderr.log")
        $exited = $process.WaitForExit($TimeoutSeconds * 1000)
        if ($exited) {
            $exitCode = $process.ExitCode
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
                $leaseAvailable = $true
            }
            finally { foreach ($lease in $leases) { $lease.Dispose() } }
        }
        Complete-WorkspaceProbeReport -ReportPath $report -ProcessId $process.Id -ExitCode $exitCode -HasExited $exited -ExpectedPhase $phase -ExpectedRoot $root -LeaseAvailable $leaseAvailable
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if (@($completed | Where-Object processId -eq $process.Id).Count -ne 0) { throw 'WorkspaceProbeProcessReused' }
        $completed += @{ phase = $phase; processId = $process.Id; exitCode = $exitCode; hasExited = $exited; checks = @($result.checks).Count }
        Write-Host "Native workspace probe passed: $phase ($(@($result.checks).Count) checks)"
    }
    catch {
        # Always turn a timeout, lease failure or missing report into failure evidence. The process
        # remains owned on timeout; this script never force-kills it or launches its successor.
        if ($null -ne $process) {
            try { Complete-WorkspaceProbeReport -ReportPath $report -ProcessId $process.Id -ExitCode $exitCode -HasExited $exited -ExpectedPhase $phase -ExpectedRoot $root -LeaseAvailable $false } catch { }
        }
        throw
    }
    finally { if ($null -ne $process) { $process.Dispose() } }
}
@{ schemaVersion = 1; succeeded = $true; userRoot = $root; phases = $completed } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'summary.json') -Encoding utf8
Write-Host "Native workspace probes completed: $output"
