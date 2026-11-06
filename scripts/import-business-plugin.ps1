[CmdletBinding()]
param(
    [string]$InputRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'tests/integration'),
    [string]$DestinationRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/native/fixtures/business'),
    [ValidateSet('Production', 'Acceptance')][string]$Mode = 'Production'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$appRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/native/app'))
$destinationPath = [IO.Path]::GetFullPath($DestinationRoot)
if ($destinationPath.Equals($appRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $destinationPath.StartsWith($appRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'External integration inputs must stay outside the shipping app and its bundled Plugins directory.'
}
$verified = @()
foreach ($name in @('dingtalk', $(if ($Mode -eq 'Acceptance') { 'upcam-acceptance' } else { 'upcam' }))) {
    $directory = Join-Path $InputRoot $name
    $provenance = Get-Content -LiteralPath (Join-Path $directory 'provenance.json') -Raw | ConvertFrom-Json
    $package = Join-Path $directory $provenance.packageFile
    if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $provenance.sha256 -or
        (Get-Item -LiteralPath $package).Length -ne $provenance.packageBytes) { throw "Pinned $name package SHA256/length mismatch." }
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $files = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
        if ($files.Count -ne $provenance.fileCount -or $files.Count -gt 1000 -or
            ($files | Measure-Object -Property Length -Sum).Sum -ne $provenance.expandedBytes -or $provenance.expandedBytes -gt 100MB) { throw "Pinned $name package inventory/size mismatch." }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($file in $files) {
            $relative = $file.FullName
            if (-not $seen.Add($relative) -or $relative -match '(^/|\\|:|(^|/)[.][.](/|$))' -or
                $relative -match '(?i)(^|/)(Workbench[.](Sdk|WinUI[.]Sdk|Core|App)[.]dll|Microsoft[.](WinUI|WindowsAppRuntime).*|WinRT[.]Runtime[.]dll|.*Desktop.*[.]dll)$' -or
                $relative -match '(?i)[.](pri|xaml|winmd|pfx|p12|dpapi)$' -or
                ($relative.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase) -and $relative -cne 'worker/UPCAMPluginWorker.exe')) {
                throw "Forbidden host/Desktop/credential payload or unsafe entry in $name input."
            }
            $pin = @($provenance.files | Where-Object { $_.path -ceq $relative })
            if ($pin.Count -ne 1 -or $pin[0].bytes -ne $file.Length) { throw "Unpinned $name package entry." }
            $stream = $file.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($hash -ne $pin[0].sha256) { throw "Pinned $name file hash mismatch." }
        }
        $manifestEntry = $archive.GetEntry('plugin.json')
        if ($null -eq $manifestEntry) { throw 'A strict root-manifest ZIP is required; arbitrary wrappers are not guessed.' }
        $reader = [IO.StreamReader]::new($manifestEntry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($manifest.id -ne $provenance.pluginId -or $manifest.version -ne $provenance.version -or $manifest.apiVersion -ne 1 -or $manifest.minimumHostVersion -ne '0.2.0') { throw "Pinned $name manifest mismatch." }
        if ($name -eq 'upcam-acceptance') {
            $reader = [IO.StreamReader]::new($archive.GetEntry('plugin-package.json').Open())
            try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            if ($manifest.id -ne 'com.upcam.task-digest.acceptance' -or $provenance.syntheticAcceptance -ne $true -or
                $metadata.syntheticAcceptance -ne $true -or $metadata.pluginId -ne $manifest.id -or
                $metadata.sourceCommit -ne $provenance.actualCheckoutCommit -or $metadata.runtimeServicesVersion -ne 1) {
                throw 'The separately compiled synthetic acceptance identity/provenance is required.'
            }
        }
    }
    finally { $archive.Dispose() }
    $verified += @{ name = $name; directory = $directory; provenance = $provenance }
}
# Audit both archives before making the inputs visible to any native probe. Never install into app/Plugins.
New-Item $DestinationRoot -ItemType Directory -Force | Out-Null
$inputs = @()
foreach ($item in $verified) {
    $destination = Join-Path $DestinationRoot $item.name
    New-Item $destination -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $item.directory 'package.zip') -Destination (Join-Path $destination 'package.zip') -Force
    Copy-Item -LiteralPath (Join-Path $item.directory 'provenance.json') -Destination (Join-Path $destination 'provenance.json') -Force
    $pin = $item.provenance
    $inputs += @{ pluginId = $pin.pluginId; version = $pin.version; sourceCommit = $pin.sourceCommit; sourceRepository = $pin.sourceRepository;
        workflowRunId = $pin.workflowRunId; sha256 = $pin.sha256; transport = $pin.transport; packageFile = "$($item.name)/package.zip"; provenanceFile = "$($item.name)/provenance.json" }
}
$inputs | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $DestinationRoot 'inputs.json') -Encoding utf8
Write-Host "Imported $Mode independently built, provenance-pinned business inputs outside bundled Plugins (722 files audited)."
