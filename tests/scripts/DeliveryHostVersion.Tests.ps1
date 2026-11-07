[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$module = Import-Module (Join-Path $repo 'scripts/BusinessPluginDelivery.psm1') -Force -PassThru
$commit = '0123456789abcdef0123456789abcdef01234567'
$passed = 0
# Pure metadata parser cases. These strings are never PE/native execution evidence.
foreach ($semantic in @('0.2.0', '0.3.0', '12.34.56')) {
    $stamp = & $module { param($product, $file) Get-DeliveryHostVersion $product $file '1.0.0.0' } "$semantic+$commit" "$semantic.0"
    if ($stamp.productVersion -cne "$semantic+$commit" -or $stamp.fileVersion -cne "$semantic.0" -or $stamp.sourceCommit -cne $commit) { throw 'Semantic/file version parsing changed the declared source.' }
    $passed++
}
foreach ($case in @(
    @("0.1.0+$commit", '0.1.0.0', '1.0.0.0'),
    @("0.0.0+$commit", '0.0.0.0', '1.0.0.0'),
    @("0.3.0+$commit", '0.2.0.0', '1.0.0.0'),
    @("0.3.0+$commit", '0.3.0.1', '1.0.0.0'),
    @("0.3.0+$commit", '0.3.0.0', '2.0.0.0'),
    @('0.3.0', '0.3.0.0', '1.0.0.0'),
    @('0.3.0+0123456', '0.3.0.0', '1.0.0.0'),
    @("00.3.0+$commit", '0.3.0.0', '1.0.0.0'),
    @("0.3.0+$($commit.ToUpperInvariant())", '0.3.0.0', '1.0.0.0'),
    @("0.3.0-preview+$commit", '0.3.0.0', '1.0.0.0'),
    @("0.3.65536+$commit", '0.3.65536.0', '1.0.0.0')
)) {
    $rejected = $false
    try { & $module { param($product, $file, $assembly) Get-DeliveryHostVersion $product $file $assembly } @case | Out-Null }
    catch { if (-not $_.Exception.Message.StartsWith('host_metadata:')) { throw }; $rejected = $true }
    if (-not $rejected) { throw "Unsupported host metadata was accepted: $($case -join ', ')" }
    $passed++
}
Write-Output "Delivery host version parser: $passed metadata-only cases passed (no PE/native execution)."
