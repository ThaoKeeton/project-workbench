$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$helper = Join-Path $PSScriptRoot '../Workbench.NativeShellRecovery/OwnedShellHost.cs'
if ($PSVersionTable.PSEdition -ceq 'Desktop') {
    Add-Type -Path $helper -ReferencedAssemblies System.dll,System.Core.dll,System.Drawing.dll
} else {
    $refs = @(Get-ChildItem (Join-Path $PSHOME 'ref/*.dll') | ForEach-Object FullName)
    foreach ($name in @('System.Drawing.Common.dll','System.Private.Windows.Core.dll','System.Private.Windows.GdiPlus.dll')) {
        $path = Join-Path $PSHOME $name
        if (Test-Path -LiteralPath $path) { $refs += $path }
    }
    Add-Type -Path $helper -ReferencedAssemblies $refs
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
Expect $directory 40 16 32 $false 'unequal dimensions'
Expect $directory 40 0 16 $false 'zero requested width'
Expect $directory 40 257 16 $false 'requested width above ICO limit'
Expect $directory 39 16 16 $false 'truncated other frame'
Expect $directory[0..36] 40 16 16 $false 'truncated directory'
$bad=[byte[]]$directory.Clone(); $bad[2]=2
Expect $bad 40 16 16 $false 'not an icon directory'
$bad=[byte[]]$directory.Clone(); $bad[18]=1
Expect $bad 40 16 16 $false 'payload overlaps directory'
$bad=[byte[]]$directory.Clone(); $bad[14]=0
Expect $bad 40 16 16 $false 'empty image payload'
[byte[]]$max = @(0,0,1,0,1,0, 0,0,0,0,1,0,32,0,1,0,0,0,22,0,0,0)
Expect $max 23 256 256 $true 'zero directory dimensions mean 256'
Expect $max 23 255 255 $false '255 is not 256'
Write-Host "Icon directory policy: $passed synthetic boundary cases passed; no native execution."

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
