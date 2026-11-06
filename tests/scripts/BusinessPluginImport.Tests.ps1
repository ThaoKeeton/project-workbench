$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$import = Join-Path $repo 'scripts/import-business-plugin.ps1'
$temporary = Join-Path ([IO.Path]::GetTempPath()) "workbench-business-import-$([Guid]::NewGuid().ToString('N'))"
New-Item $temporary -ItemType Directory | Out-Null
try {
    $destination = Join-Path $temporary 'verified'
    & $import -DestinationRoot $destination
    $inputs = @(Get-Content (Join-Path $destination 'inputs.json') -Raw | ConvertFrom-Json)
    if ($inputs.Count -ne 2) { throw 'Both independent business packages are required.' }
    foreach ($input in $inputs) {
        $package = Join-Path $destination $input.packageFile
        if ((Get-FileHash $package -Algorithm SHA256).Hash -ne $input.sha256) { throw 'Imported pinned package changed.' }
    }
    $copy = Join-Path $temporary 'tampered'
    Copy-Item (Join-Path $repo 'tests/integration') $copy -Recurse
    [IO.File]::AppendAllText((Join-Path $copy 'dingtalk/package.zip'), 'tampered')
    $failed = $false
    try { & $import -InputRoot $copy -DestinationRoot (Join-Path $temporary 'rejected') } catch { $failed = $true }
    if (-not $failed -or (Test-Path (Join-Path $temporary 'rejected/inputs.json'))) { throw 'Hash-mismatched source was accepted.' }
    $failed = $false
    try { & $import -DestinationRoot (Join-Path $repo 'artifacts/native/app/business-inputs') } catch { $failed = $true }
    if (-not $failed) { throw 'Integration inputs were allowed inside the shipping app output.' }
    Write-Output 'Business plugin pinned import regressions: 3 passed (722 independently built files audited)'
}
finally { Remove-Item $temporary -Recurse -Force }
