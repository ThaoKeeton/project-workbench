[CmdletBinding()]
param([switch]$ReferenceOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -and -not $ReferenceOnly) { throw 'Windows SDK packaging requires Windows; use -ReferenceOnly for an explicitly limited Linux compilation snapshot.' }
$repo = Split-Path $PSScriptRoot -Parent
$outputName = if ($ReferenceOnly) { 'sdk-reference-snapshot' } else { 'sdk-snapshot' }
$output = Join-Path $repo "artifacts/$outputName"
$source = Join-Path $output 'source'
$packages = Join-Path $output 'packages'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
$archive = Join-Path $repo "artifacts/$outputName.zip"
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
New-Item $source, $packages -ItemType Directory -Force | Out-Null
# Explicit source allow-list: no host/Desktop binary, private config, credentials or business data.
$files = @('Directory.Build.props', 'global.json', 'src/Workbench.Sdk/Workbench.Sdk.csproj',
    'src/Workbench.Sdk/PluginContracts.cs', 'src/Workbench.Sdk/RuntimeContracts.cs', 'src/Workbench.Sdk/ProjectContracts.cs', 'src/Workbench.Sdk/TestingContracts.cs', 'src/Workbench.Sdk/README.md',
    'src/Workbench.WinUI.Sdk/Workbench.WinUI.Sdk.csproj', 'src/Workbench.WinUI.Sdk/WinUiView.cs', 'src/Workbench.WinUI.Sdk/README.md')
$hashes = @()
foreach ($relative in $files) {
    $destination = Join-Path $source $relative
    New-Item (Split-Path $destination -Parent) -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $destination
    $hashes += @{ path = $relative; sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$commit = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot establish SDK source commit provenance.' }
$dirty = @(& git -C $repo status --porcelain -- @files).Count -ne 0
if ($LASTEXITCODE -ne 0) { throw 'Cannot establish SDK source status.' }
foreach ($project in @('Workbench.Sdk', 'Workbench.WinUI.Sdk')) {
    $projectPath = Join-Path $source "src/$project/$project.csproj"
    $arguments = @('pack', $projectPath, '-c', 'Release', '--nologo', '-o', $packages, '-p:ContinuousIntegrationBuild=true', "-p:PathMap=$source=/_/")
    if ($ReferenceOnly) { $arguments += @('-p:UseWinUI=false', '-p:AppxGeneratePriEnabled=false', '-p:IncludeProjectPriFile=false', '-p:WindowsAppSDKSelfContained=false') }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "SDK packaging failed: $project" }
}
$packageHashes = @(Get-ChildItem -LiteralPath $packages -Filter '*.nupkg' -File | Sort-Object Name | ForEach-Object {
    @{ path = "packages/$($_.Name)"; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
if ($packageHashes.Count -ne 2) { throw 'Exactly two SDK packages are required.' }
@{ sourceCommit = $commit; sdkSourcesDirty = $dirty; sdkVersion = '0.4.0'; fileVersion = '0.4.0.0'; assemblyVersion = '1.0.0.0'; workbenchApi = 1;
    runtimeServices = 1; projectServices = 1; testingServices = 1; dotnetSdk = '10.0.403'; windowsAppSdk = '1.8.260921001'; referenceOnly = [bool]$ReferenceOnly;
    sources = $hashes; packages = $packageHashes } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'manifest.json') -Encoding utf8
# Exclude pack bin/obj; consumers get only the reviewed source list and pinned build settings.
Get-ChildItem -LiteralPath $source -Directory -Recurse | Where-Object Name -in @('bin', 'obj') | Sort-Object { $_.FullName.Length } -Descending | Remove-Item -Recurse -Force
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -Force
Write-Host "SDK snapshot: $output"
