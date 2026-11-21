$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $repo 'scripts/NativeSmokeReport.psm1') -Force
$temporary = Join-Path ([IO.Path]::GetTempPath()) "workbench-report-tests-$([Guid]::NewGuid().ToString('N'))"
New-Item $temporary -ItemType Directory | Out-Null
try {
    $existing = Join-Path $temporary 'existing.json'
    @{ succeeded = $true; stage = 'completed'; checks = @('native controls attached') } | ConvertTo-Json | Set-Content $existing
    Write-NativeSmokeFailure -ReportPath $existing -Reason 'Process timed out after writing success'
    $result = Get-Content $existing -Raw | ConvertFrom-Json
    if ($result.succeeded -ne $false) { throw 'Runner failure must override a prior native success verdict.' }
    if ($result.nativeStage -ne 'completed' -or $result.checks[0] -ne 'native controls attached') { throw 'Native evidence must survive the runner failure.' }
    if ($result.runnerError -ne 'Process timed out after writing success') { throw 'Runner failure reason was lost.' }

    $missing = Join-Path $temporary 'new/report.json'
    Write-NativeSmokeFailure -ReportPath $missing -Reason 'Native module initialization crashed before Main'
    $result = Get-Content $missing -Raw | ConvertFrom-Json
    if ($result.succeeded -ne $false -or $result.runnerError -notlike '*before Main') { throw 'A pre-Main failure needs a new failed JSON report.' }

    $corrupt = Join-Path $temporary 'corrupt.json'
    Set-Content $corrupt '{ truncated'
    Write-NativeSmokeFailure -ReportPath $corrupt -Reason 'Native process crashed during report write'
    $result = Get-Content $corrupt -Raw | ConvertFrom-Json
    if ($result.succeeded -ne $false -or $result.stage -ne 'native-process-failure') { throw 'A truncated native report must become a valid failure report.' }
    Write-Output 'Native smoke report regressions: 3 passed'
}
finally { Remove-Item $temporary -Recurse -Force }
