[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory, [switch]$AllowReferenceOnly, [switch]$RequireCurrentSource)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '../../scripts/TestingPluginDelivery.psm1') -Force
Test-TestingPluginDelivery -Directory $Directory -AllowReferenceOnly:$AllowReferenceOnly -RequireCurrentSource:$RequireCurrentSource
$temp = Join-Path ([IO.Path]::GetTempPath()) ('testing-package-regression-' + [Guid]::NewGuid().ToString('N'))
function Refuses([scriptblock]$Action) { try { & $Action } catch { return }; throw 'Corrupt testing delivery was accepted.' }
try {
    New-Item $temp -ItemType Directory | Out-Null
    Copy-Item (Join-Path $Directory '*') $temp -Recurse
    Add-Content (Join-Path $temp 'README.md') 'tamper'
    Refuses { Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly }
    Remove-Item $temp -Recurse -Force
    New-Item $temp -ItemType Directory | Out-Null
    Copy-Item (Join-Path $Directory '*') $temp -Recurse
    New-Item (Join-Path $temp 'Workbench.Sdk.dll') -ItemType File | Out-Null
    Refuses { Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly }
    Remove-Item $temp -Recurse -Force
    New-Item $temp -ItemType Directory | Out-Null
    Copy-Item (Join-Path $Directory '*') $temp -Recurse
    $metadata = Get-Content (Join-Path $temp 'provenance.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($metadata.referenceOnly) { Refuses { Test-TestingPluginDelivery -Directory $temp } }
    $metadata.sourceFiles[0].sha256 = '0' * 64
    $metadata | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $temp 'provenance.json')
    Refuses { Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly -RequireCurrentSource }
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
        @('README.md','provenance.json','workbench-testing-plugin-0.1.0.zip' | ForEach-Object { (Get-FileHash (Join-Path $temp $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_ }) | Set-Content (Join-Path $temp 'SHA256SUMS')
        Refuses { Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly }
    }
    Write-Host 'Testing package integrity, exact ZIP allow-list, payload/manifest/source identity and reference-only regressions passed (8 negative cases).'

}
finally { if (Test-Path $temp) { Remove-Item $temp -Recurse -Force } }
