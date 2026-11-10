$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$helper = Join-Path $PSScriptRoot '../Workbench.NativeShellRecovery/OwnedShellHost.cs'
$regression = Join-Path $PSScriptRoot '../Workbench.NativeShellRecovery/IconComparisonRegression.cs'
if ($PSVersionTable.PSEdition -ceq 'Desktop') {
    Add-Type -Path @($helper,$regression) -ReferencedAssemblies System.dll,System.Core.dll,System.Drawing.dll
} else {
    $refs = @(Get-ChildItem (Join-Path $PSHOME 'ref/*.dll') | ForEach-Object FullName)
    foreach ($name in @('System.Drawing.Common.dll','System.Private.Windows.Core.dll','System.Private.Windows.GdiPlus.dll')) {
        $path = Join-Path $PSHOME $name
        if (Test-Path -LiteralPath $path) { $refs += $path }
    }
    Add-Type -Path @($helper,$regression) -ReferencedAssemblies $refs
}
# Hand-built ICO directory: two one-byte image payloads at offsets 38 and 39.
[byte[]]$directory = @(0,0,1,0,2,0, 16,16,0,0,1,0,32,0,1,0,0,0,38,0,0,0, 32,32,0,0,1,0,32,0,1,0,0,0,39,0,0,0)
function Expect([byte[]]$Data,[long]$Length,[int]$Width,[int]$Height,[bool]$Expected,[string]$Name) {
    $actual = [OwnedShellHost]::IconDirectoryHasSize($Data,$Length,$Width,$Height)
    if ($actual -ne $Expected) { throw "Icon directory policy failed: $Name" }
    $script:passed++
}
$passed=0
Expect $directory 40 16 16 $true '16 exists'
Expect $directory 40 32 32 $true '32 exists'
Expect $directory 40 24 24 $false 'unrepresented size'
Expect $directory 40 17 17 $false 'unrepresented 17px size'
Expect $directory 40 16 32 $false 'unequal dimensions'
Expect $directory 40 0 16 $false 'zero requested width'
Expect $directory 40 257 16 $false 'requested width above ICO limit'
Expect $directory 39 16 16 $false 'truncated other frame'
Expect $directory[0..36] 40 16 16 $false 'truncated directory'
Expect ([byte[]]@(0,0,1)) 3 16 16 $false 'truncated ICO header; directory only, no Windows payload decode'
Expect ([byte[]]@(60,115,118,103,32,120)) 6 16 16 $false 'in-memory SVG-like prefix is not an ICO directory'
$nonIcon=[byte[]]$directory.Clone()
$nonIcon[0]=60; $nonIcon[1]=115; $nonIcon[2]=118; $nonIcon[3]=103
Expect $nonIcon 40 16 16 $false 'in-memory non-ICO signature with otherwise valid directory and bounds'
$bad=[byte[]]$directory.Clone(); $bad[2]=2
Expect $bad 40 16 16 $false 'not an icon directory'
$bad=[byte[]]$directory.Clone(); $bad[18]=1
Expect $bad 40 16 16 $false 'payload overlaps directory'
$bad=[byte[]]$directory.Clone(); $bad[14]=0
Expect $bad 40 16 16 $false 'empty image payload'
$bad=[byte[]]$directory.Clone(); $bad[14]=3
Expect $bad 40 16 16 $false 'image payload exceeds file'
[byte[]]$boundedSingleByte = @(0,0,1,0,1,0, 16,16,0,0,1,0,32,0,1,0,0,0,22,0,0,0)
Expect $boundedSingleByte 23 16 16 $true 'directory bounds only; no Windows LoadImageW or corrupt-payload decoding'
[byte[]]$max = @(0,0,1,0,1,0, 0,0,0,0,1,0,32,0,1,0,0,0,22,0,0,0)
Expect $max 23 256 256 $true 'zero directory dimensions mean 256'
Expect $max 23 255 255 $false '255 is not 256'
Write-Host "Icon directory policy: $passed synthetic boundary cases passed; no Windows LoadImageW or corrupt-payload decoding."

