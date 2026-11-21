$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $repo 'scripts/NativeDebugSupport.psm1') -Force
# Synthetic test data only; never inspect or print real CI credentials.
[Environment]::SetEnvironmentVariable('WORKBENCH_TEST_DEBUG_SECRET', 'synthetic-not-a-real-token')
try {
    $start = New-NativeDebugStartInfo -FilePath 'C:\fake tools\cdb.exe' -WorkingDirectory 'C:\isolated app' -Arguments @('-cf', 'C:\diagnostic files\commands.txt', '--smoke-test')
    if ($start.Environment.ContainsKey('WORKBENCH_TEST_DEBUG_SECRET')) { throw 'Debugger children must not inherit arbitrary environment/CI token variables.' }
    if ($start.ArgumentList.Count -ne 3 -or $start.ArgumentList[1] -ne 'C:\diagnostic files\commands.txt') { throw 'Debugger arguments must preserve spaces without shell interpolation.' }
    if ($start.UseShellExecute -or -not $start.RedirectStandardOutput -or -not $start.RedirectStandardError) { throw 'Debugger execution must capture text without a shell.' }
    Write-Output 'Native debug launch regressions: 3 passed'
}
finally { [Environment]::SetEnvironmentVariable('WORKBENCH_TEST_DEBUG_SECRET', $null) }
