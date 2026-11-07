Set-StrictMode -Version Latest
function Get-TestingRequiredChecks {
    param([string]$Phase)
    if ($Phase -ceq 'seed') {
        return @('independent-zip-native-page','packaged-requirements-figma-io','traceable-manual-mapped-generation',
            'immediate-metadata-csv-bytes','immediate-step-mapping-invalidation','actual-file-share-save-failure-close-correction',
            'conflict-preserve-copy-reload-bytes','positive-actual-journal-bmp-native-decoder','negative-empty-title-run',
            'labeled-deliberate-assertion-failure','cancel-real-owned-store-io-before-first-step','native-project-isolation-empty-read',
            'synthetic-inflight-journal-seed-no-action','immediate-final-input-before-leave')
    }
    return @('separate-process-context-interrupted-synthetic-seed','restart-final-native-input-persisted',
        'reopened-exact-observed-text-bmp-native-decoder','linked-new-session-retry-frozen-step-one','restart-project-roster-preserved')
}
function Complete-TestingPluginProbeReport {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ReportPath, [Parameter(Mandatory)][int]$ProcessId,
        [AllowNull()][object]$ExitCode, [Parameter(Mandatory)][bool]$HasExited,
        [Parameter(Mandatory)][ValidateSet('seed','verify')][string]$ExpectedPhase,
        [Parameter(Mandatory)][string]$ExpectedRoot, [Parameter(Mandatory)][bool]$LeaseAvailable,
        [string]$ExpectedPackageHash = '', [bool]$ObservationFailed = $false)
    $runnerPath = [IO.Path]::ChangeExtension($ReportPath, 'runner.json')
    if (Test-Path -LiteralPath $runnerPath) { throw 'TestingPhaseAlreadyCompleted' }
    $reason = $null; $native = @{}
    if (Test-Path -LiteralPath $ReportPath -PathType Leaf) {
        try { $native = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json -AsHashtable } catch { $reason = 'TestingReportUnreadable' }
    } else { $reason = 'TestingReportMissing' }
    if ($native -isnot [Collections.IDictionary]) { $native = @{}; $reason = 'TestingReportInvalid' }
    if (-not $HasExited) { $reason = 'TestingProcessStillOwned' }
    elseif ($null -eq $ExitCode -or $ExitCode -ne 0) { $reason = 'TestingProcessFailed' }
    elseif (-not $LeaseAvailable) { $reason = 'TestingRootStillOwned' }
    elseif ($null -eq $reason -and ($native['schemaVersion'] -ne 1 -or $native['succeeded'] -ne $true -or
        $native['probe'] -cne $ExpectedPhase -or $native['userRoot'] -cne $ExpectedRoot -or $native['processId'] -ne $ProcessId -or
        $native['actualWindowClosed'] -ne $true -or $native['rootsRetired'] -ne $true -or
        $native['syntheticInterruptionSeed'] -ne $true -or [string]::IsNullOrWhiteSpace($native['osDescription']) -or $native['processArchitecture'] -cne 'X64')) { $reason = 'TestingIdentityOrRetirementMissing' }
    if ($null -eq $reason -and $ExpectedPackageHash -ne '' -and $native['packageSha256'] -cne $ExpectedPackageHash) { $reason = 'TestingPackageHashMismatch' }
    if ($null -eq $reason) {
        $required = @(Get-TestingRequiredChecks $ExpectedPhase)
        if (@($native['checks']).Count -ne $required.Count) { $reason = 'TestingChecksIncomplete' }
        foreach ($check in $required) { if (@($native['checks'] | Where-Object { $_ -ceq $check }).Count -ne 1) { $reason = 'TestingChecksIncomplete' } }
    }
    if ($null -eq $reason) {
        try {
            $root = Join-Path (Split-Path $ReportPath -Parent) "$ExpectedPhase-evidence"
            $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            if (@($native['evidence']).Count -eq 0) { throw 'TestingEvidenceMissing' }
            foreach ($receipt in $native['evidence']) {
                $path = [string]$receipt.path
                if ($path -notmatch '^(store/[a-zA-Z0-9/.-]+|testing-cases\.csv)$' -or $path.Contains('..') -or -not $seen.Add($path) -or $receipt.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'TestingEvidencePathInvalid' }
                $file = Join-Path $root $path
                for ($part = $file; $part.Length -ge $root.Length; $part = Split-Path $part -Parent) {
                    if (-not (Test-Path -LiteralPath $part) -or ((Get-Item -LiteralPath $part).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'TestingEvidencePathInvalid' }
                }
                if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -ne $receipt.length -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $receipt.sha256) { throw 'TestingEvidenceHashMismatch' }
            }
            $actual = @(Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/') })
            if ($actual.Count -ne $seen.Count -or @($actual | Where-Object { -not $seen.Contains($_) }).Count -ne 0 -or -not $seen.Contains('testing-cases.csv') -or @($actual | Where-Object { $_.EndsWith('/frame.bmp') }).Count -eq 0 -or @($actual | Where-Object { $_ -match '/events/[0-9]+\.json$' }).Count -eq 0) { throw 'TestingEvidenceIncomplete' }
        }
        catch { $reason = 'TestingEvidenceInvalid' }
    }
    if ($null -eq $reason -and $ObservationFailed) { $reason = 'TestingObservationFailed' }
    $result = @{ schemaVersion = 1; succeeded = ($null -eq $reason); phase = $ExpectedPhase; userRoot = $ExpectedRoot;
        processId = $ProcessId; nativeProcessId = $native['processId']; hasExited = $HasExited; exitCode = $ExitCode;
        rootLeaseAvailable = $LeaseAvailable; observationFailed = $ObservationFailed; runnerError = $reason; checks = @($native['checks']).Count; runnerCompletedUtc = [DateTimeOffset]::UtcNow }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($result | ConvertTo-Json -Depth 12))
    $stream = [IO.File]::Open($runnerPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $stream.Write($bytes) } finally { $stream.Dispose() }
    if ($null -ne $reason) { throw $reason }
    return $result
}
function Invoke-TestingPluginProbePhases {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$ExpectedRoot,
        [Parameter(Mandatory)][scriptblock]$Observe, [string]$ExpectedPackageHash = '')
    $completed = @()
    foreach ($phase in @('seed','verify')) {
        $report = Join-Path $OutputDirectory "$phase.json"
        if (Test-Path -LiteralPath $report) { throw 'TestingNativeReportAlreadyExists' }
        $observation = @{ ProcessId = $null; ExitCode = $null; HasExited = $false; LeaseAvailable = $false }
        $identity = @{ ReportPath = $report; ExpectedPhase = $phase; ExpectedRoot = $ExpectedRoot; ExpectedPackageHash = $ExpectedPackageHash }
        try { & $Observe $phase $report $observation }
        catch {
            if ($null -ne $observation.ProcessId) {
                try { Complete-TestingPluginProbeReport @identity @observation -ObservationFailed $true | Out-Null } catch { }
            }
            throw
        }
        $result = Complete-TestingPluginProbeReport @identity @observation
        if (@($completed | Where-Object processId -EQ $result.processId).Count -ne 0) { throw 'TestingProbeProcessReused' }
        $completed += $result
    }
    return $completed
}
Export-ModuleMember -Function Complete-TestingPluginProbeReport, Invoke-TestingPluginProbePhases
