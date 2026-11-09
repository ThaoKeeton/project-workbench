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
    $processes = @($process)
    foreach ($role in @('duplicate-retry','corrupt-retry','close-pending','restored-start')) {
        $n = $processes.Count; $processes += [ordered]@{ role = $role; processId = (123 + $n); creationFileTime = [long](133444444444444444 + $n); windowHandle = [long](5678 + $n); windowClosed = $true; exitCode = 0; rootReacquired = $true; jobEmpty = $true }
    }
    foreach ($i in 0..1) {
        $owner = $processes[$i+1]; $reports[$i].processId = $owner.processId; $reports[$i].creationFileTime = $owner.creationFileTime; $reports[$i].windowHandle = $owner.windowHandle
    }
    $receipt = [ordered]@{ schemaVersion = 1; succeeded = $true; sourceCommit = $commit; executableSha256 = $exe; dllSha256 = $dll; forcedCleanup = $false; processes = $processes; images = @($light,$dark); reports = $reports; cancelWritten = $false; collisionBefore = $conflictHash; collisionAfter = $conflictHash; catalogBefore = $catalogHash; catalogAfter = $catalogHash; pickerPendingAtClose = $true; checks = @('normal','leaseConflict','retry','corruptCatalog','cancel','export','serialization','collision','closePending','light','dark','maximizeRestore','icon') }
    $result = Write-Json $receipt 'result.json'
    Test-ShellRecoveryFinal -Path $result -Directory $root -ExpectedSourceCommit $commit -ExpectedExeSha256 $exe -ExpectedDllSha256 $dll | Out-Null; $passed++
    foreach ($case in @('source', 'exit', 'root', 'close', 'job', 'forced', 'cancel', 'collision', 'checks', 'pending', 'process', 'birth', 'hwnd', 'image', 'missingfinal', 'missingreport', 'reportclassification', 'collision-unbound', 'catalog', 'processmissing', 'duplicaterole', 'duplicateimage', 'finalsource-array', 'finalhash-array', 'collision-array', 'report-process', 'image-role')) {
        $r = $receipt | ConvertTo-Json -Depth 12 | ConvertFrom-Json -AsHashtable
        switch ($case) {
            source { $r.sourceCommit = 'f' * 40 }; exit { $r.processes[0].exitCode = 1 }; root { $r.processes[0].rootReacquired = $false }
            close { $r.processes[0].windowClosed = $false }; job { $r.processes[0].jobEmpty = $false }; forced { $r.forcedCleanup = $true }
            cancel { $r.cancelWritten = $true }; collision { $r.collisionAfter = 'd' * 64 }; checks { $r.checks = @('normal') }
            pending { $r.pickerPendingAtClose = $false }; process { $r.processes[0].processId++ }; birth { $r.processes[0].creationFileTime++ }
            hwnd { $r.processes[0].windowHandle++ }; image { $r.images = @() }; missingfinal { $r.Remove('succeeded') }; missingreport { $r.reports = @() }; reportclassification { $r.reports[0].code = 'StoreIoFailure' }; 'collision-unbound' { $r.collisionBefore = 'd' * 64; $r.collisionAfter = 'd' * 64 }; catalog { $r.catalogAfter = 'd' * 64 }; processmissing { $r.processes = @($r.processes | Select-Object -First 4) }; duplicaterole { $r.processes[1].role = 'healthy' }; duplicateimage { $r.images[1].sha256 = $r.images[0].sha256 }; 'finalsource-array' { $r.sourceCommit = @($commit) }; 'finalhash-array' { $r.executableSha256 = @($exe) }; 'collision-array' { $r.collisionAfter = @($r.collisionBefore) }; 'report-process' { $r.reports[0].processId = $r.processes[2].processId; $r.reports[0].creationFileTime = $r.processes[2].creationFileTime; $r.reports[0].windowHandle = $r.processes[2].windowHandle }; 'image-role' { $r.images[0].processId = $r.processes[1].processId; $r.images[0].creationFileTime = $r.processes[1].creationFileTime; $r.images[0].windowHandle = $r.processes[1].windowHandle }
        }
        $f = Write-Json $r 'bad-result.json'
        Refuse { Test-ShellRecoveryFinal -Path $f -Directory $root -ExpectedSourceCommit $commit -ExpectedExeSha256 $exe -ExpectedDllSha256 $dll } $case
    }

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
