Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Repo = Split-Path $PSScriptRoot -Parent
# Independent, frozen production pins. Caller-supplied ZIP + provenance cannot redefine these.
$script:ProductionPins = @(
    @{ name = 'dingtalk'; pluginId = 'com.projectworkbench.dingtalk-insights'; version = '0.3.2'; sourceRepository = 'ThaoKeeton/dingtalk-insights';
        sourceCommit = 'c0c1d3303ae8b59c36dd7c4c1de85ddc6578abcf'; workflowRunId = 42295585483L; packageBytes = 179239L;
        sha256 = '38d7a8f30ce6d4d5c6f6e26c757cbb6b9fc9f812546bf1db2ecdbadb9b578200'; provenanceSha256 = '06799bf01ae3cf272a73ddc5a1a1b873bf5132d48bd4d2ce38e91bbf67f31d0c' },
    @{ name = 'upcam'; pluginId = 'com.upcam.task-digest'; version = '0.1.0'; sourceRepository = 'ThaoKeeton/upcam-task-digest';
        sourceCommit = '6370b9df46af564fde022924d1dbeace7d575c6a'; workflowRunId = 42268731102L; packageBytes = 11209273L;
        sha256 = '3e07b7db0e3f17871b88ce1e45ac09d3b8724e6628a35fd5e0cdc1eb9ad95dbe'; provenanceSha256 = '8c62c0eb6b26ef6e68efa3c7b09dd36070f7bb86a2e6ae01f17e4046f05511fa' }
)
$script:PayloadPaths = @('dingtalk/package.zip', 'dingtalk/provenance.json', 'upcam/package.zip', 'upcam/provenance.json', 'README.zh-CN.md')

function Get-DeliveryPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'delivery_path: A filesystem path is required.' }
    $provider = $null
    $drive = $null
    $filesystemPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path, [ref]$provider, [ref]$drive)
    if ($provider.Name -ne 'FileSystem') { throw 'delivery_path: Only filesystem paths are supported.' }
    return [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($filesystemPath))
}
function Assert-DeliveryNoLinks([string]$Path) {
    $current = Get-DeliveryPath $Path
    while ($current) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'delivery_path: Linked paths are forbidden.' }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}
