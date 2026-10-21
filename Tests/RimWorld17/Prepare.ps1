#requires -Version 7.0
param(
 [Parameter(Mandatory=$true)][string]$GameRoot,
 [Parameter(Mandatory=$true)][string]$Nivarian17Root,
 [Parameter(Mandatory=$true)][string]$HarmonyRoot,
 [Parameter(Mandatory=$true)][string]$AlienRacesRoot,
 [string]$ValidationRoot
)
$ErrorActionPreference='Stop'
$project=(Resolve-Path "$PSScriptRoot/../..").Path
if(!$ValidationRoot){$ValidationRoot=Join-Path $project 'BuildValidation/RimWorld17'}
$ValidationRoot=[IO.Path]::GetFullPath($ValidationRoot)
$GameRoot=(Resolve-Path -LiteralPath $GameRoot).Path
if(!([IO.File]::ReadAllText("$GameRoot/Version.txt").StartsWith('1.7.'))){throw 'A complete RimWorld 1.7 installation is required'}
$game=Join-Path $ValidationRoot 'Game'
if(Test-Path -LiteralPath $game){throw 'Use a fresh ValidationRoot; existing game/evidence is never overwritten'}
function Copy-File([string]$From,[string]$To){
 $From=[IO.Path]::GetFullPath($From);$To=[IO.Path]::GetFullPath($To)
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($To)) | Out-Null
 $inputFile=[IO.File]::OpenRead($From)
 try{$outputFile=[IO.File]::Create($To);try{$inputFile.CopyTo($outputFile)}finally{$outputFile.Dispose()}}finally{$inputFile.Dispose()}
 if((Get-FileHash -LiteralPath $From).Hash -ne (Get-FileHash -LiteralPath $To).Hash){throw "Bad copy: $To"}
}
function Copy-Tree([string]$From,[string]$To){
 $From=(Resolve-Path -LiteralPath $From).Path
 foreach($file in Get-ChildItem -LiteralPath $From -Recurse -File){Copy-File $file.FullName (Join-Path $To ([IO.Path]::GetRelativePath($From,$file.FullName)))}
}
foreach($file in Get-ChildItem -LiteralPath $GameRoot -File){Copy-File $file.FullName (Join-Path $game $file.Name)}
foreach($folder in @('Data','Rendering','Assets','vulkan')){Copy-Tree "$GameRoot/$folder" "$game/$folder"}
Copy-Tree $HarmonyRoot "$game/Mods/Harmony"
Copy-Tree $AlienRacesRoot "$game/Mods/AlienRaces"
Copy-Tree $Nivarian17Root "$game/Mods/NivarianRace"
$harmony=Join-Path $HarmonyRoot '1.7/Assemblies/0Harmony.dll'
& "$project/Tools/Build-RimWorld17.ps1" -GameRoot $GameRoot -Nivarian17Root $Nivarian17Root -HarmonyAssembly $harmony -BuildRoot "$ValidationRoot/Build" -OutputRoot "$ValidationRoot/Candidate"
foreach($folder in @('About','Royalty','NoRoyalty')){Copy-Tree "$project/$folder" "$ValidationRoot/Candidate/$folder"}
Copy-File "$project/LoadFolders.xml" "$ValidationRoot/Candidate/LoadFolders.xml"
Copy-Tree "$ValidationRoot/Candidate" "$game/Mods/NivarianMotherShip"
dotnet build "$PSScriptRoot/SmokeProbe.csproj" -c Release "-p:GameRoot=$GameRoot" "-p:HarmonyAssembly=$harmony"
if($LASTEXITCODE -ne 0){throw 'Smoke probe build failed'}
dotnet build "$PSScriptRoot/Features.csproj" -c Release "-p:GameRoot=$GameRoot" "-p:HarmonyAssembly=$harmony" "-p:Nivarian17Root=$Nivarian17Root" "-p:CommanderAssembly=$ValidationRoot/Build/Bin/NivarianCommander.dll"
if($LASTEXITCODE -ne 0){throw 'Feature probe build failed'}
Write-Output "Prepared isolated game: $game"
