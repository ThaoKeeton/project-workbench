$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $repo 'scripts/BusinessPluginReport.psm1') -Force
$temporary = Join-Path ([IO.Path]::GetTempPath()) "workbench-business-report-$([Guid]::NewGuid().ToString('N'))"
New-Item $temporary -ItemType Directory | Out-Null
$inputs = @(@{ pluginId = 'first'; sourceCommit = 'abc'; sha256 = '123' }, @{ pluginId = 'second'; sourceCommit = 'def'; sha256 = '456' })
function Seed([string]$name) {
    $path = Join-Path $temporary "$name.json"
    @{ succeeded = $true; probe = 'seed'; userRoot = $temporary; processId = 101; actualWindowClosed = $true; checks = @('actual vendor UI'); provenance = $inputs } |
        ConvertTo-Json -Depth 8 | Set-Content $path
    return $path
}
function Complete([string]$path, [int]$processId = 101, [object]$code = 0, [bool]$exited = $true) {
    Complete-BusinessPluginReport -ReportPath $path -ProcessId $processId -ExitCode $code -HasExited $exited -ExpectedPhase seed -ExpectedRoot $temporary -ExpectedInputs $inputs
}
function Reject([scriptblock]$action, [string]$path) {
    $failed = $false
    try { & $action } catch { $failed = $true }
    if (-not $failed -or (Get-Content $path -Raw | ConvertFrom-Json).succeeded -ne $false) { throw 'Invalid process evidence became a successful business result.' }
}
try {
    $valid = Seed valid
    Complete $valid
    $result = Get-Content $valid -Raw | ConvertFrom-Json
    if ($result.exitCode -ne 0 -or $result.processId -ne 101 -or -not $result.hasExited -or -not $result.succeeded) { throw 'Actual process exit evidence was not persisted.' }
    $nonzero = Seed nonzero; Reject { Complete $nonzero -code 19 } $nonzero
    if ((Get-Content $nonzero -Raw | ConvertFrom-Json).exitCode -ne 19) { throw 'Nonzero exit code was lost.' }
    $timeout = Seed timeout; Reject { Complete $timeout -code $null -exited $false } $timeout
    $missing = Join-Path $temporary 'missing.json'; Reject { Complete $missing } $missing
    $failure = Get-Content $missing -Raw | ConvertFrom-Json
    if ($failure.expectedProvenance.Count -ne 2) { throw 'Missing native report lost expected pinned source provenance.' }
    $mismatch = Seed mismatch; Reject { Complete $mismatch -processId 202 } $mismatch
    $checks = Seed checks
    $data = Get-Content $checks -Raw | ConvertFrom-Json -AsHashtable; $data.Remove('checks'); $data | ConvertTo-Json -Depth 8 | Set-Content $checks
    Reject { Complete $checks } $checks
    $source = Seed source
    $data = Get-Content $source -Raw | ConvertFrom-Json -AsHashtable; $data.provenance[0].sourceCommit = 'wrong'; $data | ConvertTo-Json -Depth 8 | Set-Content $source
    Reject { Complete $source } $source
    Write-Output 'Business plugin process report regressions: 7 passed'
}
finally { Remove-Item $temporary -Recurse -Force }
