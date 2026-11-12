# Run the Linux-safe script checks and write Sonar generic coverage.
# Line hits come from breakpoints on the production scripts those checks execute.
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
    @{ File = 'tests/scripts/BusinessPluginReport.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/DeliveryHostVersion.Tests.ps1'; Args = @() },
    @{ File = 'tests/scripts/NativeShellRecoveryReport.Tests.ps1'; Args = @() }
)

$statementType = [System.Management.Automation.Language.StatementAst]
$files = @(
    Get-ChildItem -Path (Join-Path $repo 'scripts') -File |
        Where-Object { $_.Extension -eq '.ps1' -or $_.Extension -eq '.psm1' }
    Get-ChildItem -Path (Join-Path $repo 'tests/scripts') -File |
        Where-Object { ($_.Extension -eq '.ps1' -or $_.Extension -eq '.psm1') -and $_.Name -cne 'collect-powershell-coverage.ps1' }
)

$tracked = New-Object System.Collections.Generic.List[object]
$global:WorkbenchCoverageHits = @{}
foreach ($file in $files) {
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($null -eq $ast) { throw "Could not parse $($file.FullName)." }
    $lines = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($statement in $ast.FindAll({ param($node) $node -is $statementType }, $true)) {
        [void]$lines.Add($statement.Extent.StartLineNumber)
    }
    if ($lines.Count -lt 1) { continue }
    $full = [IO.Path]::GetFullPath($file.FullName)
    $relative = [IO.Path]::GetRelativePath($repo, $full).Replace('\', '/')
    $global:WorkbenchCoverageHits[$full] = [System.Collections.Generic.HashSet[int]]::new()
    $tracked.Add([pscustomobject]@{
        Full = $full
        Relative = $relative
        Lines = @($lines | Sort-Object)
    }) | Out-Null
}

$action = {
    try {
        $caller = (Get-PSCallStack)[1]
        $name = $caller.ScriptName
        if ([string]::IsNullOrEmpty($name)) { return }
        $set = $global:WorkbenchCoverageHits[$name]
        if ($null -eq $set) {
            $set = $global:WorkbenchCoverageHits[[IO.Path]::GetFullPath($name)]
        }
        if ($null -ne $set) { [void]$set.Add([int]$caller.ScriptLineNumber) }
    } catch {
    }
}

$breakpoints = New-Object System.Collections.Generic.List[object]
$snapshot = @{}
try {
    foreach ($entry in $tracked) {
        $accepted = New-Object System.Collections.Generic.List[int]
        foreach ($line in $entry.Lines) {
            try {
                $breakpoint = Set-PSBreakpoint -Script $entry.Full -Line $line -Action $action -ErrorAction Stop
                $breakpoints.Add($breakpoint) | Out-Null
                $accepted.Add($line) | Out-Null
            } catch {
                # Comments, blank lines, and pure punctuation are not breakpoint lines.
            }
        }
        $entry.Lines = @($accepted)
    }

    foreach ($case in $cases) {
        $full = Join-Path $repo $case.File
        $argv = [string[]]$case.Args
        Write-Host "coverage check: $($case.File) $($argv -join ' ')"
        Set-Location $repo
        # Array splatting turns "-Switch" into a string. The scriptblock parser keeps real switches.
        $argumentText = $argv -join ' '
        $escaped = $full.Replace("'", "''")
        $invoke = [scriptblock]::Create("& '$escaped' $argumentText")
        & $invoke
    }
    foreach ($entry in $tracked) {
        $snapshot[$entry.Full] = @($global:WorkbenchCoverageHits[$entry.Full])
    }
} finally {
    if ($breakpoints.Count -gt 0) {
        Remove-PSBreakpoint -Breakpoint $breakpoints.ToArray()
    }
    Remove-Variable -Name WorkbenchCoverageHits -Scope Global -ErrorAction SilentlyContinue
}

$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine('<coverage version="1">')
$coveredLines = 0
$totalLines = 0
foreach ($entry in ($tracked | Sort-Object Relative)) {
    if ($entry.Lines.Count -lt 1) { continue }
    $hits = [System.Collections.Generic.HashSet[int]]::new([int[]]$snapshot[$entry.Full])
    [void]$builder.AppendLine("  <file path=""$($entry.Relative)"">")
    foreach ($line in $entry.Lines) {
        $covered = $hits.Contains($line)
        $flag = if ($covered) { 'true' } else { 'false' }
        [void]$builder.AppendLine("    <lineToCover lineNumber=""$line"" covered=""$flag""/>")
        $totalLines++
        if ($covered) { $coveredLines++ }
    }
    [void]$builder.AppendLine('  </file>')
}
[void]$builder.AppendLine('</coverage>')
[IO.File]::WriteAllText($OutputPath, $builder.ToString())
Write-Host "PowerShell coverage: $coveredLines/$totalLines lines in $($tracked.Count) files -> $OutputPath"
if ($coveredLines -lt 1) { throw "PowerShell coverage recorded no executed lines." }
