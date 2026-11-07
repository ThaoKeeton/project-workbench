$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $repo 'scripts/WorkspaceProbeReport.psm1') -Force
$temporary = Join-Path ([IO.Path]::GetTempPath()) "workbench-workspace-phase-$([Guid]::NewGuid().ToString('N'))"
New-Item $temporary -ItemType Directory | Out-Null
try {
    # Synthetic metadata exercises the production orchestration catch. It is not native evidence.
    $failures = 0
    foreach ($case in @('identity', 'missing', 'incomplete')) {
        $directory = Join-Path $temporary $case
        New-Item $directory -ItemType Directory | Out-Null
        $path = Join-Path $directory 'seed.json'
        if ($case -ne 'missing') {
            @{ schemaVersion = 1; succeeded = $true; probe = 'seed'; userRoot = $temporary;
               processId = $(if ($case -eq 'identity') { 101 } else { 202 });
               actualWindowClosed = $true; rootsRetired = $true; checks = @('legacy-adoption') } |
                ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
        }
        $launches = [Collections.Generic.List[string]]::new()
        $failed = $false
        try {
            Invoke-WorkspaceProbePhases -OutputDirectory $directory -ExpectedRoot $temporary -Observe {
                param($phase, $report, $observation)
                $launches.Add($phase)
                $observation.ProcessId = 202
                $observation.ExitCode = 0
                $observation.HasExited = $true
                $observation.LeaseAvailable = $true
            } | Out-Null
        }
        catch { $failed = $true }
        $result = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $reason = switch ($case) {
            identity { 'WorkspaceIdentityOrRetirementMissing' }
            missing { 'WorkspaceReportMissing' }
            incomplete { 'WorkspaceChecksIncomplete' }
        }
        $nativePid = if ($case -eq 'identity') { 101 } elseif ($case -eq 'missing') { $null } else { 202 }
        if (-not $failed -or $result.succeeded -ne $false -or $result.runnerError -cne $reason -or
            $result.nativeProcessId -ne $nativePid -or $result.processId -ne 202 -or
            $result.exitCode -ne 0 -or $result.hasExited -ne $true -or $result.rootLeaseAvailable -ne $true -or
            $launches.Count -ne 1 -or $launches[0] -cne 'seed' -or (Test-Path (Join-Path $directory 'verify.json'))) {
            $failures++
            Write-Output "Rejected $case`: reason=$($result.runnerError), nativePid=$($result.nativeProcessId), lease=$($result.rootLeaseAvailable), launches=$($launches.Count)"
        }
    }
    if ($failures -ne 0) { throw "Production phase lost first rejection/native PID/actual lease or launched a successor: $failures cases" }
    $seedChecks = @('legacy-adoption', 'single-native-ui', 'multi-native-ui', 'project-isolation', 'draft-retention',
        'old-pending-refusal', 'candidate-pending-refusal', 'archive-retention', 'native-reopen',
        'busy-switch-native-close', 'busy-action-native-close', 'before-initialize-native-close',
        'immediate-context-switch', 'immediate-context-close', 'settled-switch-private-correction')
    $directory = Join-Path $temporary 'observation'
    New-Item $directory -ItemType Directory | Out-Null
    $observationPath = Join-Path $directory 'seed.json'
    @{ schemaVersion = 1; succeeded = $true; probe = 'seed'; userRoot = $temporary; processId = 202;
       actualWindowClosed = $true; rootsRetired = $true; checks = $seedChecks } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $observationPath
    $launches = [Collections.Generic.List[string]]::new()
    $failed = $false
    try {
        Invoke-WorkspaceProbePhases -OutputDirectory $directory -ExpectedRoot $temporary -Observe {
            param($phase, $report, $observation)
            $launches.Add($phase)
            $observation.ProcessId = 202
            $observation.ExitCode = 0
            $observation.HasExited = $true
            $observation.LeaseAvailable = $true
            throw 'Synthetic collection failure after known exit and lease'
        } | Out-Null
    }
    catch { $failed = $true }
    $result = Get-Content -LiteralPath $observationPath -Raw | ConvertFrom-Json
    if (-not $failed -or $launches.Count -ne 1 -or $launches[0] -cne 'seed' -or $result.succeeded -ne $false -or
        $result.runnerError -cne 'WorkspaceObservationFailed' -or $result.nativeProcessId -ne 202 -or
        $result.rootLeaseAvailable -ne $true -or $result.exitCode -ne 0 -or $result.hasExited -ne $true) {
        throw 'Production collection catch did not preserve actual evidence and failed status.'
    }
    # The same production helper admits a successor only after a complete accepted seed.
    $directory = Join-Path $temporary 'accepted'
    New-Item $directory -ItemType Directory | Out-Null
    $launches = [Collections.Generic.List[string]]::new()
    foreach ($phase in @('seed', 'verify')) {
        $path = Join-Path $directory "$phase.json"
        $nativePid = if ($phase -eq 'seed') { 303 } else { 404 }
        $checks = if ($phase -eq 'seed') { $seedChecks } else { @('process-restore', 'restored-native-ui', 'restored-project-isolation') }
        @{ schemaVersion = 1; succeeded = $true; probe = $phase; userRoot = $temporary; processId = $nativePid;
           actualWindowClosed = $true; rootsRetired = $true; checks = $checks } |
            ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
    }
    $completed = @(Invoke-WorkspaceProbePhases -OutputDirectory $directory -ExpectedRoot $temporary -Observe {
        param($phase, $report, $observation)
        $launches.Add($phase)
        $observation.ProcessId = if ($phase -eq 'seed') { 303 } else { 404 }
        $observation.ExitCode = 0
        $observation.HasExited = $true
        $observation.LeaseAvailable = $true
    })
    foreach ($phase in @('seed', 'verify')) {
        $nativePid = if ($phase -eq 'seed') { 303 } else { 404 }
        $result = Get-Content -LiteralPath (Join-Path $directory "$phase.json") -Raw | ConvertFrom-Json
        if (-not $result.succeeded -or $result.nativeProcessId -ne $nativePid -or $result.processId -ne $nativePid -or
            -not $result.hasExited -or -not $result.rootLeaseAvailable -or $result.exitCode -ne 0) { throw 'Accepted phase lost actual runner evidence.' }
    }
    if ($launches.Count -ne 2 -or $launches[0] -cne 'seed' -or $launches[1] -cne 'verify' -or
        $completed.Count -ne 2 -or $completed[0].phase -cne 'seed' -or $completed[0].processId -ne 303 -or $completed[0].checks -ne 15 -or
        $completed[1].phase -cne 'verify' -or $completed[1].processId -ne 404 -or $completed[1].checks -ne 3) { throw 'Accepted phases did not retain succession.' }
    Write-Output 'Workspace production phase regressions: 5 passed (synthetic metadata only)'
}
finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
