param(
    [Parameter(Mandatory = $true)][int]$Port,
    [string]$OutFile = "",
    [string]$Title = "",
    [switch]$Reload
)

$ErrorActionPreference = "Stop"
if (-not $OutFile) { $OutFile = Join-Path (Get-Location) "scene-probe.json" }
$project = Join-Path $PSScriptRoot "..\tools\Workbench.SceneProbe\Workbench.SceneProbe.csproj"
$dotnetArgs = @("run", "--project", $project, "-c", "Release", "--", "--port", "$Port", "--out", $OutFile)
if ($Title) { $dotnetArgs += @("--title", $Title) }
if ($Reload) { $dotnetArgs += "--reload" }
& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Scene probe written locally to $OutFile"
