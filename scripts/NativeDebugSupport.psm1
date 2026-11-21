function New-NativeDebugStartInfo {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string]$WorkingDirectory, [Parameter(Mandatory)][string[]]$Arguments)
    $start = [Diagnostics.ProcessStartInfo]::new($FilePath)
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment.Clear()
    # OS/runtime/proxy settings only. Do not inherit CI tokens or print environment values.
    foreach ($name in @('SystemRoot', 'windir', 'SystemDrive', 'PATH', 'PATHEXT', 'ComSpec', 'TEMP', 'TMP', 'USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramW6432', 'PROCESSOR_ARCHITECTURE', 'NUMBER_OF_PROCESSORS', 'HTTP_PROXY', 'HTTPS_PROXY', 'NO_PROXY')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) { $start.Environment[$name] = $value }
    }
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    return $start
}
Export-ModuleMember -Function New-NativeDebugStartInfo
