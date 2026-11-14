[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [switch]$IncludeSourceAcceptance)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$deliveryModule = Import-Module (Join-Path $PSScriptRoot 'TestingPluginDelivery.psm1') -Force -PassThru
$repo = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repo 'artifacts/native'
$appOutput = Join-Path $outputRoot 'app'
& $deliveryModule { param($p) Assert-TestingFreshDirectory $p } $outputRoot
$packageBuild = Join-Path $repo $(if ($IncludeSourceAcceptance) { 'artifacts/testing-source-acceptance-package-build' } else { 'artifacts/testing-package-build' })
& $deliveryModule { param($p) Assert-TestingFreshDirectory $p } $packageBuild
if (-not $IsWindows) { throw 'Native publish requires Windows; use Core tests or explicit reference-only checks on Linux.' }
function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
Push-Location $repo
try {
    # Earlier native artifacts/evidence are preserved; preflight above refuses any nonempty root.
    New-Item $appOutput -ItemType Directory -Force | Out-Null
    & (Join-Path $repo 'scripts/build-owned-lab.ps1') -Configuration $Configuration -OutputDirectory (Join-Path $outputRoot 'owned-lab')
    $app = Join-Path $repo 'src/Workbench.App/Workbench.App.csproj'
    Invoke-DotNet -Arguments @('restore', $app, '-r', 'win-x64')
    Invoke-DotNet -Arguments @('publish', $app, '--no-restore', '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true', '-o', $appOutput)
    # The unpackaged host needs its main resource map as well as framework PRI files.
    $mainPri = Join-Path $appOutput 'Workbench.App.pri'
    if (-not (Test-Path -LiteralPath $mainPri -PathType Leaf)) { throw "Published host PRI is missing: $mainPri" }
    $mainPriLength = (Get-Item -LiteralPath $mainPri).Length
    if ($mainPriLength -eq 0) { throw "Published host PRI is empty: $mainPri" }
    Write-Host "Published host PRI: $mainPri ($mainPriLength bytes)"
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
    # Acceptance fixture stays outside app/Plugins and is never discovered by ordinary startup.
    $fixtureProject = Join-Path $repo 'tests/Workbench.NativeFixture/Workbench.NativeFixture.csproj'
    $fixtureBuild = Join-Path $outputRoot 'fixture-build'
    Invoke-DotNet -Arguments @('restore', $fixtureProject)
    Invoke-DotNet -Arguments @('build', $fixtureProject, '--no-restore', '-c', $Configuration, '-o', $fixtureBuild)
    $fixtureOutput = Join-Path $outputRoot 'fixtures/runtime'
    New-Item $fixtureOutput -ItemType Directory -Force | Out-Null
    foreach ($name in @('Workbench.NativeFixture.dll', 'Workbench.NativeFixture.deps.json')) {
        Copy-Item (Join-Path $fixtureBuild $name) $fixtureOutput
    }
    Copy-Item (Join-Path $repo 'tests/Workbench.NativeFixture/plugin.json') $fixtureOutput
    # Independently installable testing ZIP stays outside app/Plugins and the frozen business companion.
    if ($IncludeSourceAcceptance) {
        & (Join-Path $repo 'scripts/package-testing-plugin.ps1') -Configuration $Configuration -Profile SourceAcceptance -OutputDirectory (Join-Path $outputRoot 'source-acceptance-fixture') -PairedProductionDirectory (Join-Path $outputRoot 'testing-plugin')
    }
    else { & (Join-Path $repo 'scripts/package-testing-plugin.ps1') -Configuration $Configuration -OutputDirectory (Join-Path $outputRoot 'testing-plugin') }
    if (-not (Test-Path (Join-Path $appOutput 'Workbench.App.exe'))) { throw 'Published executable is missing.' }
    Write-Host "Complete self-contained app directory: $appOutput"
}
finally { Pop-Location }
