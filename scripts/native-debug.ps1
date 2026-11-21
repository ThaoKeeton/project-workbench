[CmdletBinding()]
param([ValidateRange(30, 600)][int]$TimeoutSeconds = 240)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Native exception collection requires Windows CDB.' }
Import-Module (Join-Path $PSScriptRoot 'NativeDebugSupport.psm1') -Force
$repo = Split-Path $PSScriptRoot -Parent
$appDirectory = Join-Path $repo 'artifacts/native/app'
$app = Join-Path $appDirectory 'Workbench.App.exe'
$evidence = Join-Path $repo 'artifacts/native/native-debug'
New-Item $evidence -ItemType Directory -Force | Out-Null
$runnerTemporary = [Environment]::GetEnvironmentVariable('RUNNER_TEMP')
if ([string]::IsNullOrEmpty($runnerTemporary)) { $runnerTemporary = [IO.Path]::GetTempPath() }
$toolCache = Join-Path $runnerTemporary 'workbench-native-debug-tools'
New-Item $toolCache -ItemType Directory -Force | Out-Null
$readiness = @{ stage = 'locating-cdb'; collectorCompleted = $false; dumpsCreated = $false }
function Save-Readiness { $readiness | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'collector.json') -Encoding utf8 }
function Find-Cdb {
    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $candidate = Join-Path $programFilesX86 'Windows Kits/10/Debuggers/x64/cdb.exe'
    if (Test-Path $candidate) { return $candidate }
    $command = Get-Command cdb.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    return $null
}
function Confirm-MicrosoftSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature $Path
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
        throw "Microsoft signature verification failed for $([IO.Path]::GetFileName($Path)): $($signature.Status)"
    }
    return $signature.Status.ToString()
}
function Invoke-TextProcess([string]$FilePath, [string]$Directory, [string[]]$Arguments, [int]$Seconds, [string]$LogPrefix) {
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = New-NativeDebugStartInfo -FilePath $FilePath -WorkingDirectory $Directory -Arguments $Arguments
    try {
        $null = $process.Start()
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($Seconds * 1000)
        if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
        [IO.File]::WriteAllText($LogPrefix + '-stdout.log', $output.GetAwaiter().GetResult())
        [IO.File]::WriteAllText($LogPrefix + '-stderr.log', $errors.GetAwaiter().GetResult())
        return @{ exitCode = $process.ExitCode; timedOut = $timedOut }
    }
    finally { $process.Dispose() }
}
try {
    Save-Readiness
    if (-not (Test-Path $app)) { throw "Published workbench executable is missing: $app" }
    $cdb = Find-Cdb
    if ([string]::IsNullOrEmpty($cdb)) {
        $readiness.stage = 'installing-official-sdk-debuggers'; Save-Readiness
        # Official Microsoft Windows SDK 10.0.26100.4654 installer, linked by the SDK download page.
        $installerUri = 'https://download.microsoft.com/download/696beb13-858a-4361-bd85-196f22394c93/KIT_BUNDLE_WINDOWSSDK_MEDIACREATION/winsdksetup.exe'
        $readiness.installerUri = $installerUri
        $installer = Join-Path $toolCache 'winsdksetup.exe'
        Invoke-WebRequest -Uri $installerUri -OutFile $installer
        $readiness.installerSignature = Confirm-MicrosoftSignature $installer
        $readiness.installerSha256 = (Get-FileHash $installer -Algorithm SHA256).Hash
        $installation = Invoke-TextProcess -FilePath $installer -Directory $toolCache -Arguments @('/features', 'OptionId.WindowsDesktopDebuggers', '/quiet', '/norestart') -Seconds 600 -LogPrefix (Join-Path $toolCache 'sdk-install')
        if ($installation.timedOut -or $installation.exitCode -notin @(0, 3010)) { throw "Debugger-only SDK installation failed: exit=$($installation.exitCode), timeout=$($installation.timedOut)" }
        $cdb = Find-Cdb
        if ([string]::IsNullOrEmpty($cdb)) { throw 'SDK installation returned without an x64 CDB executable.' }
    }
    $readiness.cdbPath = $cdb
    $readiness.cdbSignature = Confirm-MicrosoftSignature $cdb
    $readiness.cdbSha256 = (Get-FileHash $cdb -Algorithm SHA256).Hash
    $readiness.cdbVersion = (Get-Item $cdb).VersionInfo.FileVersion
    $readiness.stage = 'collecting-live-native-text'; Save-Readiness
    $symbols = Join-Path $toolCache 'symbols'
    New-Item $symbols -ItemType Directory -Force | Out-Null
    $commandFile = Join-Path $evidence 'commands.txt'
    # CDB runs in the published app directory. Logs/scripts contain only paths, exception text and stacks.
    # No dump, heap/environment inspection or system-wide postmortem registration is performed.
    @'
.sympath "srv*__SYMBOLS__*https://msdl.microsoft.com/download/symbols"
r $t10 = 0
sxd -c2 ".exr -1;.ecxr;kv 0n40;!analyze -v;q" *
sxe -c ".exr -1;.ecxr;kv 0n40;!analyze -v;q" 0xc000027b
sxe -c ".exr -1;.ecxr;kv 0n40;!analyze -v;q" 0xc0000602
bu combase!RoOriginateErrorW ".if (@$t10 < 0n64) {r $t10 = @$t10 + 1; .printf \"RO_ORIGINATE_W HRESULT=0x%08x\n\", @ecx; .if (@r8 != 0) {.printf \"message=%mu\n\", @r8;}; kv 0n20;}; gc"
bu combase!RoOriginateLanguageException ".if (@$t10 < 0n64) {r $t10 = @$t10 + 1; .printf \"RO_LANGUAGE HRESULT=0x%08x\n\", @ecx; kv 0n20;}; gc"
g
'@.Replace('__SYMBOLS__', $symbols) | Set-Content $commandFile -Encoding ascii
    $debugReport = Join-Path $evidence 'debug-smoke.json'
    $collection = Invoke-TextProcess -FilePath $cdb -Directory $appDirectory -Arguments @('-G', '-o', '-logou', (Join-Path $evidence 'cdb.log'), '-cf', $commandFile, $app, '--smoke-test', '--smoke-report', $debugReport) -Seconds $TimeoutSeconds -LogPrefix (Join-Path $evidence 'debugger')
    $readiness.debuggerExitCode = $collection.exitCode
    $readiness.debuggerTimedOut = $collection.timedOut
    if ($collection.timedOut) { throw "CDB collection timed out after $TimeoutSeconds seconds; available text is preserved." }
    if (-not (Test-Path (Join-Path $evidence 'cdb.log'))) { throw 'CDB did not produce its required text log.' }
    $readiness.stage = 'native-text-collected'
    $readiness.collectorCompleted = $true
    Save-Readiness
    Write-Output 'Collected live native debugger text. The original smoke failure remains unchanged.'
}
catch {
    $readiness.stage = 'collector-blocked'
    $readiness.error = $_.Exception.Message
    Save-Readiness
    Write-Error $_.Exception.Message
    exit 1
}
