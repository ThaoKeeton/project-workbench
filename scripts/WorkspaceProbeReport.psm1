function Complete-WorkspaceProbeReport {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ReportPath, [Parameter(Mandatory)][int]$ProcessId,
          [AllowNull()][object]$ExitCode, [Parameter(Mandatory)][bool]$HasExited,
          [Parameter(Mandatory)][ValidateSet('seed', 'verify')][string]$ExpectedPhase,
          [Parameter(Mandatory)][string]$ExpectedRoot, [Parameter(Mandatory)][bool]$LeaseAvailable)
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
          'old-pending-refusal', 'candidate-pending-refusal', 'archive-retention', 'native-reopen')
    } else { @('process-restore', 'restored-native-ui', 'restored-project-isolation') }
    if ($null -eq $reason) {
        foreach ($check in $required) {
            if (@($result['checks'] | Where-Object { $_ -ceq $check }).Count -ne 1) { $reason = 'WorkspaceChecksIncomplete' }
        }
    }
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
Export-ModuleMember -Function Complete-WorkspaceProbeReport
