[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '../../scripts/NativeGalleryReport.psm1') -Force
$root = Join-Path ([IO.Path]::GetTempPath()) ('NativeGalleryReceipt-' + [Guid]::NewGuid().ToString('N'))
New-Item -Path $root -ItemType Directory | Out-Null
try {
    $source = 'a' * 40
    $birth = [DateTime]::UtcNow.AddMinutes(-5).ToFileTimeUtc()
    $exit = [DateTime]::UtcNow.AddMinutes(5).ToFileTimeUtc()
    $app = Join-Path $root 'Workbench.App.exe'
    [IO.File]::WriteAllBytes($app, [Text.Encoding]::ASCII.GetBytes('synthetic-executable-for-receipt-policy'))
    $appHash = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant()
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
        processExitFileTimeUtc = $exit; hasExited = $true; exitCode = 0; executableSha256 = $appHash;
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
    $binding.originalProbeSucceeded = $false; Refuse-Fixture 'failed original probe'; $binding.originalProbeSucceeded = $true
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
    Write-Host 'Native gallery receipt guard fixtures passed.'
}
finally { Remove-Item -LiteralPath $root -Recurse -Force }