# Run the native regression's malformed-directory checks as pure in-memory
# policy checks; this does not execute HICON or other Windows paths.
$memoryChecks=[IconComparisonRegression].GetMethod('CheckMalformedDirectories', [Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $memoryChecks) { throw 'Native regression in-memory directory checks are missing.' }
$null=$memoryChecks.Invoke($null,[object[]]@())
Write-Host 'Native regression in-memory directory checks passed; no Windows payload decoding.'

# Execute the real driver's receipt builder with bounded synthetic trace data.
# This verifies emission and the process-local switch observation, not GDI.
$tokens=$null; $errors=$null
$driver=Join-Path $PSScriptRoot '../Workbench.NativeShellRecovery/drive-shell-recovery.ps1'
$ast=[Management.Automation.Language.Parser]::ParseFile($driver,[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Driver parse failed.' }
$functions=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'New-FailureDiagnostic'},$true))
if ($functions.Count -ne 1) { throw 'Driver receipt builder missing.' }
. ([scriptblock]::Create($functions[0].Extent.Text))
$script:iconTrace=[pscustomobject]@{Subject='regression16';Stage='pixels';ActualWidth=16;ActualHeight=16;ReferenceWidth=16;ReferenceHeight=16;Difference=[pscustomobject]@{AlphaOnly=1;TransparentRgbOnly=1;RgbOnly=1;AlphaAndRgb=1;MaxAlphaDelta=127;MaxRgbDelta=10}}
$script:stage='healthy-icon-regression'; $script:primaryFailure=@{type='InvalidOperationException';category='InvalidOperation';hresult=-2146233079;line=391}; $script:primaryWaitFailure=$null
$script:checkpointWriteFailed=$false; $script:cleanupFailure=$null; $script:initialFrame=@{status='not-attempted';image=$null;failure=$null}
foreach ($state in @($true,$false)) {
    [AppContext]::SetSwitch('Switch.System.Drawing.DontSupportPngFramesInIcons',$state)
    $output=New-FailureDiagnostic
    if (-not $output.icon.ContainsKey('difference')) { throw 'Driver dropped completed pixel aggregate.' }
    $expectedSwitch=if ($state) {'true'} else {'false'}
    $d=$output.icon.difference
    if ($d.Count -ne 6 -or $d.alphaOnly -ne 1 -or $d.transparentRgbOnly -ne 1 -or $d.rgbOnly -ne 1 -or
        $d.alphaAndRgb -ne 1 -or $d.maxAlphaDelta -ne 127 -or $d.maxRgbDelta -ne 10 -or $output.icon.pngSwitch -cne $expectedSwitch) { throw 'Driver pixel aggregate or switch observation incorrect.' }
}
$script:iconTrace.Difference=$null
$withoutDifference=(New-FailureDiagnostic).icon
if ($withoutDifference.ContainsKey('difference')) { throw 'Driver emitted an incomplete aggregate.' }
if ($withoutDifference.pngSwitch -cne 'false') { throw 'Driver dropped switch observation before a completed scan.' }
$script:iconRegression = @{ legacyWidth = 16; legacyHeight = 16; legacyOutOfRange = $true }
$script:resizeDiag = @{ largeAfterImmediate = @{ width = 900; height = 800; x = 1; y = 2 }; largeWait = @{ threshold = 1100; met = $false } }
$retained = New-FailureDiagnostic
if ($retained.iconRegression.legacyWidth -ne 16 -or $retained.iconRegression.legacyHeight -ne 16 -or $retained.iconRegression.legacyOutOfRange -ne $true -or
    $retained.resize.largeWait.threshold -ne 1100 -or $retained.resize.largeWait.met -or $retained.resize.largeAfterImmediate.width -ne 900) {
    throw 'Driver dropped icon regression or resize observations under StrictMode.'
}
Remove-Variable iconRegression,resizeDiag -Scope Script
Write-Host 'Actual driver receipt builder: bounded synthetic aggregate and process-local switch observation passed; no native execution.'

# The same accumulator used by the bitmap loop must count exact ARGB
# differences without retaining the pixels themselves. These are synthetic
# scalar inputs, not a reconstruction of a Windows icon or HICON.
$difference = [IconPixelDifference]::new()
$pixelCases = @(
    @{ expected=@(0,0,0,0); actual=@(0,0,0,0); equal=$true },
    @{ expected=@(0,0,0,0); actual=@(0,1,2,3); equal=$false },
    @{ expected=@(128,0,0,0); actual=@(128,5,10,0); equal=$false },
    @{ expected=@(64,5,6,7); actual=@(96,5,6,7); equal=$false },
    @{ expected=@(255,10,20,30); actual=@(128,11,20,30); equal=$false }
)
foreach ($case in $pixelCases) {
    $e = $case.expected; $a = $case.actual
    $expectedArgb = [Drawing.Color]::FromArgb($e[0],$e[1],$e[2],$e[3]).ToArgb()
    $actualArgb = [Drawing.Color]::FromArgb($a[0],$a[1],$a[2],$a[3]).ToArgb()
    if ($difference.Compare($expectedArgb,$actualArgb) -ne $case.equal) { throw 'Exact ARGB comparison failed.' }
}
if ($difference.AlphaOnly -ne 1 -or $difference.TransparentRgbOnly -ne 1 -or $difference.RgbOnly -ne 1 -or
    $difference.AlphaAndRgb -ne 1 -or $difference.MaxAlphaDelta -ne 127 -or $difference.MaxRgbDelta -ne 10) { throw 'ARGB aggregate classification failed.' }
