[CmdletBinding()]
param([ValidateRange(30,600)][int]$TimeoutSeconds = 240)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Testing acceptance requires the actual shipping Windows WinUI executable.' }
$repo = Split-Path $PSScriptRoot -Parent
$app = Join-Path $repo 'artifacts/native/app/Workbench.App.exe'
$delivery = Join-Path $repo 'artifacts/native/testing-plugin'
Import-Module (Join-Path $PSScriptRoot 'TestingPluginDelivery.psm1') -Force
Test-TestingPluginDelivery -Directory $delivery -RequireCurrentSource
if (-not (Test-Path $app -PathType Leaf)) { throw 'Build the shipping host first.' }
Import-Module (Join-Path $PSScriptRoot 'TestingPluginProbe.psm1') -Force
$identity = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
$output = Join-Path $repo "artifacts/native/testing-probes/$identity"
New-Item $output -ItemType Directory | Out-Null
$root = Join-Path (Join-Path ([IO.Path]::GetTempPath()) 'ProjectWorkbench-testing-probe') ([Guid]::NewGuid().ToString('N'))
$hash = (Get-FileHash (Join-Path $delivery (Get-TestingPluginPackageName)) -Algorithm SHA256).Hash.ToLowerInvariant()
$completed = @(Invoke-TestingPluginProbePhases -OutputDirectory $output -ExpectedRoot $root -ExpectedPackageHash $hash -Observe {
    param($phase, $report, $observation)
    $process = $null
    try {
        $arguments = @('--testing-probe',$phase,'--probe-root',"`"$root`"",'--smoke-report',"`"$report`"")
        $process = Start-Process -FilePath $app -ArgumentList $arguments -PassThru -RedirectStandardOutput (Join-Path $output "$phase.stdout.log") -RedirectStandardError (Join-Path $output "$phase.stderr.log")
        $observation.ProcessId = $process.Id
        $observation.HasExited = $process.WaitForExit($TimeoutSeconds * 1000)
        if ($observation.HasExited) {
            $observation.ExitCode = $process.ExitCode
            $roots = @($root)
            $projects = Join-Path $root '.workspaces/projects'
            foreach ($part in @($root, (Join-Path $root '.workspaces'), $projects)) {
                if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'TestingOwnedRootInvalid' }
            }
            if (Test-Path -LiteralPath $projects -PathType Container) {
                foreach ($project in Get-ChildItem -LiteralPath $projects -Directory) {
                    if ($project.Name -cnotmatch '^[0-9a-f]{32}$' -or ($project.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'TestingOwnedRootInvalid' }
                    $roots += $project.FullName
                }
            }
            $leases = @()
            try {
                foreach ($owned in $roots) { $leases += [IO.File]::Open((Join-Path $owned 'host.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
                $observation.LeaseAvailable = $true
            }
            finally { foreach ($lease in $leases) { $lease.Dispose() } }
        }
    }
    finally { if ($null -ne $process) { $process.Dispose() } }
})
$summary = @{ schemaVersion = 1; succeeded = $true; sourceCommit = (& git -C $repo rev-parse HEAD).Trim(); userRoot = $root; packageSha256 = $hash; phases = $completed; limits = 'Windows Server automatic acceptance only; Windows11 human picker/IME/DPI/accessibility and real devices/providers deferred. Synthetic interrupted journal seed is not an OS crash.' }
$summary | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $output 'summary.json') -Encoding utf8
Write-Host "Testing plugin actual native probe completed: $output"
