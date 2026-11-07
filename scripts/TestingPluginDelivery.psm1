Set-StrictMode -Version Latest
$script:TestingPayload = @('Fixtures/figma.synthetic.json', 'Fixtures/requirements.synthetic.json', 'Workbench.Testing.deps.json', 'Workbench.Testing.dll', 'Workbench.Testing.Domain.dll', 'plugin.json')
if (-not ('WorkbenchTestingArchiveAudit' -as [type])) {
    Add-Type -TypeDefinition @'
public static class WorkbenchTestingArchiveAudit {
    public static uint Crc(byte[] bytes) {
        uint value = 0xffffffff;
        foreach (byte b in bytes) {
            value ^= b;
            for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xedb88320u : 0u);
        }
        return ~value;
    }
}
'@
}
function Get-TestingSourceFiles {
    param([string]$Repo)
    $paths = @('Directory.Build.props', 'global.json', '.github/workflows/build.yml', 'scripts/build.ps1', 'scripts/package-testing-plugin.ps1', 'scripts/TestingPluginDelivery.psm1', 'scripts/TestingPluginProbe.psm1', 'scripts/testing-plugin-probe.ps1')
    foreach ($part in @('plugins/Workbench.Testing', 'plugins/Workbench.Testing.Domain', 'src/Workbench.Sdk', 'src/Workbench.WinUI.Sdk', 'src/Workbench.App', 'src/Workbench.Core')) {
        $paths += @(& git -C $Repo ls-files --cached --others --exclude-standard -- $part)
        if ($LASTEXITCODE -ne 0) { throw 'TestingSourceFileListUnavailable' }
    }
    return @($paths | Sort-Object -Unique | ForEach-Object {
        $file = Get-Item -LiteralPath (Join-Path $Repo $_)
        @{ path = $_; length = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}
function Get-TestingHostIdentity {
    param([string]$AppDirectory, [string]$SourceCommit)
    $dll = Join-Path $AppDirectory 'Workbench.App.dll'
    $exe = Join-Path $AppDirectory 'Workbench.App.exe'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll)
    if ($version.ProductVersion -cne "0.4.0+$SourceCommit" -or $version.FileVersion -cne '0.4.0.0' -or [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString() -cne '1.0.0.0') { throw 'TestingHostSourceMismatch' }
    return @{ sourceCommit = $SourceCommit; productVersion = $version.ProductVersion; fileVersion = $version.FileVersion; assemblyVersion = '1.0.0.0'; executableSha256 = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant(); dllSha256 = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant() }
}
function Assert-TestingSourceIdentity {
    param([string]$RecordedCommit, [bool]$RecordedDirty, [string]$CurrentCommit, [bool]$CurrentDirty)
    if ($RecordedCommit -cne $CurrentCommit -or $RecordedDirty -or $CurrentDirty) { throw 'TestingCurrentSourceMismatch' }
}
function Assert-TestingSourceRecords {
    param([object[]]$Current, [object[]]$Recorded)
    if ($Current.Count -ne $Recorded.Count) { throw 'TestingSourceFileListInvalid' }
    foreach ($file in $Current) {
        $record = @($Recorded | Where-Object path -CEQ $file.path)
        if ($record.Count -ne 1 -or $record[0].length -ne $file.length -or $record[0].sha256 -cne $file.sha256) { throw 'TestingSourceHashMismatch' }
    }
}
function New-TestingPluginDelivery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$BuildDirectory, [Parameter(Mandatory)][string]$OutputDirectory, [switch]$ReferenceOnly)
    $repo = Split-Path $PSScriptRoot -Parent
    if ((Test-Path $OutputDirectory) -and @(Get-ChildItem $OutputDirectory -Force).Count -gt 0) { throw 'TestingDeliveryAlreadyExists' }
    New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
    $stage = Join-Path ([IO.Path]::GetTempPath()) ('testing-package-stage-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item (Join-Path $stage 'Fixtures') -ItemType Directory -Force | Out-Null
        foreach ($name in @('Workbench.Testing.dll', 'Workbench.Testing.Domain.dll', 'Workbench.Testing.deps.json')) { Copy-Item -LiteralPath (Join-Path $BuildDirectory $name) $stage }
        Copy-Item -LiteralPath (Join-Path $repo 'plugins/Workbench.Testing/plugin.json') $stage
        foreach ($name in @('requirements.synthetic.json', 'figma.synthetic.json')) { Copy-Item -LiteralPath (Join-Path $repo "plugins/Workbench.Testing.Domain/Fixtures/$name") (Join-Path $stage 'Fixtures') }
        $payload = @($script:TestingPayload | ForEach-Object {
            $file = Get-Item -LiteralPath (Join-Path $stage $_)
            @{ path = $_; length = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
        $zipName = 'workbench-testing-plugin-0.1.0.zip'
        $zip = Join-Path $OutputDirectory $zipName
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
        $sourceCommit = (& git -C $repo rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'TestingSourceIdentityUnavailable' }
        $dirty = @(& git -C $repo status --porcelain).Count -ne 0
        if (-not $ReferenceOnly -and $dirty) { throw 'TestingDeliveryRequiresCleanSource' }
        $hostIdentity = if ($ReferenceOnly) { $null } else { Get-TestingHostIdentity -AppDirectory (Join-Path $repo 'artifacts/native/app') -SourceCommit $sourceCommit }
        $metadata = @{ schemaVersion = 1; pluginId = 'com.projectworkbench.testing'; version = '0.1.0'; minimumHostVersion = '0.4.0'; sourceCommit = $sourceCommit; sourceTreeDirty = $dirty; referenceOnly = [bool]$ReferenceOnly; host = $hostIdentity; sourceFiles = @(Get-TestingSourceFiles $repo); payload = $payload; zip = @{ path = $zipName; length = (Get-Item $zip).Length; sha256 = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() }; createdUtc = [DateTimeOffset]::UtcNow.ToString('O') }
        $metadata | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $OutputDirectory 'provenance.json') -Encoding utf8
        @"
# Independent testing plugin 0.1.0

Install workbench-testing-plugin-0.1.0.zip using the native host's package installation action. Requires host 0.4.0 or later; use the host and SDK artifacts from this same source/CI run. Trust in-process code deliberately: ALC is not a sandbox.

Source commit: $sourceCommit. Reference-only build: $([bool]$ReferenceOnly). Dirty source: $dirty. A Linux reference build does not prove native Windows execution or PRI deployment. Consult the same-run testing-plugin-probes artifact for actual Server process/window/lease evidence; Windows 11 human pickers/IME/DPI/accessibility remain deferred.

The ZIP contains entry, private portable domain, entry deps, manifest and two explicitly synthetic JSON examples only. It has no SDK/Core/WinUI/.NET/WinRT copies, credentials or business plugin payloads. Importing the bundled examples opens these real packaged files with the same bounded read/parser used by the native picker. All frames and execution are SIMULATED, target virtual.task-form.v1. Manual steps are never automatic passes. CSV exports add one apostrophe to every text cell; original project records remain exact.

SHA256SUMS covers this README, provenance.json and the ZIP. Provenance binds every exact extracted payload and source file. See docs/testing-plugin.md in the source checkout for controls, lifecycle, limitations and verification commands.
"@ | Set-Content (Join-Path $OutputDirectory 'README.md') -Encoding utf8
        @($zipName, 'provenance.json', 'README.md' | ForEach-Object { (Get-FileHash (Join-Path $OutputDirectory $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_ }) | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS') -Encoding utf8
    }
    finally { if (Test-Path $stage) { Remove-Item $stage -Recurse -Force } }
    Test-TestingPluginDelivery -Directory $OutputDirectory -AllowReferenceOnly:$ReferenceOnly
}
function Test-TestingPluginDelivery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Directory, [switch]$AllowReferenceOnly, [switch]$RequireCurrentSource,
        [string]$AppDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/native/app'))
    $repo = Split-Path $PSScriptRoot -Parent
    $files = @('README.md', 'SHA256SUMS', 'provenance.json', 'workbench-testing-plugin-0.1.0.zip')
    for ($part = [IO.Path]::GetFullPath($Directory); -not [string]::IsNullOrEmpty($part); $part = Split-Path $part -Parent) {
        if (((Get-Item -LiteralPath $part).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'TestingDeliveryLinkRefused' }
    }
    if (@(Compare-Object $files @(Get-ChildItem -LiteralPath $Directory -Force | ForEach-Object Name) -CaseSensitive).Count -ne 0) { throw 'TestingDeliveryFileListInvalid' }
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Force) { if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'TestingDeliveryLinkRefused' } }
    $metadata = Get-Content -LiteralPath (Join-Path $Directory 'provenance.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($metadata.schemaVersion -ne 1 -or $metadata.pluginId -cne 'com.projectworkbench.testing' -or $metadata.version -cne '0.1.0' -or $metadata.minimumHostVersion -cne '0.4.0' -or $metadata.sourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'TestingDeliveryIdentityInvalid' }
    if ($metadata.referenceOnly -and -not $AllowReferenceOnly) { throw 'TestingDeliveryReferenceOnly' }
    if (-not $metadata.referenceOnly -and $metadata.sourceTreeDirty) { throw 'TestingDeliveryDirtySource' }
    if (-not $metadata.referenceOnly) {
        $actualHost = Get-TestingHostIdentity -AppDirectory $AppDirectory -SourceCommit $metadata.sourceCommit
        foreach ($key in $actualHost.Keys) { if ($metadata.host[$key] -cne $actualHost[$key]) { throw 'TestingHostPairingMismatch' } }
    }
    elseif ($null -ne $metadata.host) { throw 'TestingReferenceHostClaimInvalid' }
    $sums = @(Get-Content -LiteralPath (Join-Path $Directory 'SHA256SUMS'))
    if ($sums.Count -ne 3) { throw 'TestingDeliverySumsInvalid' }
    foreach ($file in @('README.md', 'provenance.json', 'workbench-testing-plugin-0.1.0.zip')) {
        $expected = (Get-FileHash -LiteralPath (Join-Path $Directory $file) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $file
        if (@($sums | Where-Object { $_ -ceq $expected }).Count -ne 1) { throw 'TestingDeliveryHashMismatch' }
    }
    $zipPath = Join-Path $Directory 'workbench-testing-plugin-0.1.0.zip'
    if ($metadata.zip.path -cne 'workbench-testing-plugin-0.1.0.zip' -or $metadata.zip.length -ne (Get-Item $zipPath).Length -or $metadata.zip.sha256 -cne (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'TestingDeliveryZipMismatch' }
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($zip.Entries)
        if (@(Compare-Object $script:TestingPayload @($entries | ForEach-Object FullName) -CaseSensitive).Count -ne 0 -or $entries.Count -ne $script:TestingPayload.Count -or @($metadata.payload).Count -ne $script:TestingPayload.Count) { throw 'TestingPayloadFileListInvalid' }
        foreach ($name in $script:TestingPayload) {
            $entry = @($entries | Where-Object FullName -CEQ $name)
            $record = @($metadata.payload | Where-Object path -CEQ $name)
            if ($entry.Count -ne 1 -or $record.Count -ne 1 -or $entry[0].Length -gt 16777216 -or $entry[0].IsEncrypted -or (($entry[0].ExternalAttributes -shr 16) -band 0xf000) -eq 0xa000) { throw 'TestingPayloadInvalid' }
            $stream = $entry[0].Open()
            try { $bytes = [IO.MemoryStream]::new(); $stream.CopyTo($bytes); $actual = $bytes.ToArray(); $bytes.Dispose() } finally { $stream.Dispose() }
            if ([WorkbenchTestingArchiveAudit]::Crc($actual) -ne $entry[0].Crc32) { throw 'TestingPayloadCrcMismatch' }
            if ($record[0].length -ne $actual.Length -or $record[0].sha256 -cne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($actual)).ToLowerInvariant()) { throw 'TestingPayloadHashMismatch' }
            if ($name -ceq 'plugin.json') {
                $manifest = [Text.Encoding]::UTF8.GetString($actual) | ConvertFrom-Json
                if ($manifest.id -cne 'com.projectworkbench.testing' -or $manifest.version -cne '0.1.0' -or $manifest.minimumHostVersion -cne '0.4.0' -or $manifest.apiVersion -ne 1 -or $manifest.entryAssembly -cne 'Workbench.Testing.dll' -or $manifest.entryType -cne 'Workbench.Testing.TestingPlugin') { throw 'TestingManifestInvalid' }
            }
        }
    }
    finally { $zip.Dispose() }
    if ($RequireCurrentSource) {
        Assert-TestingSourceIdentity -RecordedCommit $metadata.sourceCommit -RecordedDirty $metadata.sourceTreeDirty -CurrentCommit (& git -C $repo rev-parse HEAD).Trim() -CurrentDirty (@(& git -C $repo status --porcelain).Count -ne 0)
        Assert-TestingSourceRecords -Current @(Get-TestingSourceFiles $repo) -Recorded @($metadata.sourceFiles)
    }
    Write-Host 'Testing delivery verified: independent six-file ZIP, exact bytes and manifest.'
}
Export-ModuleMember -Function New-TestingPluginDelivery, Test-TestingPluginDelivery
