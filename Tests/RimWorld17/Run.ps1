#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$Label, [switch]$Baseline, [switch]$NoRoyalty, [ValidateSet('Smoke','Projection','Construction','Abilities')][string]$Probe = 'Smoke', [string]$VulkanDriverManifest, [string]$ValidationRoot)
$ErrorActionPreference='Stop'
$project=(Resolve-Path "$PSScriptRoot/../..").Path
$root=if($ValidationRoot){[IO.Path]::GetFullPath($ValidationRoot)}else{Join-Path $project 'BuildValidation/RimWorld17'}
$game=Join-Path $root 'Game'
$archive=Join-Path $root $Label
$run=Join-Path $env:TEMP ('NMC17-'+$Label)
if(Test-Path -LiteralPath $archive){throw 'Evidence directory already exists'}
if(Test-Path -LiteralPath $run){throw 'Run directory already exists'}
New-Item -ItemType Directory -Path "$run/Single/Config","$game/Mods/Probe/About","$game/Mods/Probe/Smoke/Assemblies","$game/Mods/Probe/Features/Assemblies" -Force | Out-Null
$utf=[Text.UTF8Encoding]::new($false)
$probeFolder=if($Probe -eq 'Smoke'){'Smoke'}else{'Features'}
[IO.File]::WriteAllText("$game/Mods/Probe/LoadFolders.xml",'<loadFolders><v1.7><li>'+$probeFolder+'</li></v1.7></loadFolders>',$utf)
if($Probe -eq 'Smoke'){
 $probeFile="$game/Mods/Probe/Smoke/Assemblies/NMC.RimWorld17Smoke.dll"
 Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/net10.0/NMC.RimWorld17Smoke.dll" -Destination $probeFile -Force
}else{
 $probeFile="$game/Mods/Probe/Features/Assemblies/NMC.RimWorld17Features.rimunity"
 Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/net10.0/NMC.RimWorld17Features.dll" -Destination $probeFile -Force
 Copy-Item -LiteralPath "$game/Mods/NivarianMotherShip/1.7/Assemblies/RimUnity.Embedded.dll" -Destination "$game/Mods/Probe/Features/Assemblies" -Force
 [IO.File]::WriteAllText($probeFile+'.json','{ "protocol": 1, "minimumVersion": "0.1.16" }',$utf)
}
[IO.File]::WriteAllText("$game/Mods/Probe/About/About.xml",'<ModMetaData><name>NMC 1.7 isolated validation</name><packageId>local.nmc.rimworld17probe</packageId><author>Validation</author><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>',$utf)
$ids=@('brrainz.harmony','ludeon.rimworld')
$dlcs=@('royalty','ideology','biotech','anomaly','odyssey')
foreach($dlc in $dlcs){if(!$NoRoyalty -or $dlc -ne 'royalty'){$ids+=('ludeon.rimworld.'+$dlc)}}
$ids+=@('erdelf.humanoidalienraces','keeptpa.nivarianrace')
if(!$Baseline){$ids+='local.nivarian.mothership.commander'}
$ids+='local.nmc.rimworld17probe'
$version=([IO.File]::ReadAllText("$game/Version.txt")).Trim()
$config='<ModsConfigData><version>'+$version+'</version><activeMods>'+($ids|ForEach-Object {'<li>'+$_+'</li>'})+'</activeMods><knownExpansions>'+($dlcs|ForEach-Object {'<li>ludeon.rimworld.'+$_+'</li>'})+'</knownExpansions></ModsConfigData>'
[IO.File]::WriteAllText("$run/Single/Config/ModsConfig.xml",$config,$utf)
[IO.File]::WriteAllText("$run/Single/Config/Prefs.xml",'<PrefsData><volumeMaster>0</volumeMaster><langFolderName>ChineseSimplified (简体中文)</langFolderName><runInBackground>True</runInBackground><pauseOnLoad>False</pauseOnLoad><devMode>False</devMode><screenWidth>1280</screenWidth><screenHeight>800</screenHeight><fullscreen>False</fullscreen></PrefsData>',$utf)
$inputs=@("$game/Assembly-CSharp.dll","$game/Mods/NivarianMotherShip/1.7/Assemblies/NivarianCommander.rimunity","$game/Mods/NivarianRace/1.7/Core/Assemblies/Nivarian_Race.rimunity","$game/Mods/NivarianRace/1.7/LumiParticle/Assemblies/LumiParticle.rimunity",$probeFile)
$inputs+=Get-ChildItem -LiteralPath "$game/Mods/NivarianMotherShip/1.7" -Recurse -File | ForEach-Object FullName
$inputs+=@("$game/Mods/NivarianMotherShip/About/About.xml","$game/Mods/NivarianMotherShip/LoadFolders.xml")
$inputs=$inputs | Sort-Object -Unique
$hashes=Get-FileHash -LiteralPath $inputs
$hashes | ConvertTo-Json | Set-Content "$run/inputs.json" -Encoding utf8
$flag=switch($Probe){'Smoke'{'-nmc17-smoke'}'Projection'{'-nmc-projection-probe'}'Construction'{'-nmc-native-construction-probe'}'Abilities'{'-nmc17-abilities'}}
$line='-savedatafolder="'+$run+'/Single" -logfile "'+$run+'/game.log" -noaudio -screen-fullscreen 0 -screen-width 1280 -screen-height 800 -quicktest '+$flag
if($Baseline){$line+=' -nmc17-baseline'}
Add-Type @'
using System;using System.Runtime.InteropServices;public static class Nmc17Windows{
 public delegate bool EnumProc(IntPtr h,IntPtr x);
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc f,IntPtr p);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int cmd);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hh,uint f);
 public static void Background(uint pid){EnumWindows((h,x)=>{uint p;GetWindowThreadProcessId(h,out p);if(p==pid){ShowWindow(h,4);SetWindowPos(h,new IntPtr(1),-3000,0,1280,800,0x10);}return true;},IntPtr.Zero);}
}
'@
$startArgs=@{}
if($VulkanDriverManifest){
 $driver=(Resolve-Path -LiteralPath $VulkanDriverManifest).Path
 $startArgs.Environment=@{VK_DRIVER_FILES=$driver}
 @{VK_DRIVER_FILES=$driver} | ConvertTo-Json | Set-Content "$run/environment.json" -Encoding utf8
}
$owned=Start-Process -FilePath "$game/RimWorld.exe" -ArgumentList $line -WorkingDirectory $game -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run/stdout.log" -RedirectStandardError "$run/stderr.log" @startArgs
Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)" | Select-Object ProcessId,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content "$run/process.json" -Encoding utf8
Write-Output "START $Label PID=$($owned.Id)"
$watch=[Diagnostics.Stopwatch]::StartNew();$next=0
try{
 while(!$owned.HasExited){
  if($watch.Elapsed.TotalSeconds -gt 300){throw 'Bounded timeout'}
  if($watch.Elapsed.TotalSeconds -ge $next){$next=$watch.Elapsed.TotalSeconds+20;[Nmc17Windows]::Background([uint32]$owned.Id);Write-Output "PROGRESS $Label $([int]$watch.Elapsed.TotalSeconds)s"}
  Start-Sleep -Seconds 2;$owned.Refresh()
 }
 foreach($h in $hashes){if((Get-FileHash -LiteralPath $h.Path).Hash -cne $h.Hash){throw 'Input changed during run'}}
 if(Test-Path "$run/failed"){throw ([IO.File]::ReadAllText("$run/failed"))}
 if(!(Test-Path "$run/complete")){throw 'Exited before completion'}
 @{status='PROBE_PASS';seconds=$watch.Elapsed.TotalSeconds;baseline=[bool]$Baseline;logReviewRequired=$true} | ConvertTo-Json | Set-Content "$run/result.json" -Encoding utf8
 Write-Output 'COMPLETE PROBE_PASS (review raw logs separately)'
}catch{
 @{status='FAILED';reason=$_.Exception.Message;seconds=$watch.Elapsed.TotalSeconds} | ConvertTo-Json | Set-Content "$run/result.json" -Encoding utf8;throw
}finally{
 if(!$owned.HasExited){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)";if($live.ExecutablePath -eq [IO.Path]::GetFullPath("$game/RimWorld.exe") -and $live.CommandLine.Contains($run)){Stop-Process -Id $owned.Id -Force;$owned.WaitForExit()}}
 Copy-Item -LiteralPath $run -Destination $archive -Recurse
}
