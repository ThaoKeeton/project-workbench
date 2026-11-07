[CmdletBinding()]
param([Parameter(Mandatory)][string]$SnapshotRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Assert-SdkSnapshot {
    param([string]$Root)
    $manifest = Get-Content -LiteralPath (Join-Path $Root 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.sdkVersion -ne '0.3.0' -or $manifest.fileVersion -ne '0.3.0.0' -or $manifest.assemblyVersion -ne '1.0.0.0' -or $manifest.workbenchApi -ne 1 -or $manifest.runtimeServices -ne 1 -or $manifest.projectServices -ne 1 -or $manifest.windowsAppSdk -ne '1.8.260921001') { throw 'Unexpected SDK contract versions.' }
    foreach ($source in $manifest.sources) {
        $path = Join-Path $Root "source/$($source.path)"
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.sha256) { throw 'SDK source integrity failure.' }
    }
    $expectedSources = @('Directory.Build.props', 'global.json', 'src/Workbench.Sdk/Workbench.Sdk.csproj',
        'src/Workbench.Sdk/PluginContracts.cs', 'src/Workbench.Sdk/RuntimeContracts.cs', 'src/Workbench.Sdk/ProjectContracts.cs', 'src/Workbench.Sdk/README.md',
        'src/Workbench.WinUI.Sdk/Workbench.WinUI.Sdk.csproj', 'src/Workbench.WinUI.Sdk/WinUiView.cs', 'src/Workbench.WinUI.Sdk/README.md')
    if (@($manifest.sources).Count -ne 10 -or @(Compare-Object @($manifest.sources.path) $expectedSources).Count -ne 0) { throw 'SDK source allow-list must include both consumer READMEs.' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $Root 'source') -File -Recurse).Count -ne @($manifest.sources).Count) { throw 'Source snapshot includes unlisted files.' }
    if (@($manifest.packages).Count -ne 2) { throw 'Expected exactly two SDK packages.' }
    foreach ($package in $manifest.packages) {
        $path = Join-Path $Root $package.path
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha256) { throw 'SDK package integrity failure.' }
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        $temporary = Join-Path ([IO.Path]::GetTempPath()) "workbench-sdk-test-$([Guid]::NewGuid().ToString('N')).dll"
        try {
            $assemblies = @($zip.Entries | Where-Object FullName -like '*.dll')
            if ($assemblies.Count -ne 1 -or $assemblies[0].Name -notin @('Workbench.Sdk.dll', 'Workbench.WinUI.Sdk.dll')) { throw 'An SDK package included host, Desktop or private runtime binaries.' }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($assemblies[0], $temporary)
            if ([Reflection.AssemblyName]::GetAssemblyName($temporary).Version.ToString() -ne '1.0.0.0') { throw 'Stable SDK assembly identity changed.' }
            if ([Diagnostics.FileVersionInfo]::GetVersionInfo($temporary).FileVersion -ne '0.3.0.0') { throw 'SDK file version does not match the contract.' }
            $readme = $zip.GetEntry('README.md')
            if ($null -eq $readme -or $readme.Length -eq 0) { throw 'An SDK package omitted its consumer README.' }
            $reader = [IO.StreamReader]::new($zip.GetEntry("$([IO.Path]::GetFileNameWithoutExtension($assemblies[0].Name)).nuspec").Open())
            try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ($nuspec.package.metadata.version -ne '0.3.0') { throw 'SDK NuGet version does not match the contract.' }
            if ($nuspec.package.metadata.readme -ne 'README.md') { throw 'An SDK package did not declare its README.' }
            $readmeSource = Join-Path $Root "source/src/$([IO.Path]::GetFileNameWithoutExtension($assemblies[0].Name))/README.md"
            $stream = $readme.Open()
            try { $readmeHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
            if ($readmeHash -ne (Get-FileHash -LiteralPath $readmeSource -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'SDK packaged README differs from its listed source.' }
            if (-not $manifest.referenceOnly -and $assemblies[0].Name -eq 'Workbench.WinUI.Sdk.dll') {
                $pri = @($zip.Entries | Where-Object FullName -like 'lib/*/Workbench.WinUI.Sdk.pri')
                if ($pri.Count -ne 1 -or $pri[0].Length -eq 0) { throw 'Native WinUI SDK package omitted its real PRI.' }
            }
        }
        finally { $zip.Dispose(); if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
    }
}
Assert-SdkSnapshot $SnapshotRoot
$damaged = Join-Path ([IO.Path]::GetTempPath()) "workbench-sdk-tamper-$([Guid]::NewGuid().ToString('N'))"
try {
    Copy-Item -LiteralPath $SnapshotRoot -Destination $damaged -Recurse
    Add-Content -LiteralPath (Join-Path $damaged 'source/src/Workbench.Sdk/RuntimeContracts.cs') -Value '// synthetic integrity mutation'
    $rejected = $false
    try { Assert-SdkSnapshot $damaged }
    catch { if ($_.Exception.Message -ne 'SDK source integrity failure.') { throw }; $rejected = $true }
    if (-not $rejected) { throw 'Snapshot validation accepted a changed source.' }
}
finally { if (Test-Path -LiteralPath $damaged) { Remove-Item -LiteralPath $damaged -Recurse -Force } }
Write-Output 'SDK snapshot regressions: ten source hashes/allow-list, both packaged READMEs, two package hashes, stable assembly/file versions, native PRI when applicable and source tamper rejection passed'
