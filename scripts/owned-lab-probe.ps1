[CmdletBinding()]
param([Parameter(Mandatory)][string]$DeliveryDirectory, [ValidateRange(120,600)][int]$TimeoutSeconds = 300)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') { throw 'OwnedLabProbeUnavailable: actual Windows x64 is required; no native result was produced.' }
$repo = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'TestingPluginDelivery.psm1') -Force
Test-TestingPluginDelivery -Directory $DeliveryDirectory -RequireCurrentSource
$zip = Join-Path $DeliveryDirectory 'workbench-testing-plugin-0.2.0.zip'
$zipSha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$metadata = Get-Content (Join-Path $DeliveryDirectory 'provenance.json') -Raw | ConvertFrom-Json -AsHashtable
$id = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
$output = Join-Path $repo "artifacts/native/owned-lab-probes/$id"
$stage = Join-Path $output 'probe'
$payload = Join-Path $output 'shipping-payload'
New-Item $stage -ItemType Directory -Force | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory($zip,$payload)
$helper = $metadata.helper
$literal = @($helper.ImageSha,$helper.NativeSourceSha,$helper.SourceCommit,$helper.Msvc,$helper.Sdk,$helper.Profile) | ForEach-Object { (ConvertTo-Json -InputObject ([string]$_) -Compress) }
$descriptor = Join-Path $output 'OwnedNativeBuild.g.cs'
"namespace Workbench.Testing.Windows; internal static class OwnedNativeBuild { internal static OwnedDriverDescriptor Descriptor { get; } = new(true, $($literal -join ', ')); }" | Set-Content $descriptor -Encoding utf8
$descriptorJson = Join-Path $output 'descriptor.json'
$helper | ConvertTo-Json -Depth 4 | Set-Content $descriptorJson -Encoding utf8
& dotnet build (Join-Path $repo 'tests/Workbench.OwnedLab.WindowsProbe/Workbench.OwnedLab.WindowsProbe.csproj') -c Release --nologo -o $stage "-p:OwnedLabDescriptorPath=$descriptor" "-p:OwnedLabDescriptorJsonPath=$descriptorJson"
if ($LASTEXITCODE -ne 0) { throw 'OwnedLabProbeBuildFailed' }
# Replace every shipping payload with the audited ZIP bytes AFTER building the probe harness.
foreach ($record in $metadata.payload) {
    $source = Join-Path $payload $record.path; $target = Join-Path $stage $record.path
    New-Item (Split-Path $target -Parent) -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
    if ((Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne $record.sha256) { throw 'OwnedLabProbeShippingBytesChanged' }
}
$fixtures = Join-Path $output 'native-fixtures'
& (Join-Path $PSScriptRoot 'build-owned-lab.ps1') -Configuration Release -OutputDirectory $fixtures -BuildAcceptanceFixtures
Copy-Item -LiteralPath (Join-Path $fixtures 'fixtures') -Destination $stage -Recurse
foreach ($name in @('PartialHeader','TruncatedInit','ExtraInit','OversizeInit','CrossSessionAction','OutOfOrderAction')) {
    Copy-Item -LiteralPath (Join-Path $stage 'fixtures/NestedJob') -Destination (Join-Path $stage "fixtures/$name") -Recurse
}
foreach ($name in @('Missing','FakePe','WrongHash')) {
    $directory = Join-Path $stage "fixtures/$name"; New-Item $directory -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $stage 'fixtures/NestedJob/descriptor.json') $directory
    if ($name -ceq 'FakePe') { [IO.File]::WriteAllBytes((Join-Path $directory 'Workbench.OwnedLab.exe'),[byte[]]@(0x4d,0x5a,0,0)) }
    elseif ($name -ceq 'WrongHash') {
        Copy-Item (Join-Path $stage 'fixtures/NestedJob/Workbench.OwnedLab.exe') $directory
        $bad = Get-Content (Join-Path $directory 'descriptor.json') -Raw | ConvertFrom-Json -AsHashtable
        $bad.ImageSha = '0' * 64; $bad | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $directory 'descriptor.json') -Encoding utf8
    }
}
New-Item -ItemType Junction -Path (Join-Path $stage 'fixtures/Reparse') -Target (Join-Path $stage 'fixtures/NestedJob') | Out-Null
$process = $null; $succeeded = $false; $identity = $null
try {
    $dotnet = (Get-Command dotnet).Source
    $arguments = @(('"' + (Join-Path $stage 'Workbench.OwnedLab.WindowsProbe.dll') + '"'), ('"' + (Join-Path $output 'observations') + '"'), $zipSha)
    $process = Start-Process -FilePath $dotnet -ArgumentList $arguments -WorkingDirectory $stage -PassThru -RedirectStandardOutput (Join-Path $output 'stdout.log') -RedirectStandardError (Join-Path $output 'stderr.log')
    $null = $process.Handle # retain the actual newly created process before any wait
    $birth = $process.StartTime.ToUniversalTime().ToFileTimeUtc()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill($true); $process.WaitForExit(); throw 'OwnedLabProbeTimeout' }
    $identity = @{ processId = $process.Id; processCreationFileTimeUtc = $birth; processExitFileTimeUtc = $process.ExitTime.ToUniversalTime().ToFileTimeUtc(); exitCode = $process.ExitCode }
    $console = [IO.File]::ReadAllText((Join-Path $output 'stdout.log'))
    if ([Text.Encoding]::UTF8.GetByteCount($console) -gt 65536) { throw 'OwnedLabProbeConsoleOversize' }
    Write-Host $console
    if ($process.ExitCode -ne 0) { throw 'OwnedLabProbeProcessFailed' }
    $report = Get-Content (Join-Path $output 'observations/report.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($report.schema -ne 1 -or $report.succeeded -isnot [bool] -or -not $report.succeeded -or @($report.checks).Count -ne 27) { throw 'OwnedLabProbeChecksIncomplete' }
    $leases = @()
    try {
        foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $output 'observations') -Recurse -File -Filter owner.lock) + @(Get-Item (Join-Path $stage 'Workbench.OwnedLab.exe')) + @(Get-ChildItem -LiteralPath (Join-Path $stage 'fixtures') -Recurse -File -Filter Workbench.OwnedLab.exe | Where-Object { $_.Directory.Name -cne 'Reparse' })) {
            $leases += [IO.File]::Open($file.FullName,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        }
    }
    finally { foreach ($lease in $leases) { $lease.Dispose() } }
    $succeeded = $true
}
finally {
    $summary = [ordered]@{ schema = 1; succeeded = $succeeded; sourceCommit = $metadata.sourceCommit; helper = $helper; zipSha = $zipSha; probeProcess = $identity; leaseAvailableAfterProbeExit = $succeeded; shippingLauncherSha = (Get-FileHash (Join-Path $stage 'Workbench.Testing.Windows.dll') -Algorithm SHA256).Hash.ToLowerInvariant(); fixtureLauncherSha = (Get-FileHash (Join-Path $stage 'Workbench.OwnedLab.FixtureLauncher.dll') -Algorithm SHA256).Hash.ToLowerInvariant(); limits = 'Actual standalone owned synthetic-target probe observations; not host-panel or independently downloaded Windows11 acceptance.' }
    $summary | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $output 'summary.json') -Encoding utf8
    Write-Host ($summary | ConvertTo-Json -Depth 6 -Compress)
    if ($null -ne $process) { $process.Dispose() }
}
