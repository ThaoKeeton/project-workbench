#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$HostExe, [Parameter(Mandatory=$true)][string]$EvidenceRoot,
      [Parameter(Mandatory=$true)][string]$ExpectedSourceCommit, [Parameter(Mandatory=$true)][string]$ExeHash,
      [Parameter(Mandatory=$true)][string]$DllHash)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:phase = 'preflight'; $script:hosts = New-Object System.Collections.ArrayList
$script:records = New-Object System.Collections.ArrayList; $script:images = New-Object System.Collections.ArrayList
$script:reports = New-Object System.Collections.ArrayList; $script:forced = $false; $script:current = $null
$script:result = $null; $script:canWrite = $false
function Assert-Condition([bool]$Condition) { if (-not $Condition) { throw 'NativeAssertionFailed' } }
function Wait-For([scriptblock]$Probe, [int]$Seconds = 30) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) { try { $answer = & $Probe; if ($answer) { return $answer } } catch { } Start-Sleep -Milliseconds 200 }
    throw 'NativeWaitExpired'
}
function Set-Phase([string]$Name) {
    $script:phase = $Name
    $file = Join-Path $EvidenceRoot (('phase-{0:D2}-{1}.json' -f $script:phaseNumber, $Name))
    $script:phaseNumber++
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes((@{ phase = $Name } | ConvertTo-Json -Compress))
    $stream = [IO.File]::Open($file,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
}
# Every checkpoint reads the unrelated file in the owned profile, checks its exact
# original bytes, then records the SHA-256 of those same bytes.
function Record-Sentinel([string]$Name) {
    [OwnedShellHost]::RejectLinks($script:sentinelPath)
    $actual = [IO.File]::ReadAllBytes($script:sentinelPath)
    Assert-Condition ([Convert]::ToBase64String($actual) -ceq [Convert]::ToBase64String($script:sentinelBytes))
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($hasher.ComputeHash($actual)).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
    Assert-Condition ($hash -ceq $script:sentinelHash)
    $null = $script:sentinelChecks.Add(@{ phase=$Name; sha256=$hash })
}
function Assert-HostElement($Owned,$Element) {
    $Owned.host.Validate($Owned.hwnd)
    [OwnedShellHost]::AssertElementProcessId($Owned.host.Id,$Element.Current.ProcessId)
    $Owned.host.Validate($Owned.hwnd)
}
function Assert-HostAfter($Owned,$Element) {
    if ($Owned.host.Exited -or -not [OwnedShellHost]::WindowExists($Owned.hwnd)) {
        try { [OwnedShellHost]::AssertElementProcessId($Owned.host.Id,$Element.Current.ProcessId) }
        catch { if ($_.Exception.GetBaseException() -isnot [System.Windows.Automation.ElementNotAvailableException]) { throw } }
        return
    }
    try { Assert-HostElement $Owned $Element }
    catch {
        if ($_.Exception.GetBaseException() -isnot [System.Windows.Automation.ElementNotAvailableException]) { throw }
        $Owned.host.Validate($Owned.hwnd)
    }
}
function Assert-PickerRoot($Owned,$Picker) {
    [OwnedShellHost]::AssertElementProcessId($Picker.processId,$Owned.host.PopupProcessId($Owned.hwnd,$Picker.hwnd))
}
function Assert-PickerElement($Owned,$Picker,$Element) {
    Assert-PickerRoot $Owned $Picker
    [OwnedShellHost]::AssertElementProcessId($Picker.processId,$Element.Current.ProcessId)
    Assert-PickerRoot $Owned $Picker
}
function Assert-PickerAfter($Owned,$Picker,$Element) {
    if (-not [OwnedShellHost]::WindowExists($Picker.hwnd)) {
        try { [OwnedShellHost]::AssertElementProcessId($Picker.processId,$Element.Current.ProcessId) }
        catch { if ($_.Exception.GetBaseException() -isnot [System.Windows.Automation.ElementNotAvailableException]) { throw } }
        return
    }
    try { Assert-PickerElement $Owned $Picker $Element }
    catch {
        if ($_.Exception.GetBaseException() -isnot [System.Windows.Automation.ElementNotAvailableException]) { throw }
        Assert-PickerRoot $Owned $Picker
    }
}
function New-Host([string]$Role) {
    $hostRun = [OwnedShellHost]::Start($HostExe); $null = $script:hosts.Add($hostRun)
    $hwnd = Wait-For { $h = $hostRun.Window(); if ($h -ne [IntPtr]::Zero) { $h } } 60
    $hostRun.Validate($hwnd)
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $owned = @{ host = $hostRun; hwnd = $hwnd; window = $window; role = $Role; closed = $false }
    Assert-HostElement $owned $window
    return $owned
}
function Find-Named($Element, [string]$Name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $items = $Element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($items.Count -ne 1) { throw 'NamedControlUnavailable' }
    return $items.Item(0)
}
function Get-HostControl($Owned, [string]$Name) {
    return Wait-For { Assert-HostElement $Owned $Owned.window; $item = Find-Named $Owned.window $Name; Assert-HostElement $Owned $item; $item }
}
function Invoke-Host($Owned, [string]$Name) {
    $item = Wait-For { $candidate = Get-HostControl $Owned $Name; Assert-HostElement $Owned $candidate; $enabled = $candidate.Current.IsEnabled; Assert-HostElement $Owned $candidate; if ($enabled) { $candidate } }
    $pattern = $null; Assert-HostElement $Owned $item
    $available = $item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)
    Assert-HostElement $Owned $item; Assert-Condition $available
    Assert-HostElement $Owned $item; try { $pattern.Invoke() } finally { Assert-HostAfter $Owned $item }
}
function Read-HostText($Owned,[string]$Name) {
    $item = Get-HostControl $Owned $Name; $pattern = $null
    Assert-HostElement $Owned $item; $available = $item.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern,[ref]$pattern); Assert-HostElement $Owned $item
    if ($available) { Assert-HostElement $Owned $item; try { $text = $pattern.DocumentRange.GetText(512).TrimEnd([char[]]"`r`n") } finally { Assert-HostAfter $Owned $item }; return $text }
    Assert-HostElement $Owned $item; $available = $item.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$pattern); Assert-HostElement $Owned $item
    if ($available) { Assert-HostElement $Owned $item; try { $text = $pattern.Current.Value } finally { Assert-HostAfter $Owned $item }; return $text }
    throw 'TextPatternUnavailable'
}
function Show-Picker($Owned) {
    Invoke-Host $Owned '导出脱敏错误报告'
    $popup = Wait-For { $h = $Owned.host.Popup($Owned.hwnd); if ($h -ne [IntPtr]::Zero) { $h } } 30
    $popupId = $Owned.host.PopupProcessId($Owned.hwnd,$popup)
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($popup)
    $picker = @{ hwnd = $popup; window = $window; processId = $popupId }
    Assert-PickerElement $Owned $picker $window
    $null = Wait-For { Assert-PickerElement $Owned $picker $picker.window; $item = Find-Named $picker.window '将错误报告保存到此文件夹'; Assert-PickerElement $Owned $picker $item; $item } 30
    return $picker
}
function Picker-Invoke($Owned,$Picker,[string]$Name,[switch]$Closing) {
    Assert-PickerElement $Owned $Picker $Picker.window
    $item = Find-Named $Picker.window $Name; Assert-PickerElement $Owned $Picker $item
    $enabled = $item.Current.IsEnabled; Assert-PickerElement $Owned $Picker $item; Assert-Condition $enabled
    $pattern = $null; Assert-PickerElement $Owned $Picker $item
    $available = $item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)
    Assert-PickerElement $Owned $Picker $item; Assert-Condition $available
    Assert-PickerElement $Owned $Picker $item; try { $pattern.Invoke() } finally { Assert-PickerAfter $Owned $Picker $item }
    if ($Closing) { $null = Wait-For { if ($Owned.host.Exited -or $Owned.host.Popup($Owned.hwnd) -eq [IntPtr]::Zero) { $true } } 30 }
    else { $Owned.host.Validate($Owned.hwnd); $null = Wait-For { if ($Owned.host.Popup($Owned.hwnd) -eq [IntPtr]::Zero) { $true } } 30 }
    Assert-Condition (-not [OwnedShellHost]::WindowExists($Picker.hwnd))
}
function Cancel-Picker($Owned,$Picker) {
    $cancel = $null
    foreach ($name in @('Cancel','取消')) { try { Assert-PickerElement $Owned $Picker $Picker.window; $cancel = Find-Named $Picker.window $name; break } catch { if ($_.Exception.Message -cne 'NamedControlUnavailable') { throw } } }
    if ($null -eq $cancel) { throw 'PickerCancelUnavailable' }
    Assert-PickerElement $Owned $Picker $cancel; $name = $cancel.Current.Name; Assert-PickerElement $Owned $Picker $cancel
    Picker-Invoke $Owned $Picker $name
}
function Select-PickerFolder($Owned,$Picker,[string]$Directory,[switch]$Closing) {
    Assert-PickerElement $Owned $Picker $Picker.window
    $all = $Picker.window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
    Assert-PickerElement $Owned $Picker $Picker.window
    $addresses = New-Object System.Collections.ArrayList
    foreach ($item in $all) {
        Assert-PickerElement $Owned $Picker $item
        $name = $item.Current.Name; $handle = $item.Current.NativeWindowHandle
        Assert-PickerElement $Owned $Picker $item
        if ($name -match '^(Address|地址)(:|：)' -and $handle -ne 0) {
            $pattern = $null; Assert-PickerElement $Owned $Picker $item
            $available = $item.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$pattern)
            Assert-PickerElement $Owned $Picker $item
            if ($available) {
                Assert-PickerElement $Owned $Picker $item; $readOnly = $pattern.Current.IsReadOnly; Assert-PickerElement $Owned $Picker $item
                if (-not $readOnly) { $null = $addresses.Add(@{ item = $item; value = $pattern }) }
            }
        }
    }
    Assert-Condition ($addresses.Count -eq 1)
    $address = $addresses[0]
    Assert-PickerElement $Owned $Picker $address.item; try { $address.value.SetValue($Directory) } finally { Assert-PickerAfter $Owned $Picker $address.item }
    Assert-PickerElement $Owned $Picker $address.item; $editHandle = [IntPtr]$address.item.Current.NativeWindowHandle; Assert-PickerElement $Owned $Picker $address.item
    $Owned.host.SubmitPickerAddress($Owned.hwnd,$Picker.hwnd,$editHandle)
    Assert-PickerAfter $Owned $Picker $address.item
    $expectedFolder = [IO.Path]::GetFullPath($Directory).TrimEnd('\')
    $null = Wait-For {
        Assert-PickerElement $Owned $Picker $address.item; $value = $address.value.Current.Value; Assert-PickerElement $Owned $Picker $address.item
        $actualFolder = [IO.Path]::GetFullPath($value).TrimEnd('\')
        Assert-PickerElement $Owned $Picker $Picker.window; $button = Find-Named $Picker.window '将错误报告保存到此文件夹'
        Assert-PickerElement $Owned $Picker $button; $enabled = $button.Current.IsEnabled; Assert-PickerElement $Owned $Picker $button
        if ([string]::Equals($actualFolder,$expectedFolder,[StringComparison]::OrdinalIgnoreCase) -and $enabled) { $true }
    } 30
    Picker-Invoke $Owned $Picker '将错误报告保存到此文件夹' -Closing:$Closing
}
function Export-To($Owned,[string]$Directory) {
    $picker = Show-Picker $Owned
    Select-PickerFolder $Owned $picker $Directory
    $null = Wait-For { $files = @(Get-ChildItem -LiteralPath $Directory -Filter 'Workbench-error-*.json' -File); if ($files.Count -eq 1) { $files[0] } } 30
    return @(Get-ChildItem -LiteralPath $Directory -Filter 'Workbench-error-*.json' -File)[0]
}
function Capture-Frame($Owned,[string]$Name) {
    $path = Join-Path $EvidenceRoot $Name; $bounds = $Owned.host.Capture($Owned.hwnd,$path)
    $item = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $null = $script:images.Add(@{ name = $Name; sha256 = $hash; bytes = $item.Length; width = ($bounds[2]-$bounds[0]); height = ($bounds[3]-$bounds[1]); left=$bounds[0]; top=$bounds[1]; right=$bounds[2]; bottom=$bounds[3]; processId=$Owned.host.Id; creationFileTime=$Owned.host.Birth; windowHandle=$Owned.hwnd.ToInt64(); source='PrintWindow2' })
}
function Close-Host($Owned) {
    $pattern = $null; Assert-HostElement $Owned $Owned.window
    $available = $Owned.window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern,[ref]$pattern)
    Assert-HostElement $Owned $Owned.window; Assert-Condition $available
    Assert-HostElement $Owned $Owned.window; try { $pattern.Close() } finally { Assert-HostAfter $Owned $Owned.window }; $Owned.closed = $true
    $null = Wait-For { if ($Owned.host.Exited) { $true } } 45
    Assert-Condition ($Owned.host.ExitCode -eq 0 -and -not [OwnedShellHost]::WindowExists($Owned.hwnd))
    $null = Wait-For { if ($Owned.host.LiveCount() -eq 0) { $true } } 10
    $null = $script:records.Add(@{ role=$Owned.role; processId=$Owned.host.Id; creationFileTime=$Owned.host.Birth; windowHandle=$Owned.hwnd.ToInt64(); windowClosed=$true; exitCode=0; rootReacquired=$false; jobEmpty=$true })
}
function Reacquire-Roots([string]$Root) {
    foreach ($relative in @('host.lock','.workspaces/catalog.lock')) {
        $stream = [IO.File]::Open((Join-Path $Root $relative),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        try { } finally { $stream.Dispose() }
    }
}
function Assert-RootHeld([string]$Root) {
    $held = $false
    try { $stream = [IO.File]::Open((Join-Path $Root 'host.lock'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); $stream.Dispose() }
    catch [IO.IOException] { $held = $true }
    Assert-Condition $held
}
function Select-HostItem($Owned,[string]$Name) {
    $item = Get-HostControl $Owned $Name; $pattern = $null
    Assert-HostElement $Owned $item; $select = $item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern); Assert-HostElement $Owned $item
    if ($select) { Assert-HostElement $Owned $item; try { $pattern.Select() } finally { Assert-HostAfter $Owned $item } }
    else {
        Assert-HostElement $Owned $item; $invoke = $item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern); Assert-HostElement $Owned $item
        if (-not $invoke) { throw 'SelectionPatternUnavailable' }
        Assert-HostElement $Owned $item; try { $pattern.Invoke() } finally { Assert-HostAfter $Owned $item }
    }
}
function Set-Theme($Owned,[string]$Theme) {
    Select-HostItem $Owned '工作台设置'
    $combo = Get-HostControl $Owned '应用主题'; $expand = $null
    Assert-HostElement $Owned $combo; $available = $combo.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern,[ref]$expand); Assert-HostElement $Owned $combo; Assert-Condition $available
    Assert-HostElement $Owned $combo; try { $expand.Expand() } finally { Assert-HostAfter $Owned $combo }
    Select-HostItem $Owned $Theme
    Start-Sleep -Milliseconds 900
}