Write-Host 'Pixel comparison: 5 synthetic scalar cases and their independently calculated aggregate passed; no native execution.'

# A real trace object carries a completed aggregate through the actual driver
# builder and strict JSON validator; only the bitmap observations are synthetic.
$script:iconTrace=[IconComparisonTrace]::new()
$script:iconTrace.Begin('regression16'); $script:iconTrace.SetStage('pixels')
foreach ($name in @('ActualWidth','ActualHeight','ReferenceWidth','ReferenceHeight')) {
    [IconComparisonTrace].GetProperty($name).SetValue($script:iconTrace,[int]16,$null)
}
$script:iconTrace.CompletePixels($difference)
$diagnostic=New-FailureDiagnostic
if ($diagnostic.icon.difference.alphaOnly -ne 1 -or $diagnostic.icon.difference.maxRgbDelta -ne 10) { throw 'Trace-to-driver aggregate failed.' }
if ($PSVersionTable.PSEdition -cne 'Desktop') {
    $fixture=Join-Path ([IO.Path]::GetTempPath()) ('icon-receipt-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    $receiptPath=Join-Path $fixture 'driver.json'
    try {
        $receipt=@{schemaVersion=1;succeeded=$false;phase='healthy';failureCode='NativeGateFailed';forcedCleanup=$false;diagnostic=$diagnostic}
        [IO.File]::WriteAllText($receiptPath,($receipt | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
        Import-Module (Join-Path $PSScriptRoot '../../scripts/NativeShellRecoveryReport.psm1') -Force
        Test-ShellRecoveryFailureDiagnostic -Path $receiptPath -Directory $fixture | Out-Null
    } finally { if ([IO.File]::Exists($receiptPath)) { [IO.File]::Delete($receiptPath) }; [IO.Directory]::Delete($fixture) }
}
$script:iconTrace.Begin('regression32')
if ($null -ne $script:iconTrace.Difference) { throw 'New comparison retained stale pixel aggregate.' }
Write-Host 'Real trace and driver builder: aggregate/reset and portable strict receipt validation passed; no native execution.'

# Exercise the real managed cleanup routine with controlled release outcomes.
# These delegates model managed control flow only, not Windows DeleteObject.
$cleanup=[IconComparisonRegression].GetMethod('DeleteBitmapCopies', [Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $cleanup) { throw 'Bitmap-copy cleanup routine is missing.' }
$script:released = [Collections.Generic.List[int]]::new()
$script:releaseMode = ''
$release = [Func[IntPtr,bool]] {
    param([IntPtr]$handle)
    $value=$handle.ToInt32(); $script:released.Add($value)
    if ($script:releaseMode -ceq 'first throws' -and $value -eq 1) { throw [InvalidOperationException]::new('synthetic release failure') }
    if ($script:releaseMode -ceq 'second throws' -and $value -eq 2) { throw [InvalidOperationException]::new('synthetic release failure') }
    if ($script:releaseMode -ceq 'first returns false' -and $value -eq 1) { return $false }
    if ($script:releaseMode -ceq 'second returns false' -and $value -eq 2) { return $false }
    return $true
}
foreach ($mode in @('success','first returns false','second returns false','first throws','second throws')) {
    $script:releaseMode=$mode; $script:released.Clear(); $thrown=$false
    try { $null=$cleanup.Invoke($null,[object[]]@([IntPtr]1,[IntPtr]2,$release)) } catch { $thrown=$true }
    if (($script:released -join ',') -cne '1,2' -or $thrown -ne ($mode -cne 'success')) { throw "Bitmap-copy cleanup failed: $mode" }
}
$script:releaseMode='success'; $script:released.Clear()
$null=$cleanup.Invoke($null,[object[]]@([IntPtr]::Zero,[IntPtr]2,$release))
if (($script:released -join ',') -cne '2') { throw 'Bitmap-copy cleanup mishandled a null handle.' }
Write-Host 'Bitmap-copy cleanup: six managed control-flow cases passed; Windows DeleteObject not executed.'
