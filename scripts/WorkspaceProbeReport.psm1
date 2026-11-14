function Complete-WorkspaceProbeReport {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ReportPath, [Parameter(Mandatory)][int]$ProcessId,
          [AllowNull()][object]$ExitCode, [Parameter(Mandatory)][bool]$HasExited,
          [Parameter(Mandatory)][ValidateSet('seed', 'verify')][string]$ExpectedPhase,
          [Parameter(Mandatory)][string]$ExpectedRoot, [Parameter(Mandatory)][bool]$LeaseAvailable,
          [bool]$ObservationFailed = $false)
    $result = @{}
    $reason = $null
    if (Test-Path -LiteralPath $ReportPath -PathType Leaf) {
        try { $result = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json -AsHashtable }
        catch { $reason = 'WorkspaceReportUnreadable' }
    }
    else { $reason = 'WorkspaceReportMissing' }
    if ($result -isnot [System.Collections.IDictionary]) { $result = @{}; $reason = 'WorkspaceReportInvalid' }
    if (-not $HasExited) { $reason = 'WorkspaceProcessStillOwned' }
    elseif ($null -eq $ExitCode -or $ExitCode -ne 0) { $reason = 'WorkspaceProcessFailed' }
    elseif (-not $LeaseAvailable) { $reason = 'WorkspaceRootStillOwned' }
    elseif ($null -eq $reason -and ($result['schemaVersion'] -ne 1 -or $result['succeeded'] -ne $true -or
        $result['processId'] -ne $ProcessId -or $result['probe'] -ne $ExpectedPhase -or $result['userRoot'] -ne $ExpectedRoot -or
        $result['actualWindowClosed'] -ne $true -or $result['rootsRetired'] -ne $true)) { $reason = 'WorkspaceIdentityOrRetirementMissing' }
    $required = if ($ExpectedPhase -eq 'seed') {
        @('legacy-adoption', 'single-native-ui', 'multi-native-ui', 'project-isolation', 'draft-retention',
          'old-pending-refusal', 'candidate-pending-refusal', 'archive-retention', 'native-reopen',
          'busy-switch-native-close', 'busy-action-native-close', 'before-initialize-native-close',
          'immediate-context-switch', 'immediate-context-close', 'settled-switch-private-correction',
          'actual-dispose-context-correction')
    } else { @('process-restore', 'restored-native-ui', 'restored-project-isolation') }
    if ($null -eq $reason) {
        foreach ($check in $required) {
            if (@($result['checks'] | Where-Object { $_ -ceq $check }).Count -ne 1) { $reason = 'WorkspaceChecksIncomplete' }
        }
    }
    if ($null -eq $reason -and $ObservationFailed) { $reason = 'WorkspaceObservationFailed' }
    $result['nativeProcessId'] = $result['processId']
    $result['processId'] = $ProcessId
    $result['exitCode'] = $ExitCode
    $result['hasExited'] = $HasExited
    $result['rootLeaseAvailable'] = $LeaseAvailable
    $result['runnerCompletedUtc'] = [DateTimeOffset]::UtcNow
    if ($null -ne $reason) { $result['succeeded'] = $false; $result['runnerError'] = $reason }
    New-Item (Split-Path $ReportPath -Parent) -ItemType Directory -Force | Out-Null
    $result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    if ($null -ne $reason) { throw $reason }
}
function Invoke-WorkspaceProbePhases {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$OutputDirectory,
          [Parameter(Mandatory)][string]$ExpectedRoot, [Parameter(Mandatory)][scriptblock]$Observe)
    $completed = @()
    foreach ($phase in @('seed', 'verify')) {
        $report = Join-Path $OutputDirectory "$phase.json"
        $observation = @{ ProcessId = $null; ExitCode = $null; HasExited = $false; LeaseAvailable = $false }
        $identity = @{ ReportPath = $report; ExpectedPhase = $phase; ExpectedRoot = $ExpectedRoot }
        try {
            & $Observe $phase $report $observation
        }
        catch {
            # Collection can fail before validation (timeout/lease IO). Finalize those actual
            # observations once. A validation rejection below must never re-enter this catch.
            if ($null -ne $observation.ProcessId) {
                try { Complete-WorkspaceProbeReport @identity @observation -ObservationFailed $true } catch { }
            }
            throw
        }
        Complete-WorkspaceProbeReport @identity @observation
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if (@($completed | Where-Object processId -eq $result.processId).Count -ne 0) { throw 'WorkspaceProbeProcessReused' }
        $completed += @{ phase = $phase; processId = $result.processId; exitCode = $result.exitCode; hasExited = $result.hasExited; checks = @($result.checks).Count }
    }
    return $completed
}
Export-ModuleMember -Function Complete-WorkspaceProbeReport, Invoke-WorkspaceProbePhases
