[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Native publish requires Windows; use Core tests or explicit reference-only checks on Linux.' }
$repo = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repo 'artifacts/native'
$appOutput = Join-Path $outputRoot 'app'
function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
Push-Location $repo
try {
    # This fixed, repository-local output root prevents stale packages entering the distribution.
    if (Test-Path $outputRoot) { Remove-Item $outputRoot -Recurse -Force }
    New-Item $appOutput -ItemType Directory -Force | Out-Null
    $app = Join-Path $repo 'src/Workbench.App/Workbench.App.csproj'
    Invoke-DotNet -Arguments @('restore', $app, '-r', 'win-x64')
    Invoke-DotNet -Arguments @('publish', $app, '--no-restore', '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true', '-o', $appOutput)
    $samples = @(
        @{ Project = 'Workbench.Summary.Sample'; Id = 'com.projectworkbench.summary' },
        @{ Project = 'Workbench.TestCases.Sample'; Id = 'com.projectworkbench.test-cases' }
    )
    foreach ($sample in $samples) {
        $projectDirectory = Join-Path $repo "plugins/$($sample.Project)"
        $project = Join-Path $projectDirectory "$($sample.Project).csproj"
        $buildOutput = Join-Path $outputRoot "package-build/$($sample.Id)"
        Invoke-DotNet -Arguments @('restore', $project)
        Invoke-DotNet -Arguments @('build', $project, '--no-restore', '-c', $Configuration, '-o', $buildOutput)
        $destination = Join-Path $appOutput "Plugins/$($sample.Id)"
        New-Item $destination -ItemType Directory -Force | Out-Null
        # These samples have no private dependencies. Do not stage the host SDK/WinUI/runtime copies.
        foreach ($name in @("$($sample.Project).dll", "$($sample.Project).deps.json")) {
            Copy-Item (Join-Path $buildOutput $name) $destination
        }
        Copy-Item (Join-Path $projectDirectory 'plugin.json') $destination
    }
    if (-not (Test-Path (Join-Path $appOutput 'Workbench.App.exe'))) { throw 'Published executable is missing.' }
    Write-Host "Complete self-contained app directory: $appOutput"
}
finally { Pop-Location }
