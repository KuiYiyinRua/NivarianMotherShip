#requires -Version 7.0
param(
 [Parameter(Mandatory=$true)][string]$ValidationRoot,
 [Parameter(Mandatory=$true)][string]$Label
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ValidationRoot).Path
$game=Join-Path $root 'Game'
$run=Join-Path $root $Label
if(Test-Path -LiteralPath $run){throw 'Use a unique run label'}
New-Item -ItemType Directory -Path "$run/Single/Config" -Force | Out-Null
$utf=[Text.UTF8Encoding]::new($false)
$ids=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','erdelf.humanoidalienraces','keeptpa.nivarianrace','local.nivarian.mothership.commander','memegoddess.tdpack','local.nmc.tdoverlay17probe')
$version=([IO.File]::ReadAllText("$game/Version.txt")).Trim()
$dlcs=$ids | Where-Object {$_ -like 'ludeon.rimworld.*'}
[IO.File]::WriteAllText("$run/Single/Config/ModsConfig.xml",'<ModsConfigData><version>'+$version+'</version><activeMods>'+($ids|ForEach-Object {'<li>'+$_+'</li>'})+'</activeMods><knownExpansions>'+($dlcs|ForEach-Object {'<li>'+$_+'</li>'})+'</knownExpansions></ModsConfigData>',$utf)
[IO.File]::WriteAllText("$run/Single/Config/Prefs.xml",'<PrefsData><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode><screenWidth>1280</screenWidth><screenHeight>800</screenHeight><fullscreen>False</fullscreen></PrefsData>',$utf)
$inputs=@("$game/Assembly-CSharp.dll","$game/Waylith.dll","$run/Single/Config/ModsConfig.xml")
$inputs+=Get-ChildItem -LiteralPath "$game/Mods" -Recurse -File | Where-Object { $_.Extension -in '.dll','.rimunity','.xml','.glsl','.json' } | ForEach-Object FullName
$hashes=Get-FileHash -LiteralPath $inputs
$hashes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath "$run/inputs.json"
Add-Type @'
using System;using System.Runtime.InteropServices;public static class TDOverlay17Windows{
 public delegate bool EnumProc(IntPtr h,IntPtr x);
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc f,IntPtr p);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int cmd);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hh,uint f);
 public static void Background(uint pid){EnumWindows((h,x)=>{uint p;GetWindowThreadProcessId(h,out p);if(p==pid){ShowWindow(h,4);SetWindowPos(h,new IntPtr(1),-3000,0,1280,800,0x10);}return true;},IntPtr.Zero);}
}
'@
$logRoot=Join-Path $env:USERPROFILE 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios'
New-Item -ItemType Directory -Path "$run/BeforeLogs" -Force | Out-Null
foreach($relative in @('Player.log','Player-prev.log','Logs/Player.log','Logs/Player-prev.log')){
 $old=Join-Path $logRoot $relative
 if(Test-Path -LiteralPath $old){Copy-Item -LiteralPath $old -Destination (Join-Path "$run/BeforeLogs" ($relative.Replace('/','-'))) }
}
$line='-savedatafolder="'+$run+'/Single" -logfile "'+$run+'/game.log" -noaudio -screen-fullscreen 0 -screen-width 1280 -screen-height 800 -quicktest -nmc-td-overlay17'
$owned=Start-Process -FilePath "$game/RimWorld.exe" -ArgumentList $line -WorkingDirectory $game -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run/stdout.log" -RedirectStandardError "$run/stderr.log"
Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)" | Select-Object ProcessId,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content "$run/process.json"
Write-Output "START $Label PID=$($owned.Id)"
$watch=[Diagnostics.Stopwatch]::StartNew();$next=0
try{
 while(!$owned.HasExited){
  if($watch.Elapsed.TotalSeconds -gt 300){throw 'Bounded timeout'}
  if($watch.Elapsed.TotalSeconds -ge $next){$next=$watch.Elapsed.TotalSeconds+20;[TDOverlay17Windows]::Background([uint32]$owned.Id);Write-Output "PROGRESS $Label $([int]$watch.Elapsed.TotalSeconds)s"}
  Start-Sleep -Seconds 2;$owned.Refresh()
 }
 foreach($h in $hashes){if((Get-FileHash -LiteralPath $h.Path).Hash -cne $h.Hash){throw "Input changed during run: $($h.Path)"}}
 if(Test-Path "$run/failed"){throw ([IO.File]::ReadAllText("$run/failed"))}
 if(!(Test-Path "$run/complete")){throw 'Exited before completion'}
 $result=[IO.File]::ReadAllText("$run/complete")
 @{status='COMPLETED';assertions=$result;seconds=$watch.Elapsed.TotalSeconds;logReviewRequired=$true} | ConvertTo-Json | Set-Content "$run/result.json"
 Write-Output "COMPLETE $result (review raw logs separately)"
}catch{
 @{status='FAILED';reason=$_.Exception.Message;seconds=$watch.Elapsed.TotalSeconds} | ConvertTo-Json | Set-Content "$run/result.json";throw
}finally{
 if(!$owned.HasExited){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)";if($live.ExecutablePath -eq [IO.Path]::GetFullPath("$game/RimWorld.exe") -and $live.CommandLine.Contains($run)){Stop-Process -Id $owned.Id -Force;$owned.WaitForExit()}}
 $actualLog=Join-Path $logRoot 'Logs/Player.log'
 if((Test-Path -LiteralPath $actualLog) -and (Select-String -LiteralPath $actualLog -SimpleMatch "pid $($owned.Id)" -Quiet)){Copy-Item -LiteralPath $actualLog -Destination "$run/game.log"}
}
