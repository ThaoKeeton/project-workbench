[CmdletBinding()]
param([ValidateRange(30,600)][int]$TimeoutSeconds = 240)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Source acceptance requires the actual shipping Windows WinUI executable.' }
$repo = Split-Path $PSScriptRoot -Parent
$appDirectory = Join-Path $repo 'artifacts/native/app'
$app = Join-Path $appDirectory 'Workbench.App.exe'
$delivery = Join-Path $repo 'artifacts/native/source-acceptance-fixture'
$production = Join-Path $repo 'artifacts/native/testing-plugin'
Import-Module (Join-Path $PSScriptRoot 'TestingPluginDelivery.psm1') -Force
Test-TestingPluginDelivery -Directory $delivery -Profile SourceAcceptance -ProductionDirectory $production -AppDirectory $appDirectory -RequireCurrentSource
Import-Module (Join-Path $PSScriptRoot 'SourceAcceptanceProbe.psm1') -Force
$module = Get-Module SourceAcceptanceProbe
$identity = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
$output = Join-Path $repo "artifacts/native/source-acceptance-probes/$identity"
[IO.Directory]::CreateDirectory($output) | Out-Null
$parent = Join-Path ([IO.Path]::GetTempPath()) 'ProjectWorkbench-source-acceptance-probe'
$seedRoot = Join-Path $parent ([Guid]::NewGuid().ToString('N'))
$stopFigmaRoot = Join-Path $parent ([Guid]::NewGuid().ToString('N'))
$stopAiRoot = Join-Path $parent ([Guid]::NewGuid().ToString('N'))
$packageHash = (Get-FileHash -LiteralPath (Join-Path $delivery 'workbench-testing-source-acceptance-0.2.0.zip')).Hash.ToLowerInvariant()
$hostExeHash = (Get-FileHash -LiteralPath $app).Hash.ToLowerInvariant()
$hostDllHash = (Get-FileHash -LiteralPath (Join-Path $appDirectory 'Workbench.App.dll')).Hash.ToLowerInvariant()
$source = (& git -C $repo rev-parse HEAD).Trim(); if ($LASTEXITCODE -ne 0) { throw 'SourceCommitUnavailable' }
$completed = @(Invoke-SourceAcceptanceProbePhases -OutputDirectory $output -SeedRoot $seedRoot -StopFigmaRoot $stopFigmaRoot -StopAiRoot $stopAiRoot `
    -ExpectedPackageHash $packageHash -ExpectedSourceCommit $source -ExpectedHostExeHash $hostExeHash -ExpectedHostDllHash $hostDllHash -Observe {
    param($phase,$root,$report,$observation)
    $process = $null
    function Record-Termination {
        if ($observation.Terminated) { return }
        $observation.Terminated = $true
        # Record failure before terminating only the exact retained CI Process handle. Never a graceful pass.
        & $module { param($p,$d) Write-SourceAcceptanceNewJson $p $d } (Join-Path $output "$phase.termination.json") @{
            schemaVersion=1; kind='SourceAcceptanceOwnedProcessFailure'; succeeded=$false; phase=$phase; processId=$observation.ProcessId;
            processCreationFileTimeUtc=$observation.ProcessCreationFileTimeUtc; reason='SourceProcessObservationExpired'; terminationRequested=$true }
    }
    try {
        $arguments = @('--source-acceptance-probe',$phase,'--probe-root',"`"$root`"",'--smoke-report',"`"$report`"")
        $process = Start-Process -FilePath $app -ArgumentList $arguments -PassThru -RedirectStandardOutput (Join-Path $output "$phase.stdout.log") -RedirectStandardError (Join-Path $output "$phase.stderr.log")
        $observation.ProcessId = $process.Id
        if ($process.Handle -eq [IntPtr]::Zero) { throw 'SourceProcessHandleUnavailable' }
        $observation.ProcessCreationFileTimeUtc = [long]$process.StartTime.ToUniversalTime().ToFileTimeUtc()
        $budget = [Diagnostics.Stopwatch]::StartNew()
        while (-not $process.WaitForExit(1000)) {
            if ($budget.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                Record-Termination
                $process.Kill()
                if (-not $process.WaitForExit(10000)) { throw 'SourceOwnedProcessTerminationPending' }
                break
            }
        }
        $observation.HasExited = $process.HasExited
        if ($observation.HasExited) {
            $observation.ExitCode = $process.ExitCode
            $observation.ProcessExitFileTimeUtc = [long]$process.ExitTime.ToUniversalTime().ToFileTimeUtc()
        }
        if ($observation.HasExited -and $observation.ExitCode -eq 0 -and -not $observation.Terminated) {
            $roots = [Collections.Generic.List[string]]::new(); $roots.Add($root)
            $projects = Join-Path $root '.workspaces/projects'
            & $module { param($p) Assert-SourceAcceptanceNoLinks $p } $projects
            if (Test-Path -LiteralPath $projects -PathType Container) {
                $entries = @(Get-ChildItem -LiteralPath $projects -Force)
                if ($entries.Count -gt 1) { throw 'SourceOwnedProjectInventoryInvalid' }
                foreach ($project in $entries) {
                    if ($project -isnot [IO.DirectoryInfo] -or $project.Name -cnotmatch '^[0-9a-f]{32}$' -or
                        ($project.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'SourceOwnedProjectInventoryInvalid' }
                    $roots.Add($project.FullName)
                }
            }
            $leases = [Collections.Generic.List[IO.FileStream]]::new()
            try {
                # Hold all actual base/project leases simultaneously; Open never invents a missing host.lock.
                foreach ($owned in $roots) {
                    $lock = Join-Path $owned 'host.lock'; & $module { param($p) Assert-SourceAcceptanceNoLinks $p } $lock
                    $leases.Add([IO.File]::Open($lock,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None))
                }
                $observation.LeaseRoots = [string[]]$roots.ToArray()
            } finally { foreach ($lease in $leases) { $lease.Dispose() } }
        }
    } finally {
        if ($null -ne $process) {
            try {
                if (-not $process.HasExited) {
                    Record-Termination
                    $process.Kill()
                    if (-not $process.WaitForExit(10000)) { throw 'SourceOwnedProcessTerminationPending' }
                }
                if ($process.HasExited) {
                    $observation.HasExited=$true; $observation.ExitCode=$process.ExitCode
                    $observation.ProcessExitFileTimeUtc=[long]$process.ExitTime.ToUniversalTime().ToFileTimeUtc()
                }
            } finally { $process.Dispose() }
        }
    }
})
# Validation completed without changing native report bytes or backfilling native Closed/birth.
& $module { param($p,$d) Write-SourceAcceptanceNewJson $p $d } (Join-Path $output 'summary.json') @{
    schemaVersion=1; kind='SourceAcceptanceSummary'; succeeded=$true; sourceCommit=$source; packageSha256=$packageHash; hostExeSha256=$hostExeHash; hostDllSha256=$hostDllHash;
    seedRoot=$seedRoot; stopFigmaRoot=$stopFigmaRoot; stopAiRoot=$stopAiRoot; phases=$completed; phaseQualifiedChecks=36;
    limits='Actual installed synthetic source controls and Windows CurrentUser DPAPI. Terminal in-memory HTTP only; generated cases manual, no AUT execution. Live services and Windows11 human acceptance remain separate.' }
Write-Host "Installed synthetic native source acceptance completed: $output"
