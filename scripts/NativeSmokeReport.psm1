function Write-NativeSmokeFailure {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ReportPath, [Parameter(Mandatory)][string]$Reason)
    $failure = @{}
    if (Test-Path $ReportPath) {
        try {
            $native = Get-Content $ReportPath -Raw | ConvertFrom-Json -AsHashtable
            if ($native -is [System.Collections.IDictionary]) { $failure = $native }
        }
        catch { $failure = @{ nativeReportError = $_.Exception.Message } }
    }
    if ($failure.Contains('stage')) { $failure['nativeStage'] = $failure['stage'] }
    $failure['succeeded'] = $false
    $failure['stage'] = 'native-process-failure'
    $failure['runnerError'] = $Reason
    $failure['completedUtc'] = [DateTimeOffset]::UtcNow
    $directory = Split-Path $ReportPath -Parent
    New-Item $directory -ItemType Directory -Force | Out-Null
    $failure | ConvertTo-Json -Depth 20 | Set-Content $ReportPath -Encoding utf8
}
Export-ModuleMember -Function Write-NativeSmokeFailure
