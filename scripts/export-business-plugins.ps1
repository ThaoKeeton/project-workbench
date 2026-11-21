[CmdletBinding()]
param(
    [string]$AppDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/native/app'),
    [string]$InputRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'tests/integration'),
    [string]$DestinationRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/native/business-plugins'),
    [switch]$RequireCurrentSource
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'BusinessPluginDelivery.psm1') -Force
Export-BusinessPlugins -AppDirectory $AppDirectory -InputRoot $InputRoot -DestinationRoot $DestinationRoot -RequireCurrentSource:$RequireCurrentSource
