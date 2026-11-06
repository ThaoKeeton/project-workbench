[CmdletBinding()]
param([ValidateRange(60, 900)][int]$TimeoutSeconds = 300)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Business plugin smoke requires actual Windows WinUI, DPAPI and owned native worker processes.' }
Import-Module (Join-Path $PSScriptRoot 'BusinessPluginReport.psm1') -Force
$repo = Split-Path $PSScriptRoot -Parent
$app = Join-Path $repo 'artifacts/native/app/Workbench.App.exe'
$output = Join-Path $repo 'artifacts/native/business-probes'
$fixtures = Join-Path $repo 'artifacts/native/fixtures/business'
$acceptanceFixtures = Join-Path $repo 'artifacts/native/fixtures/business-acceptance'
if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw 'Build the current shipping native Workbench.App.exe first.' }
& (Join-Path $PSScriptRoot 'import-business-plugin.ps1') -DestinationRoot $fixtures
$inputs = @(Get-Content -LiteralPath (Join-Path $fixtures 'inputs.json') -Raw | ConvertFrom-Json)
& (Join-Path $PSScriptRoot 'import-business-plugin.ps1') -Mode Acceptance -DestinationRoot $acceptanceFixtures
$acceptanceInputs = @(Get-Content -LiteralPath (Join-Path $acceptanceFixtures 'inputs.json') -Raw | ConvertFrom-Json)
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item $output -ItemType Directory -Force | Out-Null
$productionRoot = Join-Path (Join-Path ([IO.Path]::GetTempPath()) 'ProjectWorkbench-business-probe') ([Guid]::NewGuid().ToString('N'))
$acceptanceRoot = Join-Path (Join-Path ([IO.Path]::GetTempPath()) 'ProjectWorkbench-business-probe') ([Guid]::NewGuid().ToString('N'))
$phases = @('seed', 'restart', 'update', 'fault', 'rollback', 'remove',
    'acceptance-seed', 'acceptance-restore', 'acceptance-stopped', 'acceptance-config', 'acceptance-missing', 'acceptance-disabled')
$completed = @()
$binarySha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash
foreach ($phase in $phases) {
    # The variants share an upstream singleton: this linear sequence starts acceptance only after every production OS exit/Closed/lease gate.
    $synthetic = $phase.StartsWith('acceptance-', [StringComparison]::Ordinal)
    $root = if ($synthetic) { $acceptanceRoot } else { $productionRoot }
    $expected = if ($synthetic) { $acceptanceInputs } else { $inputs }
    $report = Join-Path $output "$phase.json"
    $process = $null
    $exited = $false
    $code = $null
    try {
        # The shipping app owns installation, UI, tasks and startup maintenance; never run a vendor/source-linked host.
        $arguments = @('--business-probe', $phase, '--probe-root', "`"$root`"", '--smoke-report', "`"$report`"")
        $process = Start-Process -FilePath $app -ArgumentList $arguments -PassThru -RedirectStandardOutput (Join-Path $output "$phase.stdout.log") -RedirectStandardError (Join-Path $output "$phase.stderr.log")
        $exited = $process.WaitForExit($TimeoutSeconds * 1000)
        if ($exited) { $code = $process.ExitCode }
        Complete-BusinessPluginReport -ReportPath $report -ProcessId $process.Id -ExitCode $code -HasExited $exited -ExpectedPhase $phase -ExpectedRoot $root -ExpectedInputs $expected
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        # Launcher independently reacquires the production lease after actual OS exit, before any successor.
        $lease = [IO.File]::Open((Join-Path $root 'host.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $lease.Dispose()
        $result | Add-Member -NotePropertyName launcherReacquiredHostLease -NotePropertyValue $true
        $result | Add-Member -NotePropertyName shippingExecutableSha256 -NotePropertyValue $binarySha256
        $result | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $report -Encoding utf8
        $completed += @{ phase = $phase; userRoot = $root; syntheticAcceptance = $synthetic; processId = $process.Id; exitCode = $code; hasExited = $exited; checks = @($result.checks).Count; report = $report }
        Write-Host "Business plugin probe passed: $phase ($(@($result.checks).Count) checks; actual PID $($process.Id), exit $code)"
    }
    catch {
        if ($null -ne $process) {
            # Preserve the real PID/exit even if lease verification fails after a valid native success report.
            $failure = @{}
            if (Test-Path -LiteralPath $report -PathType Leaf) {
                try { $failure = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json -AsHashtable } catch { }
            }
            $failure['succeeded'] = $false; $failure['processId'] = $process.Id; $failure['exitCode'] = $code; $failure['hasExited'] = $exited
            $failure['runnerError'] = $_.Exception.Message; $failure['shippingExecutableSha256'] = $binarySha256
            $failure | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $report -Encoding utf8
        }
        @{ succeeded = $false; userRoot = $root; productionRoot = $productionRoot; acceptanceRoot = $acceptanceRoot; failedPhase = $phase; completedPhases = $completed; provenance = $expected; error = $_.Exception.Message } |
            ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
        throw
    }
    finally { if ($null -ne $process) { $process.Dispose() } }
}
@{ succeeded = $true; userRoot = $productionRoot; productionRoot = $productionRoot; acceptanceRoot = $acceptanceRoot; phases = $completed; productionProvenance = $inputs; acceptanceProvenance = $acceptanceInputs; shippingExecutableSha256 = $binarySha256;
    platformCommit = (& git -C $repo rev-parse HEAD); coverage = 'Actual shipping host: six production package phases, followed by six separate genuine UPCAM acceptance-ID phases with actual automatic Core and upstream local fake providers. Synthetic sent is not real delivery. No actual HTTP/Webhook/provider calls, business messages or real credentials.';
    limitations = @('Production UPCAM provider delivery remains unexecuted; positive automatic work uses a separate compiled synthetic acceptance ID/root.', 'DingTalk host value coverage is synthetic model-only native save/approval with commit-revision read/revoke/delete; model-key value read/provider dispatch unexecuted, no Webhook values/purpose. UPCAM host secret is generated binding key only.', 'Manifest/version/fault variants are synthetic integration inputs; external canonical data and schema rollback are not covered.', 'Windows11 manual acceptance is unexecuted.') } |
    ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
Write-Host "Business plugin actual process sequence completed: $output"
