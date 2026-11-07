[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$ReferenceOnly,
      [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/native/testing-plugin'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -and -not $ReferenceOnly) { throw 'Native testing packaging requires Windows or explicit -ReferenceOnly.' }
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'plugins/Workbench.Testing/Workbench.Testing.csproj'
$build = Join-Path $repo 'artifacts/testing-package-build'
$arguments = @('build', $project, '-c', $Configuration, '--nologo', '-o', $build)
if ($ReferenceOnly) { $arguments += @('-p:UseWinUI=false','-p:AppxGeneratePriEnabled=false','-p:WindowsAppSDKSelfContained=false') }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Testing plugin build failed ($LASTEXITCODE)." }
Import-Module (Join-Path $PSScriptRoot 'TestingPluginDelivery.psm1') -Force
New-TestingPluginDelivery -BuildDirectory $build -OutputDirectory $OutputDirectory -ReferenceOnly:$ReferenceOnly
