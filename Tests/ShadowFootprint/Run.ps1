param([Parameter(Mandatory=$true)][string]$Label,[switch]$Baseline,[string]$ValidationFolder='ShadowFootprint-20261019')
$ErrorActionPreference='Stop'
$project=(Resolve-Path "$PSScriptRoot\..\..").Path
$root=Join-Path $project ('BuildValidation\'+$ValidationFolder)
$run=Join-Path $root $Label
if(Test-Path -LiteralPath $run){throw 'Run directory already exists'}
$gameRoot=(Resolve-Path "$project\..\..").Path
$game=Join-Path $env:TEMP ('NMCShadowFootprint-'+[guid]::NewGuid().ToString('N')+'\Game')
New-Item -ItemType Directory -Path "$game\Mods\Probe\About","$game\Mods\Probe\Assemblies","$run\Single\Config" -Force | Out-Null
foreach($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','Version.txt','steam_appid.txt')){if(Test-Path "$gameRoot\$name"){Copy-Item -LiteralPath "$gameRoot\$name" -Destination $game}}
foreach($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$game\$name" -Target "$gameRoot\$name" | Out-Null}
$mods=@('2009463077','839005762','3624805128','3735573834','2988801276','3256974620')

foreach($name in $mods){Copy-Item -LiteralPath "$gameRoot\Mods\$name" -Destination "$game\Mods\$name" -Recurse}
New-Item -ItemType Directory -Path "$game\Mods\NivarianCommander" | Out-Null
foreach($name in @('About','1.6','Royalty','NoRoyalty','Multiplayer','LoadFolders.xml')){Copy-Item -LiteralPath "$project\$name" -Destination "$game\Mods\NivarianCommander" -Recurse}
$build=if($Baseline){'BaselineBuild'}else{'CandidateBuild'}
Copy-Item -LiteralPath "$root\$build\NivarianCommander.dll" -Destination "$game\Mods\NivarianCommander\1.6\Assemblies\NivarianCommander.dll" -Force
Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\net48\NMC.ShadowFootprintProbe.dll" -Destination "$game\Mods\Probe\Assemblies"
$utf=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText("$game\Mods\Probe\About\About.xml",'<ModMetaData><name>Shadow footprint regression</name><packageId>local.nmc.shadowfootprintprobe</packageId><author>Validation</author><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>',$utf)
$ids=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','erdelf.humanoidalienraces','ancot.ancotlibrary','ancot.milirarace','keeptpa.nivarianrace','keeptpa.nivariandraconicmilitary','local.nivarian.mothership.commander','local.nmc.shadowfootprintprobe')
$expansions=@('ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey')
$cfg='<ModsConfigData><version>1.6.4850 rev645</version><activeMods>'+($ids|ForEach-Object {'<li>'+$_+'</li>'})+'</activeMods><knownExpansions>'+($expansions|ForEach-Object {'<li>'+$_+'</li>'})+'</knownExpansions></ModsConfigData>'
[IO.File]::WriteAllText("$run\Single\Config\ModsConfig.xml",$cfg,$utf)
[IO.File]::WriteAllText("$run\Single\Config\Prefs.xml",'<PrefsData><volumeMaster>0</volumeMaster><langFolderName>ChineseSimplified</langFolderName><runInBackground>True</runInBackground><pauseOnLoad>False</pauseOnLoad><devMode>False</devMode><screenWidth>960</screenWidth><screenHeight>720</screenHeight><fullscreen>False</fullscreen></PrefsData>',$utf)
$inputs=@("$game\Mods\NivarianCommander\1.6\Assemblies\NivarianCommander.dll","$game\Mods\Probe\Assemblies\NMC.ShadowFootprintProbe.dll","$run\Single\Config\ModsConfig.xml")
$hashes=Get-FileHash -LiteralPath $inputs
$hashes | ConvertTo-Json | Set-Content "$run\inputs.json" -Encoding utf8
$line='-savedatafolder="'+$run+'\Single" -logfile "'+$run+'\game.log" -noaudio -screen-fullscreen 0 -screen-width 960 -screen-height 720 -quicktest -nmc-shadow-footprint-probe'
Add-Type @'
using System;using System.Runtime.InteropServices;public static class NmcEntryWindows{
 public delegate bool EnumProc(IntPtr h,IntPtr x);
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc f,IntPtr p);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int cmd);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hh,uint f);
 public static void Restore(uint pid){EnumWindows((h,x)=>{uint p;GetWindowThreadProcessId(h,out p);if(p==pid){SetWindowPos(h,new IntPtr(1),-3000,0,960,640,0x10);ShowWindow(h,8);}return true;},IntPtr.Zero);}
}
'@
$owned=Start-Process -FilePath "$game\RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory $game -WindowStyle Hidden -PassThru
Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)" | Select-Object ProcessId,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content "$run\process.json" -Encoding utf8
Write-Output "START $Label PID=$($owned.Id)"
$watch=[Diagnostics.Stopwatch]::StartNew();$next=0
try{
 while(!$owned.HasExited){
  if($watch.Elapsed.TotalSeconds -gt 600){throw 'Bounded timeout'}
  if($watch.Elapsed.TotalSeconds -ge $next){$next=$watch.Elapsed.TotalSeconds+30;[NmcEntryWindows]::Restore([uint32]$owned.Id);Write-Output "PROGRESS $Label $([int]$watch.Elapsed.TotalSeconds)s"}
  Start-Sleep -Seconds 2;$owned.Refresh()
 }
 foreach($h in $hashes){if((Get-FileHash -LiteralPath $h.Path).Hash -cne $h.Hash){throw 'Input changed'}}
 if(Test-Path "$run\failed"){throw ([IO.File]::ReadAllText("$run\failed"))}
 if(!(Test-Path "$run\complete")){throw 'Exited before completion'}
 $log=[IO.File]::ReadAllText("$run\game.log")
 if($log -match 'Exception ticking|Exception drawing|Exception in GameComponent|Error in static constructor|ReflectionTypeLoadException|NullReferenceException'){throw 'Runtime errors in log'}
 @{status='PASS';game=$game;seconds=$watch.Elapsed.TotalSeconds;baseline=[bool]$Baseline} | ConvertTo-Json | Set-Content "$run\result.json" -Encoding utf8
 Write-Output 'COMPLETE PASS'
}catch{@{status='FAILED';reason=$_.Exception.Message;game=$game} | ConvertTo-Json | Set-Content "$run\result.json" -Encoding utf8;throw}
finally{if(!$owned.HasExited){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)";if($live.ExecutablePath -eq "$game\RimWorldWin64.exe" -and $live.CommandLine.Contains($run)){Stop-Process -Id $owned.Id -Force}}}
