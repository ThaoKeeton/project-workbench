Set-StrictMode -Version Latest

# WinRT BitmapEncoder's bounded RGBA8 PNG profile. Validate CRC, inflate,
# scanline filters and nonblank decoded pixels; a matching IHDR alone is insufficient.
if (-not ('NativeGalleryPngV1' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.IO.Compression;
public static class NativeGalleryPngV1 {
  static uint U(byte[] b,int p)=>((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];
  static uint C(byte[] b,int p,int n){uint c=0xffffffff;for(int i=p;i<p+n;i++){c^=b[i];for(int j=0;j<8;j++)c=(c&1)!=0?(c>>1)^0xedb88320:c>>1;}return ~c;}
  static int P(int a,int b,int c){int p=a+b-c,x=Math.Abs(p-a),y=Math.Abs(p-b),z=Math.Abs(p-c);return x<=y&&x<=z?a:y<=z?b:c;}
  public static bool Valid(byte[] b,int w,int h){
    try{
      byte[] sig={137,80,78,71,13,10,26,10};if(b.Length<57||b.Length>16777216)return false;
      for(int i=0;i<8;i++)if(b[i]!=sig[i])return false;
      int q=8,channels=0;bool head=false,data=false,end=false;using var packed=new MemoryStream();
      while(q+12<=b.Length){
        uint n=U(b,q);q+=4;if(n>16777216||(long)q+8+n>b.Length)return false;
        int t=q,d=q+4;q=checked((int)(d+n));if(C(b,t,checked((int)n+4))!=U(b,q))return false;q+=4;
        string name=System.Text.Encoding.ASCII.GetString(b,t,4);
        if(!head&&name!="IHDR")return false;
        if(name=="IHDR"){if(head||n!=13||U(b,d)!=w||U(b,d+4)!=h||w<1||w>4096||h<1||h>4096||
          b[d+8]!=8||(b[d+9]!=6&&b[d+9]!=2)||b[d+10]!=0||b[d+11]!=0||b[d+12]!=0)return false;
          channels=b[d+9]==6?4:3;head=true;}
        else if(name=="IDAT"){if(end)return false;data=true;packed.Write(b,d,checked((int)n));}
        else if(name=="IEND"){if(!data||end||n!=0||q!=b.Length)return false;end=true;break;}
        else if(name=="tRNS")return false;
        else if((b[t]&32)==0)return false;
      }
      if(!head||!data||!end)return false;
      int stride=checked(w*channels),size=checked((stride+1)*h);if(size>67112960)return false;
      byte[] scan=new byte[size];packed.Position=0;
      using(var z=new ZLibStream(packed,CompressionMode.Decompress,true)){int got=0;while(got<size){int k=z.Read(scan,got,size-got);if(k==0)return false;got+=k;}if(z.ReadByte()!=-1)return false;}
      byte[] prev=new byte[stride],cur=new byte[stride],first=new byte[channels];bool visible=false,varied=false;
      for(int y=0;y<h;y++){
        int row=y*(stride+1),f=scan[row];if(f>4)return false;
        for(int x=0;x<stride;x++){int a=x>=channels?cur[x-channels]:0,u=prev[x],v=x>=channels?prev[x-channels]:0;
          int pred=f==0?0:f==1?a:f==2?u:f==3?(a+u)/2:P(a,u,v);cur[x]=(byte)(scan[row+1+x]+pred);}
        for(int x=0;x<stride;x+=channels){visible|=channels==3||cur[x+3]!=0;
          if(y==0&&x==0)Array.Copy(cur,x,first,0,channels);
          else for(int c=0;c<channels;c++)if(cur[x+c]!=first[c])varied=true;}
        var tmp=prev;prev=cur;cur=tmp;
      }
      return visible&&varied;
    }catch{return false;}
  }
}
'@
}
function Assert-GalleryPath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path)
    for($part=$full;$part;$part=[IO.Path]::GetDirectoryName($part)){
        if(Test-Path -LiteralPath $part) {
            if(((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'GalleryReparsePath'}
        }
    }
    if(-not (Test-Path -LiteralPath $full -PathType Leaf)){throw 'GalleryFileMissing'}
    return $full
}
function Test-GalleryNames([System.Text.Json.JsonElement]$Element){
    if($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object){
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($property in $Element.EnumerateObject()){if(-not $seen.Add($property.Name) -or -not (Test-GalleryNames $property.Value)){return $false}}
    }elseif($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array){
        foreach($value in $Element.EnumerateArray()){if(-not (Test-GalleryNames $value)){return $false}}
    }
    return $true
}
function Read-GalleryJson([string]$Path,[long]$MaxBytes=65536){
    $safe=Assert-GalleryPath $Path
    if((Get-Item -LiteralPath $safe).Length -gt $MaxBytes){throw 'GalleryJsonOversized'}
    $text=[Text.UTF8Encoding]::new($false,$true).GetString([IO.File]::ReadAllBytes($safe)).TrimStart([char]0xfeff)
    $options=[Text.Json.JsonDocumentOptions]::new();$options.MaxDepth=16
    $doc=[Text.Json.JsonDocument]::Parse($text,$options)
    try{
        if($doc.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object -or -not (Test-GalleryNames $doc.RootElement)){throw 'GalleryJsonInvalid'}
        return ConvertFrom-Json -InputObject $text -AsHashtable -NoEnumerate -Depth 16 -DateKind String
    }finally{$doc.Dispose()}
}
function Test-GalleryInt([AllowNull()][object]$Value,[long]$Min,[long]$Max){
    return (($Value -is [int] -or $Value -is [long]) -and $Value -ge $Min -and $Value -le $Max)
}
function Test-GalleryHash([AllowNull()][object]$Value){return ($Value -is [string] -and $Value -cmatch '^[0-9a-f]{64}$')}
function Get-GalleryHash([string]$Path){return (Get-FileHash -LiteralPath (Assert-GalleryPath $Path) -Algorithm SHA256).Hash.ToLowerInvariant()}
function Assert-GalleryText([AllowNull()][object]$Actual,[AllowNull()][object]$Expected){
    if($null -eq $Expected){if($null -ne $Actual){throw 'GalleryIdentityMismatch'}}
    elseif($Actual -isnot [string] -or $Actual -cne $Expected){throw 'GalleryIdentityMismatch'}
}
function Write-NativeGalleryBinding {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$OriginalReportPath,[Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][string]$ExecutablePath,[Parameter(Mandatory)][string]$SourceCommit,
        [AllowNull()][object]$ProcessId,[AllowNull()][object]$ProcessCreationFileTimeUtc,
        [AllowNull()][object]$ProcessExitFileTimeUtc,[AllowNull()][object]$ExitCode,[bool]$HasExited,
        [string]$InstalledRoot='',[string[]]$PackageIds=@(),[hashtable]$SourceZipPaths=@{},[string]$SourceZipRoot='')
    if($Phase -cnotmatch '^(native-smoke|workspace-(seed|verify)|testing-(seed|verify)|business-(seed|acceptance-seed))$' -or
        $SourceCommit -cnotmatch '^[0-9a-f]{40}$'){throw 'GalleryBindingArgumentsInvalid'}
    $path=[IO.Path]::ChangeExtension($OriginalReportPath,'gallery-binding.json')
    $dll=Join-Path (Split-Path $ExecutablePath -Parent) 'Workbench.App.dll'
    $reportHash=$null;$nativeSucceeded=$false
    if(Test-Path -LiteralPath $OriginalReportPath -PathType Leaf){
        $reportHash=Get-GalleryHash $OriginalReportPath
        try{$report=Read-GalleryJson $OriginalReportPath 4194304;$nativeSucceeded=$report['succeeded'] -is [bool] -and $report['succeeded']}catch{}
    }
    $succeeded=$nativeSucceeded -and $HasExited -and (Test-GalleryInt $ExitCode 0 0)
    $runnerReportFile=$null;$runnerReportHash=$null;$runnerSucceeded=$null
    if($Phase -like 'testing-*'){
        $runnerPath=[IO.Path]::ChangeExtension($OriginalReportPath,'runner.json')
        $runnerSucceeded=$false
        if(Test-Path -LiteralPath $runnerPath -PathType Leaf){
            $runnerReportFile=[IO.Path]::GetFileName($runnerPath)
            $runnerReportHash=Get-GalleryHash $runnerPath
            try{
                $runner=Read-GalleryJson $runnerPath
                $runnerSucceeded=$runner['succeeded'] -is [bool] -and $runner['succeeded'] -and
                    (Test-GalleryInt $runner['processId'] 1 ([int]::MaxValue)) -and $runner['processId'] -eq $ProcessId -and
                    $runner['hasExited'] -is [bool] -and $runner['hasExited'] -and
                    (Test-GalleryInt $runner['exitCode'] ([int]::MinValue) ([int]::MaxValue)) -and
                    $runner['exitCode'] -eq $ExitCode -and $runner['rootLeaseAvailable'] -is [bool] -and $runner['rootLeaseAvailable']
            }catch{}
        }
        $succeeded=$succeeded -and $runnerSucceeded
    }
    $packages=@{}
    foreach($id in $PackageIds){
        if($InstalledRoot -eq '' -or $id -cnotmatch '^com\.[a-z0-9.-]{1,100}$' -or $packages.ContainsKey($id)){throw 'GalleryPackageIdInvalid'}
        $folder=Join-Path $InstalledRoot $id;$manifest=Join-Path $folder 'plugin.json'
        if(-not (Test-Path -LiteralPath $manifest -PathType Leaf)){continue}
        $data=Read-GalleryJson $manifest;$entry=$data['entryAssembly']
        if($data['id'] -cne $id -or $entry -isnot [string] -or $entry -cnotmatch '^[A-Za-z0-9_.-]+\.dll$'){throw 'GalleryPackageManifestInvalid'}
        $packages[$id]=@{manifestSha256=(Get-GalleryHash $manifest);entrySha256=(Get-GalleryHash (Join-Path $folder $entry));entryAssembly=$entry}
        if($SourceZipPaths.ContainsKey($id)){
            $zip=$SourceZipPaths[$id]
            if($SourceZipRoot -eq ''){throw 'GallerySourceZipRootMissing'}
            $relative=[IO.Path]::GetRelativePath($SourceZipRoot,$zip).Replace('\','/')
            if($relative -cnotmatch '^([A-Za-z0-9_.-]+/)?[A-Za-z0-9_.-]+\.zip$'){throw 'GallerySourceZipPathInvalid'}
            $packages[$id]['sourceZipName']=$relative
            $packages[$id]['sourceZipSha256']=Get-GalleryHash $zip
        }
    }
    $record=[ordered]@{schemaVersion=1;phase=$Phase;processId=$ProcessId;processCreationFileTimeUtc=$ProcessCreationFileTimeUtc;
        processExitFileTimeUtc=$ProcessExitFileTimeUtc;hasExited=$HasExited;exitCode=$ExitCode;
        executableSha256=(Get-GalleryHash $ExecutablePath);hostDllSha256=(Get-GalleryHash $dll);
        hostInformationalVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion;sourceCommit=$SourceCommit;
        originalReportFile=[IO.Path]::GetFileName($OriginalReportPath);originalReportSha256=$reportHash;
        nativeReportSucceeded=$nativeSucceeded;runnerReportFile=$runnerReportFile;
        runnerReportSha256=$runnerReportHash;runnerReportSucceeded=$runnerSucceeded;
        originalProbeSucceeded=$succeeded;packages=$packages}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($record | ConvertTo-Json -Depth 12))
    $stream=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
    return $path
}
function Test-NativeGalleryReceipt {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ReceiptPath,[Parameter(Mandatory)][string]$BindingPath,
        [Parameter(Mandatory)][string]$OriginalReportPath,[Parameter(Mandatory)][string]$ExecutablePath,
        [Parameter(Mandatory)][string]$ExpectedSourceCommit,[Parameter(Mandatory)][string]$ExpectedShotId,
        [Parameter(Mandatory)][string]$ExpectedPageKey,[Parameter(Mandatory)][string]$ExpectedPhase,
        [Parameter(Mandatory)][string]$ExpectedProbeKind,[AllowNull()][object]$ExpectedPluginId,
        [AllowNull()][object]$ExpectedViewId,[string]$ExpectedPackageRoot='',[string]$ExpectedZipRoot='')
    $r=Read-GalleryJson $ReceiptPath;$b=Read-GalleryJson $BindingPath;$original=Read-GalleryJson $OriginalReportPath 4194304
    if($ExpectedShotId -cnotmatch '^[a-z][a-z0-9-]{0,63}$' -or
        -not (Test-GalleryInt $r['schemaVersion'] 1 1) -or -not (Test-GalleryInt $b['schemaVersion'] 1 1) -or
        $r['captureKind'] -cne 'WinUiContentRender' -or $r['scope'] -cne 'actual-window-content-only' -or
        $r['titleBarIncluded'] -isnot [bool] -or $r['titleBarIncluded'] -or
        $r['popupsIncluded'] -isnot [bool] -or $r['popupsIncluded']){throw 'GallerySchemaInvalid'}
    Assert-GalleryText $r['shotId'] $ExpectedShotId
    Assert-GalleryText $r['pageKey'] $ExpectedPageKey
    Assert-GalleryText $r['phase'] $ExpectedPhase
    Assert-GalleryText $r['probeKind'] $ExpectedProbeKind
    Assert-GalleryText $r['pluginId'] $ExpectedPluginId
    Assert-GalleryText $r['viewId'] $ExpectedViewId
    Assert-GalleryText $b['phase'] $ExpectedPhase
    Assert-GalleryText $b['sourceCommit'] $ExpectedSourceCommit
    Assert-GalleryText $b['originalReportFile'] ([IO.Path]::GetFileName($OriginalReportPath))
    if(-not (Test-GalleryHash $b['executableSha256']) -or $b['executableSha256'] -cne (Get-GalleryHash $ExecutablePath) -or
       -not (Test-GalleryHash $b['originalReportSha256']) -or $b['originalReportSha256'] -cne (Get-GalleryHash $OriginalReportPath)){throw 'GalleryBindingHashMismatch'}
    $dll=Join-Path (Split-Path $ExecutablePath -Parent) 'Workbench.App.dll'
    if(-not $b.Contains('hostDllSha256') -or -not (Test-GalleryHash $b['hostDllSha256']) -or
       $b['hostDllSha256'] -cne (Get-GalleryHash $dll)){throw 'GalleryHostIdentityMismatch'}
    $actualVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Assert-GalleryPath $dll)).ProductVersion
    if($actualVersion -cne ("0.4.0+" + $ExpectedSourceCommit) -or
       $b['hostInformationalVersion'] -cne $actualVersion){throw 'GallerySourceIdentityMismatch'}
    if(-not (Test-GalleryInt $b['processId'] 1 ([int]::MaxValue)) -or
       -not (Test-GalleryInt $b['processCreationFileTimeUtc'] 1 ([long]::MaxValue)) -or
       -not (Test-GalleryInt $b['processExitFileTimeUtc'] 1 ([long]::MaxValue)) -or
       $b['processExitFileTimeUtc'] -le $b['processCreationFileTimeUtc'] -or
       $b['hasExited'] -isnot [bool] -or -not $b['hasExited'] -or
       -not (Test-GalleryInt $b['exitCode'] ([int]::MinValue) ([int]::MaxValue)) -or
       $b['originalProbeSucceeded'] -isnot [bool] -or
       $original['succeeded'] -isnot [bool] -or
       ($b['originalProbeSucceeded'] -and (-not $original['succeeded'] -or $b['exitCode'] -ne 0)) -or
       ($b.Contains('nativeReportSucceeded') -and
           ($b['nativeReportSucceeded'] -isnot [bool] -or $b['nativeReportSucceeded'] -ne $original['succeeded'])) -or
       (($original['succeeded'] -or $original.Contains('processId')) -and
           (-not (Test-GalleryInt $original['processId'] 1 ([int]::MaxValue)) -or
            $original['processId'] -ne $b['processId']))){throw 'GalleryOriginalProbeFailed'}
    if(($original.Contains('processCreationFileTimeUtc') -and
            (-not (Test-GalleryInt $original['processCreationFileTimeUtc'] 1 ([long]::MaxValue)) -or
             $original['processCreationFileTimeUtc'] -ne $b['processCreationFileTimeUtc'])) -or
       ($original.Contains('exitCode') -and
            (-not (Test-GalleryInt $original['exitCode'] ([int]::MinValue) ([int]::MaxValue)) -or
             $original['exitCode'] -ne $b['exitCode'])) -or
       ($original.Contains('hasExited') -and ($original['hasExited'] -isnot [bool] -or -not $original['hasExited']))){
        throw 'GalleryOriginalProbeIdentityMismatch'
    }
    if($ExpectedProbeKind -eq 'testing'){
        $runnerPath=[IO.Path]::ChangeExtension($OriginalReportPath,'runner.json')
        $runnerSucceeded=$false
        if(Test-Path -LiteralPath $runnerPath -PathType Leaf){
            Assert-GalleryText $b['runnerReportFile'] ([IO.Path]::GetFileName($runnerPath))
            if(-not (Test-GalleryHash $b['runnerReportSha256']) -or
               $b['runnerReportSha256'] -cne (Get-GalleryHash $runnerPath)){throw 'GalleryRunnerHashMismatch'}
            $runner=Read-GalleryJson $runnerPath
            if(-not (Test-GalleryInt $runner['schemaVersion'] 1 1) -or
               $runner['succeeded'] -isnot [bool] -or
               -not (Test-GalleryInt $runner['processId'] 1 ([int]::MaxValue)) -or $runner['processId'] -ne $b['processId'] -or
               $runner['hasExited'] -isnot [bool] -or -not $runner['hasExited'] -or
               -not (Test-GalleryInt $runner['exitCode'] ([int]::MinValue) ([int]::MaxValue)) -or
               $runner['exitCode'] -ne $b['exitCode'] -or $runner['rootLeaseAvailable'] -isnot [bool]){
                throw 'GalleryRunnerIdentityMismatch'
            }
            $runnerSucceeded=$runner['succeeded'] -and $runner['rootLeaseAvailable'] -and $runner['exitCode'] -eq 0
        }elseif($null -ne $b['runnerReportFile'] -or $null -ne $b['runnerReportSha256']){
            throw 'GalleryRunnerHashMismatch'
        }
        if($b['runnerReportSucceeded'] -isnot [bool] -or $b['runnerReportSucceeded'] -ne $runnerSucceeded -or
           $b['originalProbeSucceeded'] -ne ($original['succeeded'] -and $runnerSucceeded -and $b['exitCode'] -eq 0)){
            throw 'GalleryRunnerVerdictMismatch'
        }
    }
    if($r['status'] -isnot [string] -or $r['status'] -cnotin @('captured','blocked','failed','skipped-unavailable','skipped-unsafe') -or
       $r['reasonCode'] -isnot [string] -or $r['reasonCode'] -cnotin @('none','precondition-unavailable','page-detached',
           'desktop-unavailable','focus-detached','focus-outside-viewport','layout-unavailable','render-invalid','capture-error') -or
       $r['witnessCode'] -isnot [string] -or $r['witnessCode'] -cnotmatch '^[a-z][a-z0-9-]{0,63}$' -or
       -not (Test-GalleryInt $r['witnessCount'] 0 1000000)){throw 'GalleryReceiptFieldsInvalid'}
    if($r['status'] -cne 'captured'){
        if($null -ne $r['pngPath'] -or $null -ne $r['pngSha256'] -or $r['pngBytes'] -ne 0){throw 'GalleryBlockedImageInvalid'}
        return @{shotId=$ExpectedShotId;status=$r['status'];reasonCode=$r['reasonCode'];phase=$ExpectedPhase;
            originalProbeSucceeded=$b['originalProbeSucceeded'];nativeReportSucceeded=$original['succeeded'];
            processExitCode=$b['exitCode'];imagePath=$null}
    }
    Assert-GalleryText $r['selectedPageKey'] $ExpectedPageKey
    Assert-GalleryText $r['selectedPluginId'] $ExpectedPluginId
    Assert-GalleryText $r['selectedViewId'] $ExpectedViewId
    if($ExpectedShotId -eq 'workspace-single-context' -or $ExpectedProbeKind -eq 'testing'){
        Assert-GalleryText $r['projectId'] 'default'
    }elseif($ExpectedShotId -eq 'workspace-multi-context'){
        if($r['projectId'] -isnot [string] -or $r['projectId'] -cnotmatch '^[0-9a-f]{32}$'){throw 'GalleryProjectIdentityMismatch'}
    }elseif($null -ne $r['projectId']){throw 'GalleryUnexpectedProjectIdentity'}
    if($r['reasonCode'] -cne 'none' -or -not (Test-GalleryInt $r['processId'] 1 ([int]::MaxValue)) -or
       $r['processId'] -ne $b['processId'] -or
       -not (Test-GalleryInt $r['processCreationFileTimeUtc'] 1 ([long]::MaxValue)) -or
       $r['processCreationFileTimeUtc'] -ne $b['processCreationFileTimeUtc'] -or
       -not (Test-GalleryInt $r['hwnd'] 1 ([long]::MaxValue)) -or
       $r['desktopVisible'] -isnot [bool] -or -not $r['desktopVisible'] -or
       $r['windowVisible'] -isnot [bool] -or -not $r['windowVisible'] -or
       -not (Test-GalleryInt $r['sessionId'] 0 ([int]::MaxValue)) -or
       $r['architecture'] -cne 'X64' -or $r['osDescription'] -isnot [string] -or $r['osDescription'].Length -gt 256 -or
       $r['sourceCommit'] -cne $ExpectedSourceCommit -or
       $r['hostInformationalVersion'] -cne $b['hostInformationalVersion'] -or
       $r['executableSha256'] -cne $b['executableSha256']){throw 'GalleryCaptureIdentityMismatch'}
    if($r['captureUtc'] -isnot [string] -or $r['captureUtc'].Length -gt 64 -or
       $r['captureUtc'] -cnotmatch '^\d{4}-\d\d-\d\dT' -or
       -not (Test-GalleryInt $r['width'] 1 4096) -or -not (Test-GalleryInt $r['height'] 1 4096) -or
       ([long]$r['width']*[long]$r['height']*4) -gt 67108864 -or
       -not ($r['rasterizationScale'] -is [double] -or $r['rasterizationScale'] -is [int] -or $r['rasterizationScale'] -is [long]) -or
       $r['rasterizationScale'] -le 0 -or $r['rasterizationScale'] -gt 8 -or
       -not ($r['viewportWidth'] -is [double] -or $r['viewportWidth'] -is [int] -or $r['viewportWidth'] -is [long]) -or
       $r['viewportWidth'] -le 0 -or $r['viewportWidth'] -gt 4096 -or
       -not ($r['viewportHeight'] -is [double] -or $r['viewportHeight'] -is [int] -or $r['viewportHeight'] -is [long]) -or
       $r['viewportHeight'] -le 0 -or $r['viewportHeight'] -gt 4096 -or
       -not ($r['scrollOffset'] -is [double] -or $r['scrollOffset'] -is [int] -or $r['scrollOffset'] -is [long]) -or
       $r['scrollOffset'] -lt 0 -or $r['scrollOffset'] -gt 1000000 -or
       $r['theme'] -cnotin @('Default','Light','Dark')){throw 'GalleryGeometryInvalid'}
    try{$capture=[DateTimeOffset]::Parse($r['captureUtc'],[Globalization.CultureInfo]::InvariantCulture)}
    catch{throw 'GalleryCaptureTimeInvalid'}
    $captureFileTime=$capture.UtcDateTime.ToFileTimeUtc()
    if($capture.Offset.TotalMinutes -ne 0 -or $captureFileTime -lt $b['processCreationFileTimeUtc'] -or
       $captureFileTime -gt $b['processExitFileTimeUtc']){throw 'GalleryCaptureTimeInvalid'}
    $manifestReceiptHash=$null;$entryReceiptHash=$null;$sourceZipHash=$null
    if($ExpectedPluginId){
        if($ExpectedPackageRoot -eq '' -or $b['packages'] -isnot [Collections.IDictionary] -or
           -not $b['packages'].Contains($ExpectedPluginId)){throw 'GalleryPackageBindingMissing'}
        $p=$b['packages'][$ExpectedPluginId];$folder=Join-Path $ExpectedPackageRoot $ExpectedPluginId
        if($p -isnot [Collections.IDictionary] -or $p['entryAssembly'] -isnot [string] -or
           $p['entryAssembly'] -cnotmatch '^[A-Za-z0-9_.-]+\.dll$' -or
           $r['packageManifestSha256'] -cne $p['manifestSha256'] -or $r['packageEntrySha256'] -cne $p['entrySha256'] -or
           $p['manifestSha256'] -cne (Get-GalleryHash (Join-Path $folder 'plugin.json')) -or
           $p['entrySha256'] -cne (Get-GalleryHash (Join-Path $folder $p['entryAssembly']))){throw 'GalleryPackageHashMismatch'}
        if($ExpectedZipRoot -ne ''){
            if($p['sourceZipName'] -isnot [string] -or $p['sourceZipName'] -cnotmatch '^([A-Za-z0-9_.-]+/)?[A-Za-z0-9_.-]+\.zip$' -or
               -not (Test-GalleryHash $p['sourceZipSha256']) -or
               $p['sourceZipSha256'] -cne (Get-GalleryHash (Join-Path $ExpectedZipRoot $p['sourceZipName']))){throw 'GallerySourceZipMismatch'}
            if($ExpectedProbeKind -eq 'testing' -and $original['packageSha256'] -cne $p['sourceZipSha256']){throw 'GallerySourceZipMismatch'}
            if($ExpectedProbeKind -eq 'business' -and
                @($original['provenance'] | Where-Object { $_['pluginId'] -ceq $ExpectedPluginId -and $_['sha256'] -ceq $p['sourceZipSha256'] }).Count -ne 1){
                throw 'GallerySourceZipMismatch'
            }
        }
        $manifestReceiptHash=$p['manifestSha256'];$entryReceiptHash=$p['entrySha256'];$sourceZipHash=$p['sourceZipSha256']
    }elseif($null -ne $r['packageManifestSha256'] -or $null -ne $r['packageEntrySha256']){throw 'GalleryUnexpectedPackageIdentity'}
    if($r['pngPath'] -isnot [string] -or $r['pngPath'] -cne ($ExpectedShotId+'.png') -or
       -not (Test-GalleryInt $r['pngBytes'] 1 16777216) -or -not (Test-GalleryHash $r['pngSha256'])){throw 'GalleryPngReceiptInvalid'}
    $image=Assert-GalleryPath (Join-Path (Split-Path $ReceiptPath -Parent) $r['pngPath'])
    if((Get-Item -LiteralPath $image).Length -ne $r['pngBytes'] -or
       (Get-GalleryHash $image) -cne $r['pngSha256'] -or
       -not [NativeGalleryPngV1]::Valid([IO.File]::ReadAllBytes($image),$r['width'],$r['height'])){throw 'GalleryPngInvalid'}
    return @{shotId=$ExpectedShotId;status='captured';reasonCode='none';phase=$ExpectedPhase;
        originalProbeSucceeded=$b['originalProbeSucceeded'];nativeReportSucceeded=$original['succeeded'];
        processExitCode=$b['exitCode'];
        imagePath=$image;pngBytes=$r['pngBytes'];pngSha256=$r['pngSha256'];width=$r['width'];height=$r['height'];
        processId=$b['processId'];processCreationFileTimeUtc=$b['processCreationFileTimeUtc'];
        processExitFileTimeUtc=$b['processExitFileTimeUtc'];executableSha256=$b['executableSha256'];sourceCommit=$ExpectedSourceCommit;
        pluginId=$ExpectedPluginId;viewId=$ExpectedViewId;packageManifestSha256=$manifestReceiptHash;
        packageEntrySha256=$entryReceiptHash;packageZipSha256=$sourceZipHash;
        witnessCode=$r['witnessCode'];witnessCount=$r['witnessCount']}
}
function Assert-NativeGalleryDirectory {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Directory,[Parameter(Mandatory)][string[]]$ExpectedShotIds)
    if(-not (Test-Path -LiteralPath $Directory -PathType Container)){return}
    $full=[IO.Path]::GetFullPath($Directory)
    for($part=$full;$part;$part=[IO.Path]::GetDirectoryName($part)){
        if((Test-Path -LiteralPath $part) -and
            ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'GalleryReparsePath'}
    }
    $allowed=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($id in $ExpectedShotIds){
        if($id -cnotmatch '^[a-z][a-z0-9-]{0,63}$' -or -not $allowed.Add($id)){throw 'GalleryRosterDuplicate'}
    }
    $files=@(Get-ChildItem -LiteralPath $full -Force)
    if($files.Count -gt 64){throw 'GalleryDirectoryOversized'}
    foreach($file in $files){
        if($file -isnot [IO.FileInfo] -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
           $file.Name -cnotmatch '^([a-z][a-z0-9-]{0,63})\.(json|png)$' -or
           -not $allowed.Contains($Matches[1])){throw 'GalleryUnexpectedFile'}
        if($Matches[2] -ceq 'png' -and -not (Test-Path -LiteralPath (Join-Path $full ($Matches[1]+'.json')) -PathType Leaf)){
            throw 'GalleryOrphanImage'
        }
    }
}
function Export-NativeGallery {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $repo=[IO.Path]::GetFullPath($RepositoryRoot)
    $native=Join-Path $repo 'artifacts/native'
    $exe=Join-Path $native 'app/Workbench.App.exe'
    $source=(& git -C $repo rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0 -or $source -cnotmatch '^[0-9a-f]{40}$'){throw 'GallerySourceUnavailable'}
    $testingBase=Join-Path $native 'testing-probes'
    $testingOutput=Join-Path $testingBase '_unreached'
    if(Test-Path -LiteralPath $testingBase -PathType Container){
        $candidates=@(Get-ChildItem -LiteralPath $testingBase -Directory -Force)
        if($candidates.Count -gt 1 -or ($candidates.Count -eq 1 -and $candidates[0].Name -cnotmatch '^\d{8}T\d{6}Z-[0-9a-f]{32}$')){
            throw 'GalleryTestingRootAmbiguous'
        }
        if($candidates.Count -eq 1){$testingOutput=$candidates[0].FullName}
    }
    $smoke=Join-Path $native 'native-smoke.json'
    $workspace=Join-Path $native 'workspace-probes/seed.json'
    $testing=Join-Path $testingOutput 'seed.json'
    $business=Join-Path $native 'business-probes/seed.json'
    $acceptance=Join-Path $native 'business-probes/acceptance-seed.json'
    $groups=@(
        @{phase='native-smoke';probe='smoke';report=$smoke;dir=(Join-Path $native 'gallery/native-smoke');
          packages=(Join-Path $native 'app/Plugins');zipRoot='';shots=@(
            @('home','home',$null,$null),@('host-plugins-disabled','plugins',$null,$null),@('host-plugins','plugins',$null,$null),
            @('host-commands','commands',$null,$null),@('host-background','background',$null,$null),
            @('host-credentials','credentials',$null,$null),@('host-packages','packages',$null,$null),
            @('host-settings','settings',$null,$null),
            @('summary-page','view','com.projectworkbench.summary','summary'),
            @('summary-settings','view','com.projectworkbench.summary','preferences'),
            @('cases-page','view','com.projectworkbench.test-cases','cases'),
            @('cases-settings','view','com.projectworkbench.test-cases','preferences'))},
        @{phase='workspace-seed';probe='workspace';report=$workspace;dir=(Join-Path $native 'workspace-probes/gallery/workspace-seed');
          packages=(Join-Path $native 'app/Plugins');zipRoot='';shots=@(
            @('workspace-single-context','context',$null,$null),@('workspace-multi-context','context',$null,$null))},
        @{phase='testing-seed';probe='testing';report=$testing;dir=(Join-Path $testingOutput 'gallery/testing-seed');
          packages='';zipRoot=(Join-Path $native 'testing-plugin');shots=@(
            @('testing-authoring','view','com.projectworkbench.testing','testing'),
            @('testing-virtual-evidence','view','com.projectworkbench.testing','testing'),
            @('testing-owned-panel','view','com.projectworkbench.testing','testing'))},
        @{phase='business-seed';probe='business';report=$business;dir=(Join-Path $native 'business-probes/gallery/business-seed');
          packages='';zipRoot=(Join-Path $native 'fixtures/business');shots=@(
            @('business-plugins','plugins',$null,$null),@('business-background','background',$null,$null),
            @('dingtalk-settings','view','com.projectworkbench.dingtalk-insights','settings'),
            @('dingtalk-workspace','view','com.projectworkbench.dingtalk-insights','workspace'),
            @('upcam-preferences','view','com.upcam.task-digest','preferences'),
            @('upcam-preview','view','com.upcam.task-digest','digest'))},
        @{phase='business-acceptance-seed';probe='business';report=$acceptance;dir=(Join-Path $native 'business-probes/gallery/business-acceptance-seed');
          packages='';zipRoot=(Join-Path $native 'fixtures/business-acceptance');shots=@(,@('upcam-acceptance-history','view','com.upcam.task-digest.acceptance','digest'))}
    )
    $rows=[Collections.Generic.List[object]]::new()
    $phases=[Collections.Generic.List[object]]::new()
    $captured=[Collections.Generic.List[object]]::new()
    $allIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($group in $groups){
        $ids=@($group.shots | ForEach-Object { $_[0] })
        Assert-NativeGalleryDirectory -Directory $group.dir -ExpectedShotIds $ids
        $binding=[IO.Path]::ChangeExtension($group.report,'gallery-binding.json')
        $originalStatus='unreached';$nativeReportStatus='unreached'
        if(Test-Path -LiteralPath $group.report -PathType Leaf){
            $original=Read-GalleryJson $group.report 4194304
            $nativeReportStatus=if($original['succeeded'] -is [bool] -and $original['succeeded']){'succeeded'}else{'failed'}
            $originalStatus=$nativeReportStatus
            if(Test-Path -LiteralPath $binding -PathType Leaf){
                $phaseBinding=Read-GalleryJson $binding
                if($phaseBinding['originalProbeSucceeded'] -isnot [bool] -or -not $phaseBinding['originalProbeSucceeded']){
                    $originalStatus='failed'
                }
            }
        }
        $phases.Add(@{phase=$group.phase;originalProbeStatus=$originalStatus;nativeReportStatus=$nativeReportStatus})
        foreach($shot in $group.shots){
            $id=$shot[0]
            if(-not $allIds.Add($id)){throw 'GalleryRosterDuplicate'}
            $receipt=Join-Path $group.dir ($id+'.json')
            if(-not (Test-Path -LiteralPath $receipt -PathType Leaf)){
                $status=if($originalStatus -eq 'failed'){'failed'}else{'blocked'}
                $reason=if($originalStatus -eq 'failed'){'original-probe-failed'}else{'checkpoint-unreached'}
                $rows.Add(@{shotId=$id;phase=$group.phase;status=$status;reasonCode=$reason;originalProbeStatus=$originalStatus})
                continue
            }
            if($originalStatus -eq 'failed'){
                $attempt=Read-GalleryJson $receipt
                if($attempt['status'] -isnot [string] -or $attempt['status'] -cne 'captured'){
                    if(Test-Path -LiteralPath (Join-Path $group.dir ($id+'.png'))){throw 'GalleryUnexpectedImage'}
                    $rows.Add(@{shotId=$id;phase=$group.phase;status='failed';reasonCode='original-probe-failed';originalProbeStatus='failed';
                        rawAttemptReceipt=($id+'.json')})
                    continue
                }
            }
            if(-not (Test-Path -LiteralPath $binding -PathType Leaf)){throw 'GalleryBindingMissing'}
            $packageRoot=$group.packages
            if($group.phase -eq 'testing-seed' -or $group.phase -like 'business-*'){
                $bindingData=Read-GalleryJson $binding
                $rootReport=Read-GalleryJson $group.report 4194304
                $kind=if($group.phase -eq 'testing-seed'){'testing'}else{'business'}
                $parent=Join-Path ([IO.Path]::GetTempPath()) ('ProjectWorkbench-'+$kind+'-probe')
                if($rootReport['userRoot'] -isnot [string] -or
                   [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($rootReport['userRoot'])) -cne [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($parent)) -or
                   [IO.Path]::GetFileName($rootReport['userRoot']) -cnotmatch '^[0-9a-f]{32}$'){throw 'GalleryProbeRootInvalid'}
                $packageRoot=Join-Path $rootReport['userRoot'] 'Plugins'
            }
            $result=Test-NativeGalleryReceipt -ReceiptPath $receipt -BindingPath $binding -OriginalReportPath $group.report `
                -ExecutablePath $exe -ExpectedSourceCommit $source -ExpectedShotId $id -ExpectedPageKey $shot[1] `
                -ExpectedPhase $group.phase -ExpectedProbeKind $group.probe -ExpectedPluginId $shot[2] -ExpectedViewId $shot[3] `
                -ExpectedPackageRoot $packageRoot -ExpectedZipRoot $group.zipRoot
            if($result.status -ne 'captured' -and (Test-Path -LiteralPath (Join-Path $group.dir ($id+'.png')))){
                throw 'GalleryUnexpectedImage'
            }
            $result['originalProbeStatus']=$originalStatus
            if($id -eq 'testing-owned-panel'){$result['actualOwnedRunProof']='blocked-no-owned-run-in-testing-probe'}
            if($id -eq 'testing-virtual-evidence'){$result['embeddedFrame']='SIMULATED virtual BMP inside genuine WinUI capture'}
            if($id -eq 'upcam-acceptance-history'){$result['providerOutcome']='synthetic local fake sent; no real delivery'}
            if($result.status -eq 'captured'){$captured.Add($result)}
            if($originalStatus -eq 'failed'){
                $result['captureStatus']='captured'
                $result['status']='failed'
                $result['reasonCode']='original-probe-failed'
                $result['rawAttemptReceipt']=$id+'.json'
            }
            $rows.Add($result)
        }
    }
    $rows.Add(@{shotId='docs-page';phase='navigation';status='skipped-unavailable';
        reasonCode='no-separate-docs-page';availableSurface='project-context-editor'})
    if($captured.Count -gt 32 -or $rows.Count -gt 32){throw 'GalleryLimitExceeded'}
    $output=Join-Path $native 'genuine-gallery'
    for($part=[IO.Path]::GetFullPath($output);$part;$part=[IO.Path]::GetDirectoryName($part)){
        if((Test-Path -LiteralPath $part) -and
            ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'GalleryReparsePath'}
    }
    if(Test-Path -LiteralPath (Join-Path $output 'index.json')){throw 'GalleryIndexAlreadyExists'}
    # Stage on the same volume and publish only a complete directory. A failure leaves
    # every original probe PNG untouched for the consolidated raw-evidence artifact.
    $stage=Join-Path $native ('genuine-gallery.staging-'+[Guid]::NewGuid().ToString('N'))
    if(Test-Path -LiteralPath $stage){throw 'GalleryStageAlreadyExists'}
    $imageFolder=Join-Path $stage 'images'
    [IO.Directory]::CreateDirectory($imageFolder) | Out-Null
    foreach($row in $captured){
        $sourceImage=Assert-GalleryPath $row.imagePath
        $destination=Join-Path $imageFolder ($row.shotId+'.png')
        $total=0L
        $buffer=[byte[]]::new(65536)
        $inputStream=[IO.File]::OpenRead($sourceImage)
        try{
            $outputStream=[IO.FileStream]::new($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try{
                while(($count=$inputStream.Read($buffer,0,$buffer.Length)) -gt 0){
                    $total+=$count
                    if($total -gt $row.pngBytes -or $total -gt 16777216){throw 'GalleryStageImageChanged'}
                    $outputStream.Write($buffer,0,$count)
                }
                $outputStream.Flush($true)
            }finally{$outputStream.Dispose()}
        }finally{$inputStream.Dispose()}
        if($total -ne $row.pngBytes -or (Get-GalleryHash $destination) -cne $row.pngSha256){
            throw 'GalleryStageImageChanged'
        }
        $row['imagePath']='images/'+$row.shotId+'.png'
    }
    $index=[ordered]@{schemaVersion=1;captureKind='WinUiContentRender';scope='actual-window-content-only';
        titleBarIncluded=$false;popupsIncluded=$false;sourceCommit=$source;acceptedImages=$captured.Count;
        phases=@($phases);rows=@($rows);reviewedByHuman=$false;
        limits='Windows Server gallery evidence only; Windows 11 human and visual review pending.'}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($index | ConvertTo-Json -Depth 16))
    if($bytes.Length -lt 2 -or $bytes.Length -gt 65536){throw 'GalleryIndexInvalid'}
    $stageIndex=Join-Path $stage 'index.json'
    $stream=[IO.FileStream]::new($stageIndex,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
    $staged=Read-GalleryJson $stageIndex
    if($staged['acceptedImages'] -ne $captured.Count -or @($staged['rows']).Count -ne $rows.Count){throw 'GalleryIndexInvalid'}
    [IO.Directory]::Move($stage,$output)
    return $index
}
Export-ModuleMember -Function Test-NativeGalleryReceipt,Write-NativeGalleryBinding,Assert-NativeGalleryDirectory,Export-NativeGallery
