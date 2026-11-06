[CmdletBinding()]
param([ValidateRange(10, 600)][int]$TimeoutSeconds = 120)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Native smoke requires a real Windows WinUI runtime.' }
Import-Module (Join-Path $PSScriptRoot 'NativeSmokeReport.psm1') -Force
$repo = Split-Path $PSScriptRoot -Parent
$app = Join-Path $repo 'artifacts/native/app/Workbench.App.exe'
$report = Join-Path $repo 'artifacts/native/native-smoke.json'
if (-not (Test-Path $app)) { throw "Build the app first: $app" }
if (Test-Path $report) { Remove-Item $report -Force }
$process = $null
try {
    $process = Start-Process -FilePath $app -ArgumentList @('--smoke-test', '--smoke-report', "`"$report`"") -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw "Native smoke timed out after $TimeoutSeconds seconds; WinUI startup or a plugin may be unresponsive."
    }
    if ($process.ExitCode -ne 0) { throw "Native smoke process exited with $($process.ExitCode)." }
    if (-not (Test-Path $report)) { throw 'Native process returned without producing the required JSON report.' }
    $result = Get-Content $report -Raw | ConvertFrom-Json
    if ($result.succeeded -ne $true) { throw 'Native smoke report does not indicate success.' }
    Write-Host "Native smoke passed: $report"
}
catch {
    # Pre-Main module-initializer/WinUI failures may prevent the executable from writing JSON.
    $reason = $_.Exception.Message
    Write-NativeSmokeFailure -ReportPath $report -Reason $reason
    Write-Error $reason
    exit 1
}
finally { if ($null -ne $process) { $process.Dispose() } }
