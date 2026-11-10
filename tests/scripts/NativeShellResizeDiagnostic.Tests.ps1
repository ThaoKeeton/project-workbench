#requires -Version 7.0
# Portable checks for the native resize receipt. No WinUI, UIA, or window is started.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$driverPath = Join-Path $PSScriptRoot '../Workbench.NativeShellRecovery/drive-shell-recovery.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($driverPath, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Driver parse failed.' }
function Get-DriverFunction([string]$Name) {
    $found = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $Name }, $true))
    if ($found.Count -ne 1) { throw "Driver function missing: $Name" }
    return $found[0]
}
$receiptBuilder = Get-DriverFunction 'New-FailureDiagnostic'
$save = Get-DriverFunction 'Save-ResizeDiagnostic'
if ($receiptBuilder.Body.Extent.Text -notmatch '\$legacy = \$null' -or $receiptBuilder.Body.Extent.Text -notmatch 'Get-Variable -Name iconRegression') {
    throw 'Failure receipt uses icon regression without assigning it.'
}
$persistedWaits = @($ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.TryStatementAst] -and
    $null -ne $node.Finally -and
    $node.Body.Extent.Text -match 'Wait-For' -and
    $node.Body.Extent.Text -match '-ge 1100' -and
    $node.Finally.Extent.Text -match 'Save-ResizeDiagnostic' -and
    $node.Finally.Extent.Text -match 'threshold = 1100'
}, $true))
if ($persistedWaits.Count -ne 1) { throw 'The 1,100-pixel wait does not persist resize-diagnostic.json from its finally block.' }
if ($persistedWaits[0].Body.Extent.Text -match '-ge\s+(?!1100\b)\d+') { throw 'The large-window wait no longer uses the 1,100-pixel threshold.' }
. ([scriptblock]::Create($receiptBuilder.Extent.Text))
. ([scriptblock]::Create($save.Extent.Text))

$script:stage = 'healthy-resize-large'
$script:primaryFailure = @{ type = 'TimeoutException'; category = 'WaitExpired'; hresult = $null; line = 472 }
$script:primaryWaitFailure = $null
$script:checkpointWriteFailed = $false
$script:cleanupFailure = $null
$script:initialFrame = @{ status = 'not-attempted'; image = $null; failure = $null }
Remove-Variable iconTrace,iconRegression,resizeDiag -Scope Script -ErrorAction SilentlyContinue
$empty = New-FailureDiagnostic
if ($null -ne $empty.iconRegression -or $null -ne $empty.resize -or $empty.stage -cne 'healthy-resize-large') {
    throw 'Failure receipt threw or invented observations before resize capture.'
}

$script:iconRegression = @{ legacyWidth = 16; legacyHeight = 16; legacyOutOfRange = $true }
$script:resizeDiag = @{
    smallBefore = @{ width = 1180; height = 800; x = 10; y = 20 }
    smallAfter = @{ width = 640; height = 600; x = 10; y = 20 }
    largeBefore = @{ width = 640; height = 600; x = 10; y = 20 }
    largeAfter = $null
    largeAfterImmediate = @{ width = 900; height = 700; x = 10; y = 20 }
    largeResizeOutcome = @{ started = $true; returned = $true }
    largeWait = @{ threshold = 1100; met = $false }
}
$diagnostic = New-FailureDiagnostic
if ($diagnostic.iconRegression.legacyWidth -ne 16 -or -not $diagnostic.iconRegression.legacyOutOfRange -or
    $diagnostic.resize.largeWait.threshold -ne 1100 -or $diagnostic.resize.largeWait.met -or
    $diagnostic.resize.largeAfterImmediate.width -ne 900 -or $null -ne $diagnostic.resize.largeAfter) {
    throw 'Failure receipt dropped the 1,100-pixel wait observations.'
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('resize-diagnostic-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
try {
    $EvidenceRoot = $root
    $script:canWrite = $false
    Save-ResizeDiagnostic
    if (Test-Path -LiteralPath (Join-Path $root 'resize-diagnostic.json')) { throw 'Resize diagnostic was written before evidence was writable.' }
    $script:canWrite = $true
    Save-ResizeDiagnostic
    $diagPath = Join-Path $root 'resize-diagnostic.json'
    $firstBytes = [IO.File]::ReadAllBytes($diagPath)
    $saved = [Text.Encoding]::UTF8.GetString($firstBytes) | ConvertFrom-Json
    if ($saved.largeWait.threshold -ne 1100 -or $saved.largeWait.met -or $saved.largeAfterImmediate.width -ne 900 -or $null -ne $saved.largeAfter) {
        throw 'resize-diagnostic.json did not keep the failed 1,100-pixel wait.'
    }
    Save-ResizeDiagnostic
    if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$firstBytes, [byte[]][IO.File]::ReadAllBytes($diagPath))) {
        throw 'A second resize receipt replaced the failed-wait observation.'
    }
    $receiptPath = Join-Path $root 'driver.json'
    $receipt = @{ schemaVersion = 1; succeeded = $false; phase = 'healthy'; failureCode = 'NativeGateFailed'; forcedCleanup = $false; diagnostic = $diagnostic }
    [IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Import-Module (Join-Path $PSScriptRoot '../../scripts/NativeShellRecoveryReport.psm1') -Force
    Test-ShellRecoveryFailureDiagnostic -Path $receiptPath -Directory $root | Out-Null
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
Write-Host 'Resize diagnostic: StrictMode failure receipt and failed 1,100-pixel wait file passed; threshold unchanged; no native window.'
