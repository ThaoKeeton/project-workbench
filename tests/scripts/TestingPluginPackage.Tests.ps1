[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory, [switch]$AllowReferenceOnly, [switch]$RequireCurrentSource)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '../../scripts/TestingPluginDelivery.psm1') -Force
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Test-TestingPluginDelivery -Directory $Directory -AllowReferenceOnly:$AllowReferenceOnly -RequireCurrentSource:$RequireCurrentSource
$temp = Join-Path ([IO.Path]::GetTempPath()) ('testing-package-regression-' + [Guid]::NewGuid().ToString('N'))
function Refuses([scriptblock]$Action, [string]$Expected) {
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -cne $Expected) { throw "Expected $Expected; observed $($_.Exception.Message)" }
        Write-Host "Exact refusal: $Expected"; return
    }
    throw "Expected $Expected; corrupt testing delivery was accepted."
}
function Refresh-Sums([string]$Root) {
    @('README.md','provenance.json','workbench-testing-plugin-0.1.0.zip' | ForEach-Object { (Get-FileHash (Join-Path $Root $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_ }) | Set-Content (Join-Path $Root 'SHA256SUMS')
}
try {
    New-Item $temp -ItemType Directory | Out-Null
    Copy-Item (Join-Path $Directory '*') $temp -Recurse
    Add-Content (Join-Path $temp 'README.md') 'tamper'
    Refuses { Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly } 'TestingDeliveryHashMismatch'
    Remove-Item $temp -Recurse -Force
    New-Item $temp -ItemType Directory | Out-Null
    Copy-Item (Join-Path $Directory '*') $temp -Recurse
    New-Item (Join-Path $temp 'Workbench.Sdk.dll') -ItemType File | Out-Null
    Refuses { Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly } 'TestingDeliveryFileListInvalid'
    Remove-Item $temp -Recurse -Force
    New-Item $temp -ItemType Directory | Out-Null
    Copy-Item (Join-Path $Directory '*') $temp -Recurse
    $metadata = Get-Content (Join-Path $temp 'provenance.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($metadata.referenceOnly) { Refuses { Test-TestingPluginDelivery -Directory $temp } 'TestingDeliveryReferenceOnly' }
    $module = Get-Module TestingPluginDelivery
    $sourceRecords = @($metadata.sourceFiles)
    & $module { param($records, $root) Assert-TestingSourceRecords -Current @(Get-TestingSourceFiles $root) -Recorded $records } $sourceRecords $repo
    $metadata.sourceFiles[0].sha256 = '0' * 64
    $metadata | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $temp 'provenance.json')
    Refresh-Sums $temp
    # This deliberately dirty/ReferenceOnly fixture passes all outer guards after sums refresh.
    # Invoke the SAME private source-record guard used by RequireCurrentSource, without inventing clean provenance.
    Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly
    Refuses { & $module { param($records, $root) Assert-TestingSourceRecords -Current @(Get-TestingSourceFiles $root) -Recorded $records } @($metadata.sourceFiles) $repo } 'TestingSourceHashMismatch'
    Refuses { & $module { param($records, $root) Assert-TestingSourceRecords -Current @(Get-TestingSourceFiles $root) -Recorded $records } @($metadata.sourceFiles | Select-Object -Skip 1) $repo } 'TestingSourceFileListInvalid'
    & $module { Assert-TestingSourceIdentity -RecordedCommit ('a' * 40) -RecordedDirty $false -CurrentCommit ('a' * 40) -CurrentDirty $false }
    Refuses { & $module { Assert-TestingSourceIdentity -RecordedCommit ('b' * 40) -RecordedDirty $false -CurrentCommit ('a' * 40) -CurrentDirty $false } } 'TestingCurrentSourceMismatch'
    Refuses { & $module { Assert-TestingSourceIdentity -RecordedCommit ('a' * 40) -RecordedDirty $true -CurrentCommit ('a' * 40) -CurrentDirty $false } } 'TestingCurrentSourceMismatch'
    Refuses { & $module { Assert-TestingSourceIdentity -RecordedCommit ('a' * 40) -RecordedDirty $false -CurrentCommit ('a' * 40) -CurrentDirty $true } } 'TestingCurrentSourceMismatch'
    foreach ($mutation in @('extra-sdk','duplicate-manifest','changed-entry','wrong-manifest')) {
        Remove-Item $temp -Recurse -Force
        New-Item $temp -ItemType Directory | Out-Null
        Copy-Item (Join-Path $Directory '*') $temp -Recurse
        $zipPath = Join-Path $temp 'workbench-testing-plugin-0.1.0.zip'
        $archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Update)
        try {
            if ($mutation -eq 'extra-sdk') { $entry = $archive.CreateEntry('Workbench.Sdk.dll'); $text = 'forbidden SDK copy' }
            elseif ($mutation -eq 'duplicate-manifest') { $entry = $archive.CreateEntry('plugin.json'); $text = '{}' }
            elseif ($mutation -eq 'changed-entry') { $archive.GetEntry('Workbench.Testing.dll').Delete(); $entry = $archive.CreateEntry('Workbench.Testing.dll'); $text = 'changed payload' }
            else {
                $entry = $archive.GetEntry('plugin.json')
                $reader = [IO.StreamReader]::new($entry.Open()); try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable } finally { $reader.Dispose() }
                $manifest.minimumHostVersion = '0.3.0'; $text = $manifest | ConvertTo-Json -Depth 10
                $entry.Delete(); $entry = $archive.CreateEntry('plugin.json')
            }
            $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false)); try { $writer.Write($text) } finally { $writer.Dispose() }
        }
        finally { $archive.Dispose() }
        $metadata = Get-Content (Join-Path $temp 'provenance.json') -Raw | ConvertFrom-Json -AsHashtable
        $metadata.zip.length = (Get-Item $zipPath).Length
        $metadata.zip.sha256 = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($mutation -eq 'wrong-manifest') {
            $record = @($metadata.payload | Where-Object path -CEQ 'plugin.json')[0]
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text)
            $record.length = $bytes.Length; $record.sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        }
        $metadata | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $temp 'provenance.json')
        Refresh-Sums $temp
        $expected = switch ($mutation) { 'extra-sdk' { 'TestingPayloadFileListInvalid' }; 'duplicate-manifest' { 'TestingPayloadFileListInvalid' }; 'changed-entry' { 'TestingPayloadHashMismatch' }; 'wrong-manifest' { 'TestingManifestInvalid' } }
        Refuses { Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly } $expected
    }
    Write-Host 'Testing package regressions passed: independently attributed outer checksum, file list, ReferenceOnly, source hash/list/three identity refusals, and four ZIP/payload/manifest refusals.'

}
finally { if (Test-Path $temp) { Remove-Item $temp -Recurse -Force } }
