#requires -Version 7.5
[CmdletBinding()]
param([AllowEmptyString()][string]$ProducerRunId = '', [AllowEmptyString()][string]$ArtifactId = '',
      [AllowEmptyString()][string]$HostSource = '', [AllowEmptyString()][string]$ArchiveSha256 = '',
      [string]$EvidenceRoot = (Join-Path $PSScriptRoot '../artifacts/native/shell-recovery-probes'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$canWrite = $false; $phase = 'preflight'; $driver = $null; $api = $null; $downloadClient = $null
$driverSource = $null; $exeHash = $null; $dllHash = $null; $producer = $null
$unverified = @('blocking-file','acl-denial','drag','snap','high-dpi','high-contrast','taskbar','windows-11','visual-review')
$safeHost = $null; $safeRun = $null; $safeArtifact = $null; $safeArchive = $null
function Exists-Exact([string]$Path) {
    try { $null = [IO.File]::GetAttributes($Path); return $true }
    catch [IO.FileNotFoundException] { return $false }
    catch [IO.DirectoryNotFoundException] { return $false }
}
function Reject-Links([string]$Path) {
    for ($part = [IO.Path]::GetFullPath($Path); $part; $part = [IO.Path]::GetDirectoryName($part)) {
        if (Exists-Exact $part) { if (([IO.File]::GetAttributes($part) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'UnsafePath' } }
    }
}
if (-not ('ShellRecoveryExclusiveDirectory' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShellRecoveryExclusiveDirectory {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern bool CreateDirectoryW(string path, IntPtr security);
    public static void Create(string path) { if (!CreateDirectoryW(path, IntPtr.Zero)) throw new InvalidOperationException(); }
    public static async System.Threading.Tasks.Task<long> Drain(System.IO.TextReader reader) {
        var buffer = new char[4096]; long count = 0; int n;
        while ((n = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0) count = Math.Min(65537, count + n);
        return count;
    }
}
'@
}
function Write-NewJson([string]$Name,$Value) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 10))
    $stream = [IO.File]::Open((Join-Path $EvidenceRoot $Name),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
}
function New-HttpClient([bool]$Authenticated) {
    $handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromMinutes(6)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('ProjectWorkbench-ShellRecovery-CI')
    $client.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
    if ($Authenticated) { $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$env:SHELL_RECOVERY_API_TOKEN) }
    return $client
}
function Read-ApiJson([string]$Relative) {
    $uri = 'https://api.github.com/repos/ThaoKeeton/project-workbench/' + $Relative
    $response = $api.GetAsync($uri,[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    try {
        if ([int]$response.StatusCode -ne 200 -or ($null -ne $response.Content.Headers.ContentLength -and $response.Content.Headers.ContentLength -gt 1048576)) { throw 'ApiMetadataUnavailable' }
        $stream = $response.Content.ReadAsStream(); $memory = [IO.MemoryStream]::new(); $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromMinutes(1))
        try {
            $buffer = [byte[]]::new(16384)
            while (($read = $stream.ReadAsync($buffer,0,$buffer.Length,$deadline.Token).GetAwaiter().GetResult()) -gt 0) {
                if ($memory.Length + $read -gt 1048576) { throw 'ApiMetadataOversize' }; $memory.Write($buffer,0,$read)
            }
            $text = [Text.UTF8Encoding]::new($false,$true).GetString($memory.ToArray())
            return ConvertFrom-Json -InputObject $text -AsHashtable -Depth 32 -DateKind String
        } finally { $deadline.Dispose(); $memory.Dispose(); $stream.Dispose() }
    } finally { $response.Dispose() }
}
function Save-ArtifactArchive([string]$Path) {
    $uri = "https://api.github.com/repos/ThaoKeeton/project-workbench/actions/artifacts/$ArtifactId/zip"
    $response = $api.GetAsync($uri,[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    try {
        if ([int]$response.StatusCode -ne 302 -or $null -eq $response.Headers.Location) { throw 'ArtifactRedirectUnavailable' }
        $location = $response.Headers.Location
        if (-not $location.IsAbsoluteUri -or $location.Scheme -cne 'https' -or $location.Port -ne 443 -or $location.UserInfo -or
            -not ($location.Host.EndsWith('.blob.core.windows.net',[StringComparison]::OrdinalIgnoreCase) -or
                  $location.Host.EndsWith('.actions.githubusercontent.com',[StringComparison]::OrdinalIgnoreCase) -or
                  $location.Host.Equals('objects.githubusercontent.com',[StringComparison]::OrdinalIgnoreCase))) { throw 'ArtifactRedirectInvalid' }
        $download = $downloadClient.GetAsync($location,[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            if ([int]$download.StatusCode -ne 200 -or ($null -ne $download.Content.Headers.ContentLength -and $download.Content.Headers.ContentLength -gt 1073741824)) { throw 'ArtifactDownloadUnavailable' }
            $inputStream = $download.Content.ReadAsStream(); $output = [IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None); $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromMinutes(5))
            try {
                $buffer = [byte[]]::new(65536); $total=0L
                while (($read=$inputStream.ReadAsync($buffer,0,$buffer.Length,$deadline.Token).GetAwaiter().GetResult()) -gt 0) { $total += $read; if ($total -gt 1073741824) { throw 'ArtifactDownloadOversize' }; $output.Write($buffer,0,$read) }
                $output.Flush($true)
            } finally { $deadline.Dispose(); $output.Dispose(); $inputStream.Dispose() }
        } finally { $download.Dispose() }
    } finally { $response.Dispose() }
}
try {
    if (-not $IsWindows -or $env:CI -cne 'true' -or $env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or -not [Environment]::UserInteractive) { throw 'UnsafeEnvironment' }
    if ($ProducerRunId -cmatch '\A[1-9][0-9]{0,17}\z') { $safeRun=[long]$ProducerRunId }
    if ($ArtifactId -cmatch '\A[1-9][0-9]{0,17}\z') { $safeArtifact=[long]$ArtifactId }
    if ($HostSource -cmatch '\A[0-9a-f]{40}\z') { $safeHost=$HostSource }
    if ($ArchiveSha256 -cmatch '\A[0-9a-f]{64}\z') { $safeArchive=$ArchiveSha256 }
    $EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
    $userRoot = [IO.Path]::Combine([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData),'ProjectWorkbench')
    Reject-Links $userRoot; Reject-Links $EvidenceRoot
    if (Exists-Exact $userRoot) { throw 'NormalRootExists' }
    $prefix = $userRoot.TrimEnd('\') + '\'
    if ($EvidenceRoot.Equals($userRoot,[StringComparison]::OrdinalIgnoreCase) -or $EvidenceRoot.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or (Exists-Exact $EvidenceRoot)) { throw 'EvidenceNotFresh' }
    $allowedArtifact = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/native')).TrimEnd('\') + '\'
    $allowedTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
    if (-not ($EvidenceRoot.StartsWith($allowedArtifact,[StringComparison]::OrdinalIgnoreCase) -or $EvidenceRoot.StartsWith($allowedTemp,[StringComparison]::OrdinalIgnoreCase))) { throw 'EvidenceOutsideAllowedRoot' }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($EvidenceRoot)) | Out-Null
    [ShellRecoveryExclusiveDirectory]::Create($EvidenceRoot)
    $marker = [IO.File]::Open((Join-Path $EvidenceRoot '.owned'),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None); $marker.Dispose()
    $canWrite = $true; $phase = 'input-validation'
    if ($null -eq $safeRun -or $null -eq $safeArtifact -or $null -eq $safeHost -or $null -eq $safeArchive) { throw 'InputInvalid' }
    Import-Module (Join-Path $PSScriptRoot 'NativeShellRecoveryReport.psm1') -Force
    $candidateSource = (& git -C $repo rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'DriverSourceInvalid' }
    Test-ShellRecoveryDriverSource -ActualSource $candidateSource -ExpectedSource $env:GITHUB_SHA
    $driverSource = $candidateSource
    $dirty = @(& git -C $repo status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'DriverSourceDirty' }
    $phase = 'portable-regressions'
    # Tests use synthetic data and do not emit their potentially path-bearing errors.
    try { & (Join-Path $repo 'tests/scripts/NativeShellRecoveryReport.Tests.ps1') *> $null }
    catch { throw 'PortableRegressionsFailed' }
    if (Exists-Exact $userRoot) { throw 'NormalRootExists' }
    if ([string]::IsNullOrWhiteSpace($env:SHELL_RECOVERY_API_TOKEN)) { throw 'ApiTokenUnavailable' }
    $phase = 'producer-metadata'
    $api = New-HttpClient $true; $downloadClient = New-HttpClient $false
    $run = Read-ApiJson "actions/runs/$ProducerRunId"
    $artifact = Read-ApiJson "actions/artifacts/$ArtifactId"
    $producer = Test-ShellRecoveryProducer -Run $run -Artifact $artifact -RunId $ProducerRunId -ArtifactId $ArtifactId -HostSource $HostSource -DriverSource $driverSource -ArchiveSha256 $ArchiveSha256
    $producer['workflowId'] = 409147502; $producer['repository'] = 'ThaoKeeton/project-workbench'; $producer['runStatus'] = 'completed'; $producer['runConclusion'] = 'success'; $producer['runEvent'] = 'workflow_dispatch'
    Write-NewJson 'producer.json' $producer
    $phase = 'artifact-download'
    $tempRoot = [IO.Path]::GetFullPath((Join-Path $env:RUNNER_TEMP ('workbench-shell-recovery-' + [Guid]::NewGuid().ToString('N'))))
    Reject-Links $tempRoot
    if (Exists-Exact $tempRoot) { throw 'ArtifactRootExists' }
    [ShellRecoveryExclusiveDirectory]::Create($tempRoot)
    $archive = Join-Path $tempRoot 'original-a1.zip'; Save-ArtifactArchive $archive
    $api.Dispose(); $api=$null; $downloadClient.Dispose(); $downloadClient=$null
    [Environment]::SetEnvironmentVariable('SHELL_RECOVERY_API_TOKEN',$null,'Process')
    $phase = 'artifact-validation'
    $app = Join-Path $tempRoot 'app'
    $extraction = Expand-ShellRecoveryArchive -Archive $archive -Destination $app -ExpectedSha256 $ArchiveSha256
    $exe = Join-Path $app 'Workbench.App.exe'; $dll = Join-Path $app 'Workbench.App.dll'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll)
    Test-ShellRecoveryHostVersion -ProductVersion $version.ProductVersion -FileVersion $version.FileVersion -AssemblyVersion ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString()) -HostSource $HostSource
    $assets = [ordered]@{}
    foreach ($relative in @('Workbench.App.exe','Workbench.App.dll','Workbench.App.pri','Assets/Workbench.ico','Assets/Workbench.svg')) {
        $path=Join-Path $app $relative; Reject-Links $path; $file=Get-Item -LiteralPath $path -Force
        if ($file.PSIsContainer -or $file.Length -lt 1 -or $file.Length -gt 536870912) { throw 'HostAssetInvalid' }
        $assets[$relative]=@{ bytes=[long]$file.Length; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $exeHash=$assets['Workbench.App.exe'].sha256; $dllHash=$assets['Workbench.App.dll'].sha256
    Write-NewJson 'host.json' @{ hostSource=$HostSource; driverSource=$driverSource; producerRunId=[long]$ProducerRunId; artifactId=[long]$ArtifactId; artifactName='workbench-win-x64'; archiveSha256=$ArchiveSha256; archiveBytes=$extraction.archiveBytes; files=$assets; productVersion=$version.ProductVersion; fileVersion=$version.FileVersion; assemblyVersion='1.0.0.0' }
    if (Exists-Exact $userRoot) { throw 'NormalRootExists' }
    $phase = 'native-driver'
    $ps = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $script = Join-Path $repo 'tests/Workbench.NativeShellRecovery/drive-shell-recovery.ps1'
    $start = [Diagnostics.ProcessStartInfo]::new(); $start.FileName=$ps; $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.Environment.Clear()
    foreach ($name in @('SystemRoot','WINDIR','SystemDrive','ComSpec','TEMP','TMP','USERPROFILE','LOCALAPPDATA','APPDATA','HOMEDRIVE','HOMEPATH','ProgramData','ProgramFiles','ProgramFiles(x86)','ProgramW6432','CommonProgramFiles','CommonProgramFiles(x86)','CommonProgramW6432','OS','PROCESSOR_ARCHITECTURE','PROCESSOR_IDENTIFIER','NUMBER_OF_PROCESSORS','USERNAME','USERDOMAIN','COMPUTERNAME','SESSIONNAME','PSModulePath','CI','GITHUB_ACTIONS','RUNNER_ENVIRONMENT')) {
        $value=[Environment]::GetEnvironmentVariable($name); if ($value) { $start.Environment[$name]=$value }
    }
    $start.Environment['PATH'] = (Join-Path $env:SystemRoot 'System32') + ';' + $env:SystemRoot
    foreach ($arg in @('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-STA','-File',$script,'-HostExe',$exe,'-EvidenceRoot',$EvidenceRoot,'-ExpectedSourceCommit',$HostSource,'-ExeHash',$exeHash,'-DllHash',$dllHash)) { $null=$start.ArgumentList.Add($arg) }
    $driver=[Diagnostics.Process]::Start($start); if ($null -eq $driver) { throw 'DriverStartFailed' }
    $stdout=[ShellRecoveryExclusiveDirectory]::Drain($driver.StandardOutput); $stderr=[ShellRecoveryExclusiveDirectory]::Drain($driver.StandardError); $clock=[Diagnostics.Stopwatch]::StartNew()
    while (-not $driver.WaitForExit(1000) -and $clock.Elapsed.TotalSeconds -lt 420) { }
    if (-not $driver.HasExited) { $driver.Kill(); $driver.WaitForExit(); $phase='driver-timeout'; throw 'DriverTimeout' }
    $driver.WaitForExit(); $exitCode=$driver.ExitCode; $candidate=Join-Path $EvidenceRoot 'driver.json'
    if ($stdout.GetAwaiter().GetResult() -gt 65536 -or $stderr.GetAwaiter().GetResult() -gt 65536) { throw 'DriverOutputOversize' }
    if ($exitCode -ne 0 -or -not (Exists-Exact $candidate)) { throw 'DriverFailed' }
    $phase = 'receipt-validation'
    foreach ($relative in $assets.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $app $relative) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $assets[$relative].sha256) { throw 'HostChanged' }
    }
    Test-ShellRecoveryFinal -Path $candidate -Directory $EvidenceRoot -ExpectedSourceCommit $HostSource -ExpectedExeSha256 $exeHash -ExpectedDllSha256 $dllHash | Out-Null
    Write-NewJson 'final.json' @{ schemaVersion=1; succeeded=$true; hostSource=$HostSource; driverSource=$driverSource; producerRunId=[long]$ProducerRunId; artifactId=[long]$ArtifactId; archiveSha256=$ArchiveSha256; executableSha256=$exeHash; dllSha256=$dllHash; driverReceiptSha256=(Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant(); driverExitCode=$exitCode; failureCode=$null; unverified=$unverified }
    Write-Host 'Owned shell recovery checks passed launcher validation; original frames require visual review.'
} catch {
    if ($canWrite) {
        try { Write-NewJson 'final.json' @{ schemaVersion=1; succeeded=$false; hostSource=$safeHost; driverSource=$driverSource; producerRunId=$safeRun; artifactId=$safeArtifact; archiveSha256=$safeArchive; executableSha256=$exeHash; dllSha256=$dllHash; phase=$phase; failureCode='ShellRecoveryFailed'; forcedCleanup=$null; pickerCleanup='unverified'; unverified=$unverified } } catch { [Console]::Error.WriteLine('Could not write the sanitized final receipt.') }
    }
    [Console]::Error.WriteLine('Owned shell recovery gate failed; partial evidence remains available.')
    exit 1
} finally {
    if ($null -ne $api) { $api.Dispose() }; if ($null -ne $downloadClient) { $downloadClient.Dispose() }; if ($null -ne $driver) { $driver.Dispose() }
    [Environment]::SetEnvironmentVariable('SHELL_RECOVERY_API_TOKEN',$null,'Process')
}
