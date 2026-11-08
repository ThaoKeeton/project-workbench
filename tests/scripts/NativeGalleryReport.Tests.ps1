[CmdletBinding()]
param([switch]$SeedIdentityOnly, [string]$SeedIdentityRoot = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '../../scripts/NativeGalleryReport.psm1') -Force
function Assert-GalleryFixtureVersionResource([string]$Path, [string]$ExpectedVersion) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $resource = $reader.PEHeaders.PEHeader.ResourceTableDirectory
            if ($resource.RelativeVirtualAddress -eq 0 -or $resource.Size -lt 16) {
                throw 'Test-only PE lacks a Win32 version resource directory.'
            }
            [byte[]]$bytes = $reader.GetSectionData($resource.RelativeVirtualAddress).GetContent(0, $resource.Size)
            $count = [BitConverter]::ToUInt16($bytes, 12) + [BitConverter]::ToUInt16($bytes, 14)
            $hasVersionType = $false
            for ($i = 0; $i -lt $count; $i++) {
                if ([BitConverter]::ToUInt32($bytes, 16 + 8 * $i) -eq 16) { $hasVersionType = $true; break }
            }
            if (-not $hasVersionType) { throw 'Test-only PE lacks Win32 RT_VERSION.' }
            $needle = [Text.Encoding]::Unicode.GetBytes($ExpectedVersion)
            $hasExpectedVersion = $false
            for ($i = 0; $i -le $bytes.Length - $needle.Length; $i++) {
                $match = $true
                for ($j = 0; $j -lt $needle.Length; $j++) {
                    if ($bytes[$i + $j] -ne $needle[$j]) { $match = $false; break }
                }
                if ($match) { $hasExpectedVersion = $true; break }
            }
            if (-not $hasExpectedVersion) { throw 'Test-only Win32 RT_VERSION lacks the expected source version.' }
            # The tiny fixture source has no version literal; this UTF-8 payload must
            # come from SDK-generated managed assembly metadata, without loading net10
            # code into the possibly older PowerShell runtime on Windows.
            [byte[]]$managedBytes = $reader.GetMetadata().GetContent()
            $managedNeedle = [Text.Encoding]::UTF8.GetBytes($ExpectedVersion)
            $hasManagedVersion = $false
            for ($i = 0; $i -le $managedBytes.Length - $managedNeedle.Length; $i++) {
                $match = $true
                for ($j = 0; $j -lt $managedNeedle.Length; $j++) {
                    if ($managedBytes[$i + $j] -ne $managedNeedle[$j]) { $match = $false; break }
                }
                if ($match) { $hasManagedVersion = $true; break }
            }
            if (-not $hasManagedVersion) { throw 'Test-only PE lacks the expected managed version payload.' }
        } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}
