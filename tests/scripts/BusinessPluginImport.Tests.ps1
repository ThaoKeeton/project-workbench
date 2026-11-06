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
    $acceptance = Join-Path $temporary 'acceptance'
    & $import -Mode Acceptance -DestinationRoot $acceptance
    $acceptanceInputs = @(Get-Content (Join-Path $acceptance 'inputs.json') -Raw | ConvertFrom-Json)
    if ($acceptanceInputs.Count -ne 2 -or @($acceptanceInputs | Where-Object pluginId -eq 'com.upcam.task-digest.acceptance').Count -ne 1 -or
        @($acceptanceInputs | Where-Object pluginId -eq 'com.upcam.task-digest').Count -ne 0) { throw 'Separate acceptance fixture identity was lost.' }
    foreach ($input in $acceptanceInputs) {
        if ((Get-FileHash (Join-Path $acceptance $input.packageFile) -Algorithm SHA256).Hash -ne $input.sha256) { throw 'Imported genuine acceptance package changed.' }
    }
    $copy = Join-Path $temporary 'tampered'
    Copy-Item (Join-Path $repo 'tests/integration') $copy -Recurse
    [IO.File]::AppendAllText((Join-Path $copy 'dingtalk/package.zip'), 'tampered')
    $failed = $false
    try { & $import -InputRoot $copy -DestinationRoot (Join-Path $temporary 'rejected') } catch { $failed = $true }
    if (-not $failed -or (Test-Path (Join-Path $temporary 'rejected/inputs.json'))) { throw 'Hash-mismatched source was accepted.' }
    $failed = $false
    $pinPath = Join-Path $copy 'upcam-acceptance/provenance.json'
    $pin = Get-Content $pinPath -Raw | ConvertFrom-Json
    $pin.syntheticAcceptance = $false
    $pin | ConvertTo-Json -Depth 20 | Set-Content $pinPath
    # Restore the independent DingTalk input so this rejection specifically exercises the acceptance-mode guard.
    Copy-Item (Join-Path $repo 'tests/integration/dingtalk/package.zip') (Join-Path $copy 'dingtalk/package.zip') -Force
    try { & $import -Mode Acceptance -InputRoot $copy -DestinationRoot (Join-Path $temporary 'wrong-mode') } catch { $failed = $true }
    if (-not $failed -or (Test-Path (Join-Path $temporary 'wrong-mode/inputs.json'))) { throw 'A false synthetic build claim was accepted.' }
    $failed = $false
    try { & $import -DestinationRoot (Join-Path $repo 'artifacts/native/app/business-inputs') } catch { $failed = $true }
    if (-not $failed) { throw 'Integration inputs were allowed inside the shipping app output.' }
    Write-Output 'Business plugin pinned import regressions: 5 passed (separate production/acceptance matrices, 1436 unique independently built files audited)'
}
finally { Remove-Item $temporary -Recurse -Force }
