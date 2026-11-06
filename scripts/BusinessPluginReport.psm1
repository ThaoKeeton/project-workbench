function Complete-BusinessPluginReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ReportPath,
        [Parameter(Mandatory)][int]$ProcessId,
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
    if (-not $HasExited) { $reason = 'The process timed out and remains owned; no successor or force termination was requested.' }
    elseif ($null -eq $ExitCode -or $ExitCode -ne 0) { $reason = "The business probe process exited with $ExitCode." }
    elseif ($null -eq $reason -and ($result['succeeded'] -ne $true -or $result['processId'] -ne $ProcessId -or
            $result['probe'] -ne $ExpectedPhase -or $result['userRoot'] -ne $ExpectedRoot -or
            $result['actualWindowClosed'] -ne $true -or $null -eq $result['checks'] -or @($result['checks']).Count -eq 0)) {
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
    $result['nativeProcessId'] = $result['processId']
    $result['expectedProvenance'] = $ExpectedInputs
    $result['processId'] = $ProcessId
    $result['exitCode'] = $ExitCode
    $result['hasExited'] = $HasExited
    $result['runnerCompletedUtc'] = [DateTimeOffset]::UtcNow
    if ($null -ne $reason) { $result['succeeded'] = $false; $result['runnerError'] = $reason }
    New-Item (Split-Path $ReportPath -Parent) -ItemType Directory -Force | Out-Null
    $result | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    if ($null -ne $reason) { throw $reason }
}
Export-ModuleMember -Function Complete-BusinessPluginReport