function New-GalleryVersionPe([string]$Path, [string]$Commit, [string]$Case, [string]$WorkRoot) {
    if ($Commit -cnotmatch '^[0-9a-f]{40}$' -or $Case -cnotmatch '^[a-z-]+$') {
        throw 'Test-only PE version arguments are invalid.'
    }
    $project = Join-Path $PSScriptRoot 'fixtures/NativeGalleryVersionPe/NativeGalleryVersionPe.csproj'
    $work = Join-Path $WorkRoot "version-pe-$Case"
    $obj = [IO.Path]::GetFullPath((Join-Path $work 'obj')).Replace('\', '/') + '/'
    $out = [IO.Path]::GetFullPath((Join-Path $work 'out')).Replace('\', '/') + '/'
    $dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
    if ($dotnet -isnot [string] -or [string]::IsNullOrWhiteSpace($dotnet)) {
        throw 'Test-only PE SDK command must have a nonempty scalar source path.'
    }
    $buildArguments = @('build', $project, '-c', 'Release', '--nologo',
        "-p:GalleryFixtureCommit=$Commit", "-p:BaseIntermediateOutputPath=$obj",
        "-p:OutputPath=$out", '-p:AppendTargetFrameworkToOutputPath=false')
    Write-Host ('Test-only PE argv JSON: ' + (@($dotnet) + $buildArguments | ConvertTo-Json -Compress -Depth 3))
    & $dotnet @buildArguments
    $buildExit = $LASTEXITCODE
    Write-Host "Test-only PE build observed exit: $buildExit"
    if ($buildExit -ne 0) { throw "Test-only PE build failed: $Case" }
    [IO.File]::Copy((Join-Path $out 'NativeGalleryVersionPe.dll'), $Path, $false)
    Assert-GalleryFixtureVersionResource $Path "0.4.0+$Commit"
}
function Test-GalleryDurableSeedIdentity([string]$FixtureRoot) {
    # Break caught: rereading mutable installed code after later business removal,
    # or trusting a sidecar without the exact independently frozen seed bytes.
    New-Item -Path $FixtureRoot -ItemType Directory | Out-Null
    $repo = Join-Path $FixtureRoot 'repo'
    New-Item -Path $repo -ItemType Directory | Out-Null
    & git -C $repo init -q
    [IO.File]::WriteAllText((Join-Path $repo 'marker'), 'synthetic durable seed identity filesystem regression')
    & git -C $repo add marker
    & git -C $repo -c user.name=GalleryFixture -c user.email=gallery@example.invalid commit -qm fixture
    if ($LASTEXITCODE -ne 0) { throw 'Seed identity git fixture failed.' }
    $source = (& git -C $repo rev-parse HEAD).Trim()
    $native = Join-Path $repo 'artifacts/native'
    $appFolder = Join-Path $native 'app'
    New-Item -Path $appFolder -ItemType Directory -Force | Out-Null
    $app = Join-Path $appFolder 'Workbench.App.exe'
    [IO.File]::WriteAllText($app, 'test-only executable identity; no process or UI is simulated')
    $dll = Join-Path $appFolder 'Workbench.App.dll'
    New-GalleryVersionPe $dll $source 'durable-seed' $FixtureRoot
    $exeHash = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant()
    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8AAQv8BD/kD/YURmXYAAAAASUVORK5CYII=')
    $pngHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($png)).ToLowerInvariant()
    $birth = [DateTime]::UtcNow.AddMinutes(-5).ToFileTimeUtc()
    $exit = [DateTime]::UtcNow.AddMinutes(5).ToFileTimeUtc()
    $business = Join-Path $native 'business-probes'
    New-Item -Path $business -ItemType Directory -Force | Out-Null
    $cases = @()
    foreach ($acceptance in @($false, $true)) {
        $phase = if ($acceptance) { 'acceptance-seed' } else { 'seed' }
        $userRoot = Join-Path (Join-Path ([IO.Path]::GetTempPath()) 'ProjectWorkbench-business-probe') ([Guid]::NewGuid().ToString('N'))
        $installed = Join-Path $userRoot 'Plugins'
        $zipRoot = Join-Path $native ('fixtures/' + $(if ($acceptance) { 'business-acceptance' } else { 'business' }))
        New-Item -Path $zipRoot -ItemType Directory -Force | Out-Null
        $ids = @('com.projectworkbench.dingtalk-insights', $(if ($acceptance) { 'com.upcam.task-digest.acceptance' } else { 'com.upcam.task-digest' }))
        $zips = @{}; $packages = @{}; $provenance = @()
        foreach ($id in $ids) {
            $folder = Join-Path $installed $id
            New-Item -Path $folder -ItemType Directory -Force | Out-Null
            $manifest = Join-Path $folder 'plugin.json'; $entry = Join-Path $folder 'Seed.dll'
            [IO.File]::WriteAllText($manifest, ('{"id":"' + $id + '","entryAssembly":"Seed.dll"}'))
            [IO.File]::Copy($dll, $entry)
            $zip = Join-Path $zipRoot ($id + '.zip')
            [IO.Compression.ZipFile]::CreateFromDirectory($folder, $zip)
            $zips[$id] = $zip
            $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
            $packages[$id] = @{ manifest = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant();
                entry = (Get-FileHash -LiteralPath $entry -Algorithm SHA256).Hash.ToLowerInvariant() }
            $provenance += @{ pluginId = $id; sha256 = $zipHash }
        }
        $report = Join-Path $business ($phase + '.json')
        # A validated earlier image must retain the independent original failure.
        $code = if ($acceptance) { 0 } else { -9 }
        @{ succeeded = $acceptance; processId = 77; processCreationFileTimeUtc = $birth;
            processExitFileTimeUtc = $exit; hasExited = $true; exitCode = $code;
            userRoot = $userRoot; provenance = $provenance } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report
        $gallery = Join-Path $business ('gallery/business-' + $phase)
        New-Item -Path $gallery -ItemType Directory -Force | Out-Null
        $shots = @(@('dingtalk-workspace', $ids[0], 'workspace'), @('upcam-preview', $ids[1], 'digest'))
        if ($acceptance) { $shots = @(,@('upcam-acceptance-history', $ids[1], 'digest')) }
        foreach ($shot in $shots) {
            $id = $shot[1]
            @{ schemaVersion = 1; captureKind = 'WinUiContentRender'; scope = 'actual-window-content-only';
                titleBarIncluded = $false; popupsIncluded = $false; status = 'captured'; reasonCode = 'none';
                shotId = $shot[0]; pageKey = 'view'; pluginId = $id; viewId = $shot[2]; probeKind = 'business';
                phase = 'business-' + $phase; projectId = $null; selectedPageKey = 'view'; selectedPluginId = $id; selectedViewId = $shot[2];
                processId = 77; processCreationFileTimeUtc = $birth; hwnd = 999L; captureUtc = [DateTimeOffset]::UtcNow;
                executableSha256 = $exeHash; hostInformationalVersion = "0.4.0+$source"; sourceCommit = $source;
                packageManifestSha256 = $packages[$id].manifest; packageEntrySha256 = $packages[$id].entry;
                osDescription = 'Windows (synthetic receipt fixture; no runtime claim)'; architecture = 'X64'; sessionId = 1;
                desktopVisible = $true; windowVisible = $true; width = 2; height = 1; rasterizationScale = 1.0; theme = 'Light';
                viewportWidth = 2.0; viewportHeight = 1.0; scrollOffset = 0.0; witnessCode = 'asserted'; witnessCount = 1;
                pngPath = $shot[0] + '.png'; pngBytes = $png.Length; pngSha256 = $pngHash } |
                ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $gallery ($shot[0] + '.json'))
            [IO.File]::WriteAllBytes((Join-Path $gallery ($shot[0] + '.png')), $png)
        }
        $args = @{ OriginalReportPath = $report; Phase = 'business-' + $phase; ExecutablePath = $app; SourceCommit = $source;
            ProcessId = 77; ProcessCreationFileTimeUtc = $birth; ProcessExitFileTimeUtc = $exit; ExitCode = $code; HasExited = $true;
            InstalledRoot = $installed; PackageIds = $ids; SourceZipRoot = $zipRoot; SourceZipPaths = $zips }
        $binding = Write-NativeGalleryBinding @args
        $cases += @{ report = $report; binding = $binding; args = $args; installed = $installed; ids = $ids; zips = $zips;
            snapshot = [IO.Path]::ChangeExtension($report, 'gallery-packages'); gallery = $gallery }
        Write-Host "SEED_BOUND phase=business-$phase installed=$installed binding=$binding hostDllSha256=$((Get-FileHash -LiteralPath $dll).Hash)"
    }
    $output = Join-Path $native 'genuine-gallery'
    $writeCase = $cases[0]
    function Refuse-SeedWriter([string]$Name, [string]$Reason, [hashtable]$Arguments, [switch]$SnapshotCollision) {
        $folder = Join-Path $FixtureRoot ('writer-refusal-' + [Guid]::NewGuid().ToString('N'))
        New-Item -Path $folder -ItemType Directory | Out-Null
        $copy = Join-Path $folder 'seed.json'
        [IO.File]::Copy($writeCase.report, $copy)
        $hash = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash
        $Arguments = $Arguments.Clone(); $Arguments.OriginalReportPath = $copy
        $sentinel = $null
        if ($SnapshotCollision) {
            $collisionRoot = [IO.Path]::ChangeExtension($copy, 'gallery-packages')
            New-Item -Path $collisionRoot -ItemType Directory | Out-Null
            $sentinel = Join-Path $collisionRoot 'preserve.txt'
            [IO.File]::WriteAllText($sentinel, 'preserve preexisting destination')
        }
        $caught = $null
        try { Write-NativeGalleryBinding @Arguments | Out-Null } catch { $caught = $_.Exception.Message }
        if ($caught -cne $Reason -or (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($copy, 'gallery-binding.json'))) -or
            (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $hash -or
            ($null -ne $sentinel -and [IO.File]::ReadAllText($sentinel) -cne 'preserve preexisting destination')) {
            throw "Seed writer '$Name' expected $Reason with original files retained, observed $caught"
        }
        Write-Host "SEED_WRITER_REFUSED $Name reason=$caught originalReportPreserved=true"
    }
    Refuse-SeedWriter 'snapshot publication collision' 'GalleryPackageSnapshotAlreadyExists' $writeCase.args -SnapshotCollision
    $badArgs = $writeCase.args.Clone(); $badArgs.HasExited = $false
    Refuse-SeedWriter 'unconfirmed process exit' 'GalleryPackageSnapshotIdentityMismatch' $badArgs
    $badArgs = $writeCase.args.Clone(); $badArgs.PackageIds = @($writeCase.ids[0], $writeCase.ids[0])
    Refuse-SeedWriter 'duplicate package request' 'GalleryPackageIdInvalid' $badArgs
    $badArgs = $writeCase.args.Clone(); $badArgs.PackageIds = @($writeCase.ids) + @('com.extra.plugin')
    Refuse-SeedWriter 'too many package requests' 'GalleryPackageIdInvalid' $badArgs
    $sourceManifest = Join-Path $writeCase.installed ($writeCase.ids[0] + '/plugin.json')
    $sourceEntry = Join-Path $writeCase.installed ($writeCase.ids[0] + '/Seed.dll')
    $manifestBytes = [IO.File]::ReadAllBytes($sourceManifest)
    foreach ($badName in @('../Seed.dll', '..\Seed.dll', 'Seed.dll:ads')) {
        try {
            [IO.File]::WriteAllText($sourceManifest, ('{"id":"' + $writeCase.ids[0] + '","entryAssembly":"' + $badName.Replace('\','\\') + '"}'))
            Refuse-SeedWriter "unsafe entry $badName" 'GalleryPackageManifestInvalid' $writeCase.args
        } finally { [IO.File]::WriteAllBytes($sourceManifest, $manifestBytes) }
    }
    try {
        [IO.File]::WriteAllBytes($sourceManifest, [byte[]]::new(65537))
        Refuse-SeedWriter 'oversized manifest' 'GalleryJsonOversized' $writeCase.args
    } finally { [IO.File]::WriteAllBytes($sourceManifest, $manifestBytes) }
    $entryBytes = [IO.File]::ReadAllBytes($sourceEntry)
    try {
        $oversized = [IO.File]::OpenWrite($sourceEntry); $oversized.SetLength(16777217); $oversized.Dispose()
        Refuse-SeedWriter 'oversized entry' 'GalleryPackageSnapshotSizeInvalid' $writeCase.args
    } finally { [IO.File]::WriteAllBytes($sourceEntry, $entryBytes) }
    $heldSource = Join-Path $FixtureRoot 'held-source-manifest.json'; [IO.File]::Move($sourceManifest, $heldSource)
    try {
        New-Item -Path $sourceManifest -ItemType SymbolicLink -Target $heldSource | Out-Null
        Refuse-SeedWriter 'reparse installed identity' 'GalleryReparsePath' $writeCase.args
    } finally { Remove-Item -LiteralPath $sourceManifest; [IO.File]::Move($heldSource, $sourceManifest) }
    $outsideZip = Join-Path $FixtureRoot 'outside.zip'; [IO.File]::WriteAllText($outsideZip, 'test-only outside ZIP path')
    $badArgs = $writeCase.args.Clone(); $badArgs.SourceZipPaths = $writeCase.zips.Clone(); $badArgs.SourceZipPaths[$writeCase.ids[0]] = $outsideZip
    Refuse-SeedWriter 'source ZIP traversal' 'GallerySourceZipPathInvalid' $badArgs
    $before = Export-NativeGallery -RepositoryRoot $repo
    if ($before.acceptedImages -ne 3) { throw 'Seed collector did not accept all three package-bound synthetic receipts.' }
    Remove-Item -LiteralPath $output -Recurse -Force
    foreach ($case in $cases) {
        foreach ($id in $case.ids) { Remove-Item -LiteralPath (Join-Path $case.installed $id) -Recurse -Force }
        Write-Host "LATER_REMOVAL installed=$($case.installed) remainingPackageDirectories=$(@(Get-ChildItem -LiteralPath $case.installed -Directory).Count)"
    }
    try { $after = Export-NativeGallery -RepositoryRoot $repo }
    catch {
        Write-Host ('CAUSAL_FAILURE message=' + $_.Exception.Message)
        Write-Host ('CAUSAL_STACK ' + $_.ScriptStackTrace)
        Write-Host ('MUTABLE_READ_SCOPE ' + ($cases.installed -join ';'))
        throw
    }
    if ($after.acceptedImages -ne 3 -or
        @($after.rows | Where-Object { $_.shotId -in @('dingtalk-workspace','upcam-preview') -and
            $_.status -eq 'failed' -and $_.captureStatus -eq 'captured' -and $_.originalProbeSucceeded -eq $false -and $_.processExitCode -eq -9 }).Count -ne 2 -or
        @($after.rows | Where-Object { $_.shotId -eq 'upcam-acceptance-history' -and $_.status -eq 'captured' }).Count -ne 1) {
        throw 'Immutable seed collector lost exact images or changed the original probe verdict.'
    }
    Remove-Item -LiteralPath $output -Recurse -Force
    $case = $cases[0]
    $identity = Join-Path $case.snapshot 'identity.json'
    $entry = Join-Path $case.snapshot ($case.ids[0] + '/Seed.dll')
    $manifest = Join-Path $case.snapshot ($case.ids[0] + '/plugin.json')
    function Refuse-SeedCollector([string]$Name, [string]$Reason) {
        $caught = $null
        try { Export-NativeGallery -RepositoryRoot $repo | Out-Null } catch { $caught = $_.Exception.Message }
        if ($null -eq $caught -or $caught -cne $Reason) { throw "Seed guard '$Name' expected $Reason, observed $caught" }
        if (Test-Path -LiteralPath (Join-Path $output 'index.json')) { throw "Seed refusal '$Name' published an index." }
        Write-Host "SEED_REFUSED $Name reason=$caught"
    }
    foreach ($file in @($entry, $manifest, $identity)) {
        $bytes = [IO.File]::ReadAllBytes($file)
        try { [IO.File]::WriteAllBytes($file, [byte[]]@(1, 2, 3)); Refuse-SeedCollector "altered $([IO.Path]::GetFileName($file))" 'GalleryPackageSnapshotHashMismatch' }
        finally { [IO.File]::WriteAllBytes($file, $bytes) }
        $held = $file + '.held'; [IO.File]::Move($file, $held)
        try { Refuse-SeedCollector "missing $([IO.Path]::GetFileName($file))" 'GalleryPackageSnapshotFilesInvalid' }
        finally { [IO.File]::Move($held, $file) }
    }
    $identityBytes = [IO.File]::ReadAllBytes($identity); $bindingBytes = [IO.File]::ReadAllBytes($case.binding)
    foreach ($mutation in @('phase','processId','processCreationFileTimeUtc','sourceCommit','originalReportSha256','installedRoot','entryAssembly','duplicate-package','sourceZipSha256')) {
        $data = Get-Content -LiteralPath $identity -Raw | ConvertFrom-Json -AsHashtable
        switch ($mutation) {
            'phase' { $data.phase = 'business-acceptance-seed' }
            'processId' { $data.processId = @(77) }
            'processCreationFileTimeUtc' { $data.processCreationFileTimeUtc++ }
            'sourceCommit' { $data.sourceCommit = 'b' * 40 }
            'originalReportSha256' { $data.originalReportSha256 = '0' * 64 }
            'installedRoot' { $data.installedRoot = Join-Path $FixtureRoot 'other/Plugins' }
            'entryAssembly' { $data.packages[$case.ids[0]].entryAssembly = '../Seed.dll' }
            'duplicate-package' { $data.packages['COM.PROJECTWORKBENCH.DINGTALK-INSIGHTS'] = $data.packages[$case.ids[0]] }
            'sourceZipSha256' { $data.packages[$case.ids[0]].sourceZipSha256 = '0' * 64 }
        }
        try {
            $data | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $identity
            $b = Get-Content -LiteralPath $case.binding -Raw | ConvertFrom-Json -AsHashtable
            $b.packageSnapshotSha256 = (Get-FileHash -LiteralPath $identity -Algorithm SHA256).Hash.ToLowerInvariant()
            $b | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $case.binding
            Refuse-SeedCollector "self-consistent sidecar $mutation" 'GalleryPackageSnapshotIdentityMismatch'
        } finally { [IO.File]::WriteAllBytes($identity, $identityBytes); [IO.File]::WriteAllBytes($case.binding, $bindingBytes) }
    }
    $zip = $case.zips[$case.ids[0]]; $bytes = [IO.File]::ReadAllBytes($zip)
    try { [IO.File]::WriteAllText($zip, 'changed publisher ZIP'); Refuse-SeedCollector 'changed publisher ZIP' 'GallerySourceZipMismatch' }
    finally { [IO.File]::WriteAllBytes($zip, $bytes) }
    $orphan = Join-Path $case.snapshot 'orphan.bin'
    try { [IO.File]::WriteAllText($orphan, 'orphan'); Refuse-SeedCollector 'orphan snapshot file' 'GalleryPackageSnapshotFilesInvalid' }
    finally { Remove-Item -LiteralPath $orphan }
    $linked = Join-Path $FixtureRoot 'held-snapshot-entry.dll'; [IO.File]::Move($entry, $linked)
    try {
        New-Item -Path $entry -ItemType SymbolicLink -Target $linked | Out-Null
        Refuse-SeedCollector 'reparse snapshot entry' 'GalleryReparsePath'
    } finally { Remove-Item -LiteralPath $entry; [IO.File]::Move($linked, $entry) }
    $b = Get-Content -LiteralPath $case.binding -Raw | ConvertFrom-Json -AsHashtable
    $b.Remove('packageSnapshotSha256') | Out-Null
    try { $b | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $case.binding; Refuse-SeedCollector 'missing immutable binding' 'GalleryPackageSnapshotIdentityMismatch' }
    finally { [IO.File]::WriteAllBytes($case.binding, $bindingBytes) }
    # Re-creation must refuse before reading the now absent current packages.
    $caught = $null
    $repeatArgs = $case.args
    try { Write-NativeGalleryBinding @repeatArgs | Out-Null } catch { $caught = $_.Exception.Message }
    if ($null -eq $caught) { throw 'Seed binding overwrite was allowed.' }
    if ((Get-FileHash -LiteralPath $case.binding -Algorithm SHA256).Hash.ToLowerInvariant() -cne
        [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bindingBytes)).ToLowerInvariant()) { throw 'Binding overwrite attempt changed original bytes.' }
    $final = Export-NativeGallery -RepositoryRoot $repo
    if ($final.acceptedImages -ne 3) { throw 'Seed collector did not recover after all refusal fixtures.' }
    foreach ($c in $cases) {
        foreach ($raw in Get-ChildItem -LiteralPath $c.gallery -Filter '*.png') {
            if ((Get-FileHash -LiteralPath $raw.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pngHash) { throw 'Original seed PNG changed.' }
        }
        Remove-Item -LiteralPath (Split-Path $c.installed -Parent) -Recurse -Force
    }
    Write-Host 'Durable seed identity collector fixtures passed: immutable bytes survive business/acceptance removal; strict refusals and original failure retained.'
}
if ($SeedIdentityOnly) {
    if ([string]::IsNullOrWhiteSpace($SeedIdentityRoot)) { throw 'SeedIdentityOnly requires a fresh explicit fixture root.' }
    Test-GalleryDurableSeedIdentity $SeedIdentityRoot
    return
}
$root = Join-Path ([IO.Path]::GetTempPath()) ('NativeGalleryReceipt-' + [Guid]::NewGuid().ToString('N'))
New-Item -Path $root -ItemType Directory | Out-Null
try {
    $source = 'a' * 40
    $birth = [DateTime]::UtcNow.AddMinutes(-5).ToFileTimeUtc()
    $exit = [DateTime]::UtcNow.AddMinutes(20).ToFileTimeUtc()
    $app = Join-Path $root 'Workbench.App.exe'
    [IO.File]::WriteAllBytes($app, [Text.Encoding]::ASCII.GetBytes('synthetic-executable-for-receipt-policy'))
    $appHash = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant()
    # The SDK emits managed metadata and RT_VERSION; this remains a test-only PE.
    $hostDll = Join-Path $root 'Workbench.App.dll'
    New-GalleryVersionPe $hostDll $source 'positive' $root
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($hostDll).ProductVersion -cne "0.4.0+$source") {
        throw 'Test-only host PE does not carry the requested informational version.'
    }
    $hostDllHash = (Get-FileHash -LiteralPath $hostDll -Algorithm SHA256).Hash.ToLowerInvariant()
    $report = Join-Path $root 'seed.json'
    @{ schemaVersion = 1; succeeded = $true; processId = 77; probe = 'seed' } | ConvertTo-Json | Set-Content -LiteralPath $report
    $reportHash = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash.ToLowerInvariant()
    $bindingPath = Join-Path $root 'seed.gallery-binding.json'
    $gallery = Join-Path $root 'gallery'
    New-Item -Path $gallery -ItemType Directory | Out-Null
    $receiptPath = Join-Path $gallery 'host-plugins.json'
    $pngPath = Join-Path $gallery 'host-plugins.png'
    [IO.File]::WriteAllBytes($pngPath, [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8AAQv8BD/kD/YURmXYAAAAASUVORK5CYII='))
    $pngHash = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $binding = @{ schemaVersion = 1; phase = 'business-seed'; processId = 77; processCreationFileTimeUtc = $birth;
        processExitFileTimeUtc = $exit; hasExited = $true; exitCode = 0; executableSha256 = $appHash; hostDllSha256 = $hostDllHash;
        sourceCommit = $source; hostInformationalVersion = "0.4.0+$source"; originalReportFile = 'seed.json'; originalReportSha256 = $reportHash; originalProbeSucceeded = $true }
    $receipt = @{ schemaVersion = 1; captureKind = 'WinUiContentRender'; scope = 'actual-window-content-only';
        titleBarIncluded = $false; popupsIncluded = $false; status = 'captured'; reasonCode = 'none';
        shotId = 'host-plugins'; pageKey = 'plugins'; pluginId = $null; viewId = $null;
        probeKind = 'business'; phase = 'business-seed'; projectId = $null;
        selectedPageKey = 'plugins'; selectedPluginId = $null; selectedViewId = $null;
        processId = 77; processCreationFileTimeUtc = $birth; hwnd = 999L; captureUtc = [DateTimeOffset]::UtcNow;
        executableSha256 = $appHash; hostInformationalVersion = "0.4.0+$source"; sourceCommit = $source;
        packageManifestSha256 = $null; packageEntrySha256 = $null;
        osDescription = 'Windows'; architecture = 'X64'; sessionId = 1; desktopVisible = $true; windowVisible = $true;
        width = 2; height = 1; rasterizationScale = 1.0; theme = 'Light'; viewportWidth = 2.0; viewportHeight = 1.0;
        scrollOffset = 0.0; witnessCode = 'asserted'; witnessCount = 1; pngPath = 'host-plugins.png'; pngBytes = 71; pngSha256 = $pngHash }
    function Save-Fixture {
        $binding | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $bindingPath
        $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath
    }
    function Check-Fixture {
        Test-NativeGalleryReceipt -ReceiptPath $receiptPath -BindingPath $bindingPath -OriginalReportPath $report `
            -ExecutablePath $app -ExpectedSourceCommit $source -ExpectedShotId 'host-plugins' -ExpectedPageKey 'plugins' `
            -ExpectedPhase 'business-seed' -ExpectedProbeKind 'business' -ExpectedPluginId $null -ExpectedViewId $null
    }
    function Refuse-Fixture([string]$name) {
        Save-Fixture
        $refused = $false
        try { $null = Check-Fixture } catch { $refused = $true }
        if (-not $refused) { throw "Native gallery accepted $name" }
    }
    Save-Fixture
    if ((Check-Fixture).status -cne 'captured') { throw 'Valid nonuniform PNG and independent binding were rejected.' }
    $validBinding = $binding.Clone(); $validReceipt = $receipt.Clone()
    $null = $binding.Remove('hostDllSha256'); Refuse-Fixture 'missing host DLL hash'; $binding.hostDllSha256 = $hostDllHash
    $binding.hostDllSha256 = 'malformed'; Refuse-Fixture 'malformed host DLL hash'; $binding.hostDllSha256 = $hostDllHash
    $binding.hostInformationalVersion = '0.4.0+' + ('b' * 40)
    Refuse-Fixture 'sidecar-only host version'; $binding.hostInformationalVersion = "0.4.0+$source"
    $withoutDll = Join-Path $root 'without-dll'
    New-Item -Path $withoutDll -ItemType Directory | Out-Null
    $plainApp = $app
    $app = Join-Path $withoutDll 'Workbench.App.exe'
    [IO.File]::Copy($plainApp, $app)
    Refuse-Fixture 'missing actual host DLL'
    $app = $plainApp
    $differentDirectory = Join-Path $root 'different-version'
    New-Item -Path $differentDirectory -ItemType Directory | Out-Null
    $differentApp = Join-Path $differentDirectory 'Workbench.App.exe'
    [IO.File]::Copy($app, $differentApp)
    $differentDll = Join-Path $differentDirectory 'Workbench.App.dll'
    New-GalleryVersionPe $differentDll ('b' * 40) 'wrong-version' $root
    $app = $differentApp
    $binding.hostDllSha256 = (Get-FileHash -LiteralPath $differentDll -Algorithm SHA256).Hash.ToLowerInvariant()
    Refuse-Fixture 'actual host metadata version mismatch'
    $app = $plainApp; $binding.hostDllSha256 = $hostDllHash
    Save-Fixture
    if ((Check-Fixture).status -cne 'captured') { throw 'Metadata-bound positive fixture did not recover.' }
    $receipt.status = 'blocked'; $receipt.reasonCode = 'desktop-unavailable'; $receipt.selectedPageKey = $null
    $receipt.pngPath = $null; $receipt.pngBytes = 0; $receipt.pngSha256 = $null
    Save-Fixture
    if ((Check-Fixture).status -cne 'blocked') { throw 'No-desktop attempt was not a bounded blocked row.' }
    $receipt.status = 'captured'; $receipt.reasonCode = 'none'; $receipt.selectedPageKey = 'plugins'
    $receipt.pngPath = 'host-plugins.png'; $receipt.pngBytes = 71; $receipt.pngSha256 = $pngHash
    $receipt.shotId = @('host-plugins'); Refuse-Fixture 'array ID'; $receipt.shotId = 'host-plugins'
    $receipt.processCreationFileTimeUtc++; Refuse-Fixture 'wrong birth'; $receipt.processCreationFileTimeUtc--
    $receipt.sourceCommit = 'b' * 40; Refuse-Fixture 'wrong source'; $receipt.sourceCommit = $source
    $receipt.pngPath = '../host-plugins.png'; Refuse-Fixture 'traversal'; $receipt.pngPath = 'host-plugins.png'
    $receipt.pngBytes++; Refuse-Fixture 'wrong PNG size'; $receipt.pngBytes--
    $receipt.width = 3; Refuse-Fixture 'wrong PNG dimensions'; $receipt.width = 2
    $receipt.pngSha256 = '0' * 64; Refuse-Fixture 'wrong PNG hash'; $receipt.pngSha256 = $pngHash
    $binding.originalProbeSucceeded = $false
    Save-Fixture
    $wrapperRejected = Check-Fixture
    if ($wrapperRejected.status -cne 'captured' -or $wrapperRejected.originalProbeSucceeded -ne $false -or
        $wrapperRejected.processExitCode -ne 0) {
        throw 'A native-success report concealed the launcher failure verdict.'
    }
    $binding.originalProbeSucceeded = $true
    @{ schemaVersion = 1; succeeded = $false; processId = 77; probe = 'seed' } | ConvertTo-Json | Set-Content -LiteralPath $report
    $binding.originalReportSha256 = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash.ToLowerInvariant()
    Refuse-Fixture 'native-failed report paired with launcher success'
    $binding.originalProbeSucceeded = $false; $binding.exitCode = -9
    Save-Fixture
    $failedCapture = Check-Fixture
    if ($failedCapture.status -cne 'captured' -or $failedCapture.originalProbeSucceeded -ne $false -or $failedCapture.processExitCode -ne -9) {
        throw 'An independently bound earlier image lost the failed original verdict or signed exit.'
    }
    @{ succeeded = $false; stage = 'initialize-or-native-smoke'; userRoot = 'synthetic-isolated-root' } |
        ConvertTo-Json | Set-Content -LiteralPath $report
    $binding.originalReportSha256 = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash.ToLowerInvariant()
    Save-Fixture
    if ((Check-Fixture).originalProbeSucceeded -ne $false) {
        throw 'A real-shape WriteFailure report without PID lost its launcher-bound earlier capture.'
    }
    @{ succeeded = $false; stage = 'initialize-or-native-smoke'; userRoot = 'synthetic-isolated-root'; processId = 78 } |
        ConvertTo-Json | Set-Content -LiteralPath $report
    $binding.originalReportSha256 = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash.ToLowerInvariant()
    Refuse-Fixture 'failed report with conflicting declared PID'
    @{ schemaVersion = 1; succeeded = $true; processId = 77; probe = 'seed' } | ConvertTo-Json | Set-Content -LiteralPath $report
    $binding.originalReportSha256 = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash.ToLowerInvariant()
    $binding.originalProbeSucceeded = $true; $binding.exitCode = 0
    $binding.processExitFileTimeUtc = 0L; Refuse-Fixture 'unconfirmed exit'; $binding.processExitFileTimeUtc = $exit
    $receipt.pluginId = 'com.forged.plugin'; Refuse-Fixture 'forged native-only plugin identity'; $receipt.pluginId = $null
    $originalPng = [IO.File]::ReadAllBytes($pngPath)
    foreach ($bad in @(
        @{ name = 'uniform PNG'; base64 = 'iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' },
        @{ name = 'transparent PNG'; base64 = 'iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4zwAC/xkACf8B/z7yQswAAAAASUVORK5CYII=' })) {
        [IO.File]::WriteAllBytes($pngPath, [Convert]::FromBase64String($bad.base64))
        $receipt.pngBytes = (Get-Item -LiteralPath $pngPath).Length
        $receipt.pngSha256 = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Refuse-Fixture $bad.name
    }
    [IO.File]::WriteAllBytes($pngPath, $originalPng)
    $receipt.pngBytes = $originalPng.Length; $receipt.pngSha256 = $pngHash
    $duplicatePath = Join-Path $gallery 'duplicate.json'
    Save-Fixture
    $duplicate = Get-Content -LiteralPath $receiptPath -Raw
    [IO.File]::WriteAllText($duplicatePath, $duplicate)
    $refused = $false
    try { Assert-NativeGalleryDirectory -Directory $gallery -ExpectedShotIds @('host-plugins') } catch { $refused = $true }
    if (-not $refused) { throw 'Duplicate or unexpected receipt was accepted.' }
    Remove-Item -LiteralPath $duplicatePath
    $orphanPath = Join-Path $gallery 'orphan.png'
    [IO.File]::WriteAllBytes($orphanPath, [IO.File]::ReadAllBytes($pngPath))
    $refused = $false
    try { Assert-NativeGalleryDirectory -Directory $gallery -ExpectedShotIds @('host-plugins') } catch { $refused = $true }
    if (-not $refused) { throw 'Orphan image was accepted.' }
    Remove-Item -LiteralPath $orphanPath
    $link = Join-Path $gallery 'linked.json'
    New-Item -Path $link -ItemType SymbolicLink -Target $receiptPath | Out-Null
    $refused = $false
    try { Test-NativeGalleryReceipt -ReceiptPath $link -BindingPath $bindingPath -OriginalReportPath $report `
        -ExecutablePath $app -ExpectedSourceCommit $source -ExpectedShotId 'host-plugins' -ExpectedPageKey 'plugins' `
        -ExpectedPhase 'business-seed' -ExpectedProbeKind 'business' -ExpectedPluginId $null -ExpectedViewId $null | Out-Null } catch { $refused = $true }
    if (-not $refused) { throw 'Linked receipt was accepted.' }
    $packageId = 'com.example.plugin'
    $packageRoot = Join-Path $root 'installed'
    $packageFolder = Join-Path $packageRoot $packageId
    New-Item -Path $packageFolder -ItemType Directory -Force | Out-Null
    $manifest = Join-Path $packageFolder 'plugin.json'
    $entry = Join-Path $packageFolder 'Example.dll'
    [IO.File]::WriteAllText($manifest, '{"id":"com.example.plugin","entryAssembly":"Example.dll"}')
    [IO.File]::WriteAllBytes($entry, [Text.Encoding]::ASCII.GetBytes('synthetic installed plugin entry'))
    $manifestHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
    $entryHash = (Get-FileHash -LiteralPath $entry -Algorithm SHA256).Hash.ToLowerInvariant()
    $binding.packages = @{ $packageId = @{ manifestSha256 = $manifestHash; entrySha256 = $entryHash; entryAssembly = 'Example.dll' } }
    $receipt.pageKey = 'view'; $receipt.selectedPageKey = 'view'; $receipt.pluginId = $packageId
    $receipt.selectedPluginId = $packageId; $receipt.viewId = 'demo'; $receipt.selectedViewId = 'demo'
    $receipt.packageManifestSha256 = $manifestHash; $receipt.packageEntrySha256 = $entryHash
    Save-Fixture
    $pluginArguments = @{ ReceiptPath = $receiptPath; BindingPath = $bindingPath; OriginalReportPath = $report;
        ExecutablePath = $app; ExpectedSourceCommit = $source; ExpectedShotId = 'host-plugins'; ExpectedPageKey = 'view';
        ExpectedPhase = 'business-seed'; ExpectedProbeKind = 'business'; ExpectedPluginId = $packageId;
        ExpectedViewId = 'demo'; ExpectedPackageRoot = $packageRoot }
    if ((Test-NativeGalleryReceipt @pluginArguments).status -cne 'captured') { throw 'Actual installed-file binding was rejected.' }
    [IO.File]::WriteAllBytes($entry, [Text.Encoding]::ASCII.GetBytes('tampered entry'))
    $refused = $false
    try { Test-NativeGalleryReceipt @pluginArguments | Out-Null } catch { $refused = $true }
    if (-not $refused) { throw 'Changed installed entry bytes were accepted.' }
    [IO.File]::WriteAllBytes($entry, [Text.Encoding]::ASCII.GetBytes('synthetic installed plugin entry'))
    $zipRoot = Join-Path $root 'zips'
    New-Item -Path $zipRoot -ItemType Directory | Out-Null
    $zip = Join-Path $zipRoot 'example.zip'
    [IO.File]::WriteAllBytes($zip, [Text.Encoding]::ASCII.GetBytes('synthetic package source bytes'))
    $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $binding.packages[$packageId].sourceZipName = 'example.zip'
    $binding.packages[$packageId].sourceZipSha256 = $zipHash
    @{ schemaVersion = 1; succeeded = $true; processId = 77; probe = 'seed';
        provenance = @(@{ pluginId = $packageId; sha256 = $zipHash }) } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report
    $binding.originalReportSha256 = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash.ToLowerInvariant()
    $pluginArguments.ExpectedZipRoot = $zipRoot
    Save-Fixture
    if ((Test-NativeGalleryReceipt @pluginArguments).status -cne 'captured') { throw 'Pinned ZIP/source binding was rejected.' }
    [IO.File]::WriteAllBytes($zip, [Text.Encoding]::ASCII.GetBytes('changed package source bytes'))
    $refused = $false
    try { Test-NativeGalleryReceipt @pluginArguments | Out-Null } catch { $refused = $true }
    if (-not $refused) { throw 'Changed source ZIP bytes were accepted.' }
    $aggregateRepo = Join-Path $root 'empty-repo'
    New-Item -Path $aggregateRepo -ItemType Directory | Out-Null
    & git -C $aggregateRepo init -q
    [IO.File]::WriteAllText((Join-Path $aggregateRepo 'marker.txt'), 'synthetic collector fixture')
    & git -C $aggregateRepo add marker.txt
    & git -C $aggregateRepo -c user.name=GalleryFixture -c user.email=gallery@example.invalid commit -qm fixture
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic collector git fixture failed.' }
    $galleryRoot = Join-Path $aggregateRepo 'artifacts/native/gallery/native-smoke'
    New-Item -Path $galleryRoot -ItemType Directory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $galleryRoot 'unexpected.json'), '{}')
    $refused = $false
    try { Export-NativeGallery -RepositoryRoot $aggregateRepo | Out-Null } catch { $refused = $true }
    if (-not $refused) { throw 'Aggregate accepted an unknown receipt.' }
    Remove-Item -LiteralPath (Join-Path $galleryRoot 'unexpected.json')
    $index = Export-NativeGallery -RepositoryRoot $aggregateRepo
    if ($index.acceptedImages -ne 0 -or @($index.rows).Count -ne 25 -or
        @($index.rows | Where-Object status -eq 'skipped-unavailable').Count -ne 1 -or
        -not (Test-Path -LiteralPath (Join-Path $aggregateRepo 'artifacts/native/genuine-gallery/index.json'))) {
        throw "Unreached fixed roster invalid: images=$($index.acceptedImages) rows=$(@($index.rows).Count) skipped=$(@($index.rows | Where-Object status -eq 'skipped-unavailable').Count)"
    }
    $failedRepo = Join-Path $root 'failed-phase-repo'
    New-Item -Path $failedRepo -ItemType Directory | Out-Null
    & git -C $failedRepo init -q
    [IO.File]::WriteAllText((Join-Path $failedRepo 'marker.txt'), 'failed original phase with earlier real PNG files')
    & git -C $failedRepo add marker.txt
    & git -C $failedRepo -c user.name=GalleryFixture -c user.email=gallery@example.invalid commit -qm fixture
    if ($LASTEXITCODE -ne 0) { throw 'Failed-phase collector git fixture failed.' }
    $failedSource = (& git -C $failedRepo rev-parse HEAD).Trim()
    $failedNative = Join-Path $failedRepo 'artifacts/native'
    $failedAppDirectory = Join-Path $failedNative 'app'
    New-Item -Path $failedAppDirectory -ItemType Directory -Force | Out-Null
    $failedApp = Join-Path $failedAppDirectory 'Workbench.App.exe'
    [IO.File]::Copy($plainApp, $failedApp)
    $failedDll = Join-Path $failedAppDirectory 'Workbench.App.dll'
    New-GalleryVersionPe $failedDll $failedSource 'failed-phase' $root
    $failedReport = Join-Path $failedNative 'native-smoke.json'
    @{ succeeded = $false; stage = 'initialize-or-native-smoke'; userRoot = 'synthetic-isolated-root' } |
        ConvertTo-Json | Set-Content -LiteralPath $failedReport
    $failedBinding = $validBinding.Clone()
    $failedBinding.phase = 'native-smoke'; $failedBinding.sourceCommit = $failedSource
    $failedBinding.hostInformationalVersion = "0.4.0+$failedSource"
    $failedBinding.hostDllSha256 = (Get-FileHash -LiteralPath $failedDll -Algorithm SHA256).Hash.ToLowerInvariant()
    $failedBinding.originalReportFile = 'native-smoke.json'
    $failedBinding.originalReportSha256 = (Get-FileHash -LiteralPath $failedReport -Algorithm SHA256).Hash.ToLowerInvariant()
    $failedBinding.originalProbeSucceeded = $false; $failedBinding.exitCode = -9
    $failedBinding | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $failedNative 'native-smoke.gallery-binding.json')
    $failedGallery = Join-Path $failedNative 'gallery/native-smoke'
    New-Item -Path $failedGallery -ItemType Directory -Force | Out-Null
    foreach ($shotId in @('host-plugins-disabled', 'host-plugins')) {
        $failedReceipt = $validReceipt.Clone()
        $failedReceipt.shotId = $shotId; $failedReceipt.pngPath = "$shotId.png"
        $failedReceipt.phase = 'native-smoke'; $failedReceipt.probeKind = 'smoke'
        $failedReceipt.sourceCommit = $failedSource; $failedReceipt.hostInformationalVersion = "0.4.0+$failedSource"
        $failedReceipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $failedGallery "$shotId.json")
        [IO.File]::WriteAllBytes((Join-Path $failedGallery "$shotId.png"), $originalPng)
    }
    $failedOutput = Join-Path $failedNative 'genuine-gallery'
    $blockedImages = Join-Path $failedOutput 'images'
    New-Item -Path $blockedImages -ItemType Directory -Force | Out-Null
    $collision = Join-Path $blockedImages 'host-plugins.png'
    [IO.File]::WriteAllBytes($collision, $originalPng)
    $refused = $false
    try { Export-NativeGallery -RepositoryRoot $failedRepo | Out-Null } catch { $refused = $true }
    if (-not $refused) { throw 'Collector published through a destination collision.' }
    if (Test-Path -LiteralPath (Join-Path $failedOutput 'index.json')) { throw 'Partial collector failure published an index.' }
    foreach ($shotId in @('host-plugins-disabled', 'host-plugins')) {
        $raw = Join-Path $failedGallery "$shotId.png"
        if (-not (Test-Path -LiteralPath $raw) -or
            (Get-FileHash -LiteralPath $raw -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pngHash) {
            throw "Collector failure discarded raw PNG bytes for $shotId"
        }
    }
    Remove-Item -LiteralPath $failedOutput -Recurse -Force
    $failedIndex = Export-NativeGallery -RepositoryRoot $failedRepo
    if ($failedIndex.acceptedImages -ne 2 -or
        @($failedIndex.phases | Where-Object { $_.phase -eq 'native-smoke' -and $_.originalProbeStatus -eq 'failed' }).Count -ne 1) {
        throw 'Failed original phase did not retain two validated earlier captures and a failed phase verdict.'
    }
    foreach ($shotId in @('host-plugins-disabled', 'host-plugins')) {
        $row = @($failedIndex.rows | Where-Object shotId -eq $shotId)
        $raw = Join-Path $failedGallery "$shotId.png"
        $copy = Join-Path $failedOutput "images/$shotId.png"
        if ($row.Count -ne 1 -or $row[0].status -cne 'failed' -or $row[0].captureStatus -cne 'captured' -or
            $row[0].originalProbeSucceeded -ne $false -or $row[0].processExitCode -ne -9 -or
            -not (Test-Path -LiteralPath $raw) -or -not (Test-Path -LiteralPath $copy) -or
            (Get-FileHash -LiteralPath $raw -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pngHash -or
            (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pngHash) {
            throw "Failed-phase captured image, original verdict or exact raw/copy bytes changed for $shotId"
        }
    }
    # The native report can remain green when the independent wrapper rejects OS exit/Closed/lease.
    Remove-Item -LiteralPath $failedOutput -Recurse -Force
    @{ schemaVersion = 1; succeeded = $true; processId = 77; probe = 'smoke' } |
        ConvertTo-Json | Set-Content -LiteralPath $failedReport
    $failedBinding.originalReportSha256 = (Get-FileHash -LiteralPath $failedReport -Algorithm SHA256).Hash.ToLowerInvariant()
    $failedBinding.nativeReportSucceeded = $true
    $failedBinding.originalProbeSucceeded = $false; $failedBinding.exitCode = 0
    $failedBinding | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $failedNative 'native-smoke.gallery-binding.json')
    $wrapperIndex = Export-NativeGallery -RepositoryRoot $failedRepo
    if ($wrapperIndex.acceptedImages -ne 2 -or
        @($wrapperIndex.phases | Where-Object { $_.phase -eq 'native-smoke' -and $_.nativeReportStatus -eq 'succeeded' -and
            $_.originalProbeStatus -eq 'failed' }).Count -ne 1 -or
        @($wrapperIndex.rows | Where-Object { $_.shotId -in @('host-plugins-disabled', 'host-plugins') -and
            $_.status -eq 'failed' -and $_.captureStatus -eq 'captured' -and $_.originalProbeSucceeded -eq $false }).Count -ne 2) {
        throw 'A green native report concealed the independent failed wrapper verdict or discarded bound earlier images.'
    }
    $testingOriginal = Join-Path $root 'testing-seed.json'
    $testingRunner = Join-Path $root 'testing-seed.runner.json'
    @{ schemaVersion = 1; succeeded = $true; processId = 77; probe = 'seed' } |
        ConvertTo-Json | Set-Content -LiteralPath $testingOriginal
    @{ schemaVersion = 1; succeeded = $false; processId = 77; hasExited = $true; exitCode = 0;
        rootLeaseAvailable = $false; runnerError = 'TestingRootStillOwned' } |
        ConvertTo-Json | Set-Content -LiteralPath $testingRunner
    $testingBindingPath = Write-NativeGalleryBinding -OriginalReportPath $testingOriginal -Phase testing-seed `
        -ExecutablePath $plainApp -SourceCommit $source -ProcessId 77 -ProcessCreationFileTimeUtc $birth `
        -ProcessExitFileTimeUtc $exit -ExitCode 0 -HasExited $true
    $testingBinding = Get-Content -LiteralPath $testingBindingPath -Raw | ConvertFrom-Json -AsHashtable
    if ($testingBinding.originalProbeSucceeded -ne $false -or $testingBinding.nativeReportSucceeded -ne $true -or
        $testingBinding.runnerReportSha256 -cne (Get-FileHash -LiteralPath $testingRunner -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'Testing runner failure did not override the native-success report in immutable launcher binding.'
    }
    $testingReceipt = $validReceipt.Clone()
    $testingReceipt.phase = 'testing-seed'; $testingReceipt.probeKind = 'testing'; $testingReceipt.projectId = 'default'
    $testingGallery = Join-Path $root 'testing-gallery'
    New-Item -Path $testingGallery -ItemType Directory | Out-Null
    $testingReceiptPath = Join-Path $testingGallery 'host-plugins.json'
    $testingReceipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $testingReceiptPath
    [IO.File]::WriteAllBytes((Join-Path $testingGallery 'host-plugins.png'), $originalPng)
    $testingResult = Test-NativeGalleryReceipt -ReceiptPath $testingReceiptPath -BindingPath $testingBindingPath `
        -OriginalReportPath $testingOriginal -ExecutablePath $plainApp -ExpectedSourceCommit $source `
        -ExpectedShotId host-plugins -ExpectedPageKey plugins -ExpectedPhase testing-seed -ExpectedProbeKind testing `
        -ExpectedPluginId $null -ExpectedViewId $null
    if ($testingResult.status -cne 'captured' -or $testingResult.originalProbeSucceeded -ne $false) {
        throw 'Testing earlier capture was relabelled as a passing wrapper probe.'
    }
    @{ schemaVersion = 1; succeeded = $true; processId = 77; hasExited = $true; exitCode = 0;
        rootLeaseAvailable = $true } | ConvertTo-Json | Set-Content -LiteralPath $testingRunner
    $refused = $false
    try { Test-NativeGalleryReceipt -ReceiptPath $testingReceiptPath -BindingPath $testingBindingPath `
        -OriginalReportPath $testingOriginal -ExecutablePath $plainApp -ExpectedSourceCommit $source `
        -ExpectedShotId host-plugins -ExpectedPageKey plugins -ExpectedPhase testing-seed -ExpectedProbeKind testing `
        -ExpectedPluginId $null -ExpectedViewId $null | Out-Null } catch { $refused = $true }
    if (-not $refused) { throw 'Mutated testing runner verdict was accepted without its original hash.' }
    Test-GalleryDurableSeedIdentity (Join-Path $root 'durable-seed')
    Write-Host 'Native gallery receipt guard fixtures passed.'
}
finally { Remove-Item -LiteralPath $root -Recurse -Force }
