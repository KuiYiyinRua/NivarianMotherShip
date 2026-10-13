param([Parameter(Mandatory=$true)][string]$Label,[switch]$Baseline,[switch]$NoOptimizers)
$ErrorActionPreference='Stop'
$project=(Resolve-Path "$PSScriptRoot\..\..").Path
$root=Join-Path $project 'BuildValidation\OptimizerBridge-20261013'
$run=Join-Path $root $Label
if(Test-Path -LiteralPath $run){throw 'Run directory already exists'}
$gameRoot=(Resolve-Path "$project\..\..").Path
$game=Join-Path $env:TEMP ('NMCOptimizer-'+[guid]::NewGuid().ToString('N')+'\Game')
New-Item -ItemType Directory -Path "$game\Mods\Probe\About","$game\Mods\Probe\Assemblies","$run\Single\Config" -Force | Out-Null
foreach($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','Version.txt','steam_appid.txt')){if(Test-Path "$gameRoot\$name"){Copy-Item -LiteralPath "$gameRoot\$name" -Destination $game}}
foreach($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$game\$name" -Target "$gameRoot\$name" | Out-Null}
$mods=@('2934420800','2009463077','839005762','3624805128','3735573834','2988801276','3256974620')
if(!$NoOptimizers){$mods+=@('2664723367','3718308218')}
foreach($name in $mods){Copy-Item -LiteralPath "$gameRoot\Mods\$name" -Destination "$game\Mods\$name" -Recurse}
New-Item -ItemType Directory -Path "$game\Mods\NivarianCommander" | Out-Null
foreach($name in @('About','1.6','Royalty','NoRoyalty','Multiplayer','LoadFolders.xml')){Copy-Item -LiteralPath "$project\$name" -Destination "$game\Mods\NivarianCommander" -Recurse}
$build=if($Baseline){'BaselineBuild'}else{'CandidateBuild'}
Copy-Item -LiteralPath "$root\$build\NivarianCommander.dll" -Destination "$game\Mods\NivarianCommander\1.6\Assemblies\NivarianCommander.dll" -Force
Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\net48\NMC.OptimizerProbe.dll" -Destination "$game\Mods\Probe\Assemblies"
$utf=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText("$game\Mods\Probe\About\About.xml",'<ModMetaData><name>Optimizer bridge regression</name><packageId>local.nmc.optimizerprobe</packageId><author>Validation</author><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>',$utf)
$cfg=[IO.File]::ReadAllText("$project\BuildValidation\WorkshopRelease-20261013\Settings-SP-R1\Single\Config\ModsConfig.xml").Replace('local.nmc.workshopprobe','local.nmc.optimizerprobe').Replace('<li>brrainz.harmony</li>','<li>zetrith.prepatcher</li><li>brrainz.harmony</li>')
if(!$NoOptimizers){$cfg=$cfg.Replace('<li>local.nmc.optimizerprobe</li>','<li>taranchuk.performanceoptimizer</li><li>sz.yaopt</li><li>local.nmc.optimizerprobe</li>')}
[IO.File]::WriteAllText("$run\Single\Config\ModsConfig.xml",$cfg,$utf)
[IO.File]::WriteAllText("$run\Single\Config\Prefs.xml",'<PrefsData><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><runInBackground>True</runInBackground><pauseOnLoad>False</pauseOnLoad><devMode>False</devMode><screenWidth>960</screenWidth><screenHeight>720</screenHeight><fullscreen>False</fullscreen></PrefsData>',$utf)
$inputs=@("$game\Mods\NivarianCommander\1.6\Assemblies\NivarianCommander.dll","$game\Mods\Probe\Assemblies\NMC.OptimizerProbe.dll","$run\Single\Config\ModsConfig.xml")
if(!$NoOptimizers){$inputs+=@("$game\Mods\3718308218\1.6\Assemblies\YaOpt.dll","$game\Mods\3718308218\1.6\Mods\Performance Optimizer\Assemblies\YaOpt.OtherMod.PerformanceOptimizer.dll","$game\Mods\2664723367\1.6\Assemblies\PerformanceOptimizer.dll")}
$hashes=$inputs | Get-FileHash
$hashes | ConvertTo-Json | Set-Content "$run\inputs.json" -Encoding utf8
$extra=if($NoOptimizers){' -nmc-no-optimizers'}else{''}
$line='-savedatafolder="'+$run+'\Single" -logfile "'+$run+'\game.log" -noaudio -screen-fullscreen 0 -screen-width 960 -screen-height 720 -quicktest -nmc-optimizer-probe'+$extra
$owned=Start-Process -FilePath "$game\RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory $game -WindowStyle Hidden -PassThru
Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)" | Select-Object ProcessId,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content "$run\process.json" -Encoding utf8
Write-Output "START $Label PID=$($owned.Id)"
$watch=[Diagnostics.Stopwatch]::StartNew();$next=0
try{
 while(!$owned.HasExited){
  if($watch.Elapsed.TotalSeconds -gt 900){throw 'Bounded timeout'}
  if($watch.Elapsed.TotalSeconds -ge $next){$next=$watch.Elapsed.TotalSeconds+30;Write-Output "PROGRESS $Label $([int]$watch.Elapsed.TotalSeconds)s"}
  Start-Sleep -Seconds 2;$owned.Refresh()
 }
 foreach($h in $hashes){if((Get-FileHash -LiteralPath $h.Path).Hash -cne $h.Hash){throw 'Input changed'}}
 if(Test-Path "$run\failed"){throw ([IO.File]::ReadAllText("$run\failed"))}
 if(!(Test-Path "$run\complete")){throw 'Exited before completion'}
 $log=[IO.File]::ReadAllText("$run\game.log")
 if($log -match 'non-concurrent collections|Exception drawing|Optimizer component-cache bridge installation failed|Error in static constructor|ReflectionTypeLoadException|Prepatcher Error: Fatal'){throw 'Runtime errors in log'}
 @{status='PASS';game=$game;seconds=$watch.Elapsed.TotalSeconds;baseline=[bool]$Baseline;noOptimizers=[bool]$NoOptimizers} | ConvertTo-Json | Set-Content "$run\result.json" -Encoding utf8
 Write-Output 'COMPLETE PASS'
}catch{@{status='FAILED';reason=$_.Exception.Message;game=$game} | ConvertTo-Json | Set-Content "$run\result.json" -Encoding utf8;throw}
finally{if(!$owned.HasExited){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)";if($live.ExecutablePath -eq "$game\RimWorldWin64.exe" -and $live.CommandLine.Contains($run)){Stop-Process -Id $owned.Id -Force}}}
