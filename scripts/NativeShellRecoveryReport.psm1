Set-StrictMode -Version Latest
# Reuse the bounded CRC/inflate/pixel validator for GDI's RGB/RGBA8 PNG output.
Import-Module (Join-Path $PSScriptRoot 'NativeGalleryReport.psm1') -ErrorAction Stop

function Read-ShellRecoveryJson([string]$Path, [int]$Limit = 65536) {
    try {
        $item = Get-Item -LiteralPath $Path -Force
        if ($item.PSIsContainer -or $item.LinkType -or $item.Length -lt 2 -or $item.Length -gt $Limit) { throw 'invalid' }
        $bytes = [IO.File]::ReadAllBytes($item.FullName)
        $document = [Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($bytes))
        try {
            Assert-ShellJsonElement $document.RootElement 0
        } finally { $document.Dispose() }
        $text = [Text.UTF8Encoding]::new($false,$true).GetString($bytes)
        return ConvertFrom-Json -InputObject $text -AsHashtable -DateKind String -NoEnumerate
    } catch { throw 'ShellRecoveryJsonInvalid' }
}
function Assert-ShellJsonElement([Text.Json.JsonElement]$Value, [int]$Depth) {
    if ($Depth -gt 5) { throw 'invalid' }
    if ($Value.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($property in $Value.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw 'invalid' }
            Assert-ShellJsonElement $property.Value ($Depth + 1)
        }
    } elseif ($Value.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        if ($Value.GetArrayLength() -gt 32) { throw 'invalid' }
        foreach ($item in $Value.EnumerateArray()) { Assert-ShellJsonElement $item ($Depth + 1) }
    }
}
function Assert-ShellKeys($Object, [string[]]$Names) {
    if ($Object -isnot [Collections.IDictionary] -or $Object.Count -ne $Names.Count) { throw 'ShellRecoveryShapeInvalid' }
    foreach ($name in $Names) {
        if (@($Object.Keys | Where-Object { $_ -ceq $name }).Count -ne 1) { throw 'ShellRecoveryShapeInvalid' }
    }
}
function Test-ShellInt($Value, [long]$Min, [long]$Max) {
    return (($Value -is [int] -or $Value -is [long]) -and $Value -ge $Min -and $Value -le $Max)
}
function Test-ShellHash($Value) { return ($Value -is [string] -and $Value -cmatch '\A[0-9a-f]{64}\z') }
# Only attributes are inspected; a preexisting normal root is never opened or enumerated.
function Get-ShellRecoveryPreflightFailure {
    [CmdletBinding()]
    param([Parameter(Mandatory)][bool]$EnvironmentSafe, [Parameter(Mandatory)][string]$NormalRoot)
    if (-not $EnvironmentSafe) { return 'UnsafeEnvironment' }
    $root = [IO.Path]::GetFullPath($NormalRoot)
    try { $null = [IO.File]::GetAttributes($root); return 'NormalRootExists' }
    catch [IO.FileNotFoundException] { }
    catch [IO.DirectoryNotFoundException] { }
    for ($part = [IO.Path]::GetDirectoryName($root); $part; $part = [IO.Path]::GetDirectoryName($part)) {
        try { $attributes = [IO.File]::GetAttributes($part) }
        catch [IO.FileNotFoundException] { continue }
        catch [IO.DirectoryNotFoundException] { continue }
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return 'UnsafeProfilePath' }
    }
    return $null
}
function Test-ShellRecoveryFailureOutcome {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Outcome)
    Assert-ShellKeys $Outcome @('schemaVersion','succeeded','hostSource','driverSource','producerRunId','artifactId','archiveSha256','executableSha256','dllSha256','phase','failureCode','forcedCleanup','pickerCleanup','unverified')
    $phases = @{ environment=@('UnsafeEnvironment'); 'profile-preflight'=@('NormalRootExists','UnsafeProfilePath','ProfileCheckFailed'); 'input-validation'=@('InputInvalid') }
    if (-not (Test-ShellInt $Outcome.schemaVersion 1 1) -or $Outcome.succeeded -isnot [bool] -or $Outcome.succeeded -or
        $Outcome.phase -isnot [string] -or $Outcome.phase -cnotin @('environment','profile-preflight','input-validation') -or -not $phases.ContainsKey($Outcome.phase) -or
        $Outcome.failureCode -isnot [string] -or $Outcome.failureCode -cnotin $phases[$Outcome.phase] -or
        $null -ne $Outcome.forcedCleanup -or $Outcome.pickerCleanup -isnot [string] -or $Outcome.pickerCleanup -cne 'unverified') { throw 'ShellRecoveryFailureOutcomeInvalid' }
    foreach ($key in @('hostSource','driverSource')) { if ($null -ne $Outcome[$key] -and ($Outcome[$key] -isnot [string] -or $Outcome[$key] -cnotmatch '\A[0-9a-f]{40}\z')) { throw 'ShellRecoveryFailureOutcomeInvalid' } }
    foreach ($key in @('archiveSha256','executableSha256','dllSha256')) { if ($null -ne $Outcome[$key] -and -not (Test-ShellHash $Outcome[$key])) { throw 'ShellRecoveryFailureOutcomeInvalid' } }
    foreach ($key in @('producerRunId','artifactId')) { if ($null -ne $Outcome[$key] -and -not (Test-ShellInt $Outcome[$key] 1 ([long]::MaxValue))) { throw 'ShellRecoveryFailureOutcomeInvalid' } }
    $unverified = @('blocking-file','acl-denial','drag','snap','high-dpi','high-contrast','taskbar','windows-11','visual-review')
    if ($Outcome.unverified -isnot [array] -or $Outcome.unverified.Count -ne $unverified.Count -or @(Compare-Object $unverified $Outcome.unverified -CaseSensitive).Count -ne 0) { throw 'ShellRecoveryFailureOutcomeInvalid' }
}
function Test-ShellRecoveryNoWriteEvidence($Evidence, [string]$Phase, [string]$Directory, $Reports) {
    Assert-ShellKeys $Evidence @('navigation','beforeFile','afterFile','beforeSha256','afterSha256')
    $expectedDestination = if ($Phase -ceq 'cancel') { 'cancel' } else { 'pending' }
    $navigation = $Evidence.navigation
    Assert-ShellKeys $navigation @('destination','witness','addressSubmitted','witnessVisible','witnessMatches','witnessControlType','viewAutomationId','viewControlType','viewName','viewProcessId','popupProcessId','witnessProcessId','popupWindowHandle')
    if ($navigation.destination -isnot [string] -or $navigation.destination -cne $expectedDestination -or
        $navigation.witness -isnot [string] -or $navigation.witness -cnotmatch '\Awitness-[0-9a-f]{32}\z' -or
        $navigation.addressSubmitted -isnot [bool] -or -not $navigation.addressSubmitted -or
        $navigation.witnessVisible -isnot [bool] -or -not $navigation.witnessVisible -or
        -not (Test-ShellInt $navigation.witnessMatches 1 1) -or $navigation.witnessControlType -isnot [string] -or $navigation.witnessControlType -cne 'ListItem' -or
        $navigation.viewAutomationId -isnot [string] -or $navigation.viewAutomationId -cne '1' -or $navigation.viewControlType -isnot [string] -or $navigation.viewControlType -cne 'List' -or
        $navigation.viewName -isnot [string] -or $navigation.viewName -cnotin @('Items View','项目视图','文件夹视图') -or
        -not (Test-ShellInt $navigation.popupProcessId 1 ([int]::MaxValue)) -or -not (Test-ShellInt $navigation.viewProcessId 1 ([int]::MaxValue)) -or -not (Test-ShellInt $navigation.witnessProcessId 1 ([int]::MaxValue)) -or
        $navigation.popupProcessId -ne $navigation.viewProcessId -or $navigation.popupProcessId -ne $navigation.witnessProcessId -or -not (Test-ShellInt $navigation.popupWindowHandle 1 ([long]::MaxValue))) { throw 'ShellRecoveryNavigationInvalid' }
    $beforeName = if ($Phase -ceq 'cancel') { 'cancel-before.json' } else { 'close-before.json' }
    $afterName = if ($Phase -ceq 'cancel') { 'cancel-after.json' } else { 'close-after.json' }
    if ($Evidence.beforeFile -isnot [string] -or $Evidence.beforeFile -cne $beforeName -or $Evidence.afterFile -isnot [string] -or $Evidence.afterFile -cne $afterName -or
        -not (Test-ShellHash $Evidence.beforeSha256) -or -not (Test-ShellHash $Evidence.afterSha256) -or $Evidence.beforeSha256 -cne $Evidence.afterSha256) { throw 'ShellRecoverySnapshotInvalid' }
    foreach ($fileName in @($beforeName,$afterName)) {
        $filePath = Join-Path $Directory $fileName
        Assert-ShellRecoveryNoLinks $filePath
        if ((Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Evidence.beforeSha256) { throw 'ShellRecoverySnapshotInvalid' }
    }
    $snapshot = Read-ShellRecoveryJson (Join-Path $Directory $beforeName) 32768
    Assert-ShellKeys $snapshot @('schemaVersion','entries')
    if (-not (Test-ShellInt $snapshot.schemaVersion 1 1) -or $snapshot.entries -isnot [array] -or $snapshot.entries.Count -gt 16) { throw 'ShellRecoverySnapshotInvalid' }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $scopes = @('cancel','conflict','catalog','pending'); $witnesses = @{}; $files = @()
    foreach ($entry in $snapshot.entries) {
        Assert-ShellKeys $entry @('path','kind','length','sha256')
        if ($entry.path -isnot [string] -or -not $paths.Add($entry.path) -or $entry.kind -isnot [string] -or
            -not (Test-ShellInt $entry.length 0 1048576)) { throw 'ShellRecoverySnapshotInvalid' }
        if ($entry.path -cin $scopes) {
            if ($entry.kind -cne 'directory' -or $entry.length -ne 0 -or $null -ne $entry.sha256) { throw 'ShellRecoverySnapshotInvalid' }
        } elseif ($entry.path -cmatch '\A(cancel|conflict|catalog|pending)/(witness-[0-9a-f]{32})\z') {
            $scope = $Matches[1]; $witness = $Matches[2]
            if ($entry.kind -cne 'directory' -or $entry.length -ne 0 -or $null -ne $entry.sha256 -or $witnesses.ContainsKey($scope) -or $witness -cin $witnesses.Values) { throw 'ShellRecoverySnapshotInvalid' }
            $witnesses[$scope] = $witness
            $witnessPath = Join-Path $Directory $entry.path; Assert-ShellRecoveryNoLinks $witnessPath
            $witnessDir = Get-Item -LiteralPath $witnessPath -Force
            if (-not $witnessDir.PSIsContainer -or @(Get-ChildItem -LiteralPath $witnessPath -Force).Count -ne 0) { throw 'ShellRecoverySnapshotInvalid' }
        } elseif ($entry.path -cmatch '\A(conflict|catalog)/Workbench-error-[0-9a-f]{32}\.json\z') {
            if ($entry.kind -cne 'file' -or $entry.length -lt 1 -or -not (Test-ShellHash $entry.sha256)) { throw 'ShellRecoverySnapshotInvalid' }
            $files += $entry
        } else { throw 'ShellRecoverySnapshotInvalid' }
    }
    foreach ($scope in $scopes) { if (-not $paths.Contains($scope) -or -not $witnesses.ContainsKey($scope)) { throw 'ShellRecoverySnapshotInvalid' } }
    if ($witnesses[$expectedDestination] -cne $navigation.witness) { throw 'ShellRecoveryNavigationInvalid' }
    if ($Phase -ceq 'cancel') { if ($files.Count -ne 0) { throw 'ShellRecoverySnapshotInvalid' } }
    else {
        if ($files.Count -ne 2) { throw 'ShellRecoverySnapshotInvalid' }
        foreach ($report in $Reports) {
            $record = @($files | Where-Object { $_.path -ceq $report.relativePath })
            $filePath = Join-Path $Directory $report.relativePath
            if ($record.Count -ne 1 -or $record[0].length -ne (Get-Item -LiteralPath $filePath).Length -or $record[0].sha256 -cne (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'ShellRecoverySnapshotInvalid' }
        }
    }
}
function Test-ShellRecoveryExport {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$ExpectedSourceCommit,
        [Parameter(Mandatory)][string]$Stage, [Parameter(Mandatory)][string]$Code, [Parameter(Mandatory)][string]$Category)
    $r = Read-ShellRecoveryJson $Path 8192
    Assert-ShellKeys $r @('schemaVersion','product','reportId','createdUtc','applicationVersion','buildCommit','runtimeVersion','operatingSystemVersion','processArchitecture','stage','errorCode','category','systemErrorCode')
    if (-not (Test-ShellInt $r.schemaVersion 1 1) -or ($r.product -isnot [string] -or $r.product -cne 'ProjectWorkbench') -or
        $r.reportId -isnot [string] -or $r.reportId -cnotmatch '\A[0-9a-f]{32}\z' -or
        $ExpectedSourceCommit -cnotmatch '\A[0-9a-f]{40}\z' -or ($r.buildCommit -isnot [string] -or $r.buildCommit -cne $ExpectedSourceCommit) -or
        ($r.applicationVersion -isnot [string] -or $r.applicationVersion -cne '0.4.1.0') -or ($r.processArchitecture -isnot [string] -or $r.processArchitecture -cne 'X64')) { throw 'ShellRecoveryExportIdentityInvalid' }
    foreach ($field in @('runtimeVersion','operatingSystemVersion')) {
        if ($r[$field] -isnot [string] -or $r[$field] -cnotmatch '\A(0|[1-9][0-9]{0,4})(\.(0|[1-9][0-9]{0,4})){1,3}\z') { throw 'ShellRecoveryExportVersionInvalid' }
    }
    $when = [DateTimeOffset]::MinValue
    if ($r.createdUtc -isnot [string] -or $r.createdUtc -cnotmatch '\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)\z' -or
        -not [DateTimeOffset]::TryParse($r.createdUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$when) -or $when.Offset -ne [TimeSpan]::Zero) { throw 'ShellRecoveryExportTimeInvalid' }
    $permitted = @{
        WorkspaceOwnership = @{ UserStoreInUse = 'Storage' }
        CatalogOpen = @{ InvalidCatalog = 'InvalidData'; RecoveryRequired = 'InvalidData' }
    }
    if (-not $permitted.ContainsKey($Stage) -or -not $permitted[$Stage].ContainsKey($Code) -or $permitted[$Stage][$Code] -cne $Category -or
        ($r.stage -isnot [string] -or $r.stage -cne $Stage) -or ($r.errorCode -isnot [string] -or $r.errorCode -cne $Code) -or ($r.category -isnot [string] -or $r.category -cne $Category) -or
        ($null -ne $r.systemErrorCode -and ($r.systemErrorCode -isnot [string] -or $r.systemErrorCode -cnotmatch '\A0x[0-9A-F]{8}\z'))) { throw 'ShellRecoveryExportClassificationInvalid' }
    return $r
}
function Test-ShellRecoveryImage {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)]$Image, [Parameter(Mandatory)]$Identity)
    Assert-ShellKeys $Image @('name','sha256','bytes','width','height','left','top','right','bottom','processId','creationFileTime','windowHandle','source')
    foreach ($key in @('processId','creationFileTime','windowHandle')) {
        if (-not (Test-ShellInt $Image[$key] 1 ([long]::MaxValue)) -or $Image[$key] -cne $Identity[$key]) { throw 'ShellRecoveryImageIdentityInvalid' }
    }
    if ($Image.name -isnot [string] -or $Image.name -cnotmatch '\A[a-z0-9-]+\.png\z' -or ($Image.source -isnot [string] -or $Image.source -cne 'PrintWindow2') -or
        -not (Test-ShellHash $Image.sha256) -or -not (Test-ShellInt $Image.bytes 1 16777216) -or
        -not (Test-ShellInt $Image.width 1 4096) -or -not (Test-ShellInt $Image.height 1 4096)) { throw 'ShellRecoveryImageInvalid' }
    foreach ($key in @('left','top','right','bottom')) { if (-not (Test-ShellInt $Image[$key] -32768 32768)) { throw 'ShellRecoveryImageBoundsInvalid' } }
    if (($Image.right - $Image.left) -ne $Image.width -or ($Image.bottom - $Image.top) -ne $Image.height) { throw 'ShellRecoveryImageBoundsInvalid' }
    $path = Join-Path $Directory $Image.name
    try {
        $file = Get-Item -LiteralPath $path -Force
        if ($file.PSIsContainer -or $file.LinkType -or $file.Length -ne $Image.bytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Image.sha256 -or
            -not [NativeGalleryPngV1]::Valid([IO.File]::ReadAllBytes($path), $Image.width, $Image.height)) { throw 'invalid' }
    } catch { throw 'ShellRecoveryImageInvalid' }
}
# Diagnostic data is deliberately bounded: no exception messages, UI names, paths or stacks.
function Assert-ShellRecoverySafeFailure($Failure) {
    Assert-ShellKeys $Failure @('type','category','hresult','line')
    $types = @('RuntimeException','MethodInvocationException','PropertyNotFoundException','ItemNotFoundException','InvalidOperationException','ArgumentException','IOException','FileNotFoundException','DirectoryNotFoundException','UnauthorizedAccessException','COMException','Win32Exception','NullReferenceException','TimeoutException','ObjectDisposedException','ElementNotAvailableException','ElementNotEnabledException','Other')
    $categories = @('AssertionFailed','ControlUnavailable','WaitExpired','PatternUnavailable','PickerNavigationUnconfirmed','UnsafeEnvironment','ElementUnavailable','ElementDisabled','NativeError','IO','AccessDenied','InvalidOperation','Other')
    if ($Failure.type -isnot [string] -or $Failure.type -cnotin $types -or $Failure.category -isnot [string] -or $Failure.category -cnotin $categories -or
        ($null -ne $Failure.hresult -and -not (Test-ShellInt $Failure.hresult ([int]::MinValue) ([int]::MaxValue))) -or
        ($null -ne $Failure.line -and -not (Test-ShellInt $Failure.line 1 1000000))) { throw 'ShellRecoverySafeFailureInvalid' }
}
function Test-ShellRecoveryDiagnosticFrame($Frame) {
    Assert-ShellKeys $Frame @('status','image','failure')
    if ($Frame.status -isnot [string] -or $Frame.status -cne 'not-attempted' -or
        $null -ne $Frame.image -or $null -ne $Frame.failure) { throw 'ShellRecoveryDiagnosticFrameInvalid' }
}
function Assert-ShellRecoveryIconRegression($Legacy) {
    Assert-ShellKeys $Legacy @('legacyWidth','legacyHeight','legacyOutOfRange')
    if (-not (Test-ShellInt $Legacy.legacyWidth 1 256) -or -not (Test-ShellInt $Legacy.legacyHeight 1 256) -or
        $Legacy.legacyOutOfRange -isnot [bool] -or $Legacy.legacyOutOfRange -ne ($Legacy.legacyWidth -lt 32 -or $Legacy.legacyHeight -lt 32)) { throw 'ShellRecoveryIconRegressionInvalid' }
}
function Test-ShellRecoveryFailureDiagnostic {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$Directory)
    $r = Read-ShellRecoveryJson $Path
    Assert-ShellKeys $r @('schemaVersion','succeeded','phase','failureCode','forcedCleanup','diagnostic')
    $phases = @('preflight','healthy','lease-conflict','corrupt-catalog','close-pending-picker','cleanup')
    $stages = @('preflight','healthy','healthy-start','healthy-window-wait','healthy-window-identity','healthy-uia-root','healthy-titlebar','healthy-readiness','healthy-catalog','healthy-project-selector','healthy-icon-regression','healthy-icon','healthy-maximize','healthy-restore','healthy-resize-small','healthy-resize-large','healthy-theme-light','healthy-capture-light','healthy-theme-dark','healthy-capture-dark','healthy-complete','lease-conflict','corrupt-catalog','close-pending-picker','cleanup','completed')
    if (-not (Test-ShellInt $r.schemaVersion 1 1) -or $r.succeeded -isnot [bool] -or $r.succeeded -or $r.phase -isnot [string] -or $r.phase -cnotin $phases -or
        $r.failureCode -isnot [string] -or $r.failureCode -cne 'NativeGateFailed' -or $r.forcedCleanup -isnot [bool]) { throw 'ShellRecoveryFailureDiagnosticInvalid' }
    $d = $r.diagnostic
    $diagnosticKeys = @('stage','failure','lastWaitFailure','checkpointWriteFailed','cleanupFailure','initialFrame')
    if ($d -is [Collections.IDictionary] -and $d.Contains('icon')) { $diagnosticKeys += 'icon' }
    if ($d -is [Collections.IDictionary] -and $d.Contains('iconRegression')) { $diagnosticKeys += 'iconRegression' }
    Assert-ShellKeys $d $diagnosticKeys
    if ($d.stage -isnot [string] -or $d.stage -cnotin $stages -or $d.checkpointWriteFailed -isnot [bool]) { throw 'ShellRecoveryFailureDiagnosticInvalid' }
    Assert-ShellRecoverySafeFailure $d.failure
    if ($null -ne $d.lastWaitFailure) { Assert-ShellRecoverySafeFailure $d.lastWaitFailure }
    if ($null -ne $d.cleanupFailure) { Assert-ShellRecoverySafeFailure $d.cleanupFailure }
    Test-ShellRecoveryDiagnosticFrame $d.initialFrame
    if ($d.Contains('icon') -and $null -ne $d.icon) {
        $icon = $d.icon
        $iconKeys = @('subject','stage','actualWidth','actualHeight','referenceWidth','referenceHeight')
        if ($icon -is [Collections.IDictionary] -and $icon.Contains('difference')) { $iconKeys += 'difference' }
        if ($icon -is [Collections.IDictionary] -and $icon.Contains('pngSwitch')) { $iconKeys += 'pngSwitch' }
        Assert-ShellKeys $icon $iconKeys
        if ($icon.subject -isnot [string] -or $icon.subject -cnotin @('embedded','window','regression16','regression32','regressionMismatch','regressionMissing') -or
            $icon.stage -isnot [string] -or $icon.stage -cnotin @('start','handle','load','legacy','bitmap','directory','reference','pixels','complete')) { throw 'ShellRecoveryIconDiagnosticInvalid' }
        foreach ($name in @('actualWidth','actualHeight','referenceWidth','referenceHeight')) {
            if (-not (Test-ShellInt $icon[$name] 0 ([int]::MaxValue))) { throw 'ShellRecoveryIconDiagnosticInvalid' }
        }
        if ($icon.Contains('pngSwitch') -and ($icon.pngSwitch -isnot [string] -or $icon.pngSwitch -cnotin @('true','false','undefined','unavailable'))) { throw 'ShellRecoveryIconDiagnosticInvalid' }
        if ($icon.Contains('difference')) {
            $diff = $icon.difference
            Assert-ShellKeys $diff @('alphaOnly','transparentRgbOnly','rgbOnly','alphaAndRgb','maxAlphaDelta','maxRgbDelta')
            foreach ($name in @('alphaOnly','transparentRgbOnly','rgbOnly','alphaAndRgb')) {
                if (-not (Test-ShellInt $diff[$name] 0 65536)) { throw 'ShellRecoveryIconDiagnosticInvalid' }
            }
            foreach ($name in @('maxAlphaDelta','maxRgbDelta')) {
                if (-not (Test-ShellInt $diff[$name] 0 255)) { throw 'ShellRecoveryIconDiagnosticInvalid' }
            }
            $total = $diff.alphaOnly + $diff.transparentRgbOnly + $diff.rgbOnly + $diff.alphaAndRgb
            $alphaChanges = $diff.alphaOnly + $diff.alphaAndRgb
            $rgbChanges = $diff.transparentRgbOnly + $diff.rgbOnly + $diff.alphaAndRgb
            $validContext = ($d.stage -ceq 'healthy-icon-regression' -and $icon.subject -cin @('regression16','regression32','regressionMismatch','regressionMissing')) -or
                            ($d.stage -ceq 'healthy-icon' -and $icon.subject -cin @('embedded','window'))
            $regressionSizes = @{ regression16=16; regression32=32; regressionMismatch=16; regressionMissing=17 }
            if ($regressionSizes.ContainsKey($icon.subject) -and
                ($icon.actualWidth -ne $regressionSizes[$icon.subject] -or $icon.actualHeight -ne $regressionSizes[$icon.subject])) { throw 'ShellRecoveryIconDiagnosticInvalid' }
            if (-not $icon.Contains('pngSwitch') -or -not $validContext -or $icon.stage -cne 'pixels' -or
                -not (Test-ShellInt $icon.actualWidth 1 256) -or -not (Test-ShellInt $icon.actualHeight 1 256) -or
                $icon.referenceWidth -ne $icon.actualWidth -or $icon.referenceHeight -ne $icon.actualHeight -or
                $total -lt 1 -or $total -gt ($icon.actualWidth * $icon.actualHeight) -or
                (($diff.maxAlphaDelta -eq 0) -ne ($alphaChanges -eq 0)) -or
                (($diff.maxRgbDelta -eq 0) -ne ($rgbChanges -eq 0))) { throw 'ShellRecoveryIconDiagnosticInvalid' }
        }
    }
    if ($d.Contains('iconRegression') -and $null -ne $d.iconRegression) { Assert-ShellRecoveryIconRegression $d.iconRegression }
    return $r
}
function Test-ShellRecoveryFinal {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$ExpectedSourceCommit, [Parameter(Mandatory)][string]$ExpectedExeSha256, [Parameter(Mandatory)][string]$ExpectedDllSha256)
    $r = Read-ShellRecoveryJson $Path
    Assert-ShellKeys $r @('schemaVersion','succeeded','sourceCommit','executableSha256','dllSha256','forcedCleanup','processes','images','diagnosticFrame','iconRegression','reports','cancelWritten','cancelEvidence','closeEvidence','repeatEvidence','collisionBefore','collisionAfter','catalogBefore','catalogAfter','sentinelBefore','sentinelAfter','sentinelChecks','pickerPendingAtClose','checks')
    Assert-ShellRecoveryIconRegression $r.iconRegression
    if (-not (Test-ShellInt $r.schemaVersion 1 1) -or $r.succeeded -isnot [bool] -or -not $r.succeeded -or
        ($r.sourceCommit -isnot [string] -or $r.sourceCommit -cne $ExpectedSourceCommit) -or $ExpectedSourceCommit -cnotmatch '\A[0-9a-f]{40}\z' -or
        -not (Test-ShellHash $ExpectedExeSha256) -or -not (Test-ShellHash $r.executableSha256) -or $r.executableSha256 -cne $ExpectedExeSha256 -or
        -not (Test-ShellHash $ExpectedDllSha256) -or -not (Test-ShellHash $r.dllSha256) -or $r.dllSha256 -cne $ExpectedDllSha256 -or
        $r.forcedCleanup -isnot [bool] -or $r.forcedCleanup -or $r.cancelWritten -isnot [bool] -or $r.cancelWritten -or
        $r.pickerPendingAtClose -isnot [bool] -or -not $r.pickerPendingAtClose -or
        -not (Test-ShellHash $r.collisionBefore) -or -not (Test-ShellHash $r.collisionAfter) -or $r.collisionAfter -cne $r.collisionBefore -or
        -not (Test-ShellHash $r.catalogBefore) -or -not (Test-ShellHash $r.catalogAfter) -or $r.catalogAfter -cne $r.catalogBefore -or
        -not (Test-ShellHash $r.sentinelBefore) -or -not (Test-ShellHash $r.sentinelAfter) -or $r.sentinelAfter -cne $r.sentinelBefore) { throw 'ShellRecoveryFinalInvalid' }
    $expectedSentinel = [Text.Encoding]::UTF8.GetBytes('ProjectWorkbench synthetic unrelated sentinel v1')
    $sentinelBase64 = [Convert]::ToBase64String($expectedSentinel)
    foreach ($name in @('unrelated-sentinel-original.bin','unrelated-sentinel-final.bin')) {
        $path = Join-Path $Directory $name
        Assert-ShellRecoveryNoLinks $path
        $item = Get-Item -LiteralPath $path -Force
        if ($item.PSIsContainer -or $item.Length -ne $expectedSentinel.Length -or
            [Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -cne $sentinelBase64 -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $r.sentinelBefore) { throw 'ShellRecoverySentinelInvalid' }
    }
    $phases = @('before-start','healthy','duplicate-root','cancel','export','collision','first-close','retry-close','corrupt-catalog','corrupt-export','corrupt-recovery','close-pending','final-recovery')
    if ($r.sentinelChecks -isnot [array] -or $r.sentinelChecks.Count -ne $phases.Count) { throw 'ShellRecoverySentinelInvalid' }
    for ($i = 0; $i -lt $phases.Count; $i++) {
        $check = $r.sentinelChecks[$i]
        Assert-ShellKeys $check @('phase','sha256')
        if ($check.phase -isnot [string] -or $check.phase -cne $phases[$i] -or
            -not (Test-ShellHash $check.sha256) -or $check.sha256 -cne $r.sentinelBefore) { throw 'ShellRecoverySentinelInvalid' }
    }
    $required = @('normal','leaseConflict','retry','corruptCatalog','cancel','export','serialization','collision','closePending','light','dark','maximizeRestore','icon')
    if ($r.checks -isnot [array] -or $r.checks.Count -ne $required.Count -or @(Compare-Object $required $r.checks -CaseSensitive).Count -ne 0 -or
        $r.processes -isnot [array] -or $r.processes.Count -ne 5 -or $r.images -isnot [array] -or $r.images.Count -ne 2 -or $r.reports -isnot [array] -or $r.reports.Count -ne 2) { throw 'ShellRecoveryChecksInvalid' }
    $identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $roles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($p in $r.processes) {
        Assert-ShellKeys $p @('role','processId','creationFileTime','windowHandle','windowClosed','exitCode','rootReacquired','jobEmpty')
        if ($p.role -isnot [string] -or $p.role -cnotin @('healthy','duplicate-retry','corrupt-retry','close-pending','restored-start') -or -not $roles.Add($p.role)) { throw 'ShellRecoveryProcessInvalid' }
        foreach ($key in @('processId','creationFileTime','windowHandle')) { if (-not (Test-ShellInt $p[$key] 1 ([long]::MaxValue))) { throw 'ShellRecoveryProcessInvalid' } }
        foreach ($key in @('windowClosed','rootReacquired','jobEmpty')) { if ($p[$key] -isnot [bool] -or -not $p[$key]) { throw 'ShellRecoveryRetirementInvalid' } }
        if (-not (Test-ShellInt $p.exitCode 0 0) -or -not $identities.Add("$($p.processId):$($p.creationFileTime):$($p.windowHandle)")) { throw 'ShellRecoveryRetirementInvalid' }
    }
    $healthy = @($r.processes | Where-Object { $_.role -ceq 'healthy' })
    if ($healthy.Count -ne 1) { throw 'ShellRecoveryProcessInvalid' }
    Test-ShellRecoveryDiagnosticFrame $r.diagnosticFrame
    $catalogPath = Join-Path $Directory 'original-catalog.json'
    Assert-ShellRecoveryNoLinks $catalogPath
    $catalogFile = Get-Item -LiteralPath $catalogPath -Force
    if ($catalogFile.PSIsContainer -or $catalogFile.Length -lt 1 -or $catalogFile.Length -gt 1048576 -or (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $r.catalogBefore) { throw 'ShellRecoveryCatalogInvalid' }
    if ($r.images[0].sha256 -ceq $r.images[1].sha256) { throw 'ShellRecoveryImageSetInvalid' }
    if (@(Compare-Object @('light.png','dark.png') @($r.images | ForEach-Object name) -CaseSensitive).Count -ne 0) { throw 'ShellRecoveryImageSetInvalid' }
    $kinds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($report in $r.reports) {
        Assert-ShellKeys $report @('relativePath','stage','code','category','processId','creationFileTime','windowHandle')
        if ($report.relativePath -isnot [string] -or $report.relativePath -cnotmatch '\A(conflict|catalog)/Workbench-error-([0-9a-f]{32})\.json\z') { throw 'ShellRecoveryReportPathInvalid' }
        $kind = $Matches[1]; $id = $Matches[2]
        foreach ($key in @('processId','creationFileTime','windowHandle')) { if (-not (Test-ShellInt $report[$key] 1 ([long]::MaxValue))) { throw 'ShellRecoveryReportOwnerInvalid' } }
        $expectedRole = if ($kind -ceq 'conflict') { 'duplicate-retry' } else { 'corrupt-retry' }
        $owner = @($r.processes | Where-Object { $_.role -ceq $expectedRole -and $_.processId -eq $report.processId -and $_.creationFileTime -eq $report.creationFileTime -and $_.windowHandle -eq $report.windowHandle })
        if ($owner.Count -ne 1) { throw 'ShellRecoveryReportOwnerInvalid' }
        if (-not $kinds.Add($kind)) { throw 'ShellRecoveryReportSetInvalid' }
        $folder = Get-Item -LiteralPath (Join-Path $Directory $kind) -Force
        if (-not $folder.PSIsContainer -or $folder.LinkType) { throw 'ShellRecoveryReportPathInvalid' }
        if (($kind -ceq 'conflict' -and ($report.stage -cne 'WorkspaceOwnership' -or $report.code -cne 'UserStoreInUse' -or $report.category -cne 'Storage')) -or
            ($kind -ceq 'catalog' -and ($report.stage -cne 'CatalogOpen' -or $report.code -cnotin @('InvalidCatalog','RecoveryRequired') -or $report.category -cne 'InvalidData'))) { throw 'ShellRecoveryReportClassificationInvalid' }
        $export = Test-ShellRecoveryExport -Path (Join-Path $Directory $report.relativePath) -ExpectedSourceCommit $ExpectedSourceCommit -Stage $report.stage -Code $report.code -Category $report.category
        if ($export.reportId -cne $id) { throw 'ShellRecoveryReportNameInvalid' }
        if ($kind -ceq 'conflict' -and (Get-FileHash -LiteralPath (Join-Path $Directory $report.relativePath) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $r.collisionBefore) { throw 'ShellRecoveryCollisionInvalid' }
    }
    foreach ($image in $r.images) {
        $matches = @($r.processes | Where-Object { $_.processId -eq $image.processId -and $_.creationFileTime -eq $image.creationFileTime -and $_.windowHandle -eq $image.windowHandle })
        if ($matches.Count -ne 1 -or $matches[0].role -cne 'healthy') { throw 'ShellRecoveryImageIdentityInvalid' }
        Test-ShellRecoveryImage -Directory $Directory -Image $image -Identity $matches[0]
    }
    Test-ShellRecoveryNoWriteEvidence $r.cancelEvidence 'cancel' $Directory $r.reports
    Test-ShellRecoveryNoWriteEvidence $r.closeEvidence 'close' $Directory $r.reports
    Assert-ShellKeys $r.repeatEvidence @('patternAvailable','attempted','outcome','popupRetained')
    if ($r.repeatEvidence.patternAvailable -isnot [bool] -or -not $r.repeatEvidence.patternAvailable -or
        $r.repeatEvidence.attempted -isnot [bool] -or -not $r.repeatEvidence.attempted -or
        $r.repeatEvidence.outcome -isnot [string] -or $r.repeatEvidence.outcome -cne 'ElementNotEnabled' -or
        $r.repeatEvidence.popupRetained -isnot [bool] -or -not $r.repeatEvidence.popupRetained) { throw 'ShellRecoveryRepeatInvalid' }
    return $r
}

# Bind the driver's actual checkout to the workflow source independently of H1.
function Test-ShellRecoveryDriverSource {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$ActualSource, [Parameter(Mandatory)]$ExpectedSource)
    if ($ActualSource -isnot [string] -or $ExpectedSource -isnot [string] -or
        $ActualSource -cnotmatch '\A[0-9a-f]{40}\z' -or $ExpectedSource -cnotmatch '\A[0-9a-f]{40}\z' -or
        $ActualSource -cne $ExpectedSource) { throw 'DriverSourceInvalid' }
}

# The API response comes only from the fixed repository. This pure check is also
# exercised against synthetic mutations before any artifact is downloaded.
function Test-ShellRecoveryProducer {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Run, [Parameter(Mandatory)]$Artifact,
        [Parameter(Mandatory)][string]$RunId, [Parameter(Mandatory)][string]$ArtifactId,
        [Parameter(Mandatory)][string]$HostSource, [Parameter(Mandatory)][string]$DriverSource,
        [Parameter(Mandatory)][string]$ArchiveSha256)
    if ($RunId -cnotmatch '\A[1-9][0-9]{0,17}\z' -or $ArtifactId -cnotmatch '\A[1-9][0-9]{0,17}\z' -or
        $HostSource -cnotmatch '\A[0-9a-f]{40}\z' -or $DriverSource -cnotmatch '\A[0-9a-f]{40}\z' -or
        -not (Test-ShellHash $ArchiveSha256)) { throw 'ShellProducerInputInvalid' }
    if ($Run -isnot [Collections.IDictionary] -or $Artifact -isnot [Collections.IDictionary] -or
        $Run.repository -isnot [Collections.IDictionary] -or $Artifact.workflow_run -isnot [Collections.IDictionary]) { throw 'ShellProducerMetadataInvalid' }
    if (-not (Test-ShellInt $Run.id 1 ([long]::MaxValue)) -or $Run.id -ne [long]$RunId -or
        ($Run.status -isnot [string] -or $Run.status -cne 'completed') -or ($Run.conclusion -isnot [string] -or $Run.conclusion -cne 'success') -or ($Run.event -isnot [string] -or $Run.event -cne 'workflow_dispatch') -or
        -not (Test-ShellInt $Run.workflow_id 409147502 409147502) -or -not (Test-ShellInt $Run.run_attempt 1 100) -or ($Run.head_sha -isnot [string] -or $Run.head_sha -cne $HostSource) -or
        ($Run.repository.full_name -isnot [string] -or $Run.repository.full_name -cne 'ThaoKeeton/project-workbench') -or -not (Test-ShellInt $Run.repository.id 1 ([long]::MaxValue))) { throw 'ShellProducerRunInvalid' }
    if (-not (Test-ShellInt $Artifact.id 1 ([long]::MaxValue)) -or $Artifact.id -ne [long]$ArtifactId -or
        ($Artifact.name -isnot [string] -or $Artifact.name -cne 'workbench-win-x64') -or
        ($Artifact.digest -isnot [string] -or $Artifact.digest -cne "sha256:$ArchiveSha256") -or $Artifact.expired -isnot [bool] -or $Artifact.expired -or
        -not (Test-ShellInt $Artifact.size_in_bytes 1 1073741824) -or
        -not (Test-ShellInt $Artifact.workflow_run.id 1 ([long]::MaxValue)) -or $Artifact.workflow_run.id -ne [long]$RunId -or
        ($Artifact.workflow_run.head_sha -isnot [string] -or $Artifact.workflow_run.head_sha -cne $HostSource) -or
        -not (Test-ShellInt $Artifact.workflow_run.repository_id 1 ([long]::MaxValue)) -or $Artifact.workflow_run.repository_id -ne $Run.repository.id -or
        -not (Test-ShellInt $Artifact.workflow_run.head_repository_id 1 ([long]::MaxValue)) -or $Artifact.workflow_run.head_repository_id -ne $Run.repository.id) { throw 'ShellProducerArtifactInvalid' }
    $expires = [DateTimeOffset]::MinValue
    if ($Artifact.expires_at -isnot [string] -or $Artifact.expires_at -cnotmatch '\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)\z' -or
        -not [DateTimeOffset]::TryParse($Artifact.expires_at,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$expires) -or
        $expires.Offset -ne [TimeSpan]::Zero -or $expires -le [DateTimeOffset]::UtcNow) { throw 'ShellProducerExpiryInvalid' }
    return @{ hostSource=$HostSource; driverSource=$DriverSource; producerRunId=[long]$RunId; artifactId=[long]$ArtifactId;
        runAttempt=[int]$Run.run_attempt; repositoryId=[long]$Run.repository.id; artifactName='workbench-win-x64'; artifactExpiresUtc=$Artifact.expires_at; artifactApiBytes=[long]$Artifact.size_in_bytes; artifactApiDigest=$Artifact.digest; archiveSha256=$ArchiveSha256 }
}
function Test-ShellRecoveryHostVersion {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProductVersion, [Parameter(Mandatory)][string]$FileVersion,
          [Parameter(Mandatory)][string]$AssemblyVersion, [Parameter(Mandatory)][string]$HostSource)
    if ($HostSource -cnotmatch '\A[0-9a-f]{40}\z' -or $ProductVersion -cne "0.4.1+$HostSource" -or
        $FileVersion -cne '0.4.1.0' -or $AssemblyVersion -cne '1.0.0.0') { throw 'ShellRecoveryHostIdentityInvalid' }
}
if (-not ('ShellRecoveryZipCopy' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class ShellRecoveryZipCopy {
    public static void Copy(Stream source, Stream destination, long length, uint expected) {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++) { uint c=n; for(int j=0;j<8;j++) c=(c&1)!=0 ? 0xedb88320u^(c>>1) : c>>1; table[n]=c; }
        uint crc=0xffffffffu; long total=0; var buffer=new byte[65536]; int count;
        while((count=source.Read(buffer,0,buffer.Length))>0) {
            total=checked(total+count); if(total>length) throw new InvalidDataException();
            for(int i=0;i<count;i++) crc=table[(crc^buffer[i])&255]^(crc>>8);
            destination.Write(buffer,0,count);
        }
        if(total!=length || ~crc!=expected) throw new InvalidDataException();
    }
}
'@
}
function Assert-ShellRecoveryNoLinks([string]$Path) {
    for ($part=[IO.Path]::GetFullPath($Path); $part; $part=[IO.Path]::GetDirectoryName($part)) {
        $item = Get-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'ShellRecoveryLinkRefused' }
    }
}
function Expand-ShellRecoveryArchive {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Archive,[Parameter(Mandatory)][string]$Destination,[Parameter(Mandatory)][string]$ExpectedSha256)
    if (-not (Test-ShellHash $ExpectedSha256)) { throw 'ShellRecoveryArchiveIdentityInvalid' }
    $archivePath=[IO.Path]::GetFullPath($Archive); $target=[IO.Path]::GetFullPath($Destination)
    Assert-ShellRecoveryNoLinks $archivePath; Assert-ShellRecoveryNoLinks $target
    if (Test-Path -LiteralPath $target) { throw 'ShellRecoveryArchiveDestinationExists' }
    $item=Get-Item -LiteralPath $archivePath -Force
    if ($item.PSIsContainer -or $item.Length -lt 22 -or $item.Length -gt 1073741824 -or
        (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $ExpectedSha256) { throw 'ShellRecoveryArchiveInvalid' }
    $zip=[IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        if ($zip.Entries.Count -lt 5 -or $zip.Entries.Count -gt 4096) { throw 'ShellRecoveryArchiveInvalid' }
        $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $files=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $expanded=0L
        foreach ($entry in $zip.Entries) {
            $name=$entry.FullName; $isDirectory=$name.EndsWith('/',[StringComparison]::Ordinal)
            $clean=if ($isDirectory) { $name.Substring(0,$name.Length-1) } else { $name }
            if ($name.Length -gt 1024 -or [string]::IsNullOrEmpty($clean) -or $name.Contains('\') -or $name.StartsWith('/') -or
                -not $names.Add($clean) -or $entry.IsEncrypted -or ($entry.ExternalAttributes -band [int][IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'ShellRecoveryArchiveEntryInvalid' }
            $mode=($entry.ExternalAttributes -shr 16) -band 0xf000
            if ($mode -notin @(0,0x8000,0x4000) -or ($isDirectory -and $mode -eq 0x8000) -or (-not $isDirectory -and $mode -eq 0x4000)) { throw 'ShellRecoveryArchiveEntryInvalid' }
            foreach ($segment in $clean.Split('/')) {
                if (-not $segment -or $segment -in @('.','..') -or $segment.Length -gt 128 -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or
                    $segment -match '[\x00-\x1f<>:"|?*]' -or $segment -match '\A(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?\z') { throw 'ShellRecoveryArchiveEntryInvalid' }
            }
            if ($isDirectory) { if ($entry.Length -ne 0) { throw 'ShellRecoveryArchiveEntryInvalid' } }
            else {
                if ($entry.Length -lt 0 -or $entry.Length -gt 536870912) { throw 'ShellRecoveryArchiveEntryInvalid' }
                $expanded += $entry.Length; if ($expanded -gt 2147483648) { throw 'ShellRecoveryArchiveEntryInvalid' }
                $null=$files.Add($clean)
            }
        }
        foreach ($name in $names) {
            $parts=$name.Split('/')
            for ($i=1;$i -lt $parts.Length;$i++) { if ($files.Contains(($parts[0..($i-1)] -join '/'))) { throw 'ShellRecoveryArchiveEntryInvalid' } }
        }
        foreach ($required in @('Workbench.App.exe','Workbench.App.dll','Workbench.App.pri','Assets/Workbench.ico','Assets/Workbench.svg')) {
            if (-not $files.Contains($required)) { throw 'ShellRecoveryArchiveContentsInvalid' }
        }
        [IO.Directory]::CreateDirectory($target) | Out-Null
        foreach ($entry in $zip.Entries) {
            $name=$entry.FullName
            if ($name.EndsWith('/')) { [IO.Directory]::CreateDirectory((Join-Path $target $name)) | Out-Null; continue }
            $path=Join-Path $target $name; $parent=[IO.Path]::GetDirectoryName($path)
            [IO.Directory]::CreateDirectory($parent) | Out-Null; Assert-ShellRecoveryNoLinks $parent
            $inputStream=$entry.Open()
            try {
                $output=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
                try { [ShellRecoveryZipCopy]::Copy($inputStream,$output,$entry.Length,$entry.Crc32) } finally { $output.Dispose() }
            } finally { $inputStream.Dispose() }
        }
        return @{ archiveSha256=$ExpectedSha256; archiveBytes=[long]$item.Length; expandedBytes=$expanded; entryCount=$zip.Entries.Count }
    } finally { $zip.Dispose() }
}
Export-ModuleMember -Function Test-ShellRecoveryExport,Test-ShellRecoveryImage,Test-ShellRecoveryFinal,Test-ShellRecoveryDriverSource,Test-ShellRecoveryProducer,Test-ShellRecoveryHostVersion,Expand-ShellRecoveryArchive,Get-ShellRecoveryPreflightFailure,Test-ShellRecoveryFailureOutcome,Test-ShellRecoveryFailureDiagnostic
