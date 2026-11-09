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
