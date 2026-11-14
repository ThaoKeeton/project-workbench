[CmdletBinding()]
param([ValidateRange(30, 600)][int]$TimeoutSeconds = 180)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Native runtime probes require actual Windows WinUI and DPAPI.' }
Import-Module (Join-Path $PSScriptRoot 'NativeSmokeReport.psm1') -Force
$repo = Split-Path $PSScriptRoot -Parent
$app = Join-Path $repo 'artifacts/native/app/Workbench.App.exe'
$output = Join-Path $repo 'artifacts/native/runtime-probes'
if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw 'Build the native host and separate fixtures first.' }
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item $output -ItemType Directory -Force | Out-Null
$temporary = Join-Path ([IO.Path]::GetTempPath()) 'ProjectWorkbench-runtime-probe'
$managementRoot = Join-Path $temporary ([Guid]::NewGuid().ToString('N'))
$restartRoot = Join-Path $temporary ([Guid]::NewGuid().ToString('N'))
$phases = @('management', 'restart-seed', 'restart-verify', 'restart-disabled', 'update-verify', 'rollback-verify', 'remove-verify')
$completed = @()

function Write-NativeProbeFailureDetail([string]$Phase, [string]$Report, [string]$Output) {
    Write-Host "Native runtime probe $Phase failed. Report: $Report"
    foreach ($path in @($Report, "$Report.evidence.json", (Join-Path $Output "$Phase.stdout.log"), (Join-Path $Output "$Phase.stderr.log"))) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        if ((Get-Item -LiteralPath $path).Length -eq 0) { Write-Host "---- $path (empty) ----"; continue }
        Write-Host "---- $path ----"
        Get-Content -LiteralPath $path -Raw | Write-Host
    }
}
foreach ($phase in $phases) {
    $report = Join-Path $output "$phase.json"
    $root = if ($phase -eq 'management') { $managementRoot } else { $restartRoot }
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
    $process = $null
    try {
        # Each phase is a new OS process. WaitForExit completes before its successor starts.
        $arguments = @('--runtime-probe', $phase, '--probe-root', "`"$root`"", '--smoke-report', "`"$report`"")
        $process = Start-Process -FilePath $app -ArgumentList $arguments -PassThru -RedirectStandardOutput (Join-Path $output "$phase.stdout.log") -RedirectStandardError (Join-Path $output "$phase.stderr.log")
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "Isolated native runtime probe $phase timed out; process $($process.Id) remains owned. No force termination was requested." }
        if ($process.ExitCode -ne 0) {
            Write-NativeProbeFailureDetail -Phase $phase -Report $report -Output $output
            throw "Native runtime probe $phase exited with $($process.ExitCode)."
        }
        if (-not (Test-Path -LiteralPath $report -PathType Leaf)) { throw "Native runtime probe $phase did not write a report." }
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($result.succeeded -ne $true -or $result.probe -ne $phase -or $result.userRoot -ne $root -or $result.checks.Count -eq 0) { throw "Native runtime probe $phase did not establish its required success evidence." }
        $completed += @{ phase = $phase; processId = $result.processId; checks = @($result.checks).Count; report = $report }
        Write-Host "Native runtime probe passed: $phase ($(@($result.checks).Count) checks)"
    }
    catch {
        Write-NativeSmokeFailure -ReportPath $report -Reason $_.Exception.Message
        throw
    }
    finally { if ($null -ne $process) { $process.Dispose() } }
}
@{ succeeded = $true; managementRoot = $managementRoot; restartRoot = $restartRoot; phases = $completed } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'summary.json') -Encoding utf8
Write-Host "Native runtime probes completed: $output"
