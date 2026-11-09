[CmdletBinding()]
param([string]$Directory, [switch]$AllowReferenceOnly, [switch]$RequireCurrentSource, [switch]$HelperGuardsOnly, [switch]$StructuralGuardsOnly,
    [ValidateSet('Production','SourceAcceptance', IgnoreCase=$false)][string]$Profile = 'Production', [string]$ProductionDirectory)
$ErrorActionPreference = 'Stop'
$script:Cases = 0
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '../../scripts/TestingPluginDelivery.psm1') -Force
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temp = Join-Path ([IO.Path]::GetTempPath()) ('testing-package-regression-' + [Guid]::NewGuid().ToString('N'))
$hiddenParent = Join-Path $temp '.hidden-parent'
$hiddenLink = Join-Path $hiddenParent 'linked-parent'
function Refuses([scriptblock]$Action, [string]$Expected) {
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -cne $Expected) { throw "Expected $Expected; observed $($_.Exception.Message)" }
        $script:Cases++; Write-Host "Exact refusal: $Expected"; return
    }
    throw "Expected $Expected; corrupt testing delivery was accepted."
}
if ($StructuralGuardsOnly) {
    # Structural fixtures deliberately contain non-PE bytes. Only the SAME private reader is exercised;
    # a passing structural fixture is never a native/managed/full delivery positive.
    $module = Get-Module TestingPluginDelivery | Where-Object { $_.Path -ceq [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/TestingPluginDelivery.psm1')) }
    New-Item $temp -ItemType Directory | Out-Null
    function Write-Archive([string]$Label, [string]$Selected, [scriptblock]$Change) {
        $manifestPath = if ($Selected -ceq 'Production') { 'plugins/Workbench.Testing/plugin.json' } else { 'tests/Workbench.Testing.SourceAcceptance/plugin.json' }
        $items = [ordered]@{
            'Fixtures/figma.synthetic.json' = [byte[]](1,2); 'Fixtures/requirements.synthetic.json' = [byte[]](3,4)
            'Workbench.Testing.deps.json' = [Text.Encoding]::UTF8.GetBytes('{}')
            'Workbench.Testing.dll' = [byte[]](5,6); 'Workbench.Testing.Domain.dll' = [byte[]](7,8)
            'Workbench.Testing.Windows.dll' = [byte[]](9,10); 'Workbench.OwnedLab.exe' = [byte[]](11,12)
            'plugin.json' = [IO.File]::ReadAllBytes((Join-Path $repo $manifestPath))
        }
        if ($Selected -ceq 'SourceAcceptance') {
            $items['Workbench.Testing.SourceAcceptance.dll'] = [byte[]](13,14)
            $items['Workbench.Testing.SourceAcceptance.deps.json'] = [Text.Encoding]::UTF8.GetBytes('{}')
        }
        $context = @{ items = $items; duplicate = $null; link = $null; reparse = $null }
        if ($Change) { & $Change $context }
        $zipPath = Join-Path $temp ($Label + '.zip')
        $archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
        $records = @()
        try {
            foreach ($name in $items.Keys) {
                $entry = $archive.CreateEntry($name)
                if ($context.reparse -ceq $name) { $entry.ExternalAttributes = [int][IO.FileAttributes]::ReparsePoint }
                if ($context.link -ceq $name) { $entry.ExternalAttributes = -1577123840 }
                $stream = $entry.Open(); try { $stream.Write($items[$name]) } finally { $stream.Dispose() }
                $records += @{ path = $name; length = $items[$name].Length; sha256 = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($items[$name])) }
            }
            if ($context.duplicate) { $archive.CreateEntry($context.duplicate) | Out-Null }
        } finally { $archive.Dispose() }
        return @{ zip = $zipPath; records = $records }
    }
    function Read-Archive($Fixture, [string]$Selected = 'Production') {
        & $module { param($f,$p) Read-TestingPluginArchive -ZipPath $f.zip -PayloadRecords $f.records -Profile $p } $Fixture $Selected
    }
    try {
        $production = Write-Archive 'production' 'Production' $null
        $source = Write-Archive 'source' 'SourceAcceptance' $null
        if ((Read-Archive $production).Count -ne 8) { throw 'Default production eight-file reader failed.' }; $script:Cases++
        if ((Read-Archive $source 'SourceAcceptance').Count -ne 10) { throw 'Explicit source ten-file reader failed.' }; $script:Cases++
        Refuses { Read-Archive $source } 'TestingPayloadFileListInvalid'
        Refuses { Read-Archive $production 'SourceAcceptance' } 'TestingPayloadFileListInvalid'
        foreach ($mutation in @('missing-dll','renamed-dll','duplicate-dll','missing-deps','renamed-deps','duplicate-deps','extra-sdk','extra-runtimeconfig','duplicate-manifest','linked-entry','windows-reparse-entry')) {
            $fixture = Write-Archive $mutation 'SourceAcceptance' {
                param($c)
                switch ($mutation) {
                    'missing-dll' { $c.items.Remove('Workbench.Testing.SourceAcceptance.dll') }
                    'renamed-dll' { $c.items['Renamed.dll'] = $c.items['Workbench.Testing.SourceAcceptance.dll']; $c.items.Remove('Workbench.Testing.SourceAcceptance.dll') }
                    'duplicate-dll' { $c.duplicate = 'Workbench.Testing.SourceAcceptance.dll' }
                    'missing-deps' { $c.items.Remove('Workbench.Testing.SourceAcceptance.deps.json') }
                    'renamed-deps' { $c.items['Renamed.deps.json'] = $c.items['Workbench.Testing.SourceAcceptance.deps.json']; $c.items.Remove('Workbench.Testing.SourceAcceptance.deps.json') }
                    'duplicate-deps' { $c.duplicate = 'Workbench.Testing.SourceAcceptance.deps.json' }
                    'extra-sdk' { $c.items['Workbench.Sdk.dll'] = [byte[]](1) }
                    'extra-runtimeconfig' { $c.items['Workbench.Testing.SourceAcceptance.runtimeconfig.json'] = [byte[]](1) }
                    'duplicate-manifest' { $c.duplicate = 'plugin.json' }
                    'windows-reparse-entry' { $c.reparse = 'Workbench.Testing.SourceAcceptance.dll' }
                    'linked-entry' { $c.link = 'Workbench.Testing.SourceAcceptance.dll' }
                }
            }
            $expected = if ($mutation -cin @('linked-entry','windows-reparse-entry')) { 'TestingPayloadInvalid' } else { 'TestingPayloadFileListInvalid' }
            Refuses { Read-Archive $fixture 'SourceAcceptance' } $expected
        }
        foreach ($selected in @('Production','SourceAcceptance')) {
            foreach ($mutation in @('id','entryAssembly','entryType','apiVersion','version','minimumHostVersion','capabilities','duplicate-capability','missing-capability','duplicate-json','unknown-field','missing-field','wrong-type','trailing-json','oversize-json','invalid-utf8','deps-duplicate-json','deps-depth','deps-oversize')) {
                $fixture = Write-Archive "$selected-$mutation" $selected {
                    param($c)
                    $m = [Text.Encoding]::UTF8.GetString($c.items['plugin.json']) | ConvertFrom-Json -AsHashtable
                    switch ($mutation) {
                        'id' { $m.id = 'com.wrong.plugin' }; 'entryAssembly' { $m.entryAssembly = 'Wrong.dll' }; 'entryType' { $m.entryType = 'Wrong.Entry' }
                        'apiVersion' { $m.apiVersion = 2 }; 'version' { $m.version = '0.3.0' }; 'minimumHostVersion' { $m.minimumHostVersion = '0.3.0' }
                        'capabilities' { $m.capabilities += 'unexpected' }; 'duplicate-capability' { $m.capabilities[1] = $m.capabilities[0] }; 'missing-capability' { $m.capabilities = @($m.capabilities | Select-Object -Skip 1) }; 'unknown-field' { $m.extra = 1 }; 'missing-field' { $m.Remove('description') }
                        'wrong-type' { $m.apiVersion = '1' }
                    }
                    $json = $m | ConvertTo-Json -Depth 10 -Compress
                    if ($mutation -ceq 'duplicate-json') { $json = $json.Insert(1, '"ID":"duplicate",') }
                    if ($mutation -ceq 'oversize-json') { $json = '{"description":"' + ('x' * 2097152) + '"}' }
                    if ($mutation -ceq 'trailing-json') { $json += '{}' }
                    $c.items['plugin.json'] = [Text.Encoding]::UTF8.GetBytes($json)
                    if ($mutation -ceq 'invalid-utf8') { $c.items['plugin.json'] = [byte[]](0xff) }
                    if ($mutation -ceq 'deps-oversize') { $c.items['Workbench.Testing.deps.json'] = [Text.Encoding]::UTF8.GetBytes('{"x":"' + ('x' * 2097152) + '"}') }
                    if ($mutation -ceq 'deps-duplicate-json') { $c.items['Workbench.Testing.deps.json'] = [Text.Encoding]::UTF8.GetBytes('{"targets":{},"TARGETS":{}}') }
                    if ($mutation -ceq 'deps-depth') { $c.items['Workbench.Testing.deps.json'] = [Text.Encoding]::UTF8.GetBytes(('[' * 33) + '0' + (']' * 33)) }
                }
                # Hashes/lengths are recomputed by Write-Archive so these reach semantic guards.
                $expected = if ($mutation -clike 'deps-*') { 'TestingDepsJsonInvalid' } else { 'TestingManifestInvalid' }
                Refuses { Read-Archive $fixture $selected } $expected
            }
        }
        $tampered = Write-Archive 'hash-tamper' 'Production' $null
        $tampered.records[0].sha256 = '0' * 64
        Refuses { Read-Archive $tampered } 'TestingPayloadHashMismatch'
        $productionBytes = Read-Archive $production
        $sourceBytes = Read-Archive $source 'SourceAcceptance'
        & $module { param($p,$s) Assert-TestingCommonPayload $p $s } $productionBytes $sourceBytes; $script:Cases++
        $sourceBytes['Workbench.Testing.dll'] = [byte[]](0,0)
        Refuses { & $module { param($p,$s) Assert-TestingCommonPayload $p $s } $productionBytes $sourceBytes } 'TestingProductionPairMismatch'
        Write-Host "Structural package guards passed: $script:Cases cases; no native, managed or delivery positive claimed."
    } finally { Remove-Item $temp -Recurse -Force }
    return
}
if ($HelperGuardsOnly) {
    $module = Get-Module TestingPluginDelivery | Where-Object { $_.Path -ceq [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/TestingPluginDelivery.psm1')) }
    $profileArguments = @{ Profile = $Profile }; if ($Profile -ceq 'SourceAcceptance') { $profileArguments.ProductionDirectory = Join-Path $temp 'production' }
    $descriptor = @{ Available = $true; ImageSha = '1' * 64; NativeSourceSha = '2' * 64; SourceCommit = '3' * 40; Msvc = 'actual-msvc-required'; Sdk = 'actual-sdk-required'; Profile = 'Production' }
    Refuses { & $module { param($d) Assert-TestingOwnedHelper -Image ([byte[]]::new(0)) -Descriptor $d } $descriptor } 'TestingOwnedHelperInvalid'
    Refuses { & $module { param($d) Assert-TestingOwnedHelper -Image ([byte[]]::new(1024)) -Descriptor $d } $descriptor } 'TestingOwnedHelperInvalid'
    # Distinguish source-format refusal from the following empty-image/ref-only guards.
    # These synthetic inputs are not an accepted package or a PE/native execution.
    $formatFailures = [Collections.Generic.List[string]]::new()
    $validCommit = '0123456789abcdef0123456789abcdef01234567'
    $invalidCommits = @(
        @{ name = 'trailing LF'; value = ($validCommit + "`n") },
        @{ name = 'short'; value = $validCommit.Substring(0, 39) },
        @{ name = 'uppercase'; value = $validCommit.ToUpperInvariant() }
        @{ name = 'array'; value = @($validCommit) }
    )
    foreach ($case in $invalidCommits) {
        $descriptor.SourceCommit = $case.value
        try { Refuses { & $module { param($d) Assert-TestingOwnedHelper -Image ([byte[]]::new(0)) -Descriptor $d } $descriptor } 'TestingOwnedHelperDescriptorInvalid' }
        catch { $formatFailures.Add("descriptor $($case.name): $($_.Exception.Message)") }
    }
    $descriptor.SourceCommit = '3' * 40
    $metadataRoot = Join-Path $temp 'metadata-only'
    New-Item -ItemType Directory -Path $metadataRoot -Force | Out-Null
    $contract = & $module { param($p) Get-TestingProfile $p } $Profile
    foreach ($name in @('README.md', 'SHA256SUMS', $contract.zip)) { [IO.File]::WriteAllText((Join-Path $metadataRoot $name), 'synthetic boundary fixture') }
    $metadata = @{ schemaVersion = 1; profile = $Profile; pluginId = $contract.id; version = '0.2.0'; minimumHostVersion = '0.4.0'; sourceCommit = $validCommit; referenceOnly = $true; sourceTreeDirty = $false }
    $metadataPath = Join-Path $metadataRoot 'provenance.json'
    [IO.File]::WriteAllText($metadataPath, ($metadata | ConvertTo-Json -Compress))
    Refuses { Test-TestingPluginDelivery -Directory $metadataRoot -AppDirectory (Join-Path $temp 'unused-app') @profileArguments } 'TestingDeliveryReferenceOnly'
    foreach ($case in $invalidCommits) {
        $metadata.sourceCommit = $case.value
        [IO.File]::WriteAllText($metadataPath, ($metadata | ConvertTo-Json -Compress))
        try { Refuses { Test-TestingPluginDelivery -Directory $metadataRoot -AppDirectory (Join-Path $temp 'unused-app') @profileArguments } 'TestingDeliveryIdentityInvalid' }
        catch { $formatFailures.Add("metadata $($case.name): $($_.Exception.Message)") }
    }
    if ($formatFailures.Count) { throw ('Malformed source format was not refused at its boundary: ' + ($formatFailures -join '; ')) }
    $descriptor.Profile = 'ReferenceOnly'; $descriptor.Available = $false
    Refuses { & $module { param($d) Assert-TestingOwnedHelper -Image ([byte[]]::new(1024)) -Descriptor $d } $descriptor } 'TestingOwnedHelperDescriptorInvalid'
    Refuses { New-TestingPluginDelivery -BuildDirectory $temp -OutputDirectory (Join-Path $temp 'delivery') -ReferenceOnly @profileArguments } 'TestingDeliveryReferenceOnly'
    Refuses { New-TestingPluginDelivery -BuildDirectory $temp -OutputDirectory (Join-Path $temp 'delivery') @profileArguments } 'TestingOwnedHelperMissing'
    if (Test-Path (Join-Path $temp 'delivery')) { throw 'Missing/reference helper created a production output.' }
    Refuses { New-TestingPluginDelivery -BuildDirectory $temp -OutputDirectory (Join-Path $temp 'delivery') -Profile SourceAcceptance } 'TestingProductionPairRequired'
    Refuses { Test-TestingPluginDelivery -Directory $temp -Profile SourceAcceptance } 'TestingProductionPairRequired'
    Refuses { New-TestingPluginDelivery -BuildDirectory $temp -OutputDirectory (Join-Path $temp 'delivery') -ProductionDirectory $temp } 'TestingProductionPairUnexpected'
    Refuses { Test-TestingPluginDelivery -Directory $temp -ProductionDirectory $temp } 'TestingProductionPairUnexpected'
    New-Item $temp -ItemType Directory -Force | Out-Null
    Set-Content (Join-Path $temp 'keep.txt') 'preserve old evidence'
    Refuses { & $module { param($p) Assert-TestingFreshDirectory $p } $temp } 'TestingDeliveryAlreadyExists'
    if ((Get-Content (Join-Path $temp 'keep.txt')) -cne 'preserve old evidence') { throw 'Stale guard modified earlier output.' }; $script:Cases++
    $linked = $temp + '-link'
    try {
        [IO.Directory]::CreateSymbolicLink($linked, $temp) | Out-Null
        Refuses { & $module { param($p) Assert-TestingFreshDirectory (Join-Path $p 'new') } $linked } 'TestingDeliveryLinkRefused'
    } finally { if (Test-Path $linked) { Remove-Item -LiteralPath $linked -Force } }
    $dangling = Join-Path $temp 'dangling'
    try {
        [IO.Directory]::CreateSymbolicLink($dangling,(Join-Path $temp 'missing-target')) | Out-Null
        Refuses { & $module { param($p) Assert-TestingFreshDirectory (Join-Path $p 'new') } $dangling } 'TestingDeliveryLinkRefused'
    } finally { if (Get-Item -LiteralPath $dangling -Force -ErrorAction SilentlyContinue) { Remove-Item -LiteralPath $dangling -Force } }
    Refuses { New-TestingPluginDelivery -BuildDirectory $temp -OutputDirectory $temp @profileArguments } 'TestingDeliveryAlreadyExists'
    $inputRoot = Join-Path $temp 'input-link'
    New-Item $inputRoot -ItemType Directory | Out-Null
    [IO.File]::CreateSymbolicLink((Join-Path $inputRoot 'Workbench.OwnedLab.exe'), (Join-Path $temp 'keep.txt')) | Out-Null
    Refuses { New-TestingPluginDelivery -BuildDirectory $inputRoot -OutputDirectory (Join-Path $temp 'never-created') @profileArguments } 'TestingDeliveryLinkRefused'
    if (Test-Path (Join-Path $temp 'never-created')) { throw 'Linked input created an output.' }; $script:Cases++
    # Execute the actual build/package scripts in an owned minimal layout. Stale roots must
    # fail before the platform/build branch and must retain their original marker bytes.
    $scriptRoot = Join-Path $temp 'script-repo'
    New-Item (Join-Path $scriptRoot 'scripts') -ItemType Directory -Force | Out-Null
    foreach ($name in @('build.ps1','package-testing-plugin.ps1','TestingPluginDelivery.psm1','TestingManagedPayloadAudit.cs')) {
        Copy-Item -LiteralPath (Join-Path $repo "scripts/$name") (Join-Path $scriptRoot 'scripts')
    }
    New-Item (Join-Path $scriptRoot 'artifacts/native') -ItemType Directory -Force | Out-Null
    Set-Content (Join-Path $scriptRoot 'artifacts/native/keep.txt') 'older native evidence'
    Refuses { & (Join-Path $scriptRoot 'scripts/build.ps1') -IncludeSourceAcceptance } 'TestingDeliveryAlreadyExists'
    if ((Get-Content (Join-Path $scriptRoot 'artifacts/native/keep.txt')) -cne 'older native evidence') { throw 'Build replaced older output.' }; $script:Cases++
    New-Item (Join-Path $scriptRoot 'artifacts/testing-source-acceptance-package-build') -ItemType Directory | Out-Null
    Set-Content (Join-Path $scriptRoot 'artifacts/testing-source-acceptance-package-build/keep.txt') 'older graph'
    Refuses { & (Join-Path $scriptRoot 'scripts/package-testing-plugin.ps1') -Profile SourceAcceptance -OutputDirectory (Join-Path $temp 'never-source') -PairedProductionDirectory (Join-Path $temp 'never-production') } 'TestingDeliveryAlreadyExists'
    if ((Get-Content (Join-Path $scriptRoot 'artifacts/testing-source-acceptance-package-build/keep.txt')) -cne 'older graph' -or (Test-Path (Join-Path $temp 'never-source')) -or (Test-Path (Join-Path $temp 'never-production'))) { throw 'Stale graph guard mutated prior/new outputs.' }; $script:Cases++
    New-Item (Join-Path $scriptRoot 'artifacts/testing-source-acceptance-reference-build') -ItemType Directory | Out-Null
    Set-Content (Join-Path $scriptRoot 'artifacts/testing-source-acceptance-reference-build/keep.txt') 'older source reference graph'
    Refuses { & (Join-Path $scriptRoot 'scripts/package-testing-plugin.ps1') -ReferenceOnly -Profile SourceAcceptance -OutputDirectory (Join-Path $temp 'never-source-reference') -PairedProductionDirectory (Join-Path $temp 'never-production-reference') } 'TestingDeliveryAlreadyExists'
    if ((Get-Content (Join-Path $scriptRoot 'artifacts/testing-source-acceptance-reference-build/keep.txt')) -cne 'older source reference graph' -or (Test-Path (Join-Path $temp 'never-source-reference')) -or (Test-Path (Join-Path $temp 'never-production-reference'))) { throw 'Source ReferenceOnly guard mutated an earlier graph or created a delivery.' }; $script:Cases++
    Import-Module (Join-Path $repo 'scripts/TestingPluginDelivery.psm1') -Force
    $module = Get-Module TestingPluginDelivery | Where-Object { $_.Path -ceq [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/TestingPluginDelivery.psm1')) }
    Remove-Item $temp -Recurse -Force
    $records = @(& $module { param($root) Get-TestingSourceFiles $root } $repo | Where-Object path -CLike 'native/Workbench.OwnedLab/*' | Sort-Object path -CaseSensitive)
    $sourceText = ($records | ForEach-Object { "$($_.path) $($_.length) $($_.sha256)`n" }) -join ''
    $sourceText += "profile=Production`nmacro=`n"
    $descriptor.NativeSourceSha = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($sourceText)))
    $build = @{ sources = $records; staticCrt = $true; macro = '' }
    & $module { param($b,$d) Assert-TestingOwnedSource $b $d } $build $descriptor
    $build.macro = 'FORBIDDEN_PRODUCTION_FAULT'
    Refuses { & $module { param($b,$d) Assert-TestingOwnedSource $b $d } $build $descriptor } 'TestingOwnedHelperSourceInvalid'
    $build.macro = ''; $records[0].sha256 = '0' * 64
    Refuses { & $module { param($b,$d) Assert-TestingOwnedSource $b $d } $build $descriptor } 'TestingOwnedHelperSourceInvalid'
    $allSources = @(& $module { param($root) Get-TestingSourceFiles $root } $repo)
    foreach ($path in @('scripts/TestingManagedPayloadAudit.cs','tests/scripts/TestingManagedPayload.Tests.ps1','scripts/package-testing-plugin.ps1','scripts/build.ps1','tests/Workbench.Testing.PackageProbe/Program.cs','scripts/OwnedLabProbeStaging.psm1','tests/scripts/OwnedLabProbeStaging.Tests.ps1','scripts/OwnedLabProbeOutput.psm1','tests/scripts/OwnedLabProbeOutput.Tests.ps1',
        'plugins/Workbench.Testing/NativeActivityCoordinator.cs','tests/Workbench.Core.Tests/TestingNativeActivityTests.cs','tests/Workbench.Core.Tests/TestingControllerAdmissionTests.cs','tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj','tests/Workbench.Core.Tests/LifecycleTests.cs',
        'plugins/Workbench.Testing/NativeOwnedPilot.cs','plugins/Workbench.Testing/NativeOwnedEvidence.cs','plugins/Workbench.Testing/NativeTestingLifecycle.cs','plugins/Workbench.Testing/TestingOwnedPilotPanel.cs',
        'plugins/Workbench.Testing/NativeInvocationFeedback.cs','tests/Workbench.Core.Tests/TestingNativeFeedbackTests.cs',
        'plugins/Workbench.Testing/TestingSourceServices.cs','plugins/Workbench.Testing/TestingPlugin.cs','plugins/Workbench.Testing/Workbench.Testing.csproj','plugins/Workbench.Testing/plugin.json',
        'tests/Workbench.Testing.SourceAcceptance/Workbench.Testing.SourceAcceptance.csproj','tests/Workbench.Testing.SourceAcceptance/plugin.json',
        'tests/Workbench.Testing.SourceAcceptance/SourceAcceptancePlugin.cs','tests/Workbench.Testing.SourceAcceptance/SourceAcceptanceProtocol.cs',
        'tests/Workbench.Testing.SourceAcceptance/SourceAcceptanceScenarioLedger.cs','tests/Workbench.Testing.SourceAcceptance/SourceAcceptanceHttpHandlers.cs','tests/Workbench.Testing.SourceAcceptance/SourceAcceptanceOwnership.cs','tests/Workbench.Testing.SourceAcceptance/SourceAcceptanceReleaseBridge.cs',
        'tests/Workbench.NativeReference/Workbench.NativeReference.csproj','tests/Workbench.NativeReference/GeneratedApplicationReference.cs',
        'tests/Workbench.Core.Tests/SourceAcceptanceTests.cs','tests/Workbench.Core.Tests/SourceAcceptanceTests.Ownership.cs','tests/Workbench.Core.Tests/SourceAcceptanceTests.Transport.cs','tests/Workbench.Core.Tests/SourceAcceptanceReleaseTests.cs',
        'tests/scripts/DeliveryHostVersion.Tests.ps1','tests/Workbench.Core.Tests/DiscoveryTests.cs',
        'tests/Workbench.Core.Tests/HostErrorReportExportTests.cs','tests/Workbench.Core.Tests/HostFailureTests.cs','tests/Workbench.Core.Tests/HostStartupDiagnosticsTests.cs','tests/Workbench.Core.Tests/WorkspaceRuntimeCoordinatorTests.cs',
        'scripts/source-acceptance-probe.ps1','scripts/SourceAcceptanceProbe.psm1','tests/scripts/SourceAcceptanceProbe.Tests.ps1','tests/Workbench.Core.Tests/RunOptionsTests.cs',
        'src/Workbench.App/TestingSourceAcceptanceProbe.cs','src/Workbench.App/TestingSourceAcceptanceProbe.Controls.cs','src/Workbench.App/TestingSourceAcceptanceProbe.Records.cs','src/Workbench.App/TestingSourceAcceptanceProbe.Lifecycle.cs',
        'src/Workbench.App/ShellTitleBar.cs','src/Workbench.App/Assets/Workbench.svg','src/Workbench.App/Assets/Workbench.ico',
        'docs/testing-plugin.md','docs/windows-verification.md','.github/workflows/build.yml','scripts/TestingPluginDelivery.psm1','tests/scripts/TestingPluginPackage.Tests.ps1',
        'tests/Workbench.Core.Tests/TestingNativeOwnedPilotTests.cs','tests/Workbench.Core.Tests/TestingNativeOwnedEvidenceTests.cs','tests/Workbench.Core.Tests/TestingNativeLifecycleTests.cs',
        'tests/Workbench.Core.Tests/OwnedAdmissionTests.cs','tests/Workbench.Core.Tests/OwnedJournalTests.cs','tests/Workbench.Core.Tests/OwnedFixedPlansTests.cs')) {
        $record = @($allSources | Where-Object path -CEQ $path)
        $file = Get-Item -LiteralPath (Join-Path $repo $path)
        if ($record.Count -ne 1 -or $record[0].length -ne $file.Length -or $record[0].sha256 -cne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) { throw "Native activity/staging source is missing or incorrect in delivery provenance: $path" }
        $changed = @($allSources | ForEach-Object { @{ path = $_.path; length = $_.length; sha256 = $(if ($_.path -ceq $path) { '0' * 64 } else { $_.sha256 }) } })
        Refuses { & $module { param($current,$recorded) Assert-TestingSourceRecords -Current $current -Recorded $recorded } $allSources $changed } 'TestingSourceHashMismatch'
        Write-Host "Source hash corruption refused: $path"
    }
    Write-Host "Owned helper/profile packaging guards passed: $script:Cases refusal/retention cases; synthetic guards do not establish native execution."
    return
}
$zipName = if ($Profile -ceq 'SourceAcceptance') { 'workbench-testing-source-acceptance-0.2.0.zip' } else { 'workbench-testing-plugin-0.2.0.zip' }
if (-not $Directory) { throw 'Pass the actual delivery directory or -HelperGuardsOnly.' }
$profileArguments = @{ Profile = $Profile }; if ($ProductionDirectory) { $profileArguments.ProductionDirectory = $ProductionDirectory }
Test-TestingPluginDelivery -Directory $Directory -AllowReferenceOnly:$AllowReferenceOnly -RequireCurrentSource:$RequireCurrentSource @profileArguments
function Refresh-Sums([string]$Root) {
    @('README.md','provenance.json',$zipName | ForEach-Object { (Get-FileHash (Join-Path $Root $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_ }) | Set-Content (Join-Path $Root 'SHA256SUMS')
}
try {
    $hiddenDelivery = Join-Path $hiddenParent 'delivery'
    New-Item $hiddenDelivery -ItemType Directory -Force | Out-Null
    # Dot names are Hidden on Unix; Windows requires the actual filesystem attribute.
    if ($IsWindows) { [IO.File]::SetAttributes($hiddenParent, [IO.File]::GetAttributes($hiddenParent) -bor [IO.FileAttributes]::Hidden) }
    $hiddenItem = Get-Item -LiteralPath $hiddenParent -Force
    if (($hiddenItem.Attributes -band [IO.FileAttributes]::Hidden) -eq 0) { throw 'Hidden-parent regression requires an actually hidden directory.' }
    Write-Host "Hidden ancestor confirmed: $($hiddenItem.Attributes); explicitly set on Windows: $IsWindows."
    Copy-Item (Join-Path $Directory '*') $hiddenDelivery -Recurse
    Test-TestingPluginDelivery -Directory $hiddenDelivery -AllowReferenceOnly:$AllowReferenceOnly @profileArguments
    Write-Host 'Valid testing delivery accepted beneath the actual hidden ancestor.'
    Add-Content (Join-Path $hiddenDelivery 'README.md') 'tamper'
    Refuses { Test-TestingPluginDelivery -Directory $hiddenDelivery -AllowReferenceOnly:$AllowReferenceOnly @profileArguments } 'TestingDeliveryHashMismatch'
    Copy-Item -LiteralPath (Join-Path $Directory 'README.md') -Destination (Join-Path $hiddenDelivery 'README.md') -Force
    $linkTarget = Join-Path $temp 'link-target'
    New-Item $linkTarget -ItemType Directory | Out-Null
    Copy-Item -LiteralPath $hiddenDelivery -Destination (Join-Path $linkTarget 'delivery') -Recurse
    if ($IsWindows) { New-Item -Path $hiddenLink -ItemType Junction -Target $linkTarget | Out-Null }
    else { [IO.Directory]::CreateSymbolicLink($hiddenLink, $linkTarget) | Out-Null }
    if (((Get-Item -LiteralPath $hiddenLink -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) { throw 'Hidden-parent regression requires an actual linked ancestor.' }
    Refuses { Test-TestingPluginDelivery -Directory (Join-Path $hiddenLink 'delivery') -AllowReferenceOnly:$AllowReferenceOnly @profileArguments } 'TestingDeliveryLinkRefused'
    Remove-Item -LiteralPath $hiddenLink -Force
    Remove-Item $temp -Recurse -Force
    New-Item $temp -ItemType Directory | Out-Null
    Copy-Item (Join-Path $Directory '*') $temp -Recurse
    Add-Content (Join-Path $temp 'README.md') 'tamper'
    Refuses { Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly @profileArguments } 'TestingDeliveryHashMismatch'
    Remove-Item $temp -Recurse -Force
    New-Item $temp -ItemType Directory | Out-Null
    Copy-Item (Join-Path $Directory '*') $temp -Recurse
    New-Item (Join-Path $temp 'Workbench.Sdk.dll') -ItemType File | Out-Null
    Refuses { Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly @profileArguments } 'TestingDeliveryFileListInvalid'
    Remove-Item $temp -Recurse -Force
    New-Item $temp -ItemType Directory | Out-Null
    Copy-Item (Join-Path $Directory '*') $temp -Recurse
    $metadata = Get-Content (Join-Path $temp 'provenance.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($metadata.referenceOnly) { Refuses { Test-TestingPluginDelivery -Directory $temp @profileArguments } 'TestingDeliveryReferenceOnly' }
    $module = Get-Module TestingPluginDelivery | Where-Object { $_.Path -ceq [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/TestingPluginDelivery.psm1')) }
    $sourceRecords = @($metadata.sourceFiles)
    & $module { param($records, $root) Assert-TestingSourceRecords -Current @(Get-TestingSourceFiles $root) -Recorded $records } $sourceRecords $repo
    $metadata.sourceFiles[0].sha256 = '0' * 64
    $metadata | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $temp 'provenance.json')
    Refresh-Sums $temp
    # This deliberately dirty/ReferenceOnly fixture passes all outer guards after sums refresh.
    # Invoke the SAME private source-record guard used by RequireCurrentSource, without inventing clean provenance.
    Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly @profileArguments
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
        $zipPath = Join-Path $temp $zipName
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
        Refuses { Test-TestingPluginDelivery -Directory $temp -AllowReferenceOnly:$AllowReferenceOnly @profileArguments } $expected
    }
    Write-Host 'Testing package regressions passed: actual hidden-parent acceptance/tamper/link guards, independently attributed outer checksum, file list, ReferenceOnly, source hash/list/three identity refusals, and four ZIP/payload/manifest refusals.'

}
finally {
    if (Test-Path -LiteralPath $hiddenLink) { Remove-Item -LiteralPath $hiddenLink -Force }
    if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
}
