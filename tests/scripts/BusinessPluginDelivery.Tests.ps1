[CmdletBinding()]
param([string]$AppDirectory = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'artifacts/native/app'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Keep the entry fixture fixed while the explicit relative-path cases change PowerShell location.
$AppDirectory = (Resolve-Path -LiteralPath $AppDirectory).ProviderPath
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$export = Join-Path $repo 'scripts/export-business-plugins.ps1'
Import-Module (Join-Path $repo 'scripts/BusinessPluginDelivery.psm1') -Force
$temporary = Join-Path ([IO.Path]::GetTempPath()) "workbench-business-delivery-$([Guid]::NewGuid().ToString('N'))"
New-Item $temporary -ItemType Directory | Out-Null
$failures = [Collections.Generic.List[string]]::new()
$passed = 0
function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Check([string]$Name, [scriptblock]$Action) {
    try { & $Action; $script:passed++; Write-Output "PASS $Name" }
    catch { $script:failures.Add("${Name}: $($_.Exception.Message)"); Write-Output "FAIL ${Name}: $($_.Exception.Message)" }
}
function New-Inputs([string]$Name) {
    $path = Join-Path $temporary $Name
    New-Item $path -ItemType Directory | Out-Null
    foreach ($plugin in @('dingtalk', 'upcam')) { Copy-Item -LiteralPath (Join-Path $repo "tests/integration/$plugin") -Destination $path -Recurse }
    return $path
}
function At-CallerLocation([scriptblock]$Action) {
    $processDirectory = [Environment]::CurrentDirectory
    Push-Location -LiteralPath $callerLocation
    try {
        Assert-True ($PWD.Provider.Name -eq 'FileSystem' -and $PWD.Path -cne $processDirectory) 'Regression needs different PowerShell and process filesystem locations.'
        & $Action
        Assert-True ([Environment]::CurrentDirectory -ceq $processDirectory) 'Delivery changed the caller process current directory.'
    } finally { Pop-Location }
}
function Assert-Refused([string]$InputPath, [string]$Destination, [string]$HostPath = $AppDirectory, [switch]$Strict, [string]$Reason) {
    $before = if (Test-Path -LiteralPath $Destination -PathType Container) {
        @(Get-ChildItem -LiteralPath $Destination -File -Recurse -Force | ForEach-Object { "$($_.FullName):$((Get-FileHash -LiteralPath $_.FullName).Hash)" })
    } else { @() }
    $refused = $false
    try { & $export -AppDirectory $HostPath -InputRoot $InputPath -DestinationRoot $Destination -RequireCurrentSource:$Strict }
    catch {
        $refused = $true
        if ($Reason) { Assert-True ($_.Exception.Message.Contains($Reason)) "Expected $Reason refusal; observed $($_.Exception.Message)" }
    }
    Assert-True $refused 'Exporter accepted a refused input/destination.'
    $after = if (Test-Path -LiteralPath $Destination -PathType Container) {
        @(Get-ChildItem -LiteralPath $Destination -File -Recurse -Force | ForEach-Object { "$($_.FullName):$((Get-FileHash -LiteralPath $_.FullName).Hash)" })
    } else { @() }
    Assert-True (($before -join "`n") -ceq ($after -join "`n")) 'Refusal changed original output bytes.'
}
function Set-UpcamMode([string]$InputPath, [string]$Variant) {
    # A real published ZIP is edited only inside this test-owned directory. Nothing is executed.
    $package = Join-Path $InputPath 'upcam/package.zip'
    $archive = [IO.Compression.ZipFile]::Open($package, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.GetEntry('plugin-package.json')
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable } finally { $reader.Dispose() }
        switch ($Variant) {
            'missing' { $metadata.Remove('syntheticAcceptance') | Out-Null }
            'unknown' { $metadata['syntheticAcceptance'] = 'false' }
            'acceptance' { $metadata['syntheticAcceptance'] = $true }
            'extra' { $metadata['syntheticAcceptance'] = @($false, $true) }
        }
        $entry.Delete()
        $entry = $archive.CreateEntry('plugin-package.json')
        $writer = [IO.StreamWriter]::new($entry.Open())
        try { $writer.Write(($metadata | ConvertTo-Json -Depth 30)) } finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
}
function New-MetadataOnlyHost([string]$SourceCommit) {
    # Deliberately modified PE metadata control; never a runnable/current native build.
    $path = Join-Path $temporary "metadata-only-$SourceCommit"
    New-Item $path -ItemType Directory | Out-Null
    foreach ($name in @('Workbench.App.exe', 'Workbench.App.dll')) { Copy-Item -LiteralPath (Join-Path $AppDirectory $name) -Destination $path }
    $dll = Join-Path $path 'Workbench.App.dll'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
    Assert-True ($version -cmatch '^([0-9]+[.][0-9]+[.][0-9]+)[+]([0-9a-f]{40})$') 'Provide actual published host files for metadata-only packaging checks.'
    $semanticVersion = $Matches[1]
    $bytes = [IO.File]::ReadAllBytes($dll)
    $replaced = 0
    # Windows reads the version resource; Unix reads the managed informational attribute.
    foreach ($encoding in @([Text.Encoding]::Unicode, [Text.Encoding]::UTF8)) {
        $old = $encoding.GetBytes($version)
        $replacement = $encoding.GetBytes("$semanticVersion+$SourceCommit")
        for ($i = 0; $i -le $bytes.Length - $old.Length; $i++) {
            $equal = $true
            for ($j = 0; $j -lt $old.Length; $j++) { if ($bytes[$i + $j] -ne $old[$j]) { $equal = $false; break } }
            if ($equal) { [Array]::Copy($replacement, 0, $bytes, $i, $old.Length); $replaced++; $i += $old.Length - 1 }
        }
    }
    Assert-True ($replaced -gt 0) 'Metadata-only control did not change the PE product resource.'
    [IO.File]::WriteAllBytes($dll, $bytes)
    Assert-True ([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion -ceq "$semanticVersion+$SourceCommit") 'Metadata-only product resource is unreadable.'
    return $path
}
try {
    $source = Join-Path $repo 'tests/integration'
    $validOutput = Join-Path $temporary 'valid'
    Check 'production provenance raw bytes survive autocrlf checkout while unrelated text still converts' {
        $checkoutRepo = Join-Path $temporary 'autocrlf-source'
        $checkoutOutput = Join-Path $temporary 'autocrlf-checkout'
        New-Item $checkoutRepo, $checkoutOutput -ItemType Directory | Out-Null
        & git -C $checkoutRepo init --quiet --initial-branch=delivery-fixture
        if ($LASTEXITCODE -ne 0) { throw 'Cannot create test-owned checkout repository.' }
        foreach ($name in @('dingtalk', 'upcam')) {
            $directory = Join-Path $checkoutRepo "tests/integration/$name"; New-Item $directory -ItemType Directory -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $source "$name/provenance.json") -Destination $directory
        }
        if (Test-Path -LiteralPath (Join-Path $repo '.gitattributes')) { Copy-Item -LiteralPath (Join-Path $repo '.gitattributes') -Destination $checkoutRepo }
        [IO.File]::WriteAllText((Join-Path $checkoutRepo 'unrelated.txt'), "one`ntwo`n")
        & git -C $checkoutRepo -c core.autocrlf=false add --all
        if ($LASTEXITCODE -ne 0) { throw 'Cannot stage test-owned checkout inputs.' }
        & git -C $checkoutRepo -c core.autocrlf=true checkout-index --all "--prefix=$checkoutOutput$([IO.Path]::DirectorySeparatorChar)"
        if ($LASTEXITCODE -ne 0) { throw 'Cannot exercise autocrlf checkout.' }
        foreach ($name in @('dingtalk', 'upcam')) {
            Assert-True ((Get-FileHash -LiteralPath (Join-Path $checkoutOutput "tests/integration/$name/provenance.json")).Hash -ceq (Get-FileHash -LiteralPath (Join-Path $source "$name/provenance.json")).Hash) 'autocrlf rewrote frozen provenance bytes.'
        }
        Assert-True ([IO.File]::ReadAllText((Join-Path $checkoutOutput 'unrelated.txt')) -ceq "one`r`ntwo`r`n") 'Unrelated text conversion was broadly disabled.'
    }
    Check 'actual published PE metadata export preserves six exact payload files and production identity' {
        New-Item $validOutput -ItemType Directory | Out-Null
        & $export -AppDirectory $AppDirectory -DestinationRoot $validOutput
        $files = @(Get-ChildItem -LiteralPath $validOutput -Recurse -File -Force | ForEach-Object { [IO.Path]::GetRelativePath($validOutput, $_.FullName).Replace('\', '/') } | Sort-Object)
        Assert-True (($files -join ',') -ceq 'dingtalk/package.zip,dingtalk/provenance.json,manifest.json,README.zh-CN.md,upcam/package.zip,upcam/provenance.json') 'Wrong output file set.'
        $manifest = Get-Content -LiteralPath (Join-Path $validOutput 'manifest.json') -Raw | ConvertFrom-Json
        Assert-True ($manifest.schemaVersion -eq 1 -and $manifest.mode -ceq 'Production' -and $manifest.plugins.Count -eq 2) 'Wrong delivery schema/mode.'
        $expected = @(
            @('dingtalk', 'com.projectworkbench.dingtalk-insights', '0.3.2', 'c0c1d3303ae8b59c36dd7c4c1de85ddc6578abcf', '38d7a8f30ce6d4d5c6f6e26c757cbb6b9fc9f812546bf1db2ecdbadb9b578200', 179239, 42295585483),
            @('upcam', 'com.upcam.task-digest', '0.1.0', '6370b9df46af564fde022924d1dbeace7d575c6a', '3e07b7db0e3f17871b88ce1e45ac09d3b8724e6628a35fd5e0cdc1eb9ad95dbe', 11209273, 42268731102)
        )
        foreach ($pin in $expected) {
            $plugin = @($manifest.plugins | Where-Object name -CEQ $pin[0])
            Assert-True ($plugin.Count -eq 1) 'Missing production plugin record.'
            Assert-True ($plugin[0].pluginId -ceq $pin[1] -and $plugin[0].version -ceq $pin[2] -and $plugin[0].sourceCommit -ceq $pin[3] -and $plugin[0].sha256 -ceq $pin[4] -and $plugin[0].packageBytes -eq $pin[5] -and $plugin[0].workflowRunId -eq $pin[6]) 'Changed immutable production identity/hash.'
            foreach ($file in @('package.zip', 'provenance.json')) {
                Assert-True ((Get-FileHash -LiteralPath (Join-Path $validOutput "$($pin[0])/$file")).Hash -ceq (Get-FileHash -LiteralPath (Join-Path $source "$($pin[0])/$file")).Hash) 'Export changed frozen package/provenance bytes.'
            }
        }
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $AppDirectory 'Workbench.App.dll'))
        Assert-True ($manifest.host.productVersion -ceq $version.ProductVersion -and $manifest.host.sourceCommit -ceq $version.ProductVersion.Split('+')[1]) 'Host stamp did not come from actual DLL.'
        Assert-True ($manifest.host.executableSha256 -eq (Get-FileHash -LiteralPath (Join-Path $AppDirectory 'Workbench.App.exe')).Hash -and $manifest.host.dllSha256 -eq (Get-FileHash -LiteralPath (Join-Path $AppDirectory 'Workbench.App.dll')).Hash) 'Host byte identity changed.'
        Assert-True ($manifest.host.fileVersion -ceq "$($version.ProductVersion.Split('+')[0]).0" -and $manifest.host.assemblyVersion -ceq '1.0.0.0') 'Incoherent host file version or wrong stable assembly identity.'
        $head = (& git -C $repo rev-parse HEAD).Trim()
        Assert-True ($manifest.packagingSourceCommit -ceq $head -and $manifest.packagingSourcesDirty -is [bool]) 'Missing separate packaging identity/dirty state.'
        Assert-True (@($manifest.files).Count -eq 5) 'Inventory must cover five payloads, excluding manifest itself.'
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($file in $manifest.files) {
            Assert-True ($seen.Add($file.path) -and $file.path -cne 'manifest.json' -and $files -ccontains $file.path -and -not [IO.Path]::IsPathRooted($file.path)) 'Inventory has duplicate/non-relative/missing path.'
            $payload = Join-Path $validOutput $file.path
            Assert-True ($file.sha256 -eq (Get-FileHash -LiteralPath $payload).Hash -and $file.bytes -eq (Get-Item -LiteralPath $payload).Length) 'Inventory does not hash actual payload.'
        }
        Assert-True (Test-BusinessPluginDelivery -Directory $validOutput -AppDirectory $AppDirectory) 'Exported delivery verification failed.'
    }
    Check 'exported bytes feed the unchanged production importer and survive a repeated export refusal' {
        & (Join-Path $repo 'scripts/import-business-plugin.ps1') -InputRoot $validOutput -DestinationRoot (Join-Path $temporary 'matrix-input') -Mode Production
        $inputs = @(Get-Content -LiteralPath (Join-Path $temporary 'matrix-input/inputs.json') -Raw | ConvertFrom-Json)
        Assert-True ($inputs.Count -eq 2 -and @($inputs | Where-Object pluginId -EQ 'com.upcam.task-digest').Count -eq 1) 'Exporter did not supply the real production importer.'
        foreach ($input in $inputs) {
            Assert-True ((Get-FileHash -LiteralPath (Join-Path $temporary "matrix-input/$($input.packageFile)")).Hash -eq $input.sha256) 'Matrix input bytes differ from the exported payload.'
        }
        Assert-Refused $source $validOutput
    }
    $callerLocation = Join-Path $temporary 'caller-location'
    $callerHost = Join-Path $callerLocation 'host'
    New-Item $callerHost -ItemType Directory -Force | Out-Null
    foreach ($name in @('Workbench.App.exe', 'Workbench.App.dll')) { Copy-Item -LiteralPath (Join-Path $AppDirectory $name) -Destination $callerHost }
    $callerInputs = New-Inputs 'caller-location/inputs'
    $callerDelivery = Join-Path $callerLocation 'verified'
    Copy-Item -LiteralPath $validOutput -Destination $callerDelivery -Recurse
    Check 'exporter resolves relative app from caller PowerShell filesystem location' {
        At-CallerLocation {
            $output = Join-Path $temporary 'relative-app-output'
            & $export -AppDirectory './host' -InputRoot $source -DestinationRoot $output
            Assert-True (Test-BusinessPluginDelivery -Directory $output -AppDirectory $callerHost) 'Relative app export did not pair with the actual caller host.'
        }
    }
    Check 'exporter resolves relative input from caller PowerShell filesystem location' {
        At-CallerLocation {
            $output = Join-Path $temporary 'relative-input-output'
            & $export -AppDirectory $AppDirectory -InputRoot './inputs' -DestinationRoot $output
            Assert-True (Test-BusinessPluginDelivery -Directory $output -AppDirectory $AppDirectory) 'Relative input export did not preserve verified production bytes.'
        }
    }
    Check 'exporter publishes relative output only under caller PowerShell filesystem location' {
        # Reserve a unique name before testing the old process-CWD behavior, and clean only that test-owned output.
        $relative = "business-delivery-location-$([Guid]::NewGuid().ToString('N'))"
        $processOutput = [IO.Path]::GetFullPath($relative)
        Assert-True (-not (Test-Path -LiteralPath $processOutput)) 'Process-location control must start absent.'
        try {
            At-CallerLocation {
                & $export -AppDirectory $AppDirectory -InputRoot $source -DestinationRoot "./$relative"
                $output = Join-Path $callerLocation $relative
                Assert-True (Test-Path -LiteralPath (Join-Path $output 'manifest.json')) 'Relative output was not published under the caller PowerShell location.'
                Assert-True (-not (Test-Path -LiteralPath $processOutput)) 'Relative output was published under process CWD.'
                Assert-True (Test-BusinessPluginDelivery -Directory $output -AppDirectory $AppDirectory) 'Caller-location output failed public verification.'
            }
        } finally { if (Test-Path -LiteralPath $processOutput) { Remove-Item -LiteralPath $processOutput -Recurse -Force } }
    }
    Check 'verifier resolves relative delivery from caller PowerShell filesystem location' {
        At-CallerLocation {
            Assert-True (Test-BusinessPluginDelivery -Directory './verified' -AppDirectory $AppDirectory) 'Verifier did not read the caller delivery.'
        }
    }
    Check 'verifier resolves relative app from caller PowerShell filesystem location' {
        At-CallerLocation {
            Assert-True (Test-BusinessPluginDelivery -Directory $callerDelivery -AppDirectory './host') 'Verifier did not pair the caller host.'
        }
    }
    foreach ($parameter in @('AppDirectory', 'InputRoot', 'DestinationRoot')) {
        Check "exporter refuses non-filesystem provider for $parameter" {
            $parameters = @{ AppDirectory = $AppDirectory; InputRoot = $source; DestinationRoot = (Join-Path $temporary "provider-$parameter") }
            $parameters[$parameter] = 'Env:WORKBENCH_DELIVERY_TEST_ONLY'
            $refused = $false
            try { & $export @parameters } catch {
                $refused = $true
                Assert-True ($_.Exception.Message.Contains('delivery_path:')) 'Exporter did not refuse the provider at its filesystem boundary.'
            }
            Assert-True $refused 'Exporter accepted a non-filesystem provider path.'
            Assert-True (-not (Test-Path -LiteralPath (Join-Path $temporary "provider-$parameter"))) 'Provider refusal created output.'
        }
    }
    foreach ($parameter in @('Directory', 'AppDirectory')) {
        Check "verifier refuses non-filesystem provider for $parameter" {
            $parameters = @{ Directory = $callerDelivery; AppDirectory = $AppDirectory }
            $parameters[$parameter] = 'Env:WORKBENCH_DELIVERY_TEST_ONLY'
            $refused = $false
            try { Test-BusinessPluginDelivery @parameters | Out-Null } catch {
                $refused = $true
                Assert-True ($_.Exception.Message.Contains('delivery_path:')) 'Verifier did not refuse the provider at its filesystem boundary.'
            }
            Assert-True $refused 'Verifier accepted a non-filesystem provider path.'
        }
    }
    Check 'relative paths from non-filesystem PowerShell location are refused' {
        Push-Location Env:
        try {
            $refused = $false
            try { & $export -AppDirectory './host' -InputRoot $source -DestinationRoot (Join-Path $temporary 'provider-location') } catch {
                $refused = $true
                Assert-True ($_.Exception.Message.Contains('delivery_path:')) 'Relative provider path bypassed the filesystem boundary.'
            }
            Assert-True $refused 'Exporter accepted a relative non-filesystem provider path.'
        } finally { Pop-Location }
    }
    Check 'missing production ZIP is refused' {
        $copy = New-Inputs 'missing'; Remove-Item -LiteralPath (Join-Path $copy 'dingtalk/package.zip')
        Assert-Refused $copy (Join-Path $temporary 'missing-output')
    }
    Check 'tampered production ZIP is refused' {
        $copy = New-Inputs 'tampered'; [IO.File]::AppendAllText((Join-Path $copy 'dingtalk/package.zip'), 'tampered')
        Assert-Refused $copy (Join-Path $temporary 'tampered-output')
    }
    Check 'replaced ZIP plus matching provenance cannot replace repository production pins' {
        $copy = New-Inputs 'replaced'
        $package = Join-Path $copy 'dingtalk/package.zip'; [IO.File]::AppendAllText($package, 'replacement')
        $pinPath = Join-Path $copy 'dingtalk/provenance.json'; $pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
        $pin.sha256 = (Get-FileHash -LiteralPath $package).Hash.ToLowerInvariant(); $pin.packageBytes = (Get-Item -LiteralPath $package).Length
        $pin | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $pinPath
        & (Join-Path $repo 'scripts/import-business-plugin.ps1') -InputRoot $copy -DestinationRoot (Join-Path $temporary 'replaced-import-audit')
        Assert-Refused $copy (Join-Path $temporary 'replaced-output') -Reason 'production_pin'
    }
    Check 'acceptance identity cannot substitute for production' {
        $copy = New-Inputs 'substituted'
        foreach ($file in @('package.zip', 'provenance.json')) { Copy-Item -LiteralPath (Join-Path $source "upcam-acceptance/$file") -Destination (Join-Path $copy "upcam/$file") -Force }
        Assert-Refused $copy (Join-Path $temporary 'substituted-output')
    }
    foreach ($variant in @('missing', 'unknown', 'acceptance', 'extra')) {
        Check "production mode $variant is refused from actual package metadata" {
            $copy = New-Inputs "mode-$variant"; Set-UpcamMode $copy $variant
            Assert-Refused $copy (Join-Path $temporary "mode-$variant-output") -Reason 'production_mode'
        }
    }
    Check 'nonempty destination bytes survive refusal' {
        $destination = Join-Path $temporary 'nonempty'; New-Item $destination -ItemType Directory | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $destination '.existing'), [byte[]]@(0, 255, 27, 12))
        Assert-Refused $source $destination
    }
    Check 'intersecting input destination is refused without changing production bytes' {
        $copy = New-Inputs 'intersection'
        Assert-Refused $copy (Join-Path $copy 'delivery')
        Assert-Refused $copy $temporary
    }
    Check 'destination inside actual app directory is refused' {
        Assert-Refused $source (Join-Path $AppDirectory 'business-delivery')
    }
    Check 'linked destination ancestor is refused without touching target bytes' {
        $target = Join-Path $temporary 'link-target'; New-Item $target -ItemType Directory | Out-Null
        $link = Join-Path $temporary 'link'; [IO.Directory]::CreateSymbolicLink($link, $target) | Out-Null
        Assert-Refused $source (Join-Path $link 'delivery')
        Assert-True (@(Get-ChildItem -LiteralPath $target -Force).Count -eq 0) 'Linked output target changed.'
    }
    Check 'linked production ZIP is refused' {
        $copy = New-Inputs 'linked-input'; $package = Join-Path $copy 'dingtalk/package.zip'; Remove-Item -LiteralPath $package
        [IO.File]::CreateSymbolicLink($package, (Join-Path $source 'dingtalk/package.zip')) | Out-Null
        Assert-Refused $copy (Join-Path $temporary 'linked-input-output')
    }
    Check 'missing/non-PE host is refused' {
        $fake = Join-Path $temporary 'invalid-metadata-only'; New-Item $fake -ItemType Directory | Out-Null
        [IO.File]::WriteAllText((Join-Path $fake 'Workbench.App.exe'), 'metadata-only invalid control')
        [IO.File]::WriteAllText((Join-Path $fake 'Workbench.App.dll'), 'metadata-only invalid control')
        Assert-Refused $source (Join-Path $temporary 'invalid-host-output') -HostPath $fake
    }
    Check 'stale PE-declared host source is rejected by strict CI' {
        $stale = New-MetadataOnlyHost ('0' * 40)
        Assert-Refused $source (Join-Path $temporary 'stale-output') -HostPath $stale -Strict -Reason 'host_source'
    }
    Check 'untracked packaging source cannot yield a clean strict declaration' {
        $head = (& git -C $repo rev-parse HEAD).Trim()
        $metadataOnly = New-MetadataOnlyHost $head
        $untracked = Join-Path $repo "scripts/delivery-test-untracked-$([Guid]::NewGuid().ToString('N')).ps1"
        try {
            [IO.File]::WriteAllText($untracked, '# Test-owned untracked packaging input; never executed.')
            Assert-Refused $source (Join-Path $temporary 'dirty-output') -HostPath $metadataOnly -Strict -Reason 'packager_dirty'
        } finally { if (Test-Path -LiteralPath $untracked) { Remove-Item -LiteralPath $untracked } }
    }
    # Mutations exercise the public verifier used before CI upload, including manifest schema and inventory.
    foreach ($variant in @('extra-output', 'missing-payload', 'changed-readme', 'missing-mode', 'unknown-mode', 'extra-mode', 'missing-inventory', 'case-collision', 'absolute-path',
        'changed-source', 'changed-host-hash', 'schema-type', 'inventory-length-type', 'plugin-value-array', 'unknown-property')) {
        Check "delivery verifier rejects $variant" {
            Assert-True (Test-Path -LiteralPath (Join-Path $validOutput 'manifest.json')) 'Positive export prerequisite absent.'
            $copy = Join-Path $temporary "verify-$variant"; Copy-Item -LiteralPath $validOutput -Destination $copy -Recurse
            $manifestPath = Join-Path $copy 'manifest.json'; $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
            switch ($variant) {
                'extra-output' { [IO.File]::WriteAllText((Join-Path $copy 'acceptance.zip'), 'test-only extra payload') }
                'missing-payload' { Remove-Item -LiteralPath (Join-Path $copy 'dingtalk/provenance.json') }
                'changed-readme' { [IO.File]::AppendAllText((Join-Path $copy 'README.zh-CN.md'), 'tampered') }
                'missing-mode' { $manifest.Remove('mode') | Out-Null }
                'unknown-mode' { $manifest.mode = 'Unknown' }
                'extra-mode' { $manifest.mode = @('Production', 'Acceptance') }
                'missing-inventory' { $manifest.files = @($manifest.files | Where-Object path -CNE 'README.zh-CN.md') }
                'case-collision' { $manifest.files += @{ path = 'DINGTALK/package.zip'; bytes = 179239; sha256 = '38d7a8f30ce6d4d5c6f6e26c757cbb6b9fc9f812546bf1db2ecdbadb9b578200' } }
                'absolute-path' { $manifest.files[0].path = (Join-Path $copy 'README.zh-CN.md') }
                'changed-source' { $manifest.plugins[0].sourceCommit = '0' * 40 }
                'changed-host-hash' { $manifest.host.dllSha256 = '0' * 64 }
                'schema-type' { $manifest.schemaVersion = '1' }
                'inventory-length-type' { $manifest.files[0].bytes = [string]$manifest.files[0].bytes }
                'plugin-value-array' { $manifest.plugins[0].sourceCommit = @($manifest.plugins[0].sourceCommit) }
                'unknown-property' { $manifest['absoluteBuildPath'] = 'C:\test-only\private-source' }
            }
            $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath
            $refused = $false
            try { Test-BusinessPluginDelivery -Directory $copy -AppDirectory $AppDirectory | Out-Null } catch { $refused = $true }
            Assert-True $refused 'Verifier accepted modified output/mode/inventory.'
        }
    }
    Assert-True (@(Get-ChildItem -LiteralPath $temporary -Force -Directory | Where-Object Name -Like '.business-plugins-stage-*').Count -eq 0) 'Export left private staging directories behind.'
    Write-Output "Business plugin delivery regressions: $passed passed, $($failures.Count) failed (PE metadata/hash checks only; no native execution)."
    if ($failures.Count -ne 0) { throw ($failures -join "`n") }
}
finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
