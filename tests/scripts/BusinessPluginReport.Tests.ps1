$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $repo 'scripts/BusinessPluginReport.psm1') -Force
$temporary = Join-Path ([IO.Path]::GetTempPath()) "workbench-business-report-$([Guid]::NewGuid().ToString('N'))"
New-Item $temporary -ItemType Directory | Out-Null
$inputs = @(@{ pluginId = 'first'; sourceCommit = 'abc'; sha256 = '123' }, @{ pluginId = 'second'; sourceCommit = 'def'; sha256 = '456' })
$passed = 0
$failures = [Collections.Generic.List[string]]::new()

function Check([string]$name, [scriptblock]$action) {
    try { & $action; $script:passed++ }
    catch { $script:failures.Add("${name}: $($_.Exception.Message)") }
}
function Seed([string]$name) {
    $path = Join-Path $temporary "$name.json"
    @{ succeeded = $true; probe = 'seed'; userRoot = $temporary; processId = 101; processCreationFileTimeUtc = 100;
        actualWindowClosed = $true; checks = @('actual vendor UI'); provenance = $inputs } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
    return $path
}
function Read([string]$path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable }
function Change([string]$path, [string]$field, [AllowNull()][object]$value) {
    $data = Read $path; $data[$field] = $value
    $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
}
function Complete([string]$path, [int]$processId = 101, [object]$code = 0, [bool]$exited = $true,
    [AllowNull()][object]$creation = 100, [AllowNull()][object]$exitTime = 300) {
    Complete-BusinessPluginReport -ReportPath $path -ProcessId $processId -ProcessCreationFileTimeUtc $creation -ProcessExitFileTimeUtc $exitTime `
        -ExitCode $code -HasExited $exited -ExpectedPhase seed -ExpectedRoot $temporary -ExpectedInputs $inputs
}
function Refuse([scriptblock]$action) {
    $failed = $false
    try { & $action }
    catch {
        if ($_.FullyQualifiedErrorId -match 'NamedParameterNotFound|CommandNotFound|ParameterBinding') { throw "Regression setup failed: $($_.Exception.Message)" }
        $failed = $true
    }
    if (-not $failed) { throw 'Invalid process identity evidence was accepted.' }
}
function Reject([scriptblock]$action, [string]$path) {
    Refuse $action
    $result = Read $path
    if ($result['succeeded'] -ne $false -or [string]::IsNullOrWhiteSpace($result['runnerError'])) { throw 'Rejected process evidence was not persisted as failed.' }
}
function Phase([string]$name, [int]$processId = 101, [long]$creation = 100, [long]$exitTime = 150) {
    return @{ phase = $name; succeeded = $true; processId = $processId; processCreationFileTimeUtc = $creation; processExitFileTimeUtc = $exitTime;
        nativeProcessId = $processId; nativeProcessCreationFileTimeUtc = $creation; exitCode = 0; hasExited = $true; checks = 1 }
}

try {
    # Removing birth comparison lets this stale same-numeric-PID report through and fails this regression.
    Check 'stale same-PID native birth is rejected and both identities survive' {
        $path = Seed stale
        Reject { Complete $path -creation 200 -exitTime 300 } $path
        $result = Read $path
        if ($result['nativeProcessId'] -ne 101 -or $result['nativeProcessCreationFileTimeUtc'] -ne 100 -or
            $result['processId'] -ne 101 -or $result['processCreationFileTimeUtc'] -ne 200 -or $result['processExitFileTimeUtc'] -ne 300) {
            throw 'Native and independent launcher identities were not both preserved on rejection.'
        }
    }
    Check 'valid native and launcher birth/exit are persisted as integers' {
        $path = Seed valid; Complete $path
        $result = Read $path
        if ($result['exitCode'] -ne 0 -or $result['processId'] -ne 101 -or $result['nativeProcessId'] -ne 101 -or
            $result['nativeProcessCreationFileTimeUtc'] -ne 100 -or $result['processCreationFileTimeUtc'] -isnot [long] -or
            $result['processCreationFileTimeUtc'] -ne 100 -or $result['processExitFileTimeUtc'] -isnot [long] -or
            $result['processExitFileTimeUtc'] -ne 300 -or -not $result['hasExited'] -or -not $result['succeeded']) {
            throw 'Actual integer process birth/exit evidence was not persisted.'
        }
    }
    Check 'FILETIME values above floating-point exact range retain all integer bits' {
        $path = Seed precise; Change $path processCreationFileTimeUtc 133000000000000001
        Complete $path -creation 133000000000000001 -exitTime 133000000000000099
        $result = Read $path
        if ($result['nativeProcessCreationFileTimeUtc'] -ne 133000000000000001 -or $result['processCreationFileTimeUtc'] -ne 133000000000000001 -or
            $result['processExitFileTimeUtc'] -ne 133000000000000099) { throw 'FILETIME integer precision was lost.' }
    }
    Check 'missing native birth is rejected' {
        $path = Seed missing-native-birth; $data = Read $path; $data.Remove('processCreationFileTimeUtc')
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
        Reject { Complete $path } $path
        if ($null -ne (Read $path)['nativeProcessCreationFileTimeUtc']) { throw 'Missing native birth was fabricated.' }
    }
    $invalidTimes = @(
        @{ name = 'null'; value = $null }, @{ name = 'zero'; value = 0 }, @{ name = 'negative'; value = -1 },
        @{ name = 'string'; value = '100' }, @{ name = 'float'; value = 100.0 }, @{ name = 'boolean'; value = $true },
        @{ name = 'overflow'; value = [ulong]::MaxValue })
    foreach ($invalid in $invalidTimes) {
        Check "native birth refuses $($invalid.name)" {
            $path = Seed "native-$($invalid.name)"; Change $path processCreationFileTimeUtc $invalid.value
            Reject { Complete $path } $path
        }
        Check "launcher creation refuses $($invalid.name)" {
            $path = Seed "creation-$($invalid.name)"; Reject { Complete $path -creation $invalid.value } $path
        }
        Check "launcher exit refuses $($invalid.name)" {
            $path = Seed "exit-$($invalid.name)"; Reject { Complete $path -exitTime $invalid.value } $path
        }
    }
    Check 'malformed native birth remains visible on rejection' {
        $path = Seed malformed-preserved; Change $path processCreationFileTimeUtc '100'
        Reject { Complete $path } $path
        $result = Read $path
        if ($result['nativeProcessCreationFileTimeUtc'] -isnot [string] -or $result['nativeProcessCreationFileTimeUtc'] -cne '100' -or
            $result['processCreationFileTimeUtc'] -ne 100) { throw 'Rejected native birth was coerced or lost.' }
    }
    Check 'exit preceding creation is rejected' {
        $path = Seed early-exit; Reject { Complete $path -exitTime 99 } $path
    }
    Check 'native PID mismatch preserves native and launcher observations' {
        $path = Seed pid-mismatch; Reject { Complete $path -processId 202 } $path
        $result = Read $path
        if ($result['nativeProcessId'] -ne 101 -or $result['processId'] -ne 202 -or $result['nativeProcessCreationFileTimeUtc'] -ne 100 -or
            $result['processCreationFileTimeUtc'] -ne 100 -or $result['processExitFileTimeUtc'] -ne 300) { throw 'Mismatched PID or known process times were lost.' }
    }
    Check 'preexisting native observations cannot be replaced by runner fields' {
        $path = Seed prior-native; Change $path nativeProcessId 202; Change $path nativeProcessCreationFileTimeUtc 75
        Reject { Complete $path } $path
        $result = Read $path
        if ($result['nativeProcessId'] -ne 202 -or $result['nativeProcessCreationFileTimeUtc'] -ne 75 -or
            $result['processId'] -ne 101 -or $result['processCreationFileTimeUtc'] -ne 100) { throw 'Incoming native identity was overwritten.' }
    }
    Check 'nonzero actual exit remains failed with identity' {
        $path = Seed nonzero; Reject { Complete $path -code 19 } $path
        $result = Read $path
        if ($result['exitCode'] -ne 19 -or $result['nativeProcessCreationFileTimeUtc'] -ne 100 -or $result['processExitFileTimeUtc'] -ne 300) { throw 'Nonzero exit evidence was lost.' }
    }
    foreach ($code in @($null, '0', 0.0, $false)) {
        Check "actual exit code refuses null or noninteger $code" {
            $path = Seed "code-$([Guid]::NewGuid().ToString('N'))"; Reject { Complete $path -code $code } $path
        }
    }
    Check 'timeout preserves known birth without fabricating exit' {
        $path = Seed timeout; Reject { Complete $path -code $null -exited $false -exitTime $null } $path
        $result = Read $path
        if ($result['hasExited'] -ne $false -or $null -ne $result['exitCode'] -or $null -ne $result['processExitFileTimeUtc'] -or
            $result['processCreationFileTimeUtc'] -ne 100) { throw 'Timeout evidence invented an exit or lost creation.' }
    }
    Check 'unconfirmed exit cannot become success even with claimed exit fields' {
        $path = Seed unconfirmed; Reject { Complete $path -exited $false } $path
    }
    Check 'missing native report preserves independent facts and pinned inputs' {
        $path = Join-Path $temporary missing.json; Reject { Complete $path } $path
        $result = Read $path
        if ($result['expectedProvenance'].Count -ne 2 -or $null -ne $result['nativeProcessId'] -or $null -ne $result['nativeProcessCreationFileTimeUtc'] -or
            $result['processId'] -ne 101 -or $result['processCreationFileTimeUtc'] -ne 100 -or $result['processExitFileTimeUtc'] -ne 300) {
            throw 'Missing report lost known runner facts or invented native identity.'
        }
    }
    foreach ($json in @('not-json', '[]', '42')) {
        Check "malformed native object refuses $json" {
            $path = Join-Path $temporary "bad-$([Guid]::NewGuid().ToString('N')).json"; $json | Set-Content -LiteralPath $path
            Reject { Complete $path } $path
        }
    }
    foreach ($field in @('checks', 'actualWindowClosed', 'userRoot', 'probe', 'provenance')) {
        Check "missing required native $field remains rejected" {
            $path = Seed "missing-$field"; $data = Read $path; $data.Remove($field)
            $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
            Reject { Complete $path } $path
        }
    }
    foreach ($change in @(
        @{ field = 'checks'; value = @() }, @{ field = 'actualWindowClosed'; value = $false },
        @{ field = 'userRoot'; value = 'wrong-root' }, @{ field = 'probe'; value = 'restart' },
        @{ field = 'succeeded'; value = $false }, @{ field = 'succeeded'; value = 'true' }, @{ field = 'processId'; value = '101' })) {
        Check "invalid native $($change.field) remains rejected" {
            $path = Seed "wrong-$([Guid]::NewGuid().ToString('N'))"; Change $path $change.field $change.value
            Reject { Complete $path } $path
        }
    }
    Check 'wrong pinned source remains rejected' {
        $path = Seed source; $data = Read $path; $data.provenance[0].sourceCommit = 'wrong'
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
        Reject { Complete $path } $path
    }
    foreach ($mainState in @('missing', 'failed', 'mismatched')) {
        Check "valid diagnostic sidecar never repairs $mainState main report" {
            $path = Seed "sidecar-$mainState"
            if ($mainState -eq 'missing') { Remove-Item -LiteralPath $path }
            elseif ($mainState -eq 'failed') { Change $path succeeded $false; Change $path processId $null; Change $path processCreationFileTimeUtc $null }
            else { Change $path processCreationFileTimeUtc 75 }
            @{ succeeded = $false; probe = 'seed'; userRoot = $temporary; processId = 101; processCreationFileTimeUtc = 100 } |
                ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($path, '.evidence.json'))
            Reject { Complete $path } $path
            $result = Read $path
            $diagnostic = $result['nativeFailureEvidence']
            if ($null -eq $diagnostic -or $diagnostic['diagnosticOnly'] -ne $true -or $diagnostic['source'] -cne 'native-failure-sidecar' -or
                $diagnostic['processId'] -ne 101 -or $diagnostic['processCreationFileTimeUtc'] -ne 100 -or $diagnostic['probe'] -cne 'seed' -or
                $diagnostic['userRoot'] -cne $temporary -or $result['processCreationFileTimeUtc'] -ne 100) { throw 'Captured diagnostic identity or independent runner facts were lost.' }
            if ($mainState -ne 'mismatched' -and ($null -ne $result['nativeProcessId'] -or $null -ne $result['nativeProcessCreationFileTimeUtc'])) {
                throw 'A diagnostic sidecar filled missing main-report native identity.'
            }
            if ($mainState -eq 'mismatched' -and $result['nativeProcessCreationFileTimeUtc'] -ne 75) { throw 'Diagnostic identity overwrote mismatched main identity.' }
        }
    }
    foreach ($json in @('not-json', '[]', '{}', '{"processId":101,"processCreationFileTimeUtc":75,"probe":"old","userRoot":"old-root"}')) {
        Check "unusable or stale diagnostic sidecar never repairs a failed main report: $json" {
            $path = Seed "bad-sidecar-$([Guid]::NewGuid().ToString('N'))"; Change $path succeeded $false
            $json | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($path, '.evidence.json'))
            Reject { Complete $path } $path
            $result = Read $path
            if ($result['nativeProcessId'] -ne 101 -or $result['nativeProcessCreationFileTimeUtc'] -ne 100 -or
                $result['processCreationFileTimeUtc'] -ne 100) { throw 'A diagnostic sidecar erased original native or runner identity.' }
        }
    }
    Check 'diagnostic match requires a strictly typed independent launcher birth' {
        $path = Seed diagnostic-type
        @{ probe = 'seed'; userRoot = $temporary; processId = 101; processCreationFileTimeUtc = 100 } |
            ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($path, '.evidence.json'))
        Reject { Complete $path -creation '100' } $path
        if ((Read $path)['nativeFailureEvidence']['matchesRunnerIdentity'] -ne $false) { throw 'Malformed launcher birth was labeled a matching identity.' }
    }

    # Dropping tuple uniqueness fails the duplicate case; numeric PID reuse alone must continue to pass.
    Check 'same numeric PID with different verified births identifies distinct instances' {
        $rows = @((Phase first 101 100 150), (Phase second 101 200 300))
        Assert-BusinessProcessInstances -Phases $rows -ExpectedPhases @('first', 'second')
        $roundTrip = $rows | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        Assert-BusinessProcessInstances -Phases $roundTrip -ExpectedPhases @('first', 'second')
    }
    Check 'same PID and birth tuple cannot count twice' {
        $rows = @((Phase first 101 100 150), (Phase second 101 100 300))
        Refuse { Assert-BusinessProcessInstances -Phases $rows -ExpectedPhases @('first', 'second') }
    }
    Check 'missing phase is rejected' {
        Refuse { Assert-BusinessProcessInstances -Phases @((Phase first)) -ExpectedPhases @('first', 'second') }
    }
    Check 'empty phase sequence is rejected' {
        Refuse { Assert-BusinessProcessInstances -Phases @() -ExpectedPhases @('first') }
    }
    Check 'reordered phases are rejected' {
        $rows = @((Phase second 101 100 150), (Phase first 202 200 300))
        Refuse { Assert-BusinessProcessInstances -Phases $rows -ExpectedPhases @('first', 'second') }
    }
    Check 'phase names are exact and case sensitive' {
        Refuse { Assert-BusinessProcessInstances -Phases @((Phase First)) -ExpectedPhases @('first') }
    }
    foreach ($change in @(
        @{ field = 'succeeded'; value = $false }, @{ field = 'succeeded'; value = 'true' }, @{ field = 'processId'; value = 0 },
        @{ field = 'processId'; value = '101' }, @{ field = 'nativeProcessId'; value = 202 },
        @{ field = 'processCreationFileTimeUtc'; value = $null }, @{ field = 'processCreationFileTimeUtc'; value = 100.0 },
        @{ field = 'nativeProcessCreationFileTimeUtc'; value = 99 }, @{ field = 'nativeProcessCreationFileTimeUtc'; value = '100' },
        @{ field = 'processExitFileTimeUtc'; value = $null }, @{ field = 'processExitFileTimeUtc'; value = 99 },
        @{ field = 'processExitFileTimeUtc'; value = '150' }, @{ field = 'exitCode'; value = 1 }, @{ field = 'exitCode'; value = '0' },
        @{ field = 'hasExited'; value = $false }, @{ field = 'hasExited'; value = 'true' }, @{ field = 'checks'; value = 0 })) {
        Check "instance sequence refuses invalid $($change.field)=$($change.value)" {
            $row = Phase first; $row[$change.field] = $change.value
            Refuse { Assert-BusinessProcessInstances -Phases @($row) -ExpectedPhases @('first') }
        }
    }
    Write-Output "Business plugin process report regressions: $passed passed, $($failures.Count) failed"
    foreach ($failure in $failures) { Write-Output "FAIL $failure" }
    if ($failures.Count -gt 0) { throw 'Business process identity regressions failed.' }
}
finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
