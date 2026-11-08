[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$ReferenceOnly,
      [ValidateSet('Production','SourceAcceptance', IgnoreCase=$false)][string]$Profile = 'Production',
      [string]$OutputDirectory = $(if ($Profile -eq 'SourceAcceptance') { Join-Path $PSScriptRoot '../artifacts/native/source-acceptance-fixture' } else { Join-Path $PSScriptRoot '../artifacts/native/testing-plugin' }),
      [string]$PairedProductionDirectory = (Join-Path $PSScriptRoot '../artifacts/native/testing-plugin'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$deliveryModule = Import-Module (Join-Path $PSScriptRoot 'TestingPluginDelivery.psm1') -Force -PassThru
$repo = Split-Path $PSScriptRoot -Parent
if ($Profile -eq 'Production' -and $PSBoundParameters.ContainsKey('PairedProductionDirectory')) { throw 'TestingProductionPairUnexpected' }
$build = Join-Path $repo $(if ($ReferenceOnly) { 'artifacts/testing-reference-build' } elseif ($Profile -eq 'SourceAcceptance') { 'artifacts/testing-source-acceptance-package-build' } else { 'artifacts/testing-package-build' })
# Refuse every reserved root before build/output mutation. Earlier evidence is never deleted.
& $deliveryModule { param($p) Assert-TestingFreshDirectory $p } $build
if (-not $ReferenceOnly) {
    & $deliveryModule { param($p) Assert-TestingFreshDirectory $p } $OutputDirectory
    if ($Profile -eq 'SourceAcceptance') {
        if ([IO.Path]::GetFullPath($OutputDirectory) -eq [IO.Path]::GetFullPath($PairedProductionDirectory)) { throw 'TestingProductionPairMismatch' }
        & $deliveryModule { param($p) Assert-TestingFreshDirectory $p } $PairedProductionDirectory
    }
}
if (-not $IsWindows -and -not $ReferenceOnly) { throw 'Native testing packaging requires actual Windows MSVC; explicit -ReferenceOnly compiles without a ZIP.' }
$contract = & $deliveryModule { param($p) Get-TestingProfile $p } $Profile
$project = Join-Path $repo $contract.project
& $deliveryModule { param($p) Assert-TestingNoLinks $p } $project
$arguments = @('build', $project, '-c', $Configuration, '--nologo', '-t:Rebuild', '-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$build/intermediates", '-o', $build)
if ($ReferenceOnly) { $arguments += @('-p:OwnedLabReferenceOnly=true','-p:UseWinUI=false','-p:AppxGeneratePriEnabled=false','-p:WindowsAppSDKSelfContained=false') }
else {
    $native = Join-Path $repo 'artifacts/native/owned-lab'
    if (-not (Test-Path -LiteralPath $native)) { & (Join-Path $PSScriptRoot 'build-owned-lab.ps1') -Configuration $Configuration -OutputDirectory $native }
    foreach ($file in @('Workbench.OwnedLab.exe','OwnedNativeBuild.g.cs','descriptor.json','native-build.json')) {
        $path = Join-Path $native $file
        & $deliveryModule { param($p) Assert-TestingNoLinks $p } $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'TestingOwnedHelperMissing' }
    }
    $arguments += @("-p:OwnedLabDescriptorPath=$(Join-Path $native 'OwnedNativeBuild.g.cs')", "-p:OwnedLabDescriptorJsonPath=$(Join-Path $native 'descriptor.json')")
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Testing plugin build failed ($LASTEXITCODE)." }
if ($ReferenceOnly) { Write-Host 'ReferenceOnly compilation completed with unavailable descriptor; no delivery or ZIP was created.'; return }
Copy-Item -LiteralPath (Join-Path $native 'Workbench.OwnedLab.exe') $build
Copy-Item -LiteralPath (Join-Path $native 'native-build.json') (Join-Path $build 'OwnedLab.native-build.json')
if ($Profile -eq 'SourceAcceptance') {
    # One full transitive Windows Rebuild supplies all seven common files for both exports.
    New-TestingPluginDelivery -BuildDirectory $build -OutputDirectory $PairedProductionDirectory
    New-TestingPluginDelivery -BuildDirectory $build -OutputDirectory $OutputDirectory -Profile SourceAcceptance -ProductionDirectory $PairedProductionDirectory
}
else { New-TestingPluginDelivery -BuildDirectory $build -OutputDirectory $OutputDirectory }