try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or $PSVersionTable.PSEdition -cne 'Desktop' -or
        [Threading.Thread]::CurrentThread.ApartmentState -ne 'STA' -or $env:CI -cne 'true' -or $env:GITHUB_ACTIONS -cne 'true' -or
        $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or -not [Environment]::UserInteractive) { throw 'UnsafeEnvironment' }
    Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
    Add-Type -Path (Join-Path $PSScriptRoot 'OwnedShellHost.cs') -ReferencedAssemblies System.dll,System.Core.dll,System.Drawing.dll
    $userRoot = [IO.Path]::Combine([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData),'ProjectWorkbench')
    [OwnedShellHost]::RejectLinks($userRoot); [OwnedShellHost]::RejectLinks($EvidenceRoot); [OwnedShellHost]::RejectLinks($HostExe)
    Assert-Condition (-not [OwnedShellHost]::PathExists($userRoot))
    $prefix = $userRoot.TrimEnd('\') + '\'
    Assert-Condition (-not ($EvidenceRoot.Equals($userRoot,[StringComparison]::OrdinalIgnoreCase) -or $EvidenceRoot.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)))
    Assert-Condition ($ExpectedSourceCommit -cmatch '\A[0-9a-f]{40}\z' -and $ExeHash -cmatch '\A[0-9a-f]{64}\z' -and $DllHash -cmatch '\A[0-9a-f]{64}\z')
    $script:canWrite = $true; $script:phaseNumber = 1
    $conflictDir = Join-Path $EvidenceRoot 'conflict'; $catalogDir = Join-Path $EvidenceRoot 'catalog'; $pendingDir = Join-Path $EvidenceRoot 'pending'
    foreach ($dir in @($conflictDir,$catalogDir,$pendingDir)) { Assert-Condition (-not [OwnedShellHost]::PathExists($dir)); [IO.Directory]::CreateDirectory($dir) | Out-Null }
    # This runner's previously absent normal profile is now created exclusively by
    # the harness. A preexisting profile is never opened or removed.
    [OwnedShellHost]::CreateDirectoryExclusive($userRoot)
    [OwnedShellHost]::RejectLinks($userRoot)
    $script:sentinelPath = Join-Path $userRoot 'unrelated-synthetic-sentinel.bin'
    $script:sentinelBytes = [Text.Encoding]::UTF8.GetBytes('ProjectWorkbench synthetic unrelated sentinel v1')
    $script:sentinelChecks = New-Object System.Collections.ArrayList
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $script:sentinelHash = [BitConverter]::ToString($hasher.ComputeHash($script:sentinelBytes)).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
    foreach ($path in @($script:sentinelPath,(Join-Path $EvidenceRoot 'unrelated-sentinel-original.bin'))) {
        $stream = [IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $stream.Write($script:sentinelBytes,0,$script:sentinelBytes.Length) } finally { $stream.Dispose() }
    }
    Record-Sentinel 'before-start'
    Set-Phase 'healthy'
    $first = New-Host 'healthy'; $script:current = $first
    $null = Get-HostControl $first '工作台标题栏'
    $null = Wait-For { Assert-HostElement $first $first.window; $name = $first.window.Current.Name; Assert-HostElement $first $first.window; if ($name -ceq 'Default · Project Workbench') { $true } } 60
    $catalogPath = Join-Path $userRoot '.workspaces/catalog.json'; [OwnedShellHost]::RejectLinks($catalogPath)
    $catalogBytes = [IO.File]::ReadAllBytes($catalogPath); $catalog = [Text.Encoding]::UTF8.GetString($catalogBytes) | ConvertFrom-Json
    Assert-Condition (@($catalog.projects).Count -eq 1 -and $catalog.activeProjectId -ceq 'default' -and $catalog.projects[0].id -ceq 'default')
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'切换项目')
    Assert-HostElement $first $first.window
    $selector = $first.window.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition)
    Assert-HostElement $first $first.window
    if ($selector.Count -eq 1) { $candidate = $selector.Item(0); Assert-HostElement $first $candidate; $offscreen = $candidate.Current.IsOffscreen; Assert-HostElement $first $candidate }
    Assert-Condition ($selector.Count -eq 0 -or ($selector.Count -eq 1 -and $offscreen))
    $icon = Join-Path ([IO.Path]::GetDirectoryName($HostExe)) 'Assets/Workbench.ico'; [OwnedShellHost]::RejectLinks($icon)
    Assert-Condition ($first.host.IconMatches($first.hwnd,$HostExe,$icon))
    $win = $null; Assert-HostElement $first $first.window
    $available = $first.window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern,[ref]$win)
    Assert-HostElement $first $first.window; Assert-Condition $available
    Assert-HostElement $first $first.window; try { $win.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized) } finally { Assert-HostAfter $first $first.window }
    $null = Wait-For { Assert-HostElement $first $first.window; $state = $win.Current.WindowVisualState; Assert-HostElement $first $first.window; if ($state -eq [System.Windows.Automation.WindowVisualState]::Maximized) { $true } }
    Assert-HostElement $first $first.window; try { $win.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal) } finally { Assert-HostAfter $first $first.window }
    $null = Wait-For { Assert-HostElement $first $first.window; $state = $win.Current.WindowVisualState; Assert-HostElement $first $first.window; if ($state -eq [System.Windows.Automation.WindowVisualState]::Normal) { $true } }
    $transform = $null; Assert-HostElement $first $first.window
    $available = $first.window.TryGetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern,[ref]$transform)
    Assert-HostElement $first $first.window; Assert-Condition $available
    Assert-HostElement $first $first.window; $canResize = $transform.Current.CanResize; Assert-HostElement $first $first.window; Assert-Condition $canResize
    Assert-HostElement $first $first.window; try { $transform.Resize(640,600) } finally { Assert-HostAfter $first $first.window }
    $null = Wait-For { Assert-HostElement $first $first.window; $width = $first.window.Current.BoundingRectangle.Width; Assert-HostElement $first $first.window; if ($width -le 660) { $true } }
    $null = Get-HostControl $first '工作台标题栏'; $null = Get-HostControl $first '当前项目标题'
    Assert-HostElement $first $first.window; try { $transform.Resize(1180,800) } finally { Assert-HostAfter $first $first.window }
    $null = Wait-For { Assert-HostElement $first $first.window; $width = $first.window.Current.BoundingRectangle.Width; Assert-HostElement $first $first.window; if ($width -ge 1100) { $true } }
    Set-Theme $first '浅色'; Capture-Frame $first 'light.png'
    Set-Theme $first '深色'; Capture-Frame $first 'dark.png'
    Record-Sentinel 'healthy'
    Set-Phase 'lease-conflict'
    $second = New-Host 'duplicate-retry'; $script:current = $second
    $null = Get-HostControl $second '导出脱敏错误报告'
    Record-Sentinel 'duplicate-root'
    $picker = Show-Picker $second
    $button = Get-HostControl $second '导出脱敏错误报告'
    Assert-HostElement $second $button; $enabled = $button.Current.IsEnabled; Assert-HostElement $second $button; Assert-Condition (-not $enabled)
    Assert-PickerRoot $second $picker; $invoke = $null
    Assert-HostElement $second $button; $available = $button.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$invoke); Assert-HostElement $second $button
    if ($available) { Assert-HostElement $second $button; try { $invoke.Invoke() } catch [System.Windows.Automation.ElementNotEnabledException] { } finally { Assert-HostAfter $second $button } }
    Assert-Condition ($second.host.Popup($second.hwnd) -eq $picker.hwnd)
    Cancel-Picker $second $picker
    $null = Wait-For { if ((Read-HostText $second '错误报告导出状态') -ceq '已取消导出，尚未写入报告。') { $true } }
    Assert-Condition (@(Get-ChildItem -LiteralPath $conflictDir -File).Count -eq 0)
    Record-Sentinel 'cancel'
    $export = Export-To $second $conflictDir
    Record-Sentinel 'export'
    $before = (Get-FileHash -LiteralPath $export.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $picker = Show-Picker $second; Select-PickerFolder $second $picker $conflictDir
    $null = Wait-For { if ((Read-HostText $second '错误报告导出状态') -ceq '此文件夹已有同名报告，原文件未覆盖。可使用已有报告或选择其他文件夹。') { $true } }
    $after = (Get-FileHash -LiteralPath $export.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); Assert-Condition ($before -ceq $after)
    Record-Sentinel 'collision'
    $null = $script:reports.Add(@{ relativePath=('conflict/'+$export.Name); stage='WorkspaceOwnership'; code='UserStoreInUse'; category='Storage'; processId=$second.host.Id; creationFileTime=$second.host.Birth; windowHandle=$second.hwnd.ToInt64() })
    Close-Host $first; Reacquire-Roots $userRoot
    Record-Sentinel 'first-close'
    Assert-Condition ((Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash -ceq ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($catalogBytes)).Replace('-','')))
    Invoke-Host $second '重试打开项目'
    $null = Wait-For { Assert-HostElement $second $second.window; $name = $second.window.Current.Name; Assert-HostElement $second $second.window; if ($name -ceq 'Default · Project Workbench') { $true } } 60
    Close-Host $second; Reacquire-Roots $userRoot
    Record-Sentinel 'retry-close'
    Set-Phase 'corrupt-catalog'
    $copy = Join-Path $EvidenceRoot 'original-catalog.json'; $stream = [IO.File]::Open($copy,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None); try { $stream.Write($catalogBytes,0,$catalogBytes.Length) } finally { $stream.Dispose() }
    $catalogBefore = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant()
    $broken = [Text.Encoding]::UTF8.GetBytes('{ invalid synthetic catalog')
    [IO.File]::WriteAllBytes($catalogPath,$broken)
    $third = New-Host 'corrupt-retry'; $script:current = $third
    $null = Get-HostControl $third '导出脱敏错误报告'
    Record-Sentinel 'corrupt-catalog'
    $export = Export-To $third $catalogDir
    $null = $script:reports.Add(@{ relativePath=('catalog/'+$export.Name); stage='CatalogOpen'; code='InvalidCatalog'; category='InvalidData'; processId=$third.host.Id; creationFileTime=$third.host.Birth; windowHandle=$third.hwnd.ToInt64() })
    Assert-Condition ([Convert]::ToBase64String([IO.File]::ReadAllBytes($catalogPath)) -ceq [Convert]::ToBase64String($broken))
    Record-Sentinel 'corrupt-export'
    [IO.File]::WriteAllBytes($catalogPath,$catalogBytes)
    Invoke-Host $third '重试打开项目'
    $null = Wait-For { Assert-HostElement $third $third.window; $name = $third.window.Current.Name; Assert-HostElement $third $third.window; if ($name -ceq 'Default · Project Workbench') { $true } } 60
    Close-Host $third; Reacquire-Roots $userRoot
    Record-Sentinel 'corrupt-recovery'
    Set-Phase 'close-pending-picker'
    Assert-Condition ([Convert]::ToBase64String([IO.File]::ReadAllBytes($catalogPath)) -ceq [Convert]::ToBase64String($catalogBytes))
    [IO.File]::WriteAllBytes($catalogPath,$broken)
    $fourth = New-Host 'close-pending'; $script:current = $fourth; $picker = Show-Picker $fourth
    $pattern = $null; Assert-HostElement $fourth $fourth.window
    $available = $fourth.window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern,[ref]$pattern)
    Assert-HostElement $fourth $fourth.window; Assert-Condition $available
    Assert-PickerRoot $fourth $picker; Assert-HostElement $fourth $fourth.window; try { $pattern.Close() } finally { Assert-HostAfter $fourth $fourth.window }
    Assert-Condition (-not $fourth.host.Exited); Assert-PickerRoot $fourth $picker; Assert-RootHeld $userRoot
    Select-PickerFolder $fourth $picker $pendingDir -Closing
    $null = Wait-For { if ($fourth.host.Exited) { $true } } 45
    Assert-Condition ($fourth.host.ExitCode -eq 0 -and -not [OwnedShellHost]::WindowExists($fourth.hwnd) -and $fourth.host.LiveCount() -eq 0)
    $null = $script:records.Add(@{ role=$fourth.role; processId=$fourth.host.Id; creationFileTime=$fourth.host.Birth; windowHandle=$fourth.hwnd.ToInt64(); windowClosed=$true; exitCode=0; rootReacquired=$false; jobEmpty=$true })
    Assert-Condition (@(Get-ChildItem -LiteralPath $pendingDir -File).Count -eq 0)
    Reacquire-Roots $userRoot
    Assert-Condition ([Convert]::ToBase64String([IO.File]::ReadAllBytes($catalogPath)) -ceq [Convert]::ToBase64String($broken))
    Record-Sentinel 'close-pending'
    [IO.File]::WriteAllBytes($catalogPath,$catalogBytes)
    $fifth = New-Host 'restored-start'; $script:current = $fifth
    $null = Wait-For { Assert-HostElement $fifth $fifth.window; $name = $fifth.window.Current.Name; Assert-HostElement $fifth $fifth.window; if ($name -ceq 'Default · Project Workbench') { $true } } 60
    Close-Host $fifth; Reacquire-Roots $userRoot
    Record-Sentinel 'final-recovery'
    $sentinelFinal = [IO.File]::ReadAllBytes($script:sentinelPath)
    Assert-Condition ([Convert]::ToBase64String($sentinelFinal) -ceq [Convert]::ToBase64String($script:sentinelBytes))
    $stream = [IO.File]::Open((Join-Path $EvidenceRoot 'unrelated-sentinel-final.bin'),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $stream.Write($sentinelFinal,0,$sentinelFinal.Length) } finally { $stream.Dispose() }
    Assert-Condition ([Convert]::ToBase64String([IO.File]::ReadAllBytes($catalogPath)) -ceq [Convert]::ToBase64String($catalogBytes))
    $catalogAfter = (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition ($catalogBefore -ceq $catalogAfter)
    foreach ($record in $script:records) { $record.rootReacquired = $true }
    $script:result = @{ schemaVersion=1; succeeded=$true; sourceCommit=$ExpectedSourceCommit; executableSha256=$ExeHash; dllSha256=$DllHash; forcedCleanup=$false; processes=@($script:records.ToArray()); images=@($script:images.ToArray()); reports=@($script:reports.ToArray()); cancelWritten=$false; collisionBefore=$before; collisionAfter=$after; catalogBefore=$catalogBefore; catalogAfter=$catalogAfter; sentinelBefore=$script:sentinelHash; sentinelAfter=$script:sentinelChecks[$script:sentinelChecks.Count-1].sha256; sentinelChecks=@($script:sentinelChecks.ToArray()); pickerPendingAtClose=$true; checks=@('normal','leaseConflict','retry','corruptCatalog','cancel','export','serialization','collision','closePending','light','dark','maximizeRestore','icon') }
    $script:phase = 'completed'
} catch {
    $script:result = @{ schemaVersion=1; succeeded=$false; phase=$script:phase; failureCode='NativeGateFailed'; forcedCleanup=$false }
} finally {
    foreach ($hostRun in $script:hosts) {
        if (-not $hostRun.Exited) {
            try {
                $hwnd = $hostRun.Window()
                if ($hwnd -ne [IntPtr]::Zero) {
                    $popup = $hostRun.Popup($hwnd)
                    $owned = @{ host=$hostRun; hwnd=$hwnd; window=[System.Windows.Automation.AutomationElement]::FromHandle($hwnd) }
                    Assert-HostElement $owned $owned.window
                    if ($popup -ne [IntPtr]::Zero) {
                        $popupId=$hostRun.PopupProcessId($hwnd,$popup); $w=[System.Windows.Automation.AutomationElement]::FromHandle($popup)
                        $picker=@{ hwnd=$popup; window=$w; processId=$popupId }; $p=$null
                        Assert-PickerElement $owned $picker $w; $available=$w.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern,[ref]$p); Assert-PickerElement $owned $picker $w
                        if ($available) { Assert-PickerElement $owned $picker $w; try { $p.Close() } finally { Assert-PickerAfter $owned $picker $w } }
                    }
                    if (-not $hostRun.Exited) {
                        Assert-HostElement $owned $owned.window; $p=$null
                        $available=$owned.window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern,[ref]$p); Assert-HostElement $owned $owned.window
                        if ($available) { Assert-HostElement $owned $owned.window; try { $p.Close() } finally { Assert-HostAfter $owned $owned.window } }
                    }
                    $null = Wait-For { if ($hostRun.Exited) { $true } } 10
                }
            } catch { }
            if (-not $hostRun.Exited) { try { $script:forced=$true; $hostRun.TerminateOwned() } catch { } }
        }
        try { if ($hostRun.LiveCount() -ne 0) { $script:forced=$true; $hostRun.TerminateOwned() } } catch { $script:forced=$true }
        $hostRun.Dispose()
    }
    if ($script:forced) { $script:result.succeeded=$false; $script:result.forcedCleanup=$true }
    if ($script:canWrite -and $null -ne $script:result) {
        try {
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($script:result | ConvertTo-Json -Depth 10))
            $out = [IO.File]::Open((Join-Path $EvidenceRoot 'driver.json'),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try { $out.Write($bytes,0,$bytes.Length) } finally { $out.Dispose() }
        } catch { [Console]::Error.WriteLine('Native gate could not write its sanitized driver receipt.'); exit 1 }
    }
}
if ($null -ne $script:result -and $script:result.succeeded) { [Console]::WriteLine('Owned native checks completed; launcher validation remains.'); exit 0 }
[Console]::Error.WriteLine('Owned native checks failed; inspect the sanitized receipt.'); exit 1
