function Test-BusinessInt64([AllowNull()][object]$Value) {
    return ($Value -is [sbyte] -or $Value -is [byte] -or $Value -is [int16] -or $Value -is [uint16] -or
        $Value -is [int32] -or $Value -is [uint32] -or $Value -is [int64] -or
        ($Value -is [uint64] -and $Value -le [long]::MaxValue))
}

function Test-BusinessPositiveInt64([AllowNull()][object]$Value) {
    return ((Test-BusinessInt64 $Value) -and $Value -gt 0)
}

function Get-BusinessProcessField([AllowNull()][object]$Record, [string]$Name) {
    if ($Record -is [System.Collections.IDictionary]) { return $Record[$Name] }
    if ($null -ne $Record) {
        $property = $Record.PSObject.Properties[$Name]
        if ($null -ne $property) { return $property.Value }
    }
    return $null
}

function Complete-BusinessPluginReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ReportPath,
        [Parameter(Mandatory)][int]$ProcessId,
        [AllowNull()][object]$ProcessCreationFileTimeUtc,
        [AllowNull()][object]$ProcessExitFileTimeUtc,
        [AllowNull()][object]$ExitCode,
        [Parameter(Mandatory)][bool]$HasExited,
        [Parameter(Mandatory)][string]$ExpectedPhase,
        [Parameter(Mandatory)][string]$ExpectedRoot,
        [Parameter(Mandatory)][object[]]$ExpectedInputs
    )
    $result = @{}
    $reason = $null
    if (Test-Path -LiteralPath $ReportPath -PathType Leaf) {
        try { $result = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json -AsHashtable }
        catch { $reason = 'The required native business report was unreadable.' }
    }
    else { $reason = 'The process did not produce its required native business report.' }
    if ($result -isnot [System.Collections.IDictionary]) { $result = @{}; $reason = 'The native business report was not an object.' }
    # Revalidation must retain the original native identity, never rename a previous runner observation as native.
    if (-not $result.Contains('nativeProcessId')) { $result['nativeProcessId'] = $result['processId'] }
    if (-not $result.Contains('nativeProcessCreationFileTimeUtc')) { $result['nativeProcessCreationFileTimeUtc'] = $result['processCreationFileTimeUtc'] }
    if (-not $HasExited) { $reason = 'The process timed out and remains owned; no successor or force termination was requested.' }
    elseif (-not (Test-BusinessInt64 $ExitCode) -or $ExitCode -ne 0) { $reason = "The business probe process did not establish actual integer exit code 0 (observed: $ExitCode)." }
    elseif ($null -eq $reason -and (-not (Test-BusinessPositiveInt64 $ProcessCreationFileTimeUtc) -or
            -not (Test-BusinessPositiveInt64 $ProcessExitFileTimeUtc) -or $ProcessExitFileTimeUtc -lt $ProcessCreationFileTimeUtc)) {
        $reason = 'The launcher did not establish actual positive integer creation/exit FILETIMEs in lifetime order.'
    }
    elseif ($null -eq $reason -and (-not (Test-BusinessPositiveInt64 $result['nativeProcessCreationFileTimeUtc']) -or
            $result['nativeProcessCreationFileTimeUtc'] -ne $ProcessCreationFileTimeUtc)) {
        $reason = 'The native process creation FILETIME was missing, malformed or did not match the retained launcher process.'
    }
    elseif ($null -eq $reason -and ($ProcessId -le 0 -or -not (Test-BusinessPositiveInt64 $result['nativeProcessId']) -or
            $result['nativeProcessId'] -ne $ProcessId -or $result['succeeded'] -isnot [bool] -or $result['succeeded'] -ne $true -or
            $result['probe'] -ne $ExpectedPhase -or $result['userRoot'] -ne $ExpectedRoot -or
            $result['actualWindowClosed'] -isnot [bool] -or $result['actualWindowClosed'] -ne $true -or $null -eq $result['checks'] -or @($result['checks']).Count -eq 0)) {
        $reason = 'The native report did not establish success, actual window close, matching phase/root/PID and checks.'
    }
    if ($null -eq $reason) {
        $sources = @($result['provenance'])
        if ($sources.Count -ne $ExpectedInputs.Count) { $reason = 'The native report omitted pinned business package provenance.' }
        foreach ($expected in $ExpectedInputs) {
            $matched = @($sources | Where-Object { $_.pluginId -eq $expected.pluginId -and $_.sourceCommit -eq $expected.sourceCommit -and $_.sha256 -eq $expected.sha256 })
            if ($matched.Count -ne 1) { $reason = 'The native report business package source or SHA256 did not match its pinned input.' }
        }
    }
    $result['expectedProvenance'] = $ExpectedInputs
    $result['processId'] = $ProcessId
    $result['processCreationFileTimeUtc'] = $ProcessCreationFileTimeUtc
    $result['processExitFileTimeUtc'] = $ProcessExitFileTimeUtc
    $result['exitCode'] = $ExitCode
    $result['hasExited'] = $HasExited
    $result['runnerCompletedUtc'] = [DateTimeOffset]::UtcNow
    if ($null -ne $reason) {
        $result['succeeded'] = $false; $result['runnerError'] = $reason
        # Generic host failure reporting may replace the main native report. This sidecar is diagnostic only:
        # it is read after rejection, never backfills success fields and never changes the rejection reason.
        $sidecarPath = [IO.Path]::ChangeExtension($ReportPath, '.evidence.json')
        if (Test-Path -LiteralPath $sidecarPath -PathType Leaf) {
            try {
                $sidecar = Get-Content -LiteralPath $sidecarPath -Raw | ConvertFrom-Json -AsHashtable
                if ($sidecar -is [System.Collections.IDictionary]) {
                    $result['nativeFailureEvidence'] = @{
                        diagnosticOnly = $true; source = 'native-failure-sidecar'; probe = $sidecar['probe']; userRoot = $sidecar['userRoot'];
                        processId = $sidecar['processId']; processCreationFileTimeUtc = $sidecar['processCreationFileTimeUtc'];
                        matchesRunnerIdentity = ($ProcessId -gt 0 -and (Test-BusinessPositiveInt64 $ProcessCreationFileTimeUtc) -and
                            (Test-BusinessPositiveInt64 $sidecar['processId']) -and $sidecar['processId'] -eq $ProcessId -and
                            (Test-BusinessPositiveInt64 $sidecar['processCreationFileTimeUtc']) -and $sidecar['processCreationFileTimeUtc'] -eq $ProcessCreationFileTimeUtc -and
                            $sidecar['probe'] -ceq $ExpectedPhase -and $sidecar['userRoot'] -ceq $ExpectedRoot)
                    }
                }
            }
            catch { } # Unreadable/malformed diagnostics cannot replace a known failure or the observed main/runner facts.
        }
    }
    New-Item (Split-Path $ReportPath -Parent) -ItemType Directory -Force | Out-Null
    $result | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    if ($null -ne $reason) { throw $reason }
}
function Assert-BusinessProcessInstances {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][AllowNull()][object[]]$Phases,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$ExpectedPhases
    )
    if ($ExpectedPhases.Count -eq 0 -or $null -eq $Phases -or $Phases.Count -ne $ExpectedPhases.Count) {
        throw 'The business process instances did not contain the complete expected phase sequence.'
    }
    $instances = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($index = 0; $index -lt $ExpectedPhases.Count; $index++) {
        $row = $Phases[$index]
        $phase = Get-BusinessProcessField $row phase
        $succeeded = Get-BusinessProcessField $row succeeded
        $processId = Get-BusinessProcessField $row processId
        $nativeId = Get-BusinessProcessField $row nativeProcessId
        $creation = Get-BusinessProcessField $row processCreationFileTimeUtc
        $nativeCreation = Get-BusinessProcessField $row nativeProcessCreationFileTimeUtc
        $exitTime = Get-BusinessProcessField $row processExitFileTimeUtc
        $code = Get-BusinessProcessField $row exitCode
        $exited = Get-BusinessProcessField $row hasExited
        $checks = Get-BusinessProcessField $row checks
        if ($phase -isnot [string] -or -not [string]::Equals($phase, $ExpectedPhases[$index], [StringComparison]::Ordinal)) {
            throw 'The business process phases were missing, unexpected or out of order.'
        }
        if ($succeeded -isnot [bool] -or -not $succeeded -or $exited -isnot [bool] -or -not $exited -or
            -not (Test-BusinessInt64 $code) -or $code -ne 0 -or -not (Test-BusinessPositiveInt64 $checks) -or
            -not (Test-BusinessPositiveInt64 $processId) -or $processId -gt [int]::MaxValue -or
            -not (Test-BusinessPositiveInt64 $nativeId) -or $nativeId -ne $processId -or
            -not (Test-BusinessPositiveInt64 $creation) -or -not (Test-BusinessPositiveInt64 $nativeCreation) -or $nativeCreation -ne $creation -or
            -not (Test-BusinessPositiveInt64 $exitTime) -or $exitTime -lt $creation) {
            throw "The business phase $phase did not establish successful matching native/launcher PID, birth and actual exit evidence."
        }
        $key = ([long]$processId).ToString([Globalization.CultureInfo]::InvariantCulture) + ':' + ([long]$creation).ToString([Globalization.CultureInfo]::InvariantCulture)
        if (-not $instances.Add($key)) { throw 'A business PID and creation FILETIME tuple was counted more than once.' }
    }
}
Export-ModuleMember -Function Complete-BusinessPluginReport, Assert-BusinessProcessInstances