function Test-DeliveryIntersection([string]$First, [string]$Second) {
    $separator = [IO.Path]::DirectorySeparatorChar
    return $First.Equals($Second, [StringComparison]::OrdinalIgnoreCase) -or
        $First.StartsWith($Second.TrimEnd($separator) + $separator, [StringComparison]::OrdinalIgnoreCase) -or
        $Second.StartsWith($First.TrimEnd($separator) + $separator, [StringComparison]::OrdinalIgnoreCase)
}
function Assert-DeliveryDestination([string]$Destination, [string]$InputPath, [string]$AppPath) {
    Assert-DeliveryNoLinks $Destination
    foreach ($protected in @($InputPath, $AppPath, (Get-DeliveryPath (Join-Path $script:Repo 'artifacts/native/app')))) {
        if (Test-DeliveryIntersection $Destination $protected) { throw 'delivery_path: Output must not intersect source inputs or the shipping app.' }
    }
    $item = Get-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
    if ($null -ne $item -and (-not $item.PSIsContainer -or @(Get-ChildItem -LiteralPath $Destination -Force).Count -ne 0)) {
        throw 'delivery_destination: Output must be absent or an empty ordinary directory; existing output is never cleared.'
    }
}
function Read-DeliveryJson([string]$Path) {
    Assert-DeliveryNoLinks $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -gt 2MB) { throw 'delivery_json: Missing or oversized JSON file.' }
    return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable)
}
function Read-DeliveryZipJson($Archive, [string]$Name) {
    $entries = @($Archive.Entries | Where-Object FullName -CEQ $Name)
    if ($entries.Count -ne 1 -or $entries[0].Length -le 0 -or $entries[0].Length -gt 2MB) { throw 'production_mode: A bounded root metadata file is required.' }
    $stream = $entries[0].Open()
    $buffer = [byte[]]::new(8192)
    $memory = [IO.MemoryStream]::new()
    try {
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            if ($memory.Length + $read -gt 2MB) { throw 'production_mode: Oversized root metadata file.' }
            $memory.Write($buffer, 0, $read)
        }
        return ([Text.Encoding]::UTF8.GetString($memory.ToArray()) | ConvertFrom-Json -AsHashtable)
    } finally { $stream.Dispose(); $memory.Dispose() }
}
function Assert-ProductionInputs([string]$InputPath) {
    Assert-DeliveryNoLinks $InputPath
    foreach ($pin in $script:ProductionPins) {
        $directory = Join-Path $InputPath $pin.name
        $package = Join-Path $directory 'package.zip'
        $provenancePath = Join-Path $directory 'provenance.json'
        Assert-DeliveryNoLinks $package
        Assert-DeliveryNoLinks $provenancePath
        if (-not (Test-Path -LiteralPath $package -PathType Leaf) -or (Get-Item -LiteralPath $package).Length -gt 100MB) { throw 'production_pin: Missing or oversized production ZIP.' }
        $archive = [IO.Compression.ZipFile]::OpenRead($package)
        try {
            $manifest = Read-DeliveryZipJson $archive 'plugin.json'
            if ($manifest.id -cne $pin.pluginId -or $manifest.version -cne $pin.version -or $manifest.apiVersion -ne 1 -or $manifest.minimumHostVersion -cne '0.2.0') {
                throw 'production_mode: Only the frozen production ID/version/API is allowed.'
            }
            if ($pin.name -ceq 'upcam') {
                $metadata = Read-DeliveryZipJson $archive 'plugin-package.json'
                if (-not $metadata.Contains('syntheticAcceptance') -or $metadata.syntheticAcceptance -isnot [bool] -or $metadata.syntheticAcceptance -ne $false -or
                    $metadata.pluginId -cne 'com.upcam.task-digest' -or $metadata.pluginVersion -cne '0.1.0' -or $metadata.runtimeServicesVersion -ne 1 -or
                    $metadata.sourceCommit -cne '1454dc3ff21de2152539d6f3462a3ddad13c4398') { throw 'production_mode: Explicit false syntheticAcceptance and production metadata are required.' }
            }
        } finally { $archive.Dispose() }
        # Check both the repository authority and selected input, including every provenance byte.
        foreach ($root in @((Join-Path $script:Repo 'tests/integration'), $InputPath)) {
            $zip = Join-Path $root "$($pin.name)/package.zip"
            $provenance = Join-Path $root "$($pin.name)/provenance.json"
            Assert-DeliveryNoLinks $zip
            Assert-DeliveryNoLinks $provenance
            if ((Get-Item -LiteralPath $zip).Length -ne $pin.packageBytes -or (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $pin.sha256 -or
                (Get-FileHash -LiteralPath $provenance -Algorithm SHA256).Hash -ne $pin.provenanceSha256) { throw 'production_pin: ZIP/provenance differs from frozen repository production pins.' }
        }
        $provenance = Read-DeliveryJson $provenancePath
        foreach ($key in @('pluginId', 'version', 'sourceRepository', 'sourceCommit', 'workflowRunId', 'sha256', 'packageBytes')) {
            if ($provenance[$key] -cne $pin[$key]) { throw 'production_pin: Production provenance identity mismatch.' }
        }
        if ($provenance.packageFile -cne 'package.zip' -or $provenance.schemaVersion -ne 1) { throw 'production_pin: Unsupported production provenance.' }
    }
}
function Get-DeliveryPackagingStamp {
    $head = (& git -C $script:Repo rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -cnotmatch '^[0-9a-f]{40}$') { throw 'packager_source: Cannot establish packaging source commit.' }
    # Include tracked modifications and relevant untracked additions. Normal ignored build/scratch output stays ignored.
    $status = @(& git -C $script:Repo status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'packager_source: Cannot establish packaging source status.' }
    return @{ packagingSourceCommit = $head; packagingSourcesDirty = $status.Count -ne 0 }
}
function Get-DeliveryHostStamp([string]$AppPath) {
    Assert-DeliveryNoLinks $AppPath
    foreach ($name in @('Workbench.App.exe', 'Workbench.App.dll')) {
        $path = Join-Path $AppPath $name
        Assert-DeliveryNoLinks $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'host_metadata: Published EXE and DLL are required.' }
        $stream = [IO.File]::OpenRead($path)
        $pe = $null
        try {
            $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
            if ($pe.PEHeaders.CoffHeader.Machine -ne [Reflection.PortableExecutable.Machine]::Amd64 -or
                $pe.PEHeaders.PEHeader.Magic -ne [Reflection.PortableExecutable.PEMagic]::PE32Plus -or
                ($name.EndsWith('.dll') -and -not $pe.HasMetadata)) { throw 'host_metadata: Expected actual win-x64 PE host components.' }
        } finally { if ($null -ne $pe) { $pe.Dispose() }; $stream.Dispose() }
    }
    $dll = Join-Path $AppPath 'Workbench.App.dll'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll)
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString()
    if ($version.ProductVersion -cnotmatch '^0[.]2[.]0[+]([0-9a-f]{40})$') { throw 'host_metadata: Actual DLL must declare 0.2.0 plus a full source commit.' }
    $sourceCommit = $Matches[1]
    if ($version.FileVersion -cne '0.2.0.0' -or $assemblyVersion -cne '1.0.0.0') { throw 'host_metadata: Unsupported host file/assembly version.' }
    return [ordered]@{ productVersion = $version.ProductVersion; fileVersion = $version.FileVersion; assemblyVersion = $assemblyVersion; sourceCommit = $sourceCommit;
        executableSha256 = (Get-FileHash -LiteralPath (Join-Path $AppPath 'Workbench.App.exe') -Algorithm SHA256).Hash.ToLowerInvariant();
        dllSha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant() }
}
function Assert-DeliveryCurrentSource($HostStamp, $PackagingStamp) {
    if ($HostStamp.sourceCommit -cne $PackagingStamp.packagingSourceCommit) { throw 'host_source: PE-declared host source must match the current packaging commit.' }
    if ($PackagingStamp.packagingSourcesDirty) { throw 'packager_dirty: Strict CI requires clean packaging sources, including untracked additions.' }
}
function Get-DeliveryPluginRecords {
    foreach ($pin in $script:ProductionPins) {
        $source = Read-DeliveryJson (Join-Path $script:Repo "tests/integration/$($pin.name)/provenance.json")
        $record = [ordered]@{ name = $pin.name; pluginId = $pin.pluginId; version = $pin.version; sourceRepository = $pin.sourceRepository; sourceCommit = $pin.sourceCommit;
            workflowRunId = $pin.workflowRunId; sha256 = $pin.sha256; packageBytes = $pin.packageBytes; transport = $source.transport;
            packageFile = "$($pin.name)/package.zip"; provenanceFile = "$($pin.name)/provenance.json" }
        if ($pin.name -ceq 'upcam') { $record['actualCheckoutCommit'] = $source.actualCheckoutCommit }
        $record
    }
}
function Assert-DeliveryShape($Value, [string[]]$Keys) {
    if ($Value -isnot [Collections.IDictionary] -or $Value.Count -ne $Keys.Count) { throw 'delivery_schema: Unsupported object shape.' }
    foreach ($key in $Keys) { if (@($Value.Keys) -cnotcontains $key) { throw 'delivery_schema: Missing or unknown property.' } }
}
function Test-DeliveryInteger($Value) { return $Value -is [int] -or $Value -is [long] }
function Export-BusinessPlugins {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$AppDirectory, [Parameter(Mandatory)][string]$InputRoot,
        [Parameter(Mandatory)][string]$DestinationRoot, [switch]$RequireCurrentSource)
    $appPath = Get-DeliveryPath $AppDirectory
    $inputPath = Get-DeliveryPath $InputRoot
    $destination = Get-DeliveryPath $DestinationRoot
    Assert-DeliveryDestination $destination $inputPath $appPath
    $hostStamp = Get-DeliveryHostStamp $appPath
    $packaging = Get-DeliveryPackagingStamp
    if ($RequireCurrentSource) { Assert-DeliveryCurrentSource $hostStamp $packaging }
    Assert-ProductionInputs $inputPath
    $parent = Split-Path $destination -Parent
    Assert-DeliveryNoLinks $parent
    New-Item -Path $parent -ItemType Directory -Force | Out-Null
    $stage = Join-Path $parent ".business-plugins-stage-$([Guid]::NewGuid().ToString('N'))"
    $ownedStage = $false
    try {
        New-Item -Path $stage -ItemType Directory | Out-Null
        $ownedStage = $true
        # Keep the full strict importer unchanged. It audits all 722 entries before copying.
        & (Join-Path $PSScriptRoot 'import-business-plugin.ps1') -InputRoot $inputPath -DestinationRoot $stage -Mode Production
        Remove-Item -LiteralPath (Join-Path $stage 'inputs.json')
        Copy-Item -LiteralPath (Join-Path $script:Repo 'docs/install-business-plugins.md') -Destination (Join-Path $stage 'README.zh-CN.md')
        Assert-ProductionInputs $stage
        $inventory = @(foreach ($relative in $script:PayloadPaths) {
            $path = Join-Path $stage $relative
            [ordered]@{ path = $relative; bytes = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
        [ordered]@{ schemaVersion = 1; mode = 'Production'; plugins = @(Get-DeliveryPluginRecords); host = $hostStamp;
            packagingSourceCommit = $packaging.packagingSourceCommit; packagingSourcesDirty = $packaging.packagingSourcesDirty; files = $inventory } |
            ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding utf8
        Test-BusinessPluginDelivery -Directory $stage -AppDirectory $appPath -RequireCurrentSource:$RequireCurrentSource | Out-Null
        # Recheck before publishing. Never recursively delete a caller-selected output directory.
        Assert-DeliveryDestination $destination $inputPath $appPath
        if (Test-Path -LiteralPath $destination) { [IO.Directory]::Delete($destination, $false) }
        [IO.Directory]::Move($stage, $destination)
        $ownedStage = $false
        Write-Host "Verified production business plugin delivery: $destination (six files; PE metadata/hash pairing, not publisher authentication)."
    }
    finally {
        if ($ownedStage -and (Test-Path -LiteralPath $stage)) {
            Assert-DeliveryNoLinks $stage
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }
}
function Test-BusinessPluginDelivery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][string]$AppDirectory, [switch]$RequireCurrentSource)
    $root = Get-DeliveryPath $Directory
    Assert-DeliveryNoLinks $root
    $expectedFiles = @($script:PayloadPaths) + @('manifest.json')
    $actualFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -Force) {
        Assert-DeliveryNoLinks $item.FullName
        $relative = [IO.Path]::GetRelativePath($root, $item.FullName).Replace('\', '/')
        if ($item.PSIsContainer) {
            if (@('dingtalk', 'upcam') -cnotcontains $relative) { throw 'delivery_inventory: Unknown output directory.' }
        } elseif ($expectedFiles -cnotcontains $relative -or -not $actualFiles.Add($relative)) { throw 'delivery_inventory: Unknown or case-colliding output file.' }
    }
    if ($actualFiles.Count -ne 6) { throw 'delivery_inventory: Exactly six output files are required.' }
    Assert-ProductionInputs $root
    $manifest = Read-DeliveryJson (Join-Path $root 'manifest.json')
    Assert-DeliveryShape $manifest @('schemaVersion', 'mode', 'plugins', 'host', 'packagingSourceCommit', 'packagingSourcesDirty', 'files')
    if (-not (Test-DeliveryInteger $manifest.schemaVersion) -or $manifest.schemaVersion -ne 1 -or $manifest.mode -isnot [string] -or $manifest.mode -cne 'Production' -or
        $manifest.packagingSourceCommit -isnot [string] -or $manifest.packagingSourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
        $manifest.packagingSourcesDirty -isnot [bool]) { throw 'delivery_schema: Unsupported schema, production mode or packaging identity.' }
    $hostStamp = Get-DeliveryHostStamp (Get-DeliveryPath $AppDirectory)
    Assert-DeliveryShape $manifest.host @($hostStamp.Keys)
    foreach ($key in $hostStamp.Keys) { if ($manifest.host[$key] -isnot [string] -or $manifest.host[$key] -cne $hostStamp[$key]) { throw 'host_metadata: Delivery stamp does not match actual PE components.' } }
    $plugins = @(Get-DeliveryPluginRecords)
    if (@($manifest.plugins).Count -ne 2) { throw 'delivery_schema: Exactly two production plugin records are required.' }
    for ($i = 0; $i -lt 2; $i++) {
        Assert-DeliveryShape $manifest.plugins[$i] @($plugins[$i].Keys)
        foreach ($key in $plugins[$i].Keys) {
            $value = $manifest.plugins[$i][$key]
            $expected = $plugins[$i][$key]
            $validType = if ($expected -is [string]) { $value -is [string] } else { Test-DeliveryInteger $value }
            if (-not $validType -or $value -cne $expected) { throw 'production_pin: Delivery plugin identity differs from frozen pins.' }
        }
    }
    if (@($manifest.files).Count -ne 5) { throw 'delivery_inventory: All five payloads except the manifest must be inventoried.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $manifest.files) {
        Assert-DeliveryShape $file @('path', 'bytes', 'sha256')
        if ($file.path -isnot [string] -or $script:PayloadPaths -cnotcontains $file.path -or -not $seen.Add($file.path) -or -not (Test-DeliveryInteger $file.bytes) -or
            $file.sha256 -isnot [string] -or $file.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'delivery_inventory: Invalid, duplicate or non-relative payload path/hash.' }
        $path = Join-Path $root $file.path
        if ($file.bytes -ne (Get-Item -LiteralPath $path).Length -or $file.sha256 -ne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) { throw 'delivery_inventory: Actual payload hash/length mismatch.' }
    }
    if ($RequireCurrentSource) {
        $packaging = Get-DeliveryPackagingStamp
        Assert-DeliveryCurrentSource $hostStamp $packaging
        if ($manifest.packagingSourceCommit -cne $packaging.packagingSourceCommit -or $manifest.packagingSourcesDirty) { throw 'packager_source: Delivery was not exported from clean current sources.' }
    }
    return $true
}
Export-ModuleMember -Function Export-BusinessPlugins, Test-BusinessPluginDelivery
