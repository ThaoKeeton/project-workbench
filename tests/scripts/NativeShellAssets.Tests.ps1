[CmdletBinding()]
param(
    [string]$Project = (Join-Path $PSScriptRoot '../../src/Workbench.App/Workbench.App.csproj'),
    [string]$ArtifactsPath = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectPath = [IO.Path]::GetFullPath($Project)
$projectRoot = [IO.Path]::GetDirectoryName($projectPath)
$dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
if (-not $dotnet) { throw 'The configured .NET SDK is required.' }
# Use the restored App project's real WinUI imports. Suppress unrelated native build
# and makepri tasks while executing the actual PRI input projection.
$arguments = @('msbuild', $projectPath, '-nologo', '-t:GetPriOutputs',
    '-p:Configuration=Release', '-p:RuntimeIdentifier=win-x64', '-p:SelfContained=true',
    '-p:BuildProjectReferences=false', '-p:AppxGeneratePriEnabled=true', '-p:WindowsAppSDKSelfContained=false',
    '-p:GetPriOutputsDependsOn=', '-p:IncludePriFilesOutputGroup=false', '-p:IncludeProjectPriFile=false', '-p:IncludeCopyLocalFilesOutputGroup=false',
    '-getProperty:ApplicationIcon,UseWinUI,EnableDefaultAssets',
    '-getItem:Content,_DeploymentManifestIconFile,PriOutputs', '-getTargetResult:GetPriOutputs')
if ($ArtifactsPath) { $arguments += '-p:UseArtifactsOutput=true'; $arguments += "-p:ArtifactsPath=$([IO.Path]::GetFullPath($ArtifactsPath))" }
$output = @(& $dotnet @arguments 2>&1)
if ($LASTEXITCODE -ne 0) { throw 'MSBuild asset evaluation failed; restore the App project before this check.' }
try { $evaluation = ($output -join "`n") | ConvertFrom-Json -AsHashtable -Depth 20 }
catch { throw 'MSBuild asset evaluation did not produce its expected result.' }
if ($evaluation.Properties.UseWinUI -cne 'true' -or $evaluation.Properties.EnableDefaultAssets -cne 'true' -or
    $evaluation.Properties.ApplicationIcon.Replace('\','/') -cne 'Assets/Workbench.ico' -or
    $evaluation.TargetResults.GetPriOutputs.Result -cne 'Success') { throw 'The restored WinUI asset graph or icon input is unavailable.' }
$iconManifestItems = @($evaluation.Items._DeploymentManifestIconFile | Where-Object { $_.Identity.Replace('\','/') -ceq 'Assets/Workbench.ico' })
if ($iconManifestItems.Count -ne 1) { throw 'The executable icon input is missing or duplicated.' }
foreach ($name in @('Workbench.ico','Workbench.svg')) {
    $target = "Assets/$name"
    $content = @($evaluation.Items.Content | Where-Object { $_.Identity.Replace('\','/') -ceq $target })
    $outputItems = @($evaluation.Items.PriOutputs | Where-Object { $_.TargetPath.Replace('\','/') -ceq $target -and $_.OutputGroup -ceq 'ContentFilesProjectOutputGroup' -and $_.ProjectName -ceq 'Workbench.App' })
    if ($content.Count -ne 1 -or $outputItems.Count -ne 1) { throw "Expected exactly one evaluated Content and PRI-input asset: $target (content=$($content.Count), output=$($outputItems.Count))." }
    if ($content[0].DefiningProjectName -cne 'Microsoft.WinUI' -or $content[0].CopyToOutputDirectory -cne 'PreserveNewest' -or
        $content[0].CopyToPublishDirectory -cne 'PreserveNewest' -or $outputItems[0].CopyToOutputDirectory -cne 'PreserveNewest' -or
        $outputItems[0].CopyToPublishDirectory -cne 'PreserveNewest') { throw "The restored WinUI asset does not retain its build/publish copying metadata: $target" }
    $expected = [IO.Path]::GetFullPath((Join-Path $projectRoot $target))
    if (-not [IO.File]::Exists($expected) -or (Get-Item -LiteralPath $expected).Length -eq 0 -or
        -not [string]::Equals([IO.Path]::GetFullPath($content[0].FullPath), $expected, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([IO.Path]::GetFullPath($outputItems[0].FullPath), $expected, [StringComparison]::OrdinalIgnoreCase)) { throw "The evaluated asset does not point at the original source: $target" }
}
Write-Host 'WinUI PRI input evaluation passed: one ICO and SVG each, with original icon input and build/publish copy metadata. No makepri or native execution claimed.'
