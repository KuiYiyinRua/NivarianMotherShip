param([Parameter(Mandatory=$true)][string]$Label,[switch]$Seed,[switch]$Trace,[int]$Ticks=20000,[int]$Port=31573,[string]$SeedSave)
$ErrorActionPreference='Stop'
$project=(Resolve-Path "$PSScriptRoot\..\..").Path
$root=Join-Path $project 'BuildValidation\TickPerformanceMP-20261021'
$run=Join-Path $root $Label
if(Test-Path -LiteralPath $run){throw 'Each run must have a unique unused label'}
$gameRoot=(Resolve-Path "$project\..\..").Path
$game=Join-Path $env:TEMP ('NMCTickMP-'+[guid]::NewGuid().ToString('N')+'\Game')
New-Item -ItemType Directory -Force "$game\Mods\Probe\About","$game\Mods\Probe\Assemblies","$game\Mods\NivarianCommander","$game\Mods\Meow","$run\HarnessSource" | Out-Null
foreach($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$game\$name" -Target "$gameRoot\$name" | Out-Null}
foreach($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','Version.txt','steam_appid.txt')){Copy-Item -LiteralPath "$gameRoot\$name" -Destination $game}
foreach($name in @('2934420800','2009463077','Multiplayer','839005762','3624805128','3735573834','2988801276','3256974620')){Copy-Item -LiteralPath "$gameRoot\Mods\$name" -Destination "$game\Mods" -Recurse}
foreach($name in @('About','1.6','Royalty','NoRoyalty','Multiplayer','LoadFolders.xml')){Copy-Item -LiteralPath "$root\Candidate\$name" -Destination "$game\Mods\NivarianCommander" -Recurse}
foreach($name in @('About','1.6','Patches','Languages','LoadFolders.xml')){Copy-Item -LiteralPath "$gameRoot\Mods\MP-meow-online-shop\$name" -Destination "$game\Mods\Meow" -Recurse}
Get-ChildItem -LiteralPath $PSScriptRoot -File | Copy-Item -Destination "$run\HarnessSource"
Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\net48\NMC.TickPerformanceMPProbe.dll" -Destination "$game\Mods\Probe\Assemblies"
$utf=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText("$game\Mods\Probe\About\About.xml",'<ModMetaData><name>NMC tick multiplayer verification</name><author>Validation</author><packageId>local.nmc.tickmpprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>',$utf)
$ids=@('zetrith.prepatcher','brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','rwmt.multiplayer','erdelf.humanoidalienraces','ancot.ancotlibrary','ancot.milirarace','keeptpa.nivarianrace','keeptpa.nivariandraconicmilitary','local.nivarian.mothership.commander','local.mp.meowonlineshop.sellslingshot','local.nmc.tickmpprobe')
$expansions=$ids | Where-Object {$_ -like 'ludeon.rimworld.*'}
$config='<ModsConfigData><version>1.6.4850 rev645</version><activeMods>'+($ids|ForEach-Object {'<li>'+$_+'</li>'})+'</activeMods><knownExpansions>'+($expansions|ForEach-Object {'<li>'+$_+'</li>'})+'</knownExpansions></ModsConfigData>'
foreach($peer in @('Host','Client')){
 New-Item -ItemType Directory -Force "$run\$peer\Config","$run\$peer\Saves","$run\$peer\MpReplays" | Out-Null
 [IO.File]::WriteAllText("$run\$peer\Config\ModsConfig.xml",$config,$utf)
 [IO.File]::WriteAllText("$run\$peer\Config\Prefs.xml",'<PrefsData><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><runInBackground>True</runInBackground><pauseOnLoad>False</pauseOnLoad><devMode>False</devMode><screenWidth>960</screenWidth><screenHeight>720</screenHeight><fullscreen>False</fullscreen></PrefsData>',$utf)
}
if(!$Seed){
 if(!$SeedSave){throw 'Non-seed run requires the frozen seed save'}
 foreach($peer in @('Host','Client')){Copy-Item -LiteralPath $SeedSave -Destination "$run\$peer\Saves\TickMPSeed.rws"}
 [IO.File]::WriteAllText("$run\seed.sha256",(Get-FileHash -LiteralPath $SeedSave).Hash,$utf)
}else{[IO.File]::WriteAllText("$run\seed.sha256",'GENERATING_FIXED_SEED_261021',$utf)}
[IO.File]::WriteAllText("$run\core.sha256",(Get-FileHash "$game\Mods\NivarianCommander\1.6\Assemblies\NivarianCommander.dll").Hash,$utf)
[IO.File]::WriteAllText("$run\core.path","$game\Mods\NivarianCommander\1.6\Assemblies\NivarianCommander.dll",$utf)
[IO.File]::WriteAllText("$run\game.path",$game,$utf)
[IO.File]::WriteAllText("$run\core.mvid",[Reflection.Assembly]::Load([IO.File]::ReadAllBytes("$game\Mods\NivarianCommander\1.6\Assemblies\NivarianCommander.dll")).ManifestModule.ModuleVersionId.ToString(),$utf)
$inputs=@(Get-ChildItem -LiteralPath "$game\Mods" -Recurse -File | Get-FileHash)
if($inputs | Where-Object {$_.Path.Length -ge 250}){throw 'Isolated asset path exceeds safe Unity path length'}
$inputs+=Get-FileHash -LiteralPath "$run\Host\Config\ModsConfig.xml","$run\Client\Config\ModsConfig.xml"
if(!$Seed){$inputs+=Get-FileHash -LiteralPath "$run\Host\Saves\TickMPSeed.rws","$run\Client\Saves\TickMPSeed.rws"}
$inputs | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath "$run\inputs.json" -Encoding utf8
Add-Type @'
using System;using System.Runtime.InteropServices;public static class NmcMpWindows{
public delegate bool EnumProc(IntPtr h,IntPtr x);
[DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc f,IntPtr p);
[DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
[DllImport("user32.dll")]public static extern bool ShowWindowAsync(IntPtr h,int cmd);
[DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hh,uint f);
public static void Restore(uint pid){EnumWindows((h,x)=>{uint p;GetWindowThreadProcessId(h,out p);if(p==pid){SetWindowPos(h,new IntPtr(1),-12000,0,960,720,0x4010);ShowWindowAsync(h,8);}return true;},IntPtr.Zero);}}
'@
function ReadLog([string]$path){
 if(!(Test-Path -LiteralPath $path)){return ''}
 $stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 $reader=[IO.StreamReader]::new($stream);try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
}
$flags=" -nmc-tick-mp -nmc-port=$Port -nmc-ticks=$Ticks"
if($Trace){$flags+=' -nmc-trace'}
$owned=[Collections.Generic.List[object]]::new()
function StartPeer([string]$name,[string]$extra){
 $line='-savedatafolder="'+$run+'\'+$name+'" -logfile "'+$run+'\'+$name.ToLower()+'.log" -noaudio -screen-fullscreen 0 -screen-width 960 -screen-height 720'+$flags+' '+$extra
 $process=Start-Process -FilePath "$game\RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory $game -WindowStyle Hidden -PassThru
 $record=Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)"
 $owned.Add([pscustomobject]@{process=$process;record=$record})
 $record | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content "$run\$name-process.json" -Encoding utf8
 Write-Output "START $Label $name PID=$($process.Id)"
}
$watch=[Diagnostics.Stopwatch]::StartNew();$next=0;$joined=$false
try{
 if($Seed){StartPeer 'Host' '-quicktest -nmc-seed -username=TickHost'}else{StartPeer 'Host' '-username=TickHost'}
 while($watch.Elapsed.TotalSeconds -lt 1800){
  foreach($item in $owned){$item.process.Refresh();if($item.process.HasExited -and !($Seed -and (Test-Path "$run\seed.complete"))){throw 'Peer exited before completion'}}
  if((Get-CimInstance Win32_OperatingSystem).FreeVirtualMemory -lt 2097152){throw 'Commit headroom below 2 GiB'}
  foreach($p in @('host','client')){if(Test-Path "$run\$p.failed"){throw (ReadLog "$run\$p.failed")}}
  $h=ReadLog "$run\host.log";$c=ReadLog "$run\client.log"
  if(($h+$c) -match 'NMCMP FAILED|Desynced after|Sync Error|Inconsistent player|PacketReadException|WriterException|Map cmd exception|World cmd exception|JobDriver threw|started 10 jobs|Prepatcher Error: Fatal|Error in static constructor|Could not resolve reference to object|MakeThing error:|Exception ticking|Exception loading UnityEngine|NullReferenceException|ReflectionTypeLoadException|Client_FrameTime in state ServerJoining'){throw 'Runtime failure; inspect peer logs'}
  if(!$Seed -and !$joined -and $h -match '(?m)^Server started\.'){
   if($h -notmatch 'NMCMP REQUIRED_TARGETS resolved=9 nativeSupplySync=2 missing=0'){throw 'Required sync targets missing'}
   StartPeer 'Client' ('-nmc-client -username=TickClient -connect=127.0.0.1:'+$Port);$joined=$true
  }
  if(($Seed -and (Test-Path "$run\seed.complete")) -or (!$Seed -and (Test-Path "$run\host.complete") -and (Test-Path "$run\client.complete"))){
   if(!$Seed){
    foreach($name in @('ready','final','receipts','states')){if((ReadLog "$run\host.$name") -cne (ReadLog "$run\client.$name")){throw "Paired $name mismatch"}}
    foreach($log in @($h,$c)){if(([regex]::Matches($log,'NMCMP ASSERT phase=')).Count -ne 24){throw 'Missing paired assertions'};if($log -notmatch 'NMCMP BOOT' -or $log -notmatch 'NMCMP REQUIRED_TARGETS resolved=9 nativeSupplySync=2 missing=0'){throw 'Startup provenance missing'}}
    if(([regex]::Matches($c,'NMCMP DISPATCH phase=')).Count -ne 24){throw 'Missing client dispatches'}
   }
   foreach($input in $inputs){if((Get-FileHash -LiteralPath $input.Path).Hash -cne $input.Hash){throw ('Frozen input changed: '+$input.Path)}}
   [ordered]@{status='PASS';label=$Label;seed=[bool]$Seed;trace=[bool]$Trace;seconds=$watch.Elapsed.TotalSeconds;activeMods=$ids.Count;core=(ReadLog "$run\core.sha256");host=(ReadLog "$run\host.complete");client=(ReadLog "$run\client.complete");pairedAssertions=if($Seed){0}else{24}} | ConvertTo-Json | Set-Content "$run\result.json" -Encoding utf8
   Get-Content "$run\result.json";break
  }
  if($joined -and ($h+$c) -match 'Session lost|Disconnected from server'){throw 'Unexpected session loss'}
  if($watch.Elapsed.TotalSeconds -ge $next){$next=$watch.Elapsed.TotalSeconds+30;foreach($item in $owned){[NmcMpWindows]::Restore([uint32]$item.process.Id)};Write-Output ("PROGRESS seconds="+[int]$watch.Elapsed.TotalSeconds+" joined="+$joined+" "+(@([regex]::Matches($h,'NMCMP (?:PROGRESS|ASSERT)[^\r\n]+') | ForEach-Object Value) | Select-Object -Last 1))}
  Start-Sleep -Seconds 2
 }
 if(!(Test-Path "$run\result.json")){throw 'Bounded run timeout'}
}catch{[ordered]@{status='FAILED';reason=$_.Exception.Message;seconds=$watch.Elapsed.TotalSeconds} | ConvertTo-Json | Set-Content "$run\result.json" -Encoding utf8;throw}
finally{
 [IO.File]::WriteAllText("$run\quit",'controller cleanup',$utf)
 foreach($item in $owned){
  if(!$item.process.WaitForExit(10000)){
   $live=Get-CimInstance Win32_Process -Filter "ProcessId=$($item.process.Id)"
   if($live -and $live.ExecutablePath -ceq $item.record.ExecutablePath -and $live.CommandLine -ceq $item.record.CommandLine -and $live.CreationDate -eq $item.record.CreationDate){Stop-Process -Id $live.ProcessId -Force}else{throw 'Owned process identity changed'}
  }
 }
}
