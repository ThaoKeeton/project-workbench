[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '../../scripts/OwnedLabProbeOutput.psm1') -Force
$temp = Join-Path ([IO.Path]::GetTempPath()) ('owned probe-output-' + [Guid]::NewGuid().ToString('N'))
$lineEnd = [Environment]::NewLine
New-Item -ItemType Directory -Path $temp | Out-Null
$child = Join-Path $temp 'owned-child.ps1'
@'
param([string]$Mode, [int]$Code)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$utf8 = [Text.UTF8Encoding]::new($false)
$stdout = "synthetic completed check`n"
$stderr = "System.InvalidOperationException: Synthetic owned probe failure`n   at OwnedSyntheticProbe.Run()`n"
switch ($Mode) {
    'Large' { $stderr = "SYNTHETIC-BEGIN`n" + ('x' * 2000000) + 'SYNTHETIC-END' }
    'Exact' { $stderr = 'x' * 16384 }
    'Empty' { $stderr = '' }
    'OversizeStdout' { $stdout = 'x' * 65537 }
}
$bytes = $utf8.GetBytes($stdout)
$stream = [Console]::OpenStandardOutput(); $stream.Write($bytes, 0, $bytes.Length); $stream.Flush()
$bytes = $utf8.GetBytes($stderr)
if ($Mode -ceq 'Invalid') { $bytes = [byte[]]@(83,255,0,27,10) }
$stream = [Console]::OpenStandardError(); $stream.Write($bytes, 0, $bytes.Length); $stream.Flush()
if ($Mode -ceq 'Wait') { [Threading.Thread]::Sleep(30000) }
exit $Code
'@ | Set-Content -LiteralPath $child -Encoding utf8NoBOM

