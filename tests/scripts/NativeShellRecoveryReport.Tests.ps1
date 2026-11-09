[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '../../scripts/NativeShellRecoveryReport.psm1') -Force
$root = Join-Path ([IO.Path]::GetTempPath()) ('shell-report-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$commit = '1234567890abcdef1234567890abcdef12345678'
$exe = 'a' * 64; $dll = 'b' * 64
$passed = 0
function Refuse([scriptblock]$Action, [string]$Case) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw "Accepted invalid synthetic fixture: $Case" }
    $script:passed++
}
function Write-Json($Object, [string]$Name) {
    $path = Join-Path $root $Name
    [IO.File]::WriteAllText($path, ($Object | ConvertTo-Json -Depth 12 -Compress), [Text.UTF8Encoding]::new($false))
    return $path
}
function Export-Fixture {
    return [ordered]@{ schemaVersion = 1; product = 'ProjectWorkbench'; reportId = ('a' * 32); createdUtc = '2026-11-09T12:34:56.1234567+00:00'; applicationVersion = '0.4.1.0'; buildCommit = $commit; runtimeVersion = '10.0.0'; operatingSystemVersion = '10.0.26100.0'; processArchitecture = 'X64'; stage = 'WorkspaceOwnership'; errorCode = 'UserStoreInUse'; category = 'Storage'; systemErrorCode = '0x80070020' }
}
try {
    $report = Export-Fixture
    $file = Write-Json $report 'export.json'
    Test-ShellRecoveryExport -Path $file -ExpectedSourceCommit $commit -Stage WorkspaceOwnership -Code UserStoreInUse -Category Storage | Out-Null
    $passed++
    foreach ($mutation in @('extra', 'case', 'nested', 'timestamp', 'offset', 'number', 'architecture', 'source', 'error', 'category', 'version', 'system', 'runtime-nl', 'time-nl', 'reportid-nl', 'system-nl', 'product-array', 'stage-array', 'code-array', 'category-array', 'appversion-array', 'arch-array', 'commit-array')) {
        $r = Export-Fixture
        switch ($mutation) {
            extra { $r['accountPath'] = 'sensitive' }
            case { $r['Product'] = $r['product']; $r.Remove('product') }
            nested { $r['systemErrorCode'] = @{ path = 'private' } }
            timestamp { $r['createdUtc'] = '2026-11-09T12:34:56' }
            offset { $r['createdUtc'] = '2026-11-09T12:34:56+01:00' }
            number { $r['schemaVersion'] = '1' }
            architecture { $r['processArchitecture'] = 'x64' }
            source { $r['buildCommit'] = 'f' * 40 }
            error { $r['errorCode'] = 'anything' }
            category { $r['category'] = 'Unexpected' }
            version { $r['applicationVersion'] = '0.4.0.0' }
            system { $r['systemErrorCode'] = '0x8007002a' }
            'runtime-nl' { $r['runtimeVersion'] += "`n" }; 'time-nl' { $r['createdUtc'] += "`n" }; 'reportid-nl' { $r['reportId'] += "`n" }; 'system-nl' { $r['systemErrorCode'] += "`n" }
            'product-array' { $r['product'] = @($r['product']) }; 'stage-array' { $r['stage'] = @($r['stage']) }; 'code-array' { $r['errorCode'] = @($r['errorCode']) }; 'category-array' { $r['category'] = @($r['category']) }; 'appversion-array' { $r['applicationVersion'] = @($r['applicationVersion']) }; 'arch-array' { $r['processArchitecture'] = @($r['processArchitecture']) }; 'commit-array' { $r['buildCommit'] = @($r['buildCommit']) }
        }
        $f = Write-Json $r 'bad-export.json'
        Refuse { Test-ShellRecoveryExport -Path $f -ExpectedSourceCommit $commit -Stage WorkspaceOwnership -Code UserStoreInUse -Category Storage } $mutation
    }
    [IO.File]::WriteAllText((Join-Path $root 'duplicate.json'), ([IO.File]::ReadAllText($file).Replace('"product":', '"product":"ProjectWorkbench","product":')))
    Refuse { Test-ShellRecoveryExport -Path (Join-Path $root 'duplicate.json') -ExpectedSourceCommit $commit -Stage WorkspaceOwnership -Code UserStoreInUse -Category Storage } 'duplicate'
    [IO.File]::WriteAllText((Join-Path $root 'twisted.json'), ([IO.File]::ReadAllText($file).Replace('"product":', '"Product":"ProjectWorkbench","product":')))
    Refuse { Test-ShellRecoveryExport -Path (Join-Path $root 'twisted.json') -ExpectedSourceCommit $commit -Stage WorkspaceOwnership -Code UserStoreInUse -Category Storage } 'case duplicate'

    # Encoded synthetic fixture is only a validator input, never native evidence.
    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8AAQv8BD/kD/YURmXYAAAAASUVORK5CYII=')
    [IO.File]::WriteAllBytes((Join-Path $root 'frame.png'), $png)
    [IO.File]::WriteAllBytes((Join-Path $root 'light.png'), $png)
    $darkPng = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAAD0lEQVR4nGNgYGA4ceIEAAS3Alm3GNsMAAAAAElFTkSuQmCC')
    [IO.File]::WriteAllBytes((Join-Path $root 'dark.png'), $darkPng)
    $hash = (Get-FileHash -LiteralPath (Join-Path $root 'frame.png') -Algorithm SHA256).Hash.ToLowerInvariant()
    $identity = [ordered]@{ processId = 123; creationFileTime = [long]133444444444444444; windowHandle = [long]5678 }
    $image = [ordered]@{ name = 'frame.png'; sha256 = $hash; bytes = $png.Length; width = 2; height = 1; left = 10; top = 20; right = 12; bottom = 21; processId = 123; creationFileTime = [long]133444444444444444; windowHandle = [long]5678; source = 'PrintWindow2' }
    Test-ShellRecoveryImage -Directory $root -Image $image -Identity $identity | Out-Null; $passed++
    foreach ($case in @('pid', 'birth', 'hwnd', 'hash', 'bytes', 'width', 'bounds', 'source', 'source-array', 'missing', 'blank')) {
        $i = [ordered]@{}; foreach ($key in $image.Keys) { $i[$key] = $image[$key] }
        switch ($case) {
            pid { $i.processId = 124 }; birth { $i.creationFileTime++ }; hwnd { $i.windowHandle++ }
            hash { $i.sha256 = 'f' * 64 }; bytes { $i.bytes++ }; width { $i.width++ }; bounds { $i.right++ }
            source { $i.source = 'desktop' }; 'source-array' { $i.source = @('PrintWindow2') }; missing { $i.name = 'missing.png' }
            blank { $i.name = 'blank.png'; [IO.File]::WriteAllBytes((Join-Path $root 'blank.png'), [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4zwAC/xkACf8B/z7yQswAAAAASUVORK5CYII=')); $i.bytes = (Get-Item (Join-Path $root 'blank.png')).Length; $i.sha256 = (Get-FileHash (Join-Path $root 'blank.png')).Hash.ToLowerInvariant() }
        }
        Refuse { Test-ShellRecoveryImage -Directory $root -Image $i -Identity $identity } $case
    }
    [IO.File]::WriteAllBytes((Join-Path $root 'diagnostic-window.png'), $png)
    $diagnosticImage = [ordered]@{}; foreach ($key in $image.Keys) { $diagnosticImage[$key] = $image[$key] }; $diagnosticImage.name = 'diagnostic-window.png'
    $diagnosticFrame = [ordered]@{ status='captured'; image=$diagnosticImage; failure=$null }
    $safeFailure = [ordered]@{ type='InvalidOperationException'; category='InvalidOperation'; hresult=[int]-2146233079; line=321 }
    $process = [ordered]@{ role = 'healthy'; processId = 123; creationFileTime = [long]133444444444444444; windowHandle = [long]5678; windowClosed = $true; exitCode = 0; rootReacquired = $true; jobEmpty = $true }
    $light = [ordered]@{}; foreach ($key in $image.Keys) { $light[$key] = $image[$key] }; $light.name = 'light.png'
    $dark = [ordered]@{}; foreach ($key in $image.Keys) { $dark[$key] = $image[$key] }; $dark.name = 'dark.png'; $dark.bytes = $darkPng.Length; $dark.sha256 = (Get-FileHash -LiteralPath (Join-Path $root 'dark.png') -Algorithm SHA256).Hash.ToLowerInvariant()
    $catalog = Export-Fixture; $catalog.stage = 'CatalogOpen'; $catalog.errorCode = 'InvalidCatalog'; $catalog.category = 'InvalidData'; $catalog.reportId = 'b' * 32
    [IO.Directory]::CreateDirectory((Join-Path $root 'conflict')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $root 'catalog')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $root ('conflict/Workbench-error-' + $report.reportId + '.json')), ($report | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $root ('catalog/Workbench-error-' + $catalog.reportId + '.json')), ($catalog | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    $reports = @([ordered]@{ relativePath = ('conflict/Workbench-error-' + $report.reportId + '.json'); stage = 'WorkspaceOwnership'; code = 'UserStoreInUse'; category = 'Storage' }, [ordered]@{ relativePath = ('catalog/Workbench-error-' + $catalog.reportId + '.json'); stage = 'CatalogOpen'; code = 'InvalidCatalog'; category = 'InvalidData' })
    $conflictHash = (Get-FileHash -LiteralPath (Join-Path $root $reports[0].relativePath) -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $root 'original-catalog.json'), 'synthetic catalog bytes')
    $catalogHash = (Get-FileHash -LiteralPath (Join-Path $root 'original-catalog.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    $sentinelBytes = [Text.Encoding]::UTF8.GetBytes('ProjectWorkbench synthetic unrelated sentinel v1')
    [IO.File]::WriteAllBytes((Join-Path $root 'unrelated-sentinel-original.bin'), $sentinelBytes)
    [IO.File]::WriteAllBytes((Join-Path $root 'unrelated-sentinel-final.bin'), $sentinelBytes)
    $sentinelHash = (Get-FileHash -LiteralPath (Join-Path $root 'unrelated-sentinel-original.bin') -Algorithm SHA256).Hash.ToLowerInvariant()
    $sentinelPhases = @('before-start','healthy','duplicate-root','cancel','export','collision','first-close','retry-close','corrupt-catalog','corrupt-export','corrupt-recovery','close-pending','final-recovery')
    $sentinelChecks = @($sentinelPhases | ForEach-Object { [ordered]@{ phase=$_; sha256=$sentinelHash } })
    $processes = @($process)
    foreach ($role in @('duplicate-retry','corrupt-retry','close-pending','restored-start')) {
        $n = $processes.Count; $processes += [ordered]@{ role = $role; processId = (123 + $n); creationFileTime = [long](133444444444444444 + $n); windowHandle = [long](5678 + $n); windowClosed = $true; exitCode = 0; rootReacquired = $true; jobEmpty = $true }
    }
    foreach ($i in 0..1) {
        $owner = $processes[$i+1]; $reports[$i].processId = $owner.processId; $reports[$i].creationFileTime = $owner.creationFileTime; $reports[$i].windowHandle = $owner.windowHandle
    }
    $witnesses = @{ cancel=('witness-' + ('c' * 32)); conflict=('witness-' + ('d' * 32)); catalog=('witness-' + ('e' * 32)); pending=('witness-' + ('f' * 32)) }
    foreach ($scope in @('cancel','conflict','catalog','pending')) { [IO.Directory]::CreateDirectory((Join-Path $root ($scope + '/' + $witnesses[$scope]))) | Out-Null }
    $entries = @()
    foreach ($scope in @('cancel','conflict','catalog','pending')) {
        $entries += [ordered]@{ path=$scope; kind='directory'; length=0; sha256=$null }
        $entries += [ordered]@{ path=($scope + '/' + $witnesses[$scope]); kind='directory'; length=0; sha256=$null }
    }
    $catalogExportPath = Join-Path $root $reports[1].relativePath
    $closeEntries = @($entries) + @(
        [ordered]@{ path=$reports[0].relativePath; kind='file'; length=(Get-Item (Join-Path $root $reports[0].relativePath)).Length; sha256=$conflictHash },
        [ordered]@{ path=$reports[1].relativePath; kind='file'; length=(Get-Item $catalogExportPath).Length; sha256=(Get-FileHash $catalogExportPath -Algorithm SHA256).Hash.ToLowerInvariant() }
    )
    $cancelSnapshot = [ordered]@{ schemaVersion=1; entries=$entries }
    $closeSnapshot = [ordered]@{ schemaVersion=1; entries=$closeEntries }
    $cancelBeforeFile = Write-Json $cancelSnapshot 'cancel-before.json'; $cancelAfterFile = Write-Json $cancelSnapshot 'cancel-after.json'
    $closeBeforeFile = Write-Json $closeSnapshot 'close-before.json'; $closeAfterFile = Write-Json $closeSnapshot 'close-after.json'
    $cancelHash = (Get-FileHash $cancelBeforeFile -Algorithm SHA256).Hash.ToLowerInvariant(); $closeHash = (Get-FileHash $closeBeforeFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $cancelNavigation = [ordered]@{ destination='cancel'; witness=$witnesses.cancel; addressSubmitted=$true; witnessVisible=$true; witnessMatches=1; witnessControlType='ListItem'; viewAutomationId='1'; viewControlType='List'; viewName='Items View'; viewProcessId=900; popupProcessId=900; witnessProcessId=900; popupWindowHandle=[long]901 }
    $closeNavigation = [ordered]@{ destination='pending'; witness=$witnesses.pending; addressSubmitted=$true; witnessVisible=$true; witnessMatches=1; witnessControlType='ListItem'; viewAutomationId='1'; viewControlType='List'; viewName='Items View'; viewProcessId=902; popupProcessId=902; witnessProcessId=902; popupWindowHandle=[long]903 }
    $cancelEvidence = [ordered]@{ navigation=$cancelNavigation; beforeFile='cancel-before.json'; afterFile='cancel-after.json'; beforeSha256=$cancelHash; afterSha256=$cancelHash }
    $closeEvidence = [ordered]@{ navigation=$closeNavigation; beforeFile='close-before.json'; afterFile='close-after.json'; beforeSha256=$closeHash; afterSha256=$closeHash }
    $repeatEvidence = [ordered]@{ patternAvailable=$true; attempted=$true; outcome='ElementNotEnabled'; popupRetained=$true }
    $receipt = [ordered]@{ schemaVersion = 1; succeeded = $true; sourceCommit = $commit; executableSha256 = $exe; dllSha256 = $dll; forcedCleanup = $false; processes = $processes; images = @($light,$dark); diagnosticFrame=$diagnosticFrame; reports = $reports; cancelWritten = $false; cancelEvidence=$cancelEvidence; closeEvidence=$closeEvidence; repeatEvidence=$repeatEvidence; collisionBefore = $conflictHash; collisionAfter = $conflictHash; catalogBefore = $catalogHash; catalogAfter = $catalogHash; sentinelBefore = $sentinelHash; sentinelAfter = $sentinelHash; sentinelChecks = $sentinelChecks; pickerPendingAtClose = $true; checks = @('normal','leaseConflict','retry','corruptCatalog','cancel','export','serialization','collision','closePending','light','dark','maximizeRestore','icon') }
    $result = Write-Json $receipt 'result.json'
    Test-ShellRecoveryFinal -Path $result -Directory $root -ExpectedSourceCommit $commit -ExpectedExeSha256 $exe -ExpectedDllSha256 $dll | Out-Null; $passed++
    foreach ($case in @('source', 'exit', 'root', 'close', 'job', 'forced', 'cancel', 'collision', 'checks', 'pending', 'process', 'birth', 'hwnd', 'image', 'missingfinal', 'missingreport', 'reportclassification', 'collision-unbound', 'catalog', 'processmissing', 'duplicaterole', 'duplicateimage', 'finalsource-array', 'finalhash-array', 'collision-array', 'report-process', 'image-role', 'sentinel-missing-field', 'sentinel-changed-hash', 'sentinel-array', 'sentinel-missing-checkpoint', 'sentinel-changed-checkpoint', 'sentinel-duplicate-phase', 'sentinel-reordered', 'navigation-pending', 'navigation-ambiguous', 'navigation-pid', 'navigation-wrong-view', 'navigation-view-name', 'navigation-view-pid', 'navigation-wrong-destination', 'cancel-snapshot-hash', 'close-snapshot-hash', 'repeat-unavailable', 'repeat-skipped', 'repeat-unexpected-success', 'repeat-lost-popup', 'diagnostic-wrong-owner', 'diagnostic-missing-image', 'diagnostic-raw-failure')) {
        $r = $receipt | ConvertTo-Json -Depth 12 | ConvertFrom-Json -AsHashtable
        switch ($case) {
            source { $r.sourceCommit = 'f' * 40 }; exit { $r.processes[0].exitCode = 1 }; root { $r.processes[0].rootReacquired = $false }
            close { $r.processes[0].windowClosed = $false }; job { $r.processes[0].jobEmpty = $false }; forced { $r.forcedCleanup = $true }
            cancel { $r.cancelWritten = $true }; collision { $r.collisionAfter = 'd' * 64 }; checks { $r.checks = @('normal') }
            pending { $r.pickerPendingAtClose = $false }; process { $r.processes[0].processId++ }; birth { $r.processes[0].creationFileTime++ }
            'diagnostic-wrong-owner' { $r.diagnosticFrame.image.processId++ }; 'diagnostic-missing-image' { $r.diagnosticFrame.image = $null }; 'diagnostic-raw-failure' { $r.diagnosticFrame.status='unavailable'; $r.diagnosticFrame.image=$null; $r.diagnosticFrame.failure=@{ type='Other'; category='Other'; hresult=$null; line=$null; rawMessage='private' } }
            'navigation-pending' { $r.cancelEvidence.navigation.addressSubmitted = $true; $r.cancelEvidence.navigation.witnessVisible = $false; $r.cancelEvidence.navigation.witnessMatches = 0 }
            'navigation-ambiguous' { $r.closeEvidence.navigation.witnessMatches = 2 }; 'navigation-pid' { $r.closeEvidence.navigation.witnessProcessId++ }; 'navigation-wrong-view' { $r.closeEvidence.navigation.viewAutomationId = 'other' }; 'navigation-view-name' { $r.closeEvidence.navigation.viewName = 'Address suggestions' }; 'navigation-view-pid' { $r.closeEvidence.navigation.viewProcessId++ }; 'navigation-wrong-destination' { $r.closeEvidence.navigation.destination = 'conflict' }
            'cancel-snapshot-hash' { $r.cancelEvidence.afterSha256 = 'd' * 64 }; 'close-snapshot-hash' { $r.closeEvidence.beforeSha256 = 'd' * 64 }
            'repeat-unavailable' { $r.repeatEvidence.patternAvailable = $false }; 'repeat-skipped' { $r.repeatEvidence.attempted = $false }; 'repeat-unexpected-success' { $r.repeatEvidence.outcome = 'Returned' }; 'repeat-lost-popup' { $r.repeatEvidence.popupRetained = $false }
            'sentinel-missing-field' { $r.Remove('sentinelBefore') }; 'sentinel-changed-hash' { $r.sentinelAfter = 'd' * 64 }; 'sentinel-array' { $r.sentinelBefore = @($r.sentinelBefore) }
            'sentinel-missing-checkpoint' { $r.sentinelChecks = @($r.sentinelChecks | Select-Object -Skip 1) }; 'sentinel-changed-checkpoint' { $r.sentinelChecks[3].sha256 = 'd' * 64 }
            'sentinel-duplicate-phase' { $r.sentinelChecks[1].phase = $r.sentinelChecks[0].phase }; 'sentinel-reordered' { $r.sentinelChecks = @($r.sentinelChecks[1],$r.sentinelChecks[0]) + @($r.sentinelChecks | Select-Object -Skip 2) }
            hwnd { $r.processes[0].windowHandle++ }; image { $r.images = @() }; missingfinal { $r.Remove('succeeded') }; missingreport { $r.reports = @() }; reportclassification { $r.reports[0].code = 'StoreIoFailure' }; 'collision-unbound' { $r.collisionBefore = 'd' * 64; $r.collisionAfter = 'd' * 64 }; catalog { $r.catalogAfter = 'd' * 64 }; processmissing { $r.processes = @($r.processes | Select-Object -First 4) }; duplicaterole { $r.processes[1].role = 'healthy' }; duplicateimage { $r.images[1].sha256 = $r.images[0].sha256 }; 'finalsource-array' { $r.sourceCommit = @($commit) }; 'finalhash-array' { $r.executableSha256 = @($exe) }; 'collision-array' { $r.collisionAfter = @($r.collisionBefore) }; 'report-process' { $r.reports[0].processId = $r.processes[2].processId; $r.reports[0].creationFileTime = $r.processes[2].creationFileTime; $r.reports[0].windowHandle = $r.processes[2].windowHandle }; 'image-role' { $r.images[0].processId = $r.processes[1].processId; $r.images[0].creationFileTime = $r.processes[1].creationFileTime; $r.images[0].windowHandle = $r.processes[1].windowHandle }
        }
        $f = Write-Json $r 'bad-result.json'
        Refuse { Test-ShellRecoveryFinal -Path $f -Directory $root -ExpectedSourceCommit $commit -ExpectedExeSha256 $exe -ExpectedDllSha256 $dll } $case
    }

    # Failure metadata is strictly fixed-shape; synthetic input never claims a native frame.
    $failureDiagnostic = [ordered]@{ schemaVersion=1; succeeded=$false; phase='healthy'; failureCode='NativeGateFailed'; forcedCleanup=$false; diagnostic=[ordered]@{ stage='healthy-titlebar'; failure=$safeFailure; lastWaitFailure=$safeFailure; checkpointWriteFailed=$false; cleanupFailure=$null; initialFrame=$diagnosticFrame } }
    $diagnosticFile = Write-Json $failureDiagnostic 'diagnostic-failure.json'
    Test-ShellRecoveryFailureDiagnostic -Path $diagnosticFile -Directory $root | Out-Null; $passed++
    foreach ($case in @('stage','stage-array','type','category','hresult-string','hresult-range','line-string','line-negative','line-range','raw-message','extra','missing-failure','wait-raw','checkpoint-type','cleanup-raw','frame-status','frame-owner','frame-name','frame-failure')) {
        $bad = $failureDiagnostic | ConvertTo-Json -Depth 12 | ConvertFrom-Json -AsHashtable
        switch ($case) {
            stage { $bad.diagnostic.stage = 'path-or-ui-text' }; 'stage-array' { $bad.diagnostic.stage = @('healthy-titlebar') }; type { $bad.diagnostic.failure.type = 'Private.Namespace' }
            category { $bad.diagnostic.failure.category = 'raw message' }; 'hresult-string' { $bad.diagnostic.failure.hresult = '-1' }; 'hresult-range' { $bad.diagnostic.failure.hresult = [long]2147483648 }
            'line-string' { $bad.diagnostic.failure.line = '321' }; 'line-negative' { $bad.diagnostic.failure.line = -1 }; 'line-range' { $bad.diagnostic.failure.line = 1000001 }
            'raw-message' { $bad.diagnostic.failure.message = 'private' }; extra { $bad.diagnostic.path = 'private' }; 'missing-failure' { $bad.diagnostic.failure = $null }
            'wait-raw' { $bad.diagnostic.lastWaitFailure.category = 'private wait' }; 'checkpoint-type' { $bad.diagnostic.checkpointWriteFailed = 'false' }
            'cleanup-raw' { $bad.diagnostic.cleanupFailure = @{ type='Other'; category='Other'; hresult=$null; line=$null; stack='private' } }
            'frame-status' { $bad.diagnostic.initialFrame.status = 'passed' }; 'frame-owner' { $bad.diagnostic.initialFrame.image.processId = 0 }
            'frame-name' { $bad.diagnostic.initialFrame.image.name = 'light.png' }; 'frame-failure' { $bad.diagnostic.initialFrame.failure = $safeFailure }
        }
        $f=Write-Json $bad 'bad-diagnostic.json'
        Refuse { Test-ShellRecoveryFailureDiagnostic -Path $f -Directory $root } "diagnostic-$case"
    }
    $diagnosticImagePath = Join-Path $root 'diagnostic-window.png'
    $diagnosticImageBytes = [IO.File]::ReadAllBytes($diagnosticImagePath)
    [IO.File]::WriteAllBytes($diagnosticImagePath,[byte[]](1,2,3))
    try { Refuse { Test-ShellRecoveryFailureDiagnostic -Path $diagnosticFile -Directory $root } 'diagnostic-tampered-image' }
    finally { [IO.File]::WriteAllBytes($diagnosticImagePath,$diagnosticImageBytes) }
    $unavailable = $failureDiagnostic | ConvertTo-Json -Depth 12 | ConvertFrom-Json -AsHashtable
    $unavailable.diagnostic.initialFrame = @{ status='unavailable'; image=$null; failure=$safeFailure }
    $f=Write-Json $unavailable 'diagnostic-unavailable.json'; Test-ShellRecoveryFailureDiagnostic -Path $f -Directory $root | Out-Null; $passed++
    $unavailable.diagnostic.initialFrame = @{ status='not-attempted'; image=$null; failure=$null }
    $f=Write-Json $unavailable 'diagnostic-unattempted.json'; Test-ShellRecoveryFailureDiagnostic -Path $f -Directory $root | Out-Null; $passed++

    # Preflight classification reads only synthetic fixture metadata, never a real profile.
    $preflightRoot = Join-Path $root 'owned-preflight-profile'
    if ($null -ne (Get-ShellRecoveryPreflightFailure -EnvironmentSafe $true -NormalRoot $preflightRoot)) { throw 'Fresh preflight root was refused.' }; $passed++
    [IO.Directory]::CreateDirectory($preflightRoot) | Out-Null
    $preflightMarker = Join-Path $preflightRoot 'untouched.bin'; $preflightBytes = [byte[]](1,0,255,9); [IO.File]::WriteAllBytes($preflightMarker,$preflightBytes)
    $preflightFailure = Get-ShellRecoveryPreflightFailure -EnvironmentSafe $true -NormalRoot $preflightRoot
    if ($preflightFailure -cne 'NormalRootExists') { throw 'Existing profile did not refuse.' }; $passed++
    if ((Get-ShellRecoveryPreflightFailure -EnvironmentSafe $false -NormalRoot $preflightRoot) -cne 'UnsafeEnvironment') { throw 'Unsafe environment did not refuse.' }; $passed++
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($preflightMarker)) -cne [Convert]::ToBase64String($preflightBytes)) { throw 'Preflight changed synthetic profile.' }; $passed++
    $failure = [ordered]@{ schemaVersion=1; succeeded=$false; hostSource=$commit; driverSource=$null; producerRunId=[long]12345; artifactId=[long]67890; archiveSha256=('e' * 64); executableSha256=$null; dllSha256=$null; phase='profile-preflight'; failureCode=$preflightFailure; forcedCleanup=$null; pickerCleanup='unverified'; unverified=@('blocking-file','acl-denial','drag','snap','high-dpi','high-contrast','taskbar','windows-11','visual-review') }
    Test-ShellRecoveryFailureOutcome -Outcome $failure; $passed++
    $failureFile = Write-Json $failure 'preflight-failure.json'
    $failureText = [IO.File]::ReadAllText($failureFile)
    if ($failureText.Contains($preflightRoot)) { throw 'Preflight receipt exposed the synthetic profile path.' }
    Test-ShellRecoveryFailureOutcome -Outcome ($failureText | ConvertFrom-Json -AsHashtable); $passed++
    $environmentFailure = $failure | ConvertTo-Json -Depth 6 | ConvertFrom-Json -AsHashtable
    $environmentFailure.phase = 'environment'; $environmentFailure.failureCode = 'UnsafeEnvironment'
    Test-ShellRecoveryFailureOutcome -Outcome $environmentFailure; $passed++
    $inputFailure = $failure | ConvertTo-Json -Depth 6 | ConvertFrom-Json -AsHashtable
    $inputFailure.phase = 'input-validation'; $inputFailure.failureCode = 'InputInvalid'
    Test-ShellRecoveryFailureOutcome -Outcome $inputFailure; $passed++
    $profileParent = Join-Path $root 'profile-parent'; [IO.Directory]::CreateDirectory($profileParent) | Out-Null
    $profileLink = Join-Path $root 'profile-parent-link'; [IO.Directory]::CreateSymbolicLink($profileLink,$profileParent) | Out-Null
    if ((Get-ShellRecoveryPreflightFailure -EnvironmentSafe $true -NormalRoot (Join-Path $profileLink 'new-profile')) -cne 'UnsafeProfilePath') { throw 'Linked profile ancestor did not refuse.' }; $passed++
    foreach ($case in @('extra-path','succeeded','wrong-code','wrong-phase','wrong-phase-case','wrong-source','wrong-id','raw-error','bad-cleanup')) {
        $bad = $failure | ConvertTo-Json -Depth 6 | ConvertFrom-Json -AsHashtable
        switch ($case) {
            'extra-path' { $bad.path = 'private' }; succeeded { $bad.succeeded = $true }; 'wrong-code' { $bad.failureCode = 'Anything' }; 'wrong-phase' { $bad.phase = 'environment' }; 'wrong-phase-case' { $bad.phase = 'PROFILE-PREFLIGHT' }
            'wrong-source' { $bad.hostSource += "`n" }; 'wrong-id' { $bad.producerRunId = '12345' }; 'raw-error' { $bad.failureCode = 'private exception message' }; 'bad-cleanup' { $bad.pickerCleanup = 'passed' }
        }
        Refuse { Test-ShellRecoveryFailureOutcome -Outcome $bad } "failure-$case"
    }
    foreach ($name in @('cancel-before.json','cancel-after.json','close-before.json','close-after.json')) {
        $path = Join-Path $root $name; $original = [IO.File]::ReadAllBytes($path)
        [IO.File]::WriteAllText($path,'changed owned output snapshot')
        try { Refuse { Test-ShellRecoveryFinal -Path $result -Directory $root -ExpectedSourceCommit $commit -ExpectedExeSha256 $exe -ExpectedDllSha256 $dll } "snapshot-file-$name" }
        finally { [IO.File]::WriteAllBytes($path,$original) }
    }

    # Even internally consistent replacement hashes cannot legitimize unknown staging output or a missing original report.
    foreach ($case in @('cancel-extra-stage','close-missing-report','missing-witness')) {
        $beforeName = if ($case -ceq 'cancel-extra-stage') { 'cancel-before.json' } else { 'close-before.json' }
        $afterName = if ($case -ceq 'cancel-extra-stage') { 'cancel-after.json' } else { 'close-after.json' }
        $beforePath = Join-Path $root $beforeName; $afterPath = Join-Path $root $afterName
        $beforeBytes = [IO.File]::ReadAllBytes($beforePath); $afterBytes = [IO.File]::ReadAllBytes($afterPath)
        $snapshot = [Text.Encoding]::UTF8.GetString($beforeBytes) | ConvertFrom-Json -AsHashtable
        switch ($case) {
            'cancel-extra-stage' { $snapshot.entries += @{ path=('cancel/.Workbench-error-' + ('9' * 32) + '.tmp'); kind='file'; length=1; sha256=('9' * 64) } }
            'close-missing-report' { $snapshot.entries = @($snapshot.entries | Where-Object { $_.path -cne $reports[0].relativePath }) }
            'missing-witness' { $snapshot.entries = @($snapshot.entries | Where-Object { $_.path -cne ('pending/' + $witnesses.pending) }) }
        }
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($snapshot | ConvertTo-Json -Depth 12 -Compress))
        [IO.File]::WriteAllBytes($beforePath,$bytes); [IO.File]::WriteAllBytes($afterPath,$bytes)
        $hash = (Get-FileHash $beforePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $changed = $receipt | ConvertTo-Json -Depth 12 | ConvertFrom-Json -AsHashtable
        $check = if ($case -ceq 'cancel-extra-stage') { $changed.cancelEvidence } else { $changed.closeEvidence }
        $check.beforeSha256=$hash; $check.afterSha256=$hash
        $changedFile=Write-Json $changed 'altered-snapshot-result.json'
        try { Refuse { Test-ShellRecoveryFinal -Path $changedFile -Directory $root -ExpectedSourceCommit $commit -ExpectedExeSha256 $exe -ExpectedDllSha256 $dll } $case }
        finally { [IO.File]::WriteAllBytes($beforePath,$beforeBytes); [IO.File]::WriteAllBytes($afterPath,$afterBytes) }
    }

    # Exercise actual evidence files, including same-hash-field tampering and missing inputs.
    foreach ($name in @('unrelated-sentinel-original.bin','unrelated-sentinel-final.bin','original-catalog.json',('conflict/Workbench-error-' + $report.reportId + '.json'))) {
        $path = Join-Path $root $name; $original = [IO.File]::ReadAllBytes($path); $away = $path + '.held'
        Move-Item -LiteralPath $path -Destination $away
        try { Refuse { Test-ShellRecoveryFinal -Path $result -Directory $root -ExpectedSourceCommit $commit -ExpectedExeSha256 $exe -ExpectedDllSha256 $dll } ("missing-file-$name") }
        finally { Move-Item -LiteralPath $away -Destination $path }
        [IO.File]::WriteAllText($path, 'changed owned fixture bytes')
        try { Refuse { Test-ShellRecoveryFinal -Path $result -Directory $root -ExpectedSourceCommit $commit -ExpectedExeSha256 $exe -ExpectedDllSha256 $dll } ("changed-file-$name") }
        finally { [IO.File]::WriteAllBytes($path, $original) }
    }
    $sentinelOriginal = Join-Path $root 'unrelated-sentinel-original.bin'; $sentinelFinal = Join-Path $root 'unrelated-sentinel-final.bin'
    [IO.File]::WriteAllText($sentinelOriginal, 'different but matching sentinel')
    [IO.File]::WriteAllText($sentinelFinal, 'different but matching sentinel')
    $alteredHash = (Get-FileHash -LiteralPath $sentinelOriginal -Algorithm SHA256).Hash.ToLowerInvariant()
    $altered = $receipt | ConvertTo-Json -Depth 12 | ConvertFrom-Json -AsHashtable
    $altered.sentinelBefore = $alteredHash; $altered.sentinelAfter = $alteredHash
    foreach ($check in $altered.sentinelChecks) { $check.sha256 = $alteredHash }
    $alteredFile = Write-Json $altered 'altered-sentinel-result.json'
    try { Refuse { Test-ShellRecoveryFinal -Path $alteredFile -Directory $root -ExpectedSourceCommit $commit -ExpectedExeSha256 $exe -ExpectedDllSha256 $dll } 'changed-original-and-evidence' }
    finally { [IO.File]::WriteAllBytes($sentinelOriginal,$sentinelBytes); [IO.File]::WriteAllBytes($sentinelFinal,$sentinelBytes) }

    # Synthetic API records exercise source/run/artifact binding only; no download is performed.
    $driverCommit = 'd' * 40; $archiveSha = 'e' * 64
    $run = @{ id = 12345; status = 'completed'; run_attempt = 1; conclusion = 'success'; event = 'workflow_dispatch'; workflow_id = 409147502; head_sha = $commit; repository = @{ id = 42; full_name = 'ThaoKeeton/project-workbench' } }
    $artifact = @{ id = 67890; name = 'workbench-win-x64'; digest = ('sha256:' + $archiveSha); expired = $false; size_in_bytes = 100; expires_at = '2031-11-09T12:00:00Z'; workflow_run = @{ id = 12345; head_sha = $commit; repository_id = 42; head_repository_id = 42 } }
    Test-ShellRecoveryProducer -Run $run -Artifact $artifact -RunId '12345' -ArtifactId '67890' -HostSource $commit -DriverSource $driverCommit -ArchiveSha256 $archiveSha | Out-Null; $passed++
    $sameSource = Test-ShellRecoveryProducer -Run $run -Artifact $artifact -RunId '12345' -ArtifactId '67890' -HostSource $commit -DriverSource $commit -ArchiveSha256 $archiveSha
    if ($sameSource.hostSource -cne $commit -or $sameSource.driverSource -cne $commit) { throw 'Matching verified sources were not retained independently.' }; $passed++
    Test-ShellRecoveryDriverSource -ActualSource $commit -ExpectedSource $commit; $passed++
    foreach ($case in @('different','actual-newline','expected-newline','actual-array','expected-array','uppercase')) {
        $actual = $commit; $expected = $commit
        switch ($case) {
            different { $expected = 'f' * 40 }; 'actual-newline' { $actual += "`n" }; 'expected-newline' { $expected += "`n" }
            'actual-array' { $actual = @($actual) }; 'expected-array' { $expected = @($expected) }; uppercase { $actual = $actual.ToUpperInvariant() }
        }
        Refuse { Test-ShellRecoveryDriverSource -ActualSource $actual -ExpectedSource $expected } "driver-$case"
    }
    foreach ($case in @('runid','artifactid','runstatus','failure','event','workflow','runhead','repository','artifactname','artifactexpired','artifactsize','artifactsourcerepo','artifactrun','artifacthead','expiry','sourceformat','driverformat','archiveformat','workflowtype','nestedidtype','headarray','repoarray','attempttype','attemptzero','digest','digesttype')) {
        $r = $run | ConvertTo-Json -Depth 8 | ConvertFrom-Json -AsHashtable
        $a = $artifact | ConvertTo-Json -Depth 8 | ConvertFrom-Json -AsHashtable -DateKind String
        $rid = '12345'; $aid = '67890'; $h1 = $commit; $h2 = $driverCommit; $sha = $archiveSha
        switch ($case) {
            runid { $rid = "12345`n" }; artifactid { $aid = '0' }; runstatus { $r.status = 'in_progress' }; failure { $r.conclusion = 'failure' }
            event { $r.event = 'pull_request' }; workflow { $r.workflow_id++ }; runhead { $r.head_sha = 'f' * 40 }; repository { $r.repository.full_name = 'someone/else' }
            artifactname { $a.name = 'another-artifact' }; artifactexpired { $a.expired = $true }; artifactsize { $a.size_in_bytes = 0 }; artifactsourcerepo { $a.workflow_run.repository_id++ }
            artifactrun { $a.workflow_run.id++ }; artifacthead { $a.workflow_run.head_sha = 'f' * 40 }; expiry { $a.expires_at = '2020-01-01T00:00:00Z' }
            sourceformat { $h1 += "`n" }; driverformat { $h2 = $h2.ToUpperInvariant() }; archiveformat { $sha += "`n" }
            workflowtype { $r.workflow_id = '409147502' }; nestedidtype { $a.workflow_run.id = '12345' }; headarray { $r.head_sha = @($commit) }; repoarray { $r.repository.full_name = @('ThaoKeeton/project-workbench') }; attempttype { $r.run_attempt = '1' }; attemptzero { $r.run_attempt = 0 }; digest { $a.digest = 'sha256:' + ('f' * 64) }; digesttype { $a.digest = @('sha256:' + $archiveSha) }
        }
        Refuse { Test-ShellRecoveryProducer -Run $r -Artifact $a -RunId $rid -ArtifactId $aid -HostSource $h1 -DriverSource $h2 -ArchiveSha256 $sha } "producer-$case"
    }
    Test-ShellRecoveryHostVersion -ProductVersion "0.4.1+$commit" -FileVersion '0.4.1.0' -AssemblyVersion '1.0.0.0' -HostSource $commit | Out-Null; $passed++
    foreach ($case in @('old','file','assembly','source','newline')) {
        $product = "0.4.1+$commit"; $fileVersion = '0.4.1.0'; $assembly = '1.0.0.0'; $source = $commit
        switch ($case) { old { $product = "0.4.0+$commit" }; file { $fileVersion = '0.4.0.0' }; assembly { $assembly = '2.0.0.0' }; source { $source = 'f' * 40 }; newline { $source += "`n"; $product = "0.4.1+$source" } }
        Refuse { Test-ShellRecoveryHostVersion -ProductVersion $product -FileVersion $fileVersion -AssemblyVersion $assembly -HostSource $source } "host-$case"
    }
    function New-Archive([string]$Path,[object[]]$Entries) {
        $zip = [IO.Compression.ZipFile]::Open($Path,[IO.Compression.ZipArchiveMode]::Create)
        try { foreach ($entry in $Entries) { $z = $zip.CreateEntry($entry.name); if ($entry.Contains('attributes')) { $z.ExternalAttributes = [int]$entry.attributes }; $stream = $z.Open(); try { $bytes = [Text.Encoding]::UTF8.GetBytes('synthetic archive only'); $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() } } } finally { $zip.Dispose() }
    }
    $validEntries = @(@{ name = 'Workbench.App.exe' },@{ name = 'Workbench.App.dll' },@{ name = 'Workbench.App.pri' },@{ name = 'Assets/Workbench.ico' },@{ name = 'Assets/Workbench.svg' })
    $archive = Join-Path $root 'fixture.zip'; New-Archive $archive $validEntries
    $zipHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $extract = Join-Path $root 'extracted'
    Expand-ShellRecoveryArchive -Archive $archive -Destination $extract -ExpectedSha256 $zipHash | Out-Null
    if (-not [IO.File]::Exists((Join-Path $extract 'Assets/Workbench.ico'))) { throw 'Safe extraction omitted input.' }; $passed++
    Refuse { Expand-ShellRecoveryArchive -Archive $archive -Destination $extract -ExpectedSha256 $zipHash } 'archive-collision'
    Refuse { Expand-ShellRecoveryArchive -Archive $archive -Destination (Join-Path $root 'wrong-hash') -ExpectedSha256 ('f' * 64) } 'archive-hash'
    # Corrupt a central-directory CRC while keeping a matching outer archive SHA.
    $crcArchive = Join-Path $root 'bad-crc.zip'; [IO.File]::Copy($archive,$crcArchive)
    $raw = [IO.File]::ReadAllBytes($crcArchive); $central = -1
    for ($i=0; $i -le $raw.Length-20; $i++) { if ($raw[$i] -eq 0x50 -and $raw[$i+1] -eq 0x4b -and $raw[$i+2] -eq 1 -and $raw[$i+3] -eq 2) { $central=$i; break } }
    if ($central -lt 0) { throw 'Synthetic ZIP central directory missing.' }
    $raw[$central+16] = $raw[$central+16] -bxor 0xff; [IO.File]::WriteAllBytes($crcArchive,$raw)
    $crcHash=(Get-FileHash -LiteralPath $crcArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Refuse { Expand-ShellRecoveryArchive -Archive $crcArchive -Destination (Join-Path $root 'crc-partial') -ExpectedSha256 $crcHash } 'archive-crc'
    $missingArchive=Join-Path $root 'missing-asset.zip'; New-Archive $missingArchive @($validEntries | Where-Object { $_.name -cne 'Workbench.App.pri' })
    $missingHash=(Get-FileHash -LiteralPath $missingArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Refuse { Expand-ShellRecoveryArchive -Archive $missingArchive -Destination (Join-Path $root 'missing-asset') -ExpectedSha256 $missingHash } 'archive-missing-asset'
    foreach ($case in @('traversal','absolute','drive','backslash','dot','empty','duplicate','case-alias','parent-file','link','reparse','reserved')) {
        $bad = Join-Path $root ("bad-$case.zip"); $entries = @($validEntries) + @(@{ name = 'okay.txt' })
        switch ($case) {
            traversal { $entries += @{ name = '../escape.txt' } }; absolute { $entries += @{ name = '/absolute.txt' } }; drive { $entries += @{ name = 'C:/escape.txt' } }
            backslash { $entries += @{ name = 'folder\escape.txt' } }; dot { $entries += @{ name = 'folder/./escape.txt' } }; empty { $entries += @{ name = 'folder//escape.txt' } }
            duplicate { $entries += @{ name = 'okay.txt' } }; 'case-alias' { $entries += @{ name = 'OKAY.TXT' } }; 'parent-file' { $entries += @{ name = 'okay.txt/child' } }
            link { $entries += @{ name = 'link'; attributes = -1577123840 } }; reparse { $entries += @{ name = 'link'; attributes = 1024 } }; reserved { $entries += @{ name = 'folder/NUL.txt' } }
        }
        New-Archive $bad $entries
        $badHash = (Get-FileHash -LiteralPath $bad -Algorithm SHA256).Hash.ToLowerInvariant(); $dest = Join-Path $root ("refused-$case")
        Refuse { Expand-ShellRecoveryArchive -Archive $bad -Destination $dest -ExpectedSha256 $badHash } "archive-$case"
        if (Test-Path -LiteralPath $dest) { throw 'Unsafe archive created partial output before validation.' }
    }
    Write-Host "Shell recovery report validators: $passed synthetic input cases passed; no native execution claimed."
} finally { Remove-Item -LiteralPath $root -Recurse -Force }
