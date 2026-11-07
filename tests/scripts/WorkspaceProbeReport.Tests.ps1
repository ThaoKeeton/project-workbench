$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $repo 'scripts/WorkspaceProbeReport.psm1') -Force
$temporary = Join-Path ([IO.Path]::GetTempPath()) "workbench-workspace-report-$([Guid]::NewGuid().ToString('N'))"
New-Item $temporary -ItemType Directory | Out-Null
function Seed([string]$name) {
    $path = Join-Path $temporary "$name.json"
    @{ schemaVersion = 1; succeeded = $true; probe = 'seed'; userRoot = $temporary; processId = 101;
       actualWindowClosed = $true; rootsRetired = $true; checks = @('legacy-adoption', 'single-native-ui', 'multi-native-ui',
       'project-isolation', 'draft-retention', 'old-pending-refusal', 'candidate-pending-refusal', 'archive-retention', 'native-reopen',
       'busy-switch-native-close', 'busy-action-native-close', 'before-initialize-native-close',
       'immediate-context-switch', 'immediate-context-close', 'settled-switch-private-correction') } |
        ConvertTo-Json -Depth 8 | Set-Content $path
    return $path
}
function Complete([string]$path, [int]$processId = 101, [object]$code = 0, [bool]$exited = $true, [bool]$lease = $true) {
    Complete-WorkspaceProbeReport -ReportPath $path -ProcessId $processId -ExitCode $code -HasExited $exited -ExpectedPhase seed -ExpectedRoot $temporary -LeaseAvailable $lease
}
function Reject([scriptblock]$action, [string]$path) {
    $failed = $false
    try { & $action } catch { $failed = $true }
    if (-not $failed -or (Get-Content $path -Raw | ConvertFrom-Json).succeeded -ne $false) { throw 'Invalid native/process/ownership evidence became a workspace success.' }
}
try {
    # Removing PID, phase, root, Closed, retirement, or actual runner exit/lease validation is a bug.
    $mismatch = Seed mismatch; Reject { Complete $mismatch -processId 202 } $mismatch
    $nonzero = Seed nonzero; Reject { Complete $nonzero -code 19 } $nonzero
    $timeout = Seed timeout; Reject { Complete $timeout -code $null -exited $false } $timeout
    $lease = Seed lease; Reject { Complete $lease -lease $false } $lease
    $missing = Join-Path $temporary 'missing.json'; Reject { Complete $missing } $missing
    foreach ($field in @('probe', 'userRoot', 'actualWindowClosed', 'rootsRetired', 'checks', 'schemaVersion')) {
        $path = Seed $field
        $data = Get-Content $path -Raw | ConvertFrom-Json -AsHashtable; $data.Remove($field)
        $data | ConvertTo-Json -Depth 8 | Set-Content $path
        Reject { Complete $path } $path
    }
    $partial = Seed partial
    $data = Get-Content $partial -Raw | ConvertFrom-Json -AsHashtable; $data.checks = @('legacy-adoption')
    $data | ConvertTo-Json -Depth 8 | Set-Content $partial
    Reject { Complete $partial } $partial
    foreach ($required in @('busy-switch-native-close', 'busy-action-native-close', 'before-initialize-native-close',
        'immediate-context-switch', 'immediate-context-close', 'settled-switch-private-correction')) {
        $path = Seed $required
        $data = Get-Content $path -Raw | ConvertFrom-Json -AsHashtable
        $data.checks = @($data.checks | Where-Object { $_ -cne $required })
        $data | ConvertTo-Json -Depth 8 | Set-Content $path
        Reject { Complete $path } $path
    }
    $valid = Seed valid; Complete $valid
    $result = Get-Content $valid -Raw | ConvertFrom-Json
    if (-not $result.succeeded -or -not $result.hasExited -or -not $result.rootLeaseAvailable -or $result.exitCode -ne 0 -or $result.processId -ne 101) { throw 'Actual runner evidence was not persisted.' }
    Write-Output 'Workspace native process report regressions: 19 passed (synthetic metadata only)'
}
finally { Remove-Item $temporary -Recurse -Force }
