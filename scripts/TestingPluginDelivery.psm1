Set-StrictMode -Version Latest
$script:ProductionPayload = @('Fixtures/figma.synthetic.json', 'Fixtures/requirements.synthetic.json', 'Workbench.Testing.deps.json', 'Workbench.Testing.dll', 'Workbench.Testing.Domain.dll', 'Workbench.Testing.Windows.dll', 'Workbench.OwnedLab.exe', 'plugin.json')
if (-not ('WorkbenchTestingManagedAudit' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'TestingManagedPayloadAudit.cs') }
if (-not ('WorkbenchTestingArchiveAudit' -as [type])) {
    Add-Type -TypeDefinition @'
public static class WorkbenchTestingArchiveAudit {
    public static bool NativePe(byte[] image) {
        try {
            using var input = new System.IO.MemoryStream(image, false);
            using var pe = new System.Reflection.PortableExecutable.PEReader(input);
            var header = pe.PEHeaders.PEHeader;
            if (pe.HasMetadata || header == null || pe.PEHeaders.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.Amd64 ||
                header.Magic != System.Reflection.PortableExecutable.PEMagic.PE32Plus || header.Subsystem != System.Reflection.PortableExecutable.Subsystem.WindowsGui ||
                (pe.PEHeaders.CoffHeader.Characteristics & System.Reflection.PortableExecutable.Characteristics.Dll) != 0 || header.AddressOfEntryPoint == 0) return false;
            bool executable = false;
            foreach (var section in pe.PEHeaders.SectionHeaders) {
                if (section.PointerToRawData < 0 || section.SizeOfRawData < 1 || (long)section.PointerToRawData + section.SizeOfRawData > image.Length) return false;
                if ((section.SectionCharacteristics & System.Reflection.PortableExecutable.SectionCharacteristics.MemExecute) != 0 && header.AddressOfEntryPoint >= section.VirtualAddress && header.AddressOfEntryPoint < (long)section.VirtualAddress + section.SizeOfRawData) executable = true;
            }
            if (!executable || header.ImportTableDirectory.RelativeVirtualAddress == 0) return false;
            var imports = pe.GetSectionData(header.ImportTableDirectory.RelativeVirtualAddress).GetReader();
            for (int i = 0; i < 128; i++) {
                uint original = imports.ReadUInt32(), time = imports.ReadUInt32(), forward = imports.ReadUInt32(), name = imports.ReadUInt32(), first = imports.ReadUInt32();
                if ((original | time | forward | name | first) == 0) return i > 0;
                var nameReader = pe.GetSectionData(checked((int)name)).GetReader();
                var text = new System.Text.StringBuilder(); bool terminated = false;
                for (int c = 0; c < 260; c++) { byte ch = nameReader.ReadByte(); if (ch == 0) { terminated = true; break; } if (ch > 127) return false; text.Append((char)ch); }
                if (!terminated) return false; var dll = text.ToString();
                if (dll.IndexOf("vcruntime", System.StringComparison.OrdinalIgnoreCase) >= 0 || dll.IndexOf("msvcp", System.StringComparison.OrdinalIgnoreCase) >= 0 || dll.IndexOf("ucrtbase", System.StringComparison.OrdinalIgnoreCase) >= 0 || dll.IndexOf("api-ms-win-crt", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }
        } catch { }
        return false;
    }
    public static byte[] Descriptor(byte[] assembly) {
        using var input = new System.IO.MemoryStream(assembly, false);
        using var pe = new System.Reflection.PortableExecutable.PEReader(input);
        var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        foreach (var typeHandle in metadata.TypeDefinitions) {
            var type = metadata.GetTypeDefinition(typeHandle);
            if (metadata.GetString(type.Name) == "NativeFixture" || metadata.GetString(type.Namespace) == "Workbench.OwnedLab.FixtureLauncher") throw new System.IO.InvalidDataException("Fixture launcher cannot ship");
        }
        foreach (var handle in metadata.ManifestResources) {
            var resource = metadata.GetManifestResource(handle);
            if (metadata.GetString(resource.Name) != "Workbench.OwnedLab.Descriptor.json") continue;
            if (!resource.Implementation.IsNil) throw new System.IO.InvalidDataException("Linked descriptor");
            var directory = pe.PEHeaders.CorHeader.ResourcesDirectory;
            var block = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader(checked((int)resource.Offset), directory.Size - checked((int)resource.Offset));
            int length = block.ReadInt32();
            if (length < 1 || length > 8192 || length > block.RemainingBytes) throw new System.IO.InvalidDataException("Descriptor size");
            return block.ReadBytes(length);
        }
        throw new System.IO.InvalidDataException("Descriptor missing");
    }
    public static uint Crc(byte[] bytes) {
        uint value = 0xffffffff;
        foreach (byte b in bytes) {
            value ^= b;
            for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xedb88320u : 0u);
        }
        return ~value;
    }
}
'@
}
function Get-TestingSourceFiles {
    param([string]$Repo)
    $paths = @('Directory.Build.props', 'global.json', '.github/workflows/build.yml', 'scripts/build.ps1', 'scripts/build-owned-lab.ps1', 'scripts/owned-lab-probe.ps1', 'scripts/OwnedLabProbeStaging.psm1', 'scripts/OwnedLabProbeOutput.psm1', 'scripts/package-testing-plugin.ps1', 'scripts/TestingPluginDelivery.psm1', 'scripts/TestingPluginProbe.psm1', 'scripts/testing-plugin-probe.ps1', 'tests/scripts/TestingPluginPackage.Tests.ps1', 'tests/scripts/TestingPluginProbe.Tests.ps1', 'tests/scripts/OwnedLabBuild.Tests.ps1', 'tests/scripts/OwnedLabProbeStaging.Tests.ps1', 'tests/scripts/OwnedLabProbeOutput.Tests.ps1', 'tests/Workbench.Core.Tests/OwnedWireTests.cs', 'tests/Workbench.Core.Tests/OwnedRunnerTests.cs', 'tests/Workbench.Core.Tests/OwnedLabTestRig.cs', 'tests/Workbench.Core.Tests/TestingNativeActivityTests.cs', 'tests/Workbench.Core.Tests/TestingNativeOwnedPilotTests.cs', 'tests/Workbench.Core.Tests/TestingNativeOwnedEvidenceTests.cs', 'tests/Workbench.Core.Tests/TestingNativeLifecycleTests.cs', 'tests/Workbench.Core.Tests/OwnedAdmissionTests.cs', 'tests/Workbench.Core.Tests/OwnedJournalTests.cs', 'tests/Workbench.Core.Tests/OwnedFixedPlansTests.cs', 'tests/Workbench.Core.Tests/TestingControllerAdmissionTests.cs', 'tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj')
    $paths += @('tests/Workbench.Core.Tests/TestingNativeFeedbackTests.cs','scripts/TestingManagedPayloadAudit.cs','tests/scripts/TestingManagedPayload.Tests.ps1')
    $paths += @('tests/Workbench.Core.Tests/SourceAcceptanceTests.cs', 'tests/Workbench.Core.Tests/SourceAcceptanceTests.Ownership.cs', 'tests/Workbench.Core.Tests/SourceAcceptanceTests.Transport.cs', 'docs/testing-plugin.md', 'docs/windows-verification.md')
    foreach ($part in @('plugins/Workbench.Testing', 'plugins/Workbench.Testing.Domain', 'plugins/Workbench.Testing.Windows', 'native/Workbench.OwnedLab', 'tests/Workbench.OwnedLab.WindowsProbe', 'tests/Workbench.OwnedLab.NativeFixtures', 'tests/Workbench.Testing.PackageProbe', 'tests/Workbench.Testing.SourceAcceptance', 'tests/Workbench.NativeReference', 'src/Workbench.Sdk', 'src/Workbench.WinUI.Sdk', 'src/Workbench.App', 'src/Workbench.Core')) {
        $paths += @(& git -C $Repo ls-files --cached --others --exclude-standard -- $part)
        if ($LASTEXITCODE -ne 0) { throw 'TestingSourceFileListUnavailable' }
    }
    return @($paths | Sort-Object -Unique | ForEach-Object {
        Assert-TestingNoLinks (Join-Path $Repo $_)
        $file = Get-Item -LiteralPath (Join-Path $Repo $_)
        @{ path = $_; length = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}
function Get-TestingHostIdentity {
    param([string]$AppDirectory, [string]$SourceCommit)
    $dll = Join-Path $AppDirectory 'Workbench.App.dll'
    $exe = Join-Path $AppDirectory 'Workbench.App.exe'
    Assert-TestingNoLinks $dll
    Assert-TestingNoLinks $exe
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll)
    if ($version.ProductVersion -cne "0.4.0+$SourceCommit" -or $version.FileVersion -cne '0.4.0.0' -or [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString() -cne '1.0.0.0') { throw 'TestingHostSourceMismatch' }
    return @{ sourceCommit = $SourceCommit; productVersion = $version.ProductVersion; fileVersion = $version.FileVersion; assemblyVersion = '1.0.0.0'; executableSha256 = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant(); dllSha256 = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant() }
}
function Assert-TestingSourceIdentity {
    param([string]$RecordedCommit, [bool]$RecordedDirty, [string]$CurrentCommit, [bool]$CurrentDirty)
    if ($RecordedCommit -cne $CurrentCommit -or $RecordedDirty -or $CurrentDirty) { throw 'TestingCurrentSourceMismatch' }
}
function Assert-TestingSourceRecords {
    param([object[]]$Current, [object[]]$Recorded)
    if ($Current.Count -ne $Recorded.Count) { throw 'TestingSourceFileListInvalid' }
    foreach ($file in $Current) {
        $record = @($Recorded | Where-Object path -CEQ $file.path)
        if ($record.Count -ne 1 -or $record[0].length -ne $file.length -or $record[0].sha256 -cne $file.sha256) { throw 'TestingSourceHashMismatch' }
    }
}
function Assert-TestingOwnedHelper {
    param([byte[]]$Image, [Collections.IDictionary]$Descriptor)
    if ($Descriptor['Available'] -isnot [bool] -or -not $Descriptor['Available'] -or $Descriptor['Profile'] -cne 'Production' -or
        $Descriptor['ImageSha'] -cnotmatch '^[0-9a-f]{64}$' -or $Descriptor['NativeSourceSha'] -cnotmatch '^[0-9a-f]{64}$' -or $Descriptor['SourceCommit'] -cnotmatch '^[0-9a-f]{40}$' -or
        [string]::IsNullOrWhiteSpace($Descriptor['Msvc']) -or [string]::IsNullOrWhiteSpace($Descriptor['Sdk'])) { throw 'TestingOwnedHelperDescriptorInvalid' }
    if ($Image.Length -lt 256 -or $Image.Length -gt 16777216 -or -not [WorkbenchTestingArchiveAudit]::NativePe($Image)) { throw 'TestingOwnedHelperInvalid' }
    $offset = [BitConverter]::ToInt32($Image, 0x3c)
    if ($offset -lt 64 -or $offset -gt $Image.Length - 256 -or [BitConverter]::ToUInt32($Image,$offset) -ne 0x4550 -or
        [BitConverter]::ToUInt16($Image,$offset + 4) -ne 0x8664 -or [BitConverter]::ToUInt16($Image,$offset + 24) -ne 0x20b -or
        ([BitConverter]::ToUInt16($Image,$offset + 22) -band 0x2000) -ne 0) { throw 'TestingOwnedHelperInvalid' }
    if ([Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($Image)) -cne $Descriptor['ImageSha']) { throw 'TestingOwnedHelperHashMismatch' }
}
function Read-TestingOwnedDescriptor {
    param([byte[]]$Assembly)
    try { $bytes = [WorkbenchTestingArchiveAudit]::Descriptor($Assembly); [WorkbenchTestingManagedAudit]::ValidateJson($bytes,'TestingOwnedHelperDescriptorInvalid'); return [Text.UTF8Encoding]::new($false,$true).GetString($bytes) | ConvertFrom-Json -AsHashtable }
    catch { throw 'TestingOwnedHelperDescriptorInvalid' }
}
function Assert-TestingOwnedSource {
    param([Collections.IDictionary]$Build, [Collections.IDictionary]$Descriptor)
    if ($Build['staticCrt'] -isnot [bool] -or -not $Build['staticCrt'] -or $Build['macro'] -cne '' -or $Build['sources'] -isnot [array]) { throw 'TestingOwnedHelperSourceInvalid' }
    $expected = @('Automation.cpp','Automation.h','Broker.cpp','Broker.h','BuildInfo.rc','Capture.cpp','Capture.h','Main.cpp','Main.h','Protocol.cpp','Protocol.h','Target.cpp','Target.h','TargetProcess.cpp','TargetProcess.h') | ForEach-Object { "native/Workbench.OwnedLab/$_" }
    if (@(Compare-Object $expected @($Build.sources | ForEach-Object path) -CaseSensitive).Count -ne 0 -or $Build.sources.Count -ne $expected.Count) { throw 'TestingOwnedHelperSourceInvalid' }
    $text = ''
    foreach ($record in @($Build.sources | Sort-Object path -CaseSensitive)) {
        if ($record.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $record.length -isnot [long] -and $record.length -isnot [int] -or $record.length -lt 1) { throw 'TestingOwnedHelperSourceInvalid' }
        $text += "$($record.path) $($record.length) $($record.sha256)`n"
    }
    $text += "profile=Production`nmacro=`n"
    if ([Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($text))) -cne $Descriptor.NativeSourceSha) { throw 'TestingOwnedHelperSourceInvalid' }
}
function Get-TestingProfile {
    param([ValidateSet('Production','SourceAcceptance', IgnoreCase=$false)][string]$Profile = 'Production')
    if ($Profile -ceq 'SourceAcceptance') {
        return @{ name = 'SourceAcceptance'; project = 'tests/Workbench.Testing.SourceAcceptance/Workbench.Testing.SourceAcceptance.csproj'; id = 'com.projectworkbench.testing.source-acceptance'; manifest = 'tests/Workbench.Testing.SourceAcceptance/plugin.json'; zip = 'workbench-testing-source-acceptance-0.2.0.zip'; payload = @($script:ProductionPayload + @('Workbench.Testing.SourceAcceptance.dll','Workbench.Testing.SourceAcceptance.deps.json')) }
    }
    return @{ name = 'Production'; project = 'plugins/Workbench.Testing/Workbench.Testing.csproj'; id = 'com.projectworkbench.testing'; manifest = 'plugins/Workbench.Testing/plugin.json'; zip = 'workbench-testing-plugin-0.2.0.zip'; payload = $script:ProductionPayload }
}
function Assert-TestingPairArgument {
    param([string]$Profile, [string]$ProductionDirectory)
    if ($Profile -ceq 'SourceAcceptance' -and [string]::IsNullOrWhiteSpace($ProductionDirectory)) { throw 'TestingProductionPairRequired' }
    if ($Profile -ceq 'Production' -and $ProductionDirectory) { throw 'TestingProductionPairUnexpected' }
}
function Assert-TestingNoLinks {
    param([string]$Path)
    for ($part = [IO.Path]::GetFullPath($Path); -not [string]::IsNullOrEmpty($part); $part = [IO.Path]::GetDirectoryName($part)) {
        if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'TestingDeliveryLinkRefused' }
    }
}
function Assert-TestingFreshDirectory {
    param([string]$Path)
    Assert-TestingNoLinks $Path
    if ((Test-Path -LiteralPath $Path) -and (-not (Test-Path -LiteralPath $Path -PathType Container) -or @(Get-ChildItem -LiteralPath $Path -Force).Count -gt 0)) { throw 'TestingDeliveryAlreadyExists' }
}
function Read-TestingJsonFile {
    param([string]$Path, [string]$Code)
    Assert-TestingNoLinks $Path
    try {
        if ((Get-Item -LiteralPath $Path).Length -gt 2097152) { throw $Code }
        $bytes = [IO.File]::ReadAllBytes($Path)
        [WorkbenchTestingManagedAudit]::ValidateJson($bytes,$Code)
        return [Text.UTF8Encoding]::new($false,$true).GetString($bytes) | ConvertFrom-Json -AsHashtable -Depth 40
    } catch { throw $Code }
}
function Read-TestingPluginArchive {
    param([string]$ZipPath, [object[]]$PayloadRecords, [ValidateSet('Production','SourceAcceptance', IgnoreCase=$false)][string]$Profile = 'Production')
    $contract = Get-TestingProfile $Profile
    Assert-TestingNoLinks $ZipPath
    $zip = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    $payload = @{}
    try {
        $entries = @($zip.Entries)
        if (@(Compare-Object $contract.payload @($entries | ForEach-Object FullName) -CaseSensitive).Count -ne 0 -or $entries.Count -ne $contract.payload.Count -or $PayloadRecords.Count -ne $contract.payload.Count) { throw 'TestingPayloadFileListInvalid' }
        foreach ($name in $contract.payload) {
            $entry = @($entries | Where-Object FullName -CEQ $name)
            $record = @($PayloadRecords | Where-Object path -CEQ $name)
            if ($entry.Count -ne 1 -or $record.Count -ne 1 -or $entry[0].Length -gt 16777216 -or $entry[0].Length -lt 1 -or $entry[0].IsEncrypted -or ($entry[0].ExternalAttributes -band [int]([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Device -bor [IO.FileAttributes]::Directory)) -ne 0 -or (($entry[0].ExternalAttributes -shr 16) -band 0xf000) -notin @(0,0x8000)) { throw 'TestingPayloadInvalid' }
            $stream = $entry[0].Open()
            try {
                $bytes = [IO.MemoryStream]::new()
                try {
                    $buffer = [byte[]]::new(65536)
                    while (($read = $stream.Read($buffer,0,$buffer.Length)) -gt 0) {
                        if ($bytes.Length + $read -gt 16777216 -or $bytes.Length + $read -gt $entry[0].Length) { throw 'TestingPayloadInvalid' }
                        $bytes.Write($buffer,0,$read)
                    }
                    $actual = $bytes.ToArray()
                } finally { $bytes.Dispose() }
            } finally { $stream.Dispose() }
            if ($actual.Length -ne $entry[0].Length -or [WorkbenchTestingArchiveAudit]::Crc($actual) -ne $entry[0].Crc32) { throw 'TestingPayloadCrcMismatch' }
            if ($record[0].length -ne $actual.Length -or $record[0].sha256 -cne [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($actual))) { throw 'TestingPayloadHashMismatch' }
            if ($name -ceq 'plugin.json') { try { [WorkbenchTestingManagedAudit]::Manifest($actual,$Profile) } catch { throw 'TestingManifestInvalid' } }
            if ($name.EndsWith('.deps.json',[StringComparison]::Ordinal)) { try { [WorkbenchTestingManagedAudit]::ValidateJson($actual,'TestingDepsJsonInvalid') } catch { throw 'TestingDepsJsonInvalid' } }
            $payload[$name] = $actual
        }
    } finally { $zip.Dispose() }
    return ,$payload
}
function Assert-TestingCommonPayload {
    param([Collections.IDictionary]$Production, [Collections.IDictionary]$Source)
    foreach ($name in @($script:ProductionPayload | Where-Object { $_ -cne 'plugin.json' })) {
        if ([Convert]::ToBase64String($Production[$name]) -cne [Convert]::ToBase64String($Source[$name])) { throw 'TestingProductionPairMismatch' }
    }
}
function Get-TestingManagedAudit {
    param([Collections.IDictionary]$Payload, [string]$AppDirectory, [string]$SourceCommit, [string]$Profile)
    try { return [WorkbenchTestingManagedAudit]::Audit($Payload,$AppDirectory,$SourceCommit,$Profile) | ConvertFrom-Json -AsHashtable -Depth 40 }
    catch { throw $_.Exception.GetBaseException().Message }
}
function Invoke-TestingPluginDeliveryValidation {
    param([string]$Directory, [string]$Profile, [string]$ProductionDirectory, [string]$AppDirectory, [switch]$RequireCurrentSource)
    $repo = Split-Path $PSScriptRoot -Parent
    $contract = Get-TestingProfile $Profile
    Assert-TestingPairArgument $Profile $ProductionDirectory
    Assert-TestingNoLinks $Directory
    Assert-TestingNoLinks $AppDirectory
    $files = @('README.md', 'SHA256SUMS', 'provenance.json', $contract.zip)
    if (@(Compare-Object $files @(Get-ChildItem -LiteralPath $Directory -Force | ForEach-Object Name) -CaseSensitive).Count -ne 0) { throw 'TestingDeliveryFileListInvalid' }
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Force) { if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'TestingDeliveryLinkRefused' } }
    $metadata = Read-TestingJsonFile (Join-Path $Directory 'provenance.json') 'TestingDeliveryIdentityInvalid'
    if ($metadata.schemaVersion -ne 1 -or $metadata.profile -cne $Profile -or $metadata.pluginId -cne $contract.id -or $metadata.version -cne '0.2.0' -or $metadata.minimumHostVersion -cne '0.4.0' -or $metadata.sourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'TestingDeliveryIdentityInvalid' }
    if ($metadata.referenceOnly -isnot [bool] -or $metadata.referenceOnly) { throw 'TestingDeliveryReferenceOnly' }
    if ($metadata.sourceTreeDirty -isnot [bool] -or $metadata.sourceTreeDirty) { throw 'TestingDeliveryDirtySource' }
    $actualHost = Get-TestingHostIdentity -AppDirectory $AppDirectory -SourceCommit $metadata.sourceCommit
    foreach ($key in $actualHost.Keys) { if ($metadata.host[$key] -cne $actualHost[$key]) { throw 'TestingHostPairingMismatch' } }
    $sums = @(Get-Content -LiteralPath (Join-Path $Directory 'SHA256SUMS'))
    if ($sums.Count -ne 3) { throw 'TestingDeliverySumsInvalid' }
    foreach ($file in @('README.md', 'provenance.json', $contract.zip)) {
        $expected = (Get-FileHash -LiteralPath (Join-Path $Directory $file) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $file
        if (@($sums | Where-Object { $_ -ceq $expected }).Count -ne 1) { throw 'TestingDeliveryHashMismatch' }
    }
    $zipPath = Join-Path $Directory $contract.zip
    if ($metadata.zip.path -cne $contract.zip -or $metadata.zip.length -ne (Get-Item $zipPath).Length -or $metadata.zip.sha256 -cne (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'TestingDeliveryZipMismatch' }
    $payload = Read-TestingPluginArchive -ZipPath $zipPath -PayloadRecords @($metadata.payload) -Profile $Profile
    $helper = Read-TestingOwnedDescriptor $payload['Workbench.Testing.Windows.dll']
    Assert-TestingOwnedHelper -Image $payload['Workbench.OwnedLab.exe'] -Descriptor $helper
    if ($helper.SourceCommit -cne $metadata.sourceCommit) { throw 'TestingOwnedHelperSourceInvalid' }
    foreach ($key in $helper.Keys) { if ($metadata.helper[$key] -cne $helper[$key]) { throw 'TestingOwnedHelperDescriptorInvalid' } }
    Assert-TestingOwnedSource $metadata.nativeBuild $helper
    $audit = Get-TestingManagedAudit $payload $AppDirectory $metadata.sourceCommit $Profile
    if (($metadata.managedAudit | ConvertTo-Json -Depth 40 -Compress) -cne ($audit | ConvertTo-Json -Depth 40 -Compress)) { throw 'TestingManagedAuditMismatch' }
    if ($Profile -ceq 'SourceAcceptance') {
        if ([IO.Path]::GetFullPath($Directory) -eq [IO.Path]::GetFullPath($ProductionDirectory)) { throw 'TestingProductionPairMismatch' }
        $production = Invoke-TestingPluginDeliveryValidation -Directory $ProductionDirectory -Profile Production -AppDirectory $AppDirectory -RequireCurrentSource:$RequireCurrentSource
        if ($production.metadata.sourceCommit -cne $metadata.sourceCommit -or $metadata.pairedProductionSha256 -cne $production.metadata.zip.sha256) { throw 'TestingProductionPairMismatch' }
        Assert-TestingCommonPayload $production.payload $payload
    }
    if ($RequireCurrentSource) {
        Assert-TestingSourceIdentity -RecordedCommit $metadata.sourceCommit -RecordedDirty $metadata.sourceTreeDirty -CurrentCommit (& git -C $repo rev-parse HEAD).Trim() -CurrentDirty (@(& git -C $repo status --porcelain).Count -ne 0)
        Assert-TestingSourceRecords -Current @(Get-TestingSourceFiles $repo) -Recorded @($metadata.sourceFiles)
    }
    return @{ payload = $payload; metadata = $metadata }
}
function New-TestingPluginDelivery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$BuildDirectory, [Parameter(Mandatory)][string]$OutputDirectory, [switch]$ReferenceOnly,
        [ValidateSet('Production','SourceAcceptance', IgnoreCase=$false)][string]$Profile = 'Production', [string]$ProductionDirectory)
    Assert-TestingPairArgument $Profile $ProductionDirectory
    if ($ReferenceOnly) { throw 'TestingDeliveryReferenceOnly' }
    Assert-TestingFreshDirectory $OutputDirectory
    Assert-TestingNoLinks $BuildDirectory
    $repo = Split-Path $PSScriptRoot -Parent
    $contract = Get-TestingProfile $Profile
    foreach ($name in @('Workbench.OwnedLab.exe','Workbench.Testing.Windows.dll','OwnedLab.native-build.json')) {
        $path = Join-Path $BuildDirectory $name
        Assert-TestingNoLinks $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'TestingOwnedHelperMissing' }
    }
    $helper = Read-TestingOwnedDescriptor ([IO.File]::ReadAllBytes((Join-Path $BuildDirectory 'Workbench.Testing.Windows.dll')))
    Assert-TestingOwnedHelper -Image ([IO.File]::ReadAllBytes((Join-Path $BuildDirectory 'Workbench.OwnedLab.exe'))) -Descriptor $helper
    $nativeBuild = Read-TestingJsonFile (Join-Path $BuildDirectory 'OwnedLab.native-build.json') 'TestingOwnedHelperSourceInvalid'
    Assert-TestingOwnedSource $nativeBuild $helper
    $sourceCommit = (& git -C $repo rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'TestingSourceIdentityUnavailable' }
    if ($helper.SourceCommit -cne $sourceCommit) { throw 'TestingOwnedHelperSourceInvalid' }
    $dirty = @(& git -C $repo status --porcelain).Count -ne 0
    if ($dirty) { throw 'TestingDeliveryRequiresCleanSource' }
    $sourceFiles = @(Get-TestingSourceFiles $repo)
    Assert-TestingSourceRecords -Current @($sourceFiles | Where-Object path -CLike 'native/Workbench.OwnedLab/*') -Recorded @($nativeBuild.sources)
    $appDirectory = Join-Path $repo 'artifacts/native/app'
    $hostIdentity = Get-TestingHostIdentity -AppDirectory $appDirectory -SourceCommit $sourceCommit
    $payloadBytes = @{}
    foreach ($name in $contract.payload) {
        $inputPath = if ($name -ceq 'plugin.json') { Join-Path $repo $contract.manifest } elseif ($name.StartsWith('Fixtures/',[StringComparison]::Ordinal)) { Join-Path $repo "plugins/Workbench.Testing.Domain/$name" } else { Join-Path $BuildDirectory $name }
        Assert-TestingNoLinks $inputPath
        if ((Get-Item -LiteralPath $inputPath).Length -gt 16777216) { throw 'TestingPayloadInvalid' }
        $payloadBytes[$name] = [IO.File]::ReadAllBytes($inputPath)
    }
    $audit = Get-TestingManagedAudit $payloadBytes $appDirectory $sourceCommit $Profile
    $pairedSha = $null
    if ($Profile -ceq 'SourceAcceptance') {
        $production = Invoke-TestingPluginDeliveryValidation -Directory $ProductionDirectory -Profile Production -AppDirectory $appDirectory -RequireCurrentSource
        if ($production.metadata.sourceCommit -cne $sourceCommit) { throw 'TestingProductionPairMismatch' }
        Assert-TestingCommonPayload $production.payload $payloadBytes
        $pairedSha = $production.metadata.zip.sha256
    }
    $payload = @($contract.payload | ForEach-Object { @{ path = $_; length = $payloadBytes[$_].Length; sha256 = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($payloadBytes[$_])) } })
    $stage = Join-Path ([IO.Path]::GetTempPath()) ('testing-package-stage-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item $stage -ItemType Directory | Out-Null
        $stagedZip = Join-Path $stage $contract.zip
        $archive = [IO.Compression.ZipFile]::Open($stagedZip,[IO.Compression.ZipArchiveMode]::Create)
        try { foreach ($name in $contract.payload) { $stream = $archive.CreateEntry($name).Open(); try { $stream.Write($payloadBytes[$name]) } finally { $stream.Dispose() } } } finally { $archive.Dispose() }
        # The producer uses exactly the same bounded reader as the full validator and portable structural tests.
        Read-TestingPluginArchive -ZipPath $stagedZip -PayloadRecords $payload -Profile $Profile | Out-Null
        Assert-TestingFreshDirectory $OutputDirectory
        New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
        $zip = Join-Path $OutputDirectory $contract.zip
        Copy-Item -LiteralPath $stagedZip -Destination $zip
        $metadata = @{ schemaVersion = 1; profile = $Profile; pluginId = $contract.id; version = '0.2.0'; minimumHostVersion = '0.4.0'; sourceCommit = $sourceCommit; sourceTreeDirty = $dirty; referenceOnly = $false; helper = $helper; nativeBuild = $nativeBuild; host = $hostIdentity; sourceFiles = $sourceFiles; payload = $payload; managedAudit = $audit; pairedProductionSha256 = $pairedSha; zip = @{ path = $contract.zip; length = (Get-Item $zip).Length; sha256 = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() }; createdUtc = [DateTimeOffset]::UtcNow.ToString('O') }
        $metadata | ConvertTo-Json -Depth 40 | Set-Content (Join-Path $OutputDirectory 'provenance.json') -Encoding utf8
        @"
# Independent testing plugin 0.2.0 — $Profile

Install $($contract.zip) deliberately with the native host package installation action. Identity: $($contract.id). Requires host/SDK 0.4.0/API1 from this same source and build. In-process code is trusted; ALC is not a sandbox or a hot-unload facility. Source: $sourceCommit. This is an exact $($contract.payload.Count)-file ZIP; the outer delivery is exactly README.md, SHA256SUMS, provenance.json and the ZIP.

Production offers explicit Figma read-only imports and selected-only OpenAI authoring with gpt-5.4-mini-2026-03-17. Selected identities, versions, titles, descriptions and criteria are sent to OpenAI and may incur charges. Manage figma-token/openai-api-key in the host protected credential UI. No credentials are packaged. Generated cases require human review and remain manual; cancellation waits for actual IO and callbacks, and partial AI receipts are never automatically retried or deleted. CSV export prefixes each text cell with an apostrophe; original records remain exact.

The virtual.task-form.v1 adapter and its BMP evidence remain SIMULATED. Owned Windows actions operate only the repository-owned synthetic task form through actual UIA and target-window PrintWindow capture; this grants no generic user-PC authority. Manual steps never become automatic passes. Unsubmitted source forms last only for the current project session; acknowledged records remain durable.

SourceAcceptance is the opt-in synthetic terminal in-memory HTTP fixture, outside app/Plugins, delegating the same production TestingPlugin/page/controller/lifecycle. Its seven non-manifest production payloads match the explicitly paired production ZIP byte-for-byte. Neither profile ships SDK/Core/WinUI/WinRT/.NET copies or reference assemblies. Metadata/deps facts are recomputed against the actual paired host. The actual static-CRT x64 Production helper remains source/toolchain/image bound.

Local/reference compilation is not Windows packaging, activation, DPAPI or provider proof. Installation remains disabled/untrusted/unloaded. Native acceptance and Server observations do not replace Windows 11 human picker/IME/DPI/accessibility acceptance. Real provider credentials, availability, permissions, charges and responses remain unverified. See docs/testing-plugin.md and same-run evidence for measured scope. SHA256SUMS binds this README, provenance and the ZIP.
"@ | Set-Content (Join-Path $OutputDirectory 'README.md') -Encoding utf8
        @($contract.zip, 'provenance.json', 'README.md' | ForEach-Object { (Get-FileHash (Join-Path $OutputDirectory $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_ }) | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS') -Encoding utf8
    } finally { if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force } }
    Test-TestingPluginDelivery -Directory $OutputDirectory -Profile $Profile -ProductionDirectory $ProductionDirectory
}
function Test-TestingPluginDelivery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Directory, [switch]$AllowReferenceOnly, [switch]$RequireCurrentSource,
        [string]$AppDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/native/app'),
        [ValidateSet('Production','SourceAcceptance', IgnoreCase=$false)][string]$Profile = 'Production', [string]$ProductionDirectory)
    Invoke-TestingPluginDeliveryValidation -Directory $Directory -Profile $Profile -ProductionDirectory $ProductionDirectory -AppDirectory $AppDirectory -RequireCurrentSource:$RequireCurrentSource | Out-Null
    Write-Host "Testing $Profile delivery verified: exact archive/bytes/manifest, native helper and managed/dependency/host closure."
}
Export-ModuleMember -Function New-TestingPluginDelivery, Test-TestingPluginDelivery
