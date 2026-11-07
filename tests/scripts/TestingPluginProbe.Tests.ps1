$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '../../scripts/TestingPluginProbe.psm1') -Force
$temp = Join-Path ([IO.Path]::GetTempPath()) ('testing-report-regression-' + [Guid]::NewGuid().ToString('N'))
New-Item $temp -ItemType Directory | Out-Null
try {
    # Synthetic runner metadata tests the actual orchestration; these are NOT native EXE evidence.
    foreach ($case in @('missing','wrong-pid','still-live','lease-held','incomplete','collection')) {
        $output = Join-Path $temp $case; New-Item $output -ItemType Directory | Out-Null
        $launches = [Collections.Generic.List[string]]::new()
        $original = $null
        $failed = $false
        try {
            Invoke-TestingPluginProbePhases -OutputDirectory $output -ExpectedRoot $temp -Observe {
                param($phase, $report, $observation)
                $launches.Add($phase)
                $observation.ProcessId = 202; $observation.HasExited = $true; $observation.ExitCode = 0; $observation.LeaseAvailable = $true
                if ($case -eq 'still-live') { $observation.HasExited = $false }
                if ($case -eq 'lease-held') { $observation.LeaseAvailable = $false }
                if ($case -ne 'missing') {
                    @{ schemaVersion = 1; succeeded = $true; probe = $phase; userRoot = $temp; processId = $(if ($case -eq 'wrong-pid') {101} else {202});
                       actualWindowClosed = $true; rootsRetired = $true; checks = @('independent-zip-native-page'); osDescription = 'Synthetic'; processArchitecture = 'X64'; syntheticInterruptionSeed = $true } | ConvertTo-Json -Depth 10 | Set-Content $report
                }
                if ($case -eq 'collection') { throw 'Synthetic collector failure' }
            } | Out-Null
        }
        catch { $failed = $true }
        $runner = Get-Content (Join-Path $output 'seed.runner.json') -Raw | ConvertFrom-Json
        if (-not $failed -or $runner.succeeded -or $runner.processId -ne 202 -or $launches.Count -ne 1 -or (Test-Path (Join-Path $output 'verify.runner.json'))) { throw "Failed phase launched successor or lost observation: $case" }
        if ($case -eq 'wrong-pid') {
            $native = Get-Content (Join-Path $output 'seed.json') -Raw | ConvertFrom-Json
            if ($native.processId -ne 101 -or -not $native.succeeded) { throw 'Native report was overwritten by the runner.' }
        }
        $firstBytes = [IO.File]::ReadAllBytes((Join-Path $output 'seed.runner.json'))
        try { Complete-TestingPluginProbeReport -ReportPath (Join-Path $output 'seed.json') -ProcessId 202 -HasExited $true -ExitCode 0 -ExpectedPhase seed -ExpectedRoot $temp -LeaseAvailable $true } catch { }
        if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$firstBytes, [byte[]][IO.File]::ReadAllBytes((Join-Path $output 'seed.runner.json')))) { throw 'A phase was completed twice and replaced its first failure.' }
    }
    $seedChecks = @('independent-zip-native-page','packaged-requirements-figma-io','traceable-manual-mapped-generation',
        'immediate-metadata-csv-bytes','immediate-step-mapping-invalidation','actual-file-share-save-failure-close-correction',
        'conflict-preserve-copy-reload-bytes','positive-actual-journal-bmp-native-decoder','negative-empty-title-run',
        'labeled-deliberate-assertion-failure','cancel-real-owned-store-io-before-first-step','native-project-isolation-empty-read',
        'synthetic-inflight-journal-seed-no-action','immediate-final-input-before-leave')
    $verifyChecks = @('separate-process-context-interrupted-synthetic-seed','restart-final-native-input-persisted',
        'reopened-exact-observed-text-bmp-native-decoder','linked-new-session-retry-frozen-step-one','restart-project-roster-preserved')
    foreach ($mode in @('accepted','corrupt-receipt','duplicate-check','same-pid')) {
        $output = Join-Path $temp $mode; New-Item $output -ItemType Directory | Out-Null
        $launches = [Collections.Generic.List[string]]::new(); $failed = $false
        try {
            $results = @(Invoke-TestingPluginProbePhases -OutputDirectory $output -ExpectedRoot $temp -Observe {
                param($phase,$report,$observation)
                $launches.Add($phase)
                $observation.ProcessId = if ($phase -eq 'seed' -or $mode -eq 'same-pid') {303} else {404}
                $observation.HasExited = $true; $observation.ExitCode = 0; $observation.LeaseAvailable = $true
                $receipts = @()
                foreach ($file in @('testing-cases.csv','store/runs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/events/0000000000.json','store/runs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/evidence/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/frame.bmp')) {
                    $path = Join-Path (Join-Path $output "$phase-evidence") $file
                    New-Item (Split-Path $path -Parent) -ItemType Directory -Force | Out-Null
                    [IO.File]::WriteAllBytes($path, [byte[]]@(1,2,3,4))
                    $receipts += @{path=$file;length=4;sha256=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()}
                }
                if ($mode -eq 'corrupt-receipt') { $receipts[0].sha256 = '0' * 64 }
                $checks = if ($phase -eq 'seed') {$seedChecks} else {$verifyChecks}
                if ($mode -eq 'duplicate-check') { $checks = @($checks) + @($checks[0]) }
                @{schemaVersion=1;succeeded=$true;probe=$phase;userRoot=$temp;processId=$observation.ProcessId;
                  actualWindowClosed=$true;rootsRetired=$true;checks=$checks;evidence=$receipts;
                  osDescription='Synthetic runner metadata, no native execution';processArchitecture='X64';syntheticInterruptionSeed=$true} | ConvertTo-Json -Depth 15 | Set-Content $report
            })
        }
        catch { $failed = $true }
        if ($mode -eq 'accepted') {
            if ($failed -or $results.Count -ne 2 -or $results[0].checks -ne 14 -or $results[1].checks -ne 5 -or $launches.Count -ne 2) { throw 'Complete evidence did not admit the two distinct phases.' }
        }
        elseif (-not $failed -or ($mode -ne 'same-pid' -and $launches.Count -ne 1)) { throw "Invalid evidence/PID admitted a successor: $mode" }
    }
    Write-Host 'Testing probe orchestration regressions passed: 6 initial failure modes, 2-phase positive metadata/hashed files, corrupt receipt, duplicate check and repeated PID. Synthetic script fixtures only.'

}
finally { Remove-Item $temp -Recurse -Force }
