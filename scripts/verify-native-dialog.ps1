# Run the native #32770 file-dialog check on a real Windows 11 desktop.
# GitHub-hosted runners skip this step. This script forces it to run and records the evidence.
[CmdletBinding()]
param(
    [string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$windows = $PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows
if (-not $windows) {
    Write-Host "FAIL"
    Write-Host "verify-native-dialog.ps1 requires a real Windows 11 desktop."
    exit 1
}

$repo = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $repo 'artifacts/native-dialog'
}
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
$EvidenceDirectory = (Resolve-Path $EvidenceDirectory).Path
$trx = Join-Path $EvidenceDirectory 'native-dialog.trx'
if (Test-Path $trx) { Remove-Item $trx -Force }

$env:TEST_EXECUTOR_INTERACTIVE_DESKTOP = '1'
$project = Join-Path $repo 'tests/Workbench.TestExecutor.WindowsTests/Workbench.TestExecutor.WindowsTests.csproj'
Write-Host "Running the native file dialog check. Evidence: $EvidenceDirectory"

& dotnet test $project -c Release --nologo --verbosity normal `
    --filter FullyQualifiedName~Native_file_dialog `
    --logger "trx;LogFileName=native-dialog.trx" `
    --results-directory $EvidenceDirectory
$code = $LASTEXITCODE

$resultPath = Join-Path $EvidenceDirectory 'result.json'
$passed = $code -eq 0
@{
    result   = $(if ($passed) { 'pass' } else { 'fail' })
    evidence = $EvidenceDirectory
    trx      = $trx
} | ConvertTo-Json | Set-Content -Path $resultPath -Encoding utf8

if ($passed) {
    Write-Host "PASS"
    Write-Host "Evidence: $EvidenceDirectory"
    exit 0
}

Write-Host "FAIL"
Write-Host "Evidence: $EvidenceDirectory"
exit $(if ($code -eq 0) { 1 } else { $code })
