[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory, [Parameter(Mandatory)][string]$HostDirectory, [Parameter(Mandatory)][string]$SourceCommit)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '../../scripts/TestingPluginDelivery.psm1') -Force
if (-not ('WorkbenchTestingManagedAudit' -as [type])) { throw 'ManagedAuditInterfaceMissing' }
$script:Cases = 0
function Refuses([scriptblock]$Action, [string]$Expected) {
    try { & $Action | Out-Null } catch {
        $message = $_.Exception.GetBaseException().Message
        if ($message -cne $Expected) { throw "Expected $Expected; observed $message" }
        $script:Cases++; Write-Host "Exact metadata refusal: $Expected"; return
    }
    throw "Expected $Expected; managed mutation accepted."
}
$payload = @{}
foreach ($name in @('Workbench.Testing.dll','Workbench.Testing.Domain.dll','Workbench.Testing.Windows.dll','Workbench.Testing.deps.json','Workbench.Testing.SourceAcceptance.dll','Workbench.Testing.SourceAcceptance.deps.json')) {
    $payload[$name] = [IO.File]::ReadAllBytes((Join-Path $BuildDirectory $name))
}
$entry = 'Workbench.Testing.SourceAcceptance.SourceAcceptancePlugin'
function Replace-PeUtf8([byte[]]$Image, [string]$Old, [string]$New) {
    if ([Text.Encoding]::UTF8.GetByteCount($Old) -ne [Text.Encoding]::UTF8.GetByteCount($New)) { throw 'Mutation must preserve PE layout.' }
    $hex = [Convert]::ToHexString($Image)
    $oldHex = [Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes($Old))
    if (-not $hex.Contains($oldHex,[StringComparison]::Ordinal)) { throw 'Actual PE attribute to mutate was not found.' }
    return ,[Convert]::FromHexString($hex.Replace($oldHex,[Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes($New))))
}
function Set-PeMetadata([byte[]]$Image, [string]$Mutation) {
    $changed = $Image.Clone()
    $stream = [IO.MemoryStream]::new($Image,$false)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $md = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        if ($Mutation -ceq 'assembly-version') {
            $offset = $pe.PEHeaders.MetadataStartOffset + [Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetTableMetadataOffset($md,[Reflection.Metadata.Ecma335.TableIndex]::Assembly) + 4
            $changed[$offset] = 2
        } else {
            foreach ($handle in $md.TypeDefinitions) {
                $type = $md.GetTypeDefinition($handle)
                if ($md.GetString($type.Name) -cne 'SourceAcceptancePlugin') { continue }
                if ($Mutation -ceq 'abstract-entry') {
                    $row = [Reflection.Metadata.Ecma335.MetadataTokens]::GetRowNumber([Reflection.Metadata.EntityHandle]$handle)
                    $offset = $pe.PEHeaders.MetadataStartOffset + [Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetTableMetadataOffset($md,[Reflection.Metadata.Ecma335.TableIndex]::TypeDef) + ($row-1) * [Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetTableRowSize($md,[Reflection.Metadata.Ecma335.TableIndex]::TypeDef)
                    $changed[$offset] = $changed[$offset] -bor 0x80
                } else {
                    foreach ($methodHandle in $type.GetMethods()) {
                        $method = $md.GetMethodDefinition($methodHandle)
                        if ($md.GetString($method.Name) -cne '.ctor') { continue }
                        $row = [Reflection.Metadata.Ecma335.MetadataTokens]::GetRowNumber([Reflection.Metadata.EntityHandle]$methodHandle)
                        $offset = $pe.PEHeaders.MetadataStartOffset + [Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetTableMetadataOffset($md,[Reflection.Metadata.Ecma335.TableIndex]::MethodDef) + ($row-1) * [Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetTableRowSize($md,[Reflection.Metadata.Ecma335.TableIndex]::MethodDef) + 6
                        $changed[$offset] = ($changed[$offset] -band 0xf8) -bor 1
                    }
                }
            }
        }
    } finally { $pe.Dispose(); $stream.Dispose() }
    return ,$changed
}

$identity = [WorkbenchTestingManagedAudit]::Inspect($payload['Workbench.Testing.SourceAcceptance.dll'], 'Workbench.Testing.SourceAcceptance', $SourceCommit, $false, $entry) | ConvertFrom-Json
if ($identity.name -cne 'Workbench.Testing.SourceAcceptance' -or $identity.assemblyVersion -cne '1.0.0.0' -or $identity.fileVersion -cne '0.2.0.0' -or $identity.informationalVersion -cne "0.2.0+$SourceCommit" -or $identity.machine -cne 'Amd64') { throw 'Actual fixture PE identity mismatch.' }; $script:Cases++
[WorkbenchTestingManagedAudit]::Inspect($payload['Workbench.Testing.Domain.dll'], 'Workbench.Testing.Domain', $SourceCommit, $true, '') | Out-Null; $script:Cases++
foreach ($name in @('Workbench.Testing','Workbench.Testing.Windows')) {
    $entryType = if ($name -ceq 'Workbench.Testing') { 'Workbench.Testing.TestingPlugin' } else { '' }
    $facts = [WorkbenchTestingManagedAudit]::Inspect($payload["$name.dll"],$name,$SourceCommit,$false,$entryType)
    Write-Host $facts; $script:Cases++
}
Write-Host ($identity | ConvertTo-Json -Depth 20 -Compress)
foreach ($mutation in @('file-version','informational-version','assembly-version','abstract-entry','private-constructor','wrong-contract')) {
    $bytes = switch ($mutation) {
        'file-version' { Replace-PeUtf8 $payload['Workbench.Testing.SourceAcceptance.dll'] '0.2.0.0' '0.3.0.0' }
        'informational-version' { Replace-PeUtf8 $payload['Workbench.Testing.SourceAcceptance.dll'] "0.2.0+$SourceCommit" "0.3.0+$SourceCommit" }
        'wrong-contract' { Replace-PeUtf8 $payload['Workbench.Testing.SourceAcceptance.dll'] 'Workbench.Sdk' 'Workbench.Xdk' }
        default { Set-PeMetadata $payload['Workbench.Testing.SourceAcceptance.dll'] $mutation }
    }
    $expected = if ($mutation -cin @('file-version','informational-version','assembly-version')) { 'TestingManagedIdentityInvalid' } else { 'TestingManagedEntryInvalid' }
    Refuses { [WorkbenchTestingManagedAudit]::Inspect($bytes,'Workbench.Testing.SourceAcceptance',$SourceCommit,$false,$entry) } $expected
}

foreach ($case in @(
    @{ image = [byte[]](0,1,2); name = 'Workbench.Testing.SourceAcceptance'; commit = $SourceCommit; portable = $false; entry = $entry; expected = 'TestingManagedPeInvalid' },
    @{ image = $payload['Workbench.Testing.Domain.dll']; name = 'Workbench.Testing.SourceAcceptance'; commit = $SourceCommit; portable = $false; entry = $entry; expected = 'TestingManagedIdentityInvalid' },
    @{ image = $payload['Workbench.Testing.SourceAcceptance.dll']; name = 'Workbench.Testing.SourceAcceptance'; commit = ('0' * 40); portable = $false; entry = $entry; expected = 'TestingManagedIdentityInvalid' },
    @{ image = $payload['Workbench.Testing.Domain.dll']; name = 'Workbench.Testing.Domain'; commit = $SourceCommit; portable = $false; entry = ''; expected = 'TestingManagedArchitectureInvalid' },
    @{ image = $payload['Workbench.Testing.SourceAcceptance.dll']; name = 'Workbench.Testing.SourceAcceptance'; commit = $SourceCommit; portable = $true; entry = ''; expected = 'TestingManagedArchitectureInvalid' },
    @{ image = $payload['Workbench.Testing.SourceAcceptance.dll']; name = 'Workbench.Testing.SourceAcceptance'; commit = $SourceCommit; portable = $false; entry = 'Missing.Plugin'; expected = 'TestingManagedEntryInvalid' }
)) {
    Refuses { [WorkbenchTestingManagedAudit]::Inspect($case.image,$case.name,$case.commit,$case.portable,$case.entry) } $case.expected
}
# Genuine SDK-produced reference assembly, never a counterfeit native helper.
$reference = Join-Path $BuildDirectory 'reference/Workbench.Testing.SourceAcceptance.dll'
if (-not (Test-Path -LiteralPath $reference)) { throw 'Pass the preserved genuine compiler reference assembly at reference/Workbench.Testing.SourceAcceptance.dll.' }
Refuses { [WorkbenchTestingManagedAudit]::Inspect([IO.File]::ReadAllBytes($reference),'Workbench.Testing.SourceAcceptance',$SourceCommit,$false,$entry) } 'TestingManagedReferenceAssembly'
$audit = [WorkbenchTestingManagedAudit]::Audit($payload,$HostDirectory,$SourceCommit,'SourceAcceptance') | ConvertFrom-Json
if ($audit.schemaVersion -ne 1 -or $audit.assemblies.Count -ne 4 -or $audit.deps.Count -ne 2 -or $audit.hostBindings.Count -lt 1) { throw 'Actual managed closure facts missing.' }; $script:Cases++
$production = @{}; foreach ($key in $payload.Keys) { if ($key -cnotlike '*SourceAcceptance*') { $production[$key] = $payload[$key] } }
[WorkbenchTestingManagedAudit]::Audit($production,$HostDirectory,$SourceCommit,'Production') | Out-Null; $script:Cases++
foreach ($mutation in @('missing-private','renamed-private','foreign-private','wrong-target','wrong-rid','wrong-root','missing-library','renamed-runtime','missing-edge','missing-reachable-edge','wrong-edge','foreign-library','foreign-runtime','private-runtime-version','duplicate-deps','missing-host','conflicting-host','linked-host','dangling-linked-host')) {
    $changed = @{}; foreach ($key in $payload.Keys) { $changed[$key] = $payload[$key].Clone() }
    $pairedHostDirectory = $HostDirectory
    switch ($mutation) {
        'missing-private' { $changed.Remove('Workbench.Testing.Domain.dll') }
        'renamed-private' { $changed['Renamed.dll'] = $changed['Workbench.Testing.Domain.dll']; $changed.Remove('Workbench.Testing.Domain.dll') }
        'foreign-private' { $changed['Workbench.Testing.Domain.dll'] = $changed['Workbench.Testing.SourceAcceptance.dll'] }
        'linked-host' { $pairedHostDirectory = Join-Path $BuildDirectory ('linked-host-' + [Guid]::NewGuid().ToString('N')); [IO.Directory]::CreateSymbolicLink($pairedHostDirectory,[IO.Path]::GetFullPath($HostDirectory)) | Out-Null }
        'dangling-linked-host' { $pairedHostDirectory = Join-Path $BuildDirectory ('dangling-host-' + [Guid]::NewGuid().ToString('N')); [IO.Directory]::CreateSymbolicLink($pairedHostDirectory,(Join-Path $BuildDirectory 'missing-host')) | Out-Null }
        'missing-host' { $pairedHostDirectory = Join-Path $BuildDirectory 'missing-host' }
        'conflicting-host' { $pairedHostDirectory = Join-Path $BuildDirectory ('conflicting-host-' + [Guid]::NewGuid().ToString('N')); New-Item $pairedHostDirectory -ItemType Directory | Out-Null; Copy-Item (Join-Path $HostDirectory '*') $pairedHostDirectory; [IO.File]::WriteAllBytes((Join-Path $pairedHostDirectory 'Workbench.Sdk.dll'),$payload['Workbench.Testing.Domain.dll']) }
        default {
            $deps = [Text.Encoding]::UTF8.GetString($changed['Workbench.Testing.SourceAcceptance.deps.json']) | ConvertFrom-Json -AsHashtable
            $target = '.NETCoreApp,Version=v10.0'
            $root = 'Workbench.Testing.SourceAcceptance/0.2.0'
            switch ($mutation) {
                'wrong-target' { $deps.runtimeTarget.name = '.NETCoreApp,Version=v9.0' }
                'wrong-rid' { $deps.runtimeTarget.name += '/win-x64' }
                'wrong-root' { $deps.targets[$target]['Other/0.2.0'] = $deps.targets[$target][$root]; $deps.targets[$target].Remove($root) }
                'missing-library' { $deps.libraries.Remove('Workbench.Testing.Domain/0.2.0') }
                'renamed-runtime' { $deps.targets[$target][$root].runtime = @{ 'Renamed.dll' = @{} } }
                'missing-reachable-edge' { $deps.targets[$target]['Workbench.Testing/0.2.0'].dependencies.Remove('Workbench.Testing.Domain') }
                'foreign-runtime' { $deps.targets[$target]['Workbench.Sdk/0.4.0'].runtime['Foreign.Private.dll'] = @{} }
                'private-runtime-version' { $deps.targets[$target][$root].runtime['Workbench.Testing.SourceAcceptance.dll']['assemblyVersion'] = '2.0.0.0' }
                'missing-edge' { $deps.targets[$target][$root].dependencies.Remove('Workbench.Testing') }
                'wrong-edge' { $deps.targets[$target][$root].dependencies['Workbench.Testing'] = '0.3.0' }
                'foreign-library' { $deps.libraries['Foreign.Private/0.2.0'] = @{ type='project'; serviceable=$false; sha512='' }; $deps.targets[$target]['Foreign.Private/0.2.0'] = @{ runtime=@{ 'Foreign.Private.dll'=@{} } } }
            }
            $json = $deps | ConvertTo-Json -Depth 32 -Compress
            if ($mutation -ceq 'duplicate-deps') { $json = $json.Insert(1,'"TARGETS":{},') }
            $changed['Workbench.Testing.SourceAcceptance.deps.json'] = [Text.Encoding]::UTF8.GetBytes($json)
        }
    }
    $expected = switch ($mutation) {
        { $_ -cin @('missing-private','renamed-private') } { 'TestingManagedPayloadInvalid' }
        'foreign-private' { 'TestingManagedIdentityInvalid' }
        { $_ -cin @('missing-host','conflicting-host') } { 'TestingManagedHostBindingInvalid' }
        'duplicate-deps' { 'TestingDepsJsonInvalid' }
        { $_ -cin @('linked-host','dangling-linked-host') } { 'TestingDeliveryLinkRefused' }
        default { 'TestingManagedDepsInvalid' }
    }
    Refuses { [WorkbenchTestingManagedAudit]::Audit($changed,$pairedHostDirectory,$SourceCommit,'SourceAcceptance') } $expected
}
Write-Host "Managed metadata/closure guards passed: $script:Cases cases on genuine compiled PEs; this is not native package/activation evidence."