function Require([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Exercise([string]$Mode, [int]$Code = 37, [string]$Missing = '') {
    $directory = Join-Path $temp ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $directory | Out-Null
    $stdout = Join-Path $directory 'stdout.log'; $stderr = Join-Path $directory 'stderr.log'
    $stdoutFile = $null; $stderrFile = $null; $copies = @()
    if ($Mode -cin @('Exact','Invalid')) {
        # Start-Process normalizes invalid UTF-8/adds a final newline. Raw pipe copies
        # keep these two real-child byte fixtures exact at the reporter's file boundary.
        $start = [Diagnostics.ProcessStartInfo]::new([Environment]::ProcessPath)
        $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        foreach ($argument in @('-NoProfile','-File',$child,$Mode,[string]$Code)) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start)
        $stdoutFile = [IO.File]::Create($stdout); $stderrFile = [IO.File]::Create($stderr)
        $copies = @($process.StandardOutput.BaseStream.CopyToAsync($stdoutFile), $process.StandardError.BaseStream.CopyToAsync($stderrFile))
    }
    else { $process = Start-Process -FilePath ([Environment]::ProcessPath) -ArgumentList @('-NoProfile','-File',('"' + $child + '"'),$Mode,$Code) -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr }
    try {
        $null = $process.Handle
        $birth = $process.StartTime.ToUniversalTime().ToFileTimeUtc()
        if (-not $process.WaitForExit(30000)) { $process.Kill($true); $process.WaitForExit(); throw 'Owned synthetic child timed out.' }
        if ($copies.Count -ne 0) {
            [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]$copies).GetAwaiter().GetResult()
            $stdoutFile.Dispose(); $stderrFile.Dispose()
        }
        $identity = [ordered]@{ processId = $process.Id; processCreationFileTimeUtc = $birth; processExitFileTimeUtc = $process.ExitTime.ToUniversalTime().ToFileTimeUtc(); exitCode = $process.ExitCode }
        $before = $identity | ConvertTo-Json -Compress
        if ($Missing -ceq 'stdout') { Move-Item -LiteralPath $stdout -Destination ($stdout + '.retained') }
        if ($Missing -ceq 'stderr') { Move-Item -LiteralPath $stderr -Destination ($stderr + '.retained') }
        $state = @{ succeeded = $false; failure = $null }
        $emitted = @(& {
            try {
                Complete-OwnedLabProbeProcessOutput -Process $process -StandardOutputPath $stdout -StandardErrorPath $stderr
                $state.succeeded = $true
            }
            catch { $state.failure = $_.Exception.Message }
        } 6>&1 | ForEach-Object { $_.ToString() })
        Require ($process.HasExited -and $identity.processId -gt 0 -and $birth -gt 0 -and $identity.processExitFileTimeUtc -ge $birth) 'Actual child identity/confirmed exit missing.'
        Require (($identity | ConvertTo-Json -Compress) -ceq $before -and $identity.exitCode -eq $Code -and $process.ExitCode -eq $Code) 'Reporting changed the observed process identity or exit code.'
        Write-Host ([ordered]@{ syntheticChild = $true; mode = $Mode; missingLog = $Missing; process = $identity; reportingSucceeded = $state.succeeded; failure = $state.failure; emittedUtf8Bytes = [Text.Encoding]::UTF8.GetByteCount(($emitted -join "`n")) } | ConvertTo-Json -Depth 4 -Compress)
        return @{ emitted = $emitted; state = $state; identity = $identity }
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        if ($copies.Count -ne 0) { [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]$copies).GetAwaiter().GetResult() }
        if ($null -ne $stdoutFile) { $stdoutFile.Dispose() }
        if ($null -ne $stderrFile) { $stderrFile.Dispose() }
        $process.Dispose()
    }
}
function FailureDiagnostic($Result) {
    Require (-not $Result.state.succeeded -and $Result.state.failure -ceq 'OwnedLabProbeProcessFailed') 'Reporting must preserve the original nonzero failure.'
    $records = @($Result.emitted | Where-Object { $_.StartsWith('{') } | ForEach-Object { $_ | ConvertFrom-Json -AsHashtable } | Where-Object diagnostic -CEQ 'OwnedLabProbeStandardError')
    Require ($records.Count -eq 1) 'Nonzero child stderr diagnostic was not emitted exactly once before the process failure.'
    return $records[0]
}
try {
    # A completion reporter must refuse a still-running owned child before any exit wait.
    $running = Start-Process -FilePath ([Environment]::ProcessPath) -ArgumentList @('-NoProfile','-File',('"' + $child + '"'),'Wait',37) -PassThru -RedirectStandardOutput (Join-Path $temp 'running.stdout.log') -RedirectStandardError (Join-Path $temp 'running.stderr.log')
    try {
        $null = $running.Handle
        $birth = $running.StartTime.ToUniversalTime().ToFileTimeUtc()
        $failure = $null
        try { Complete-OwnedLabProbeProcessOutput -Process $running -StandardOutputPath (Join-Path $temp 'running.stdout.log') -StandardErrorPath (Join-Path $temp 'running.stderr.log') }
        catch { $failure = $_.Exception.Message }
        Require ($failure -ceq 'OwnedLabProbeProcessStillRunning' -and -not $running.HasExited) 'Completion reporting waited on an unobserved running child or accepted it.'
    }
    finally {
        if (-not $running.HasExited) { $running.Kill($true) }
        $running.WaitForExit()
        Write-Host ([ordered]@{ syntheticChild = $true; mode = 'StillRunningRefused'; processId = $running.Id; processCreationFileTimeUtc = $birth; processExitFileTimeUtc = $running.ExitTime.ToUniversalTime().ToFileTimeUtc(); exitCode = $running.ExitCode; reporterFailure = $failure; cleanupJoined = $true } | ConvertTo-Json -Compress)
        $running.Dispose()
    }

    # Catches the real wrapper regression: stderr is redirected, then lost behind the nonzero throw.
    $small = Exercise Small
    $diagnostic = FailureDiagnostic $small
    Require ($small.emitted -contains "synthetic completed check$lineEnd") 'Completed stdout observations were lost.'
    Require ($diagnostic.available -and -not $diagnostic.truncated -and -not $diagnostic.invalidUtf8 -and $diagnostic.text -ceq "System.InvalidOperationException: Synthetic owned probe failure${lineEnd}   at OwnedSyntheticProbe.Run()$lineEnd") 'Actual ordinary exception type/message/stack missing.'
    Write-Host ($small.emitted -join "`n")
    Write-Host 'Nonzero real owned child: stdout, stderr exception, original failure and actual identity preserved.'

    # Catches an unbounded/full-file diagnostic read or an off-by-one truncation claim.
    $large = Exercise Large; $diagnostic = FailureDiagnostic $large
    Require ($diagnostic.available -and $diagnostic.truncated -and $diagnostic.bytesRead -eq 16384 -and $diagnostic.byteLimit -eq 16384 -and $diagnostic.text.StartsWith("SYNTHETIC-BEGIN$lineEnd") -and -not $diagnostic.text.Contains('SYNTHETIC-END')) 'Oversized stderr was not bounded to its leading 16 KiB.'
    Require ([Text.Encoding]::UTF8.GetByteCount(($large.emitted -join "`n")) -lt 100000) 'Bounded diagnostic output expanded without a finite limit.'
    $diagnostic = FailureDiagnostic (Exercise Exact)
    Require ($diagnostic.available -and -not $diagnostic.truncated -and $diagnostic.bytesRead -eq 16384 -and $diagnostic.text.Length -eq 16384) 'Exact-limit stderr was incorrectly truncated.'

    # Catches decoding/control characters masking the nonzero failure or escaping JSON framing.
    $invalid = Exercise Invalid; $diagnostic = FailureDiagnostic $invalid
    Require ($diagnostic.available -and $diagnostic.invalidUtf8 -and $diagnostic.bytesRead -eq 5 -and $diagnostic.text -ceq ("S" + [char]0xfffd + [char]0 + [char]27 + "`n")) 'Invalid UTF-8 stderr was not safely represented as diagnostic text.'
    Require (-not ($invalid.emitted -join '').Contains([char]0) -and -not ($invalid.emitted -join '').Contains([char]27)) 'Diagnostic console output contains raw control characters.'
    $diagnostic = FailureDiagnostic (Exercise Empty)
    Require ($diagnostic.available -and $diagnostic.bytesRead -eq 0 -and -not $diagnostic.truncated -and $diagnostic.text -ceq '') 'Empty stderr changed the original failure.'

    # Catches diagnostic IO/oversize errors replacing the original process-failed error.
    $diagnostic = FailureDiagnostic (Exercise Small 37 stderr)
    Require (-not $diagnostic.available) 'Unreadable stderr was reported as available.'
    $null = FailureDiagnostic (Exercise Small 37 stdout)
    $null = FailureDiagnostic (Exercise OversizeStdout)
    $oversizeSuccess = Exercise OversizeStdout 0
    Require (-not $oversizeSuccess.state.succeeded -and $oversizeSuccess.state.failure -ceq 'OwnedLabProbeConsoleOversize') 'A zero-exit child bypassed the existing stdout limit.'
    $success = Exercise Small 0
    Require ($success.state.succeeded -and $null -eq $success.state.failure -and $success.emitted.Count -eq 1 -and $success.emitted[0] -ceq "synthetic completed check$lineEnd") 'Successful reporting emitted failure diagnostics or lost stdout.'
    Write-Host 'Owned child reporting regressions passed: 11 real processes, running-child refusal, joined redirected logs, bounded/truncated/exact/invalid/empty/unreadable text, stdout guards and original failure/identity. Synthetic script proof only; no Windows native execution.'
}
finally { Remove-Item -LiteralPath $temp -Recurse -Force }
