[CmdletBinding()]
param()
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
    $dotnet = (Get-Command dotnet -CommandType Application).Source
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
    Write-Host 'Native gallery receipt guard fixtures passed.'
}
finally { Remove-Item -LiteralPath $root -Recurse -Force }
