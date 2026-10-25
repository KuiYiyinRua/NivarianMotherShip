#requires -Version 7.0
param(
 [Parameter(Mandatory=$true)][string]$ValidationRoot,
 [Parameter(Mandatory=$true)][string]$Label,
 [string]$VulkanDriverManifest,
 [switch]$StandaloneRuntime
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ValidationRoot).Path
$game=Join-Path $root 'Game'
$archive=Join-Path $root $Label
$run=Join-Path $env:TEMP ('NMC-DonationCompat17-'+$Label)
if((Test-Path -LiteralPath $archive) -or (Test-Path -LiteralPath $run)){throw 'Use a unique run label'}
New-Item -ItemType Directory -Path "$run/Single/Config","$game/Mods/Probe/About","$game/Mods/Probe/Assemblies" -Force | Out-Null
$utf=[Text.UTF8Encoding]::new($false)
$harmony="$game/Mods/Harmony/1.7/Assemblies/0Harmony.dll"
dotnet build "$PSScriptRoot/Probe.csproj" -c Release "-p:GameRoot=$game" "-p:HarmonyAssembly=$harmony"
if($LASTEXITCODE -ne 0){throw 'Probe build failed'}
Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/net10.0/NMC.DonationCompat17.dll" -Destination "$game/Mods/Probe/Assemblies" -Force
[IO.File]::WriteAllText("$game/Mods/Probe/About/About.xml",'<ModMetaData><name>Point shop isolated validation</name><packageId>local.nmc.donationcompat17probe</packageId><author>Validation</author><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>',$utf)
$ids=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','erdelf.humanoidalienraces','keeptpa.nivarianrace','local.nivarian.mothership.commander','local.nmc.donationcompat17probe')
if($StandaloneRuntime){
 $runtime=[xml][IO.File]::ReadAllText("$game/Mods/RimUnity/About/About.xml")
 $runtimeId=[string]$runtime.ModMetaData.packageId
 if(!$runtimeId){throw 'Missing standalone runtime package ID'}
 $ids=@($ids[0..7])+$runtimeId+@($ids[8..10])
}
$version=([IO.File]::ReadAllText("$game/Version.txt")).Trim()
$dlcs=$ids | Where-Object {$_ -like 'ludeon.rimworld.*'}
[IO.File]::WriteAllText("$run/Single/Config/ModsConfig.xml",'<ModsConfigData><version>'+$version+'</version><activeMods>'+($ids|ForEach-Object {'<li>'+$_+'</li>'})+'</activeMods><knownExpansions>'+($dlcs|ForEach-Object {'<li>'+$_+'</li>'})+'</knownExpansions></ModsConfigData>',$utf)
[IO.File]::WriteAllText("$run/Single/Config/Prefs.xml",'<PrefsData><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode><screenWidth>1280</screenWidth><screenHeight>800</screenHeight><fullscreen>False</fullscreen></PrefsData>',$utf)
$inputs=@("$game/Assembly-CSharp.dll","$game/Waylith.dll","$run/Single/Config/ModsConfig.xml")
$inputs+=Get-ChildItem -LiteralPath "$game/Mods" -Recurse -File | ForEach-Object FullName
$hashes=Get-FileHash -LiteralPath $inputs
$hashes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath "$run/inputs.json"
$line='-savedatafolder="'+$run+'/Single" -logfile "'+$run+'/game.log" -noaudio -screen-fullscreen 0 -screen-width 1280 -screen-height 800 -quicktest -nmc17-donation-compat'
Add-Type @'
using System;using System.Runtime.InteropServices;public static class Shop17Windows{
 public delegate bool EnumProc(IntPtr h,IntPtr x);
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc f,IntPtr p);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int cmd);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hh,uint f);
 public static void Background(uint pid){EnumWindows((h,x)=>{uint p;GetWindowThreadProcessId(h,out p);if(p==pid){ShowWindow(h,4);SetWindowPos(h,new IntPtr(1),-3000,0,1280,800,0x10);}return true;},IntPtr.Zero);}
}
'@
$startArgs=@{}
if($VulkanDriverManifest){$driver=(Resolve-Path -LiteralPath $VulkanDriverManifest).Path;$startArgs.Environment=@{VK_DRIVER_FILES=$driver};@{VK_DRIVER_FILES=$driver}|ConvertTo-Json|Set-Content "$run/environment.json"}
$logRoot=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios'
New-Item -ItemType Directory -Path "$run/BeforeLogs" -Force | Out-Null
foreach($relative in @('Player.log','Player-prev.log','Logs/Player.log','Logs/Player-prev.log')){
 $old=Join-Path $logRoot $relative
 if(Test-Path -LiteralPath $old){Copy-Item -LiteralPath $old -Destination (Join-Path "$run/BeforeLogs" ($relative.Replace('/','-'))) }
}
$owned=Start-Process -FilePath "$game/RimWorld.exe" -ArgumentList $line -WorkingDirectory $game -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run/stdout.log" -RedirectStandardError "$run/stderr.log" @startArgs
Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)" | Select-Object ProcessId,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content "$run/process.json"
Write-Output "START $Label PID=$($owned.Id)"
$watch=[Diagnostics.Stopwatch]::StartNew();$next=0
try{
 while(!$owned.HasExited){
  if($watch.Elapsed.TotalSeconds -gt 300){throw 'Bounded timeout'}
  if($watch.Elapsed.TotalSeconds -ge $next){$next=$watch.Elapsed.TotalSeconds+20;[Shop17Windows]::Background([uint32]$owned.Id);Write-Output "PROGRESS $Label $([int]$watch.Elapsed.TotalSeconds)s"}
  Start-Sleep -Seconds 2;$owned.Refresh()
 }
 foreach($h in $hashes){if((Get-FileHash -LiteralPath $h.Path).Hash -cne $h.Hash){throw 'Input changed during run'}}
 if(Test-Path "$run/failed"){throw ([IO.File]::ReadAllText("$run/failed"))}
 if(!(Test-Path "$run/complete")){throw 'Exited before completion'}
 @{status='PROBE_PASS';seconds=$watch.Elapsed.TotalSeconds;logReviewRequired=$true} | ConvertTo-Json | Set-Content "$run/result.json"
 Write-Output 'COMPLETE PROBE_PASS (review raw logs separately)'
}catch{
 @{status='FAILED';reason=$_.Exception.Message;seconds=$watch.Elapsed.TotalSeconds} | ConvertTo-Json | Set-Content "$run/result.json";throw
}finally{
 if(!$owned.HasExited){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)";if($live.ExecutablePath -eq [IO.Path]::GetFullPath("$game/RimWorld.exe") -and $live.CommandLine.Contains($run)){Stop-Process -Id $owned.Id -Force;$owned.WaitForExit()}}
 # Waylith 1.7.4985 ignores -logfile; preserve the actual log only if its PID matches.
 $actualLog=Join-Path $logRoot 'Logs/Player.log'
 if((Test-Path -LiteralPath $actualLog) -and (Select-String -LiteralPath $actualLog -SimpleMatch "pid $($owned.Id)" -Quiet)) {Copy-Item -LiteralPath $actualLog -Destination "$run/game.log"}
 Copy-Item -LiteralPath $run -Destination $archive -Recurse
}
