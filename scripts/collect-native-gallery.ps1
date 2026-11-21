[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'NativeGalleryReport.psm1') -Force
$repo = Split-Path $PSScriptRoot -Parent
$index = Export-NativeGallery -RepositoryRoot $repo
Write-Host "Native gallery index: $($index.acceptedImages) independently bound PNGs; blocked/skipped/unreached rows remain explicit. Human visual review pending."
