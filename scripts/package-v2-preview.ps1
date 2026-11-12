# Build a Windows x64 portable zip of the v2 preview.
# The zip is an Actions artifact. It is not a GitHub Release.
[CmdletBinding()]
param(
    [string]$OutputDirectory = "./artifacts/v2-preview",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$windows = $PSVersionTable.PSVersion.Major -ge 6 -and $IsWindows
if (-not $windows) {
    throw "package-v2-preview.ps1 builds the Windows x64 preview and must run on Windows."
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path (Get-Location) $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$stageParent = Join-Path $OutputDirectory "stage"
$rootName = "ProjectWorkbench-v2-preview-win-x64"
$layout = Join-Path $stageParent $rootName
if (Test-Path $stageParent) { Remove-Item $stageParent -Recurse -Force }
New-Item -ItemType Directory -Force -Path $layout | Out-Null
$service = Join-Path $layout "service"

Write-Host "Publishing the loopback service (win-x64, self-contained)"
& dotnet publish (Join-Path $repo "src/Workbench.Service/Workbench.Service.csproj") `
    -c $Configuration -r win-x64 --self-contained true -o $service --nologo
if ($LASTEXITCODE -ne 0) { throw "Service publish failed." }

function Copy-PreviewPlugin {
    param([string]$Project, [string]$Id)
    $candidates = @(
        (Join-Path $repo "plugins/v2/$Project/bin/$Configuration/net10.0"),
        (Join-Path $repo "plugins/v2/$Project/bin/$Configuration/net10.0/win-x64")
    )
    $source = $null
    foreach ($candidate in $candidates) {
        if ((Test-Path $candidate) -and (Get-ChildItem -Path $candidate -Filter *.dll -File -ErrorAction SilentlyContinue)) {
            $source = $candidate
            break
        }
    }
    if (-not $source) { throw "Plugin output missing for $Project" }
    $dest = Join-Path $service "preview-plugins/$Id"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Get-ChildItem -Path $source -File | Where-Object { $_.Extension -eq ".dll" -or $_.Extension -eq ".json" } | Copy-Item -Destination $dest -Force
    $manifest = Join-Path $repo "plugins/v2/$Project/plugin.v2.json"
    Copy-Item $manifest (Join-Path $dest "plugin.v2.json") -Force
    $document = Get-Content -Raw -LiteralPath (Join-Path $dest "plugin.v2.json") | ConvertFrom-Json
    if ($document.autoSend -ne $false) { throw "autoSend must stay false for $Id" }
    if ($document.id -ne $Id) { throw "Plugin id mismatch for $Project" }
}

Copy-PreviewPlugin "Workbench.TestCases.V2" "com.projectworkbench.test-cases.v2"
Copy-PreviewPlugin "Workbench.UpcamStandup.V2" "com.projectworkbench.upcam-standup.v2"
Copy-PreviewPlugin "Workbench.DingTalkInsights.V2" "com.projectworkbench.dingtalk-insights.v2"
Copy-PreviewPlugin "Workbench.TestingDesign.V2" "com.projectworkbench.testing-design.v2"
Copy-PreviewPlugin "Workbench.TestExecutor" "com.projectworkbench.test-executor"

$npm = (Get-Command npm.cmd -ErrorAction Stop).Source
$node = (Get-Command node.exe -ErrorAction Stop).Source
Write-Host "Building the React UI"
Push-Location (Join-Path $repo "web/host")
try {
    & $npm ci --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw "web npm ci failed." }
    & $npm rebuild esbuild
    if ($LASTEXITCODE -ne 0) { throw "web esbuild rebuild failed." }
    & $npm run build
    if ($LASTEXITCODE -ne 0) { throw "web build failed." }
}
finally { Pop-Location }
$ui = Join-Path $service "ui"
if (Test-Path $ui) { Remove-Item $ui -Recurse -Force }
Copy-Item (Join-Path $repo "web/host/dist") $ui -Recurse
if (-not (Test-Path (Join-Path $ui "index.html"))) { throw "UI index.html was not staged." }

Write-Host "Building the Electron shell"
$shell = Join-Path $repo "desktop/shell"
Push-Location $shell
try {
    & $npm ci --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw "shell npm ci failed." }
    & $npm rebuild esbuild
    if ($LASTEXITCODE -ne 0) { throw "shell esbuild rebuild failed." }
    & $npm rebuild electron
    if ($LASTEXITCODE -ne 0) { throw "Electron binary download failed." }
    & $npm run build
    if ($LASTEXITCODE -ne 0) { throw "shell build failed." }
}
finally { Pop-Location }
$electronPathFile = Join-Path $shell "node_modules/electron/path.txt"
if (-not (Test-Path -LiteralPath $electronPathFile)) { throw "Electron path.txt was not written." }
$electronNameInDist = (Get-Content -Raw -LiteralPath $electronPathFile).Trim()
$electronDist = (Resolve-Path -LiteralPath (Join-Path $shell "node_modules/electron/dist")).Path.TrimEnd('\', '/')
$electronExe = Join-Path $electronDist $electronNameInDist
if (-not (Test-Path -LiteralPath $electronExe)) { throw "Electron executable was not found: $electronExe" }
Write-Host "Electron runtime: $electronExe"
$layoutRoot = $layout.TrimEnd('\', '/')
& robocopy.exe $electronDist $layoutRoot /E /NFL /NDL /NJH /NJS /NC /NS /NP
if ($LASTEXITCODE -ge 8) { throw "Copying the Electron runtime failed ($LASTEXITCODE)." }
$global:LASTEXITCODE = 0
$defaultAsar = Join-Path $layout "resources/default_app.asar"
if (Test-Path $defaultAsar) { Remove-Item $defaultAsar -Force }
$electronName = Join-Path $layout "electron.exe"
if (Test-Path $electronName) { Rename-Item $electronName "ProjectWorkbench.exe" }
$appDir = Join-Path $layout "resources/app"
New-Item -ItemType Directory -Force -Path (Join-Path $appDir "dist") | Out-Null
Copy-Item -Path (Join-Path $shell "dist\*") -Destination (Join-Path $appDir "dist") -Recurse -Force
Copy-Item (Join-Path $shell "preload.cjs") (Join-Path $appDir "preload.cjs") -Force
@"
{
  "name": "workbench-v2-electron-shell",
  "version": "0.5.0-preview.1",
  "private": true,
  "main": "dist/main.js",
  "description": "Portable v2 preview. autoSend stays off. Loopback only."
}
"@ | Set-Content -LiteralPath (Join-Path $appDir "package.json") -Encoding utf8

$readme = @"
项目工作台 v2 预览（Windows x64 免安装）

这是预览压缩包，不是正式版，也不是 GitHub Release。

1. 解压整个文件夹。不要只抽出 exe。
2. 双击 ProjectWorkbench.exe。
3. 草稿和插件数据只写在 %LOCALAPPDATA%\ProjectWorkbench-v2-preview。
   不会写入 %LOCALAPPDATA%\ProjectWorkbench。
4. 启动器里可以打开 UPCAM 站会日报、钉钉 Insights、测试设计，以及自动化测试执行器（含 3D 场景识别和路径切削）。
5. 窗口标题是「项目工作台」。最小宽度 1100，最小高度 640。
6. 用 --allow-lan 启动会直接退出。服务只听 127.0.0.1。
7. 钉钉 Insights 的「发送已关闭」按钮不能发送。autoSend 保持关闭。
8. 只用合成数据。不要导入真实聊天、密钥或业务文件。

本机验收脚本在这份预览的源码仓库里，不在这个压缩包里：
  scripts\verify-native-dialog.ps1
  scripts\probe-scene.ps1 -Port <远程调试端口> -OutFile .\scene-probe.json
probe-scene.ps1 只读探测已打开远程调试端口的本机 3D 应用，报告只写本地文件。
"@
$utf8 = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText((Join-Path $layout "README.txt"), $readme.Replace("`n", "`r`n"), $utf8)

$required = @(
    "ProjectWorkbench.exe",
    "service/Workbench.Service.exe",
    "service/ui/index.html",
    "service/preview-plugins/com.projectworkbench.upcam-standup.v2/plugin.v2.json",
    "service/preview-plugins/com.projectworkbench.upcam-standup.v2/Workbench.UpcamStandup.V2.dll",
    "service/preview-plugins/com.projectworkbench.dingtalk-insights.v2/plugin.v2.json",
    "service/preview-plugins/com.projectworkbench.dingtalk-insights.v2/Workbench.DingTalkInsights.V2.dll",
    "service/preview-plugins/com.projectworkbench.testing-design.v2/plugin.v2.json",
    "service/preview-plugins/com.projectworkbench.testing-design.v2/Workbench.TestingDesign.V2.dll",
    "service/preview-plugins/com.projectworkbench.test-executor/plugin.v2.json",
    "service/preview-plugins/com.projectworkbench.test-executor/Workbench.TestExecutor.Plugin.dll",
    "service/preview-plugins/com.projectworkbench.test-cases.v2/plugin.v2.json",
    "service/preview-plugins/com.projectworkbench.test-cases.v2/Workbench.TestCases.V2.dll",
    "resources/app/dist/main.js",
    "resources/app/preload.cjs",
    "README.txt"
)
foreach ($relative in $required) {
    $path = Join-Path $layout ($relative -replace "/", [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $path)) { throw "Preview layout is missing $relative" }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$zip = Join-Path $OutputDirectory "$rootName.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
& tar -a -cf $zip -C $stageParent $rootName
if ($LASTEXITCODE -ne 0) { throw "Could not zip the preview." }
$info = Get-Item $zip
if ($info.Length -lt 1MB) { throw "Preview zip is unexpectedly small ($($info.Length) bytes)." }
Write-Host "Preview zip: $($info.FullName) ($([math]::Round($info.Length / 1MB, 1)) MB)"
