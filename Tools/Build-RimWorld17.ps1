#requires -Version 7.0
param(
 [Parameter(Mandatory=$true)][string]$GameRoot,
 [Parameter(Mandatory=$true)][string]$Nivarian17Root,
 [Parameter(Mandatory=$true)][string]$HarmonyAssembly,
 [string]$OutputRoot,
 [string]$BuildRoot
)
$ErrorActionPreference='Stop'
$mod=(Resolve-Path "$PSScriptRoot/..").Path
$GameRoot=(Resolve-Path $GameRoot).Path
$Nivarian17Root=(Resolve-Path $Nivarian17Root).Path
$HarmonyAssembly=(Resolve-Path $HarmonyAssembly).Path
if(!$OutputRoot){$OutputRoot=$mod}
if(!$BuildRoot){$BuildRoot=Join-Path $mod 'BuildValidation/RimWorld17Build'}
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
$BuildRoot=[IO.Path]::GetFullPath($BuildRoot)
function Copy-CheckedFile([string]$Source,[string]$Destination){
 $Source=[IO.Path]::GetFullPath($Source);$Destination=[IO.Path]::GetFullPath($Destination)
 if($Source -eq $Destination){return}
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination)) | Out-Null
 [IO.File]::WriteAllBytes($Destination,[IO.File]::ReadAllBytes($Source))
 if((Get-FileHash -LiteralPath $Source).Hash -ne (Get-FileHash -LiteralPath $Destination).Hash){throw "Copy verification failed: $Destination"}
}
function Copy-CheckedTree([string]$Source,[string]$Destination){
 $Source=[IO.Path]::GetFullPath($Source)
 foreach($file in Get-ChildItem -LiteralPath $Source -File -Recurse){
  Copy-CheckedFile $file.FullName (Join-Path $Destination ([IO.Path]::GetRelativePath($Source,$file.FullName)))
 }
}
if(!(Test-Path "$GameRoot/RimWorld.exe") -or !(Test-Path "$GameRoot/UnityStubs.dll") -or
 !([IO.File]::ReadAllText("$GameRoot/Version.txt").StartsWith('1.7.'))){throw 'GameRoot must be a complete RimWorld 1.7 installation'}
$provider=Join-Path $Nivarian17Root '1.7/RU/Assemblies/RimUnity.Embedded.dll'
if(!(Test-Path -LiteralPath $provider)){throw 'The Nivarian 1.7 embedded runtime is missing'}
$references=Join-Path $BuildRoot 'Runtime'
dotnet run --project "$PSScriptRoot/RuntimeReferences/RuntimeReferences.csproj" -c Release -- $provider $references
if($LASTEXITCODE -ne 0){throw 'Runtime reference extraction failed'}
$runtime=Get-Content "$references/runtime.json" -Raw | ConvertFrom-Json
if([version]$runtime.version -lt [version]'0.1.16'){throw 'Nivarian must include RimUnity 0.1.16 or newer'}
dotnet build "$mod/Source/NivarianCommander.RimWorld17.csproj" -c Release "-p:GameRoot=$GameRoot" "-p:Nivarian17Root=$Nivarian17Root" "-p:HarmonyAssembly=$HarmonyAssembly" "-p:RimUnityReferences=$references/Assemblies" "-p:OutputPath=$BuildRoot/Bin/"
if($LASTEXITCODE -ne 0){throw 'RimWorld 1.7 build failed'}
$target=Join-Path $OutputRoot '1.7'
if(Test-Path "$target/Assemblies/NivarianCommander.dll"){throw 'Remove the old ordinary 1.7 DLL before deploying the deferred assembly'}
New-Item -ItemType Directory -Path "$target/Assemblies","$target/ThirdParty/RimUnity" -Force | Out-Null
foreach($name in @('Defs','Languages','Sounds','Textures')){
 if(!(Test-Path "$target/$name")){New-Item -ItemType Directory -Path "$target/$name" | Out-Null}
 Copy-CheckedTree "$mod/1.6/$name" "$target/$name"
}
if($OutputRoot -ne $mod){
 Copy-CheckedFile "$mod/1.7/Defs/CommanderShaders.xml" "$target/Defs/CommanderShaders.xml"
 if(!(Test-Path "$target/Shaders")){New-Item -ItemType Directory -Path "$target/Shaders" | Out-Null}
 Copy-CheckedTree "$mod/1.7/Shaders" "$target/Shaders"
}
$utf=[Text.UTF8Encoding]::new($false)
$xml="$target/Defs/CommanderStructures.xml"
[IO.File]::WriteAllText($xml,[IO.File]::ReadAllText($xml).Replace('Nivarian/Building/cryo_forge','Nivarian/Building/CryoForge/cryo_forge'),$utf)
Copy-CheckedFile "$BuildRoot/Bin/NivarianCommander.dll" "$target/Assemblies/NivarianCommander.rimunity"
Copy-CheckedFile $provider "$target/Assemblies/RimUnity.Embedded.dll"
[IO.File]::WriteAllText("$target/Assemblies/NivarianCommander.rimunity.json",'{ "protocol": 1, "minimumVersion": "0.1.16" }',$utf)
Copy-CheckedFile "$references/LICENSE" "$target/ThirdParty/RimUnity/LICENSE"
Copy-CheckedTree "$references/ThirdParty" "$target/ThirdParty/RimUnity/ThirdParty"
Get-FileHash -LiteralPath "$target/Assemblies/NivarianCommander.rimunity","$target/Assemblies/RimUnity.Embedded.dll"
