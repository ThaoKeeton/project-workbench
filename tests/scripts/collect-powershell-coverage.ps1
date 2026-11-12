# Run the Linux-safe script checks under Pester and write Sonar generic coverage.
# The checks already exist; this only records which script lines they execute.
[CmdletBinding()]
param(
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $repo
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repo 'artifacts/coverage/powershell.xml'
}
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath -Parent) | Out-Null

if (-not (Get-Module -ListAvailable -Name Pester | Where-Object { $_.Version.Major -ge 5 })) {
    Install-Module -Name Pester -MinimumVersion 5.5.0 -Force -Scope CurrentUser -AllowClobber
}
Import-Module Pester -MinimumVersion 5.5.0

$cases = @(
    @{ File = 'tests/scripts/NativeSmokeReport.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/WorkspaceProbeReport.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/WorkspaceProbePhase.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/TestingPluginProbe.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/SourceAcceptanceProbe.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/TestingPluginPackage.Tests.ps1'; Args = @('-HelperGuardsOnly') },
    @{ File = 'tests/scripts/TestingPluginPackage.Tests.ps1'; Args = @('-HelperGuardsOnly', '-Profile', 'SourceAcceptance') },
    @{ File = 'tests/scripts/TestingPluginPackage.Tests.ps1'; Args = @('-StructuralGuardsOnly') },
    @{ File = 'tests/scripts/NativeGalleryReport.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/OwnedLabBuild.Tests.ps1'; Args = @('-Mode', 'ToolchainContract') },
    @{ File = 'tests/scripts/OwnedLabProbeStaging.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/OwnedLabProbeOutput.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/NativeDebugSupport.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/BusinessPluginImport.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/BusinessPluginReport.Tests.ps1'; Args = @() }
)

$containers = foreach ($case in $cases) {
    $full = Join-Path $repo $case.File
    # Switches must stay unquoted or PowerShell binds them as strings.
    $argumentText = $case.Args -join ' '
    $command = "& '$($full -replace "'", "''")' $argumentText"
    $label = ("$($case.File) $argumentText").Trim()
    New-PesterContainer -ScriptBlock {
        param($Label, $Command)
        Describe $Label {
            It 'runs the existing check' {
                Invoke-Expression $Command
            }
        }
    } -Data @{ Label = $label; Command = $command }
}

$lcov = Join-Path $repo 'artifacts/coverage/powershell.lcov'
$config = New-PesterConfiguration
$config.Run.Container = $containers
$config.Run.PassThru = $true
$config.Output.Verbosity = 'Normal'
$config.CodeCoverage.Enabled = $true
$config.CodeCoverage.Path = @(
    Get-ChildItem -Path (Join-Path $repo 'scripts') -File |
        Where-Object { $_.Extension -eq '.ps1' -or $_.Extension -eq '.psm1' } |
        ForEach-Object { $_.FullName }
)
$config.CodeCoverage.OutputFormat = 'CoverageGutters'
$config.CodeCoverage.OutputPath = $lcov
$config.CodeCoverage.CoveragePercentTarget = 0

$result = Invoke-Pester -Configuration $config
if ($result.FailedCount -gt 0) {
    throw "PowerShell coverage checks failed: $($result.FailedCount)."
}
if (-not (Test-Path -LiteralPath $lcov)) {
    throw "Pester did not write $lcov."
}

$hits = @{}
$current = $null
foreach ($line in [IO.File]::ReadAllLines($lcov)) {
    if ($line.StartsWith('SF:')) {
        $source = $line.Substring(3)
        $path = if ([IO.Path]::IsPathRooted($source)) { $source } else { Join-Path $repo $source }
        $full = [IO.Path]::GetFullPath($path)
        $relative = [IO.Path]::GetRelativePath($repo, $full).Replace('\', '/')
        if ($relative.StartsWith('scripts/')) {
            $current = $relative
            if (-not $hits.ContainsKey($current)) { $hits[$current] = @{} }
        } else {
            $current = $null
        }
    } elseif ($current -and $line.StartsWith('DA:')) {
        $pair = $line.Substring(3).Split(',')
        $number = [int]$pair[0]
        $count = [int]$pair[1]
        if (-not $hits[$current].ContainsKey($number) -or $count -gt $hits[$current][$number]) {
            $hits[$current][$number] = $count
        }
    }
}

$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine('<coverage version="1">')
foreach ($file in ($hits.Keys | Sort-Object)) {
    [void]$builder.AppendLine("  <file path=""$file"">")
    foreach ($number in ($hits[$file].Keys | Sort-Object)) {
        $covered = if ($hits[$file][$number] -gt 0) { 'true' } else { 'false' }
        [void]$builder.AppendLine("    <lineToCover lineNumber=""$number"" covered=""$covered""/>")
    }
    [void]$builder.AppendLine('  </file>')
}
[void]$builder.AppendLine('</coverage>')
[IO.File]::WriteAllText($OutputPath, $builder.ToString())
$coveredLines = 0
$totalLines = 0
foreach ($file in $hits.Keys) {
    foreach ($number in $hits[$file].Keys) {
        $totalLines++
        if ($hits[$file][$number] -gt 0) { $coveredLines++ }
    }
}
Write-Host "PowerShell coverage: $coveredLines/$totalLines lines in $($hits.Keys.Count) files -> $OutputPath"
if ($coveredLines -lt 1) { throw "PowerShell coverage recorded no executed lines." }
