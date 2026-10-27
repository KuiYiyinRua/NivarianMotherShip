#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$mod=(Resolve-Path -LiteralPath "$PSScriptRoot/../..").Path
$core=(Resolve-Path -LiteralPath "$GameRoot/Data/Core/Shaders").Path
$shaderRoot=Join-Path $mod '1.7/Shaders'
$coreNames=@{}
Get-ChildItem -LiteralPath $core -Recurse -File -Filter '*.glsl' | ForEach-Object {$coreNames[$_.Name]=$true}
$sources=Get-ChildItem -LiteralPath $shaderRoot -Recurse -File -Filter '*.glsl'
foreach($file in $sources){
 if($coreNames.ContainsKey($file.Name)){throw "Mod shader shadows Core by basename: $($file.FullName)"}
}
$duplicates=@($sources | Group-Object Name | Where-Object Count -gt 1)
if($duplicates.Count){throw 'Mod shader basenames must be unique even across subfolders'}
$defs=[xml][IO.File]::ReadAllText((Join-Path $mod '1.7/Defs/CommanderShaders.xml'))
foreach($node in $defs.SelectNodes('//vertexShaderFileName | //fragmentShaderFileName')){
 if(!(Test-Path -LiteralPath (Join-Path $shaderRoot $node.InnerText))){throw "Missing shader source: $($node.InnerText)"}
}
Write-Output "PASS: $($sources.Count) shader basenames avoid Core collisions; XML sources exist"
