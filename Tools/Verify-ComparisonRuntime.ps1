$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Program Files/Unity/Hub/Editor/6000.3.12f1/Editor/Data/Managed/Unity.Cecil.dll'
$repo=Split-Path -Parent $PSScriptRoot
function Get-Code($path){
 $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
 $all=[System.Collections.Generic.List[string]]::new()
 function Visit-Type($type){
  $all.Add($type.FullName)
  foreach($field in $type.Fields){$all.Add($field.FullName)}
  foreach($method in $type.Methods){
   $all.Add($method.FullName)
   if($method.HasBody){foreach($instruction in $method.Body.Instructions){$all.Add($instruction.ToString())}}
  }
  foreach($nested in $type.NestedTypes){Visit-Type $nested}
 }
 foreach($type in $a.MainModule.Types){Visit-Type $type}
 $code=$all -join "`n"
 $digest=[Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($code)))
 [pscustomobject]@{mvid=$a.MainModule.Mvid.ToString();ilSha256=$digest;assemblySha256=(Get-FileHash -LiteralPath $path).Hash;code=$code}
}
$pcf=Get-Code (Join-Path $repo 'Builds/PCF-Baseline-20261107/NeuralShadingLab_Data/Managed/Assembly-CSharp.dll')
$pcss=Get-Code (Join-Path $repo 'Builds/WindowsDevelopment/NeuralShadingLab_Data/Managed/Assembly-CSharp.dll')
if($pcf.code -cne $pcss.code){throw 'Runtime IL differs'}
[ordered]@{identicalTypeFieldMethodIL=$true;scope='Assembly-CSharp type/field/method signatures and instruction text, including nested types; excludes MVID and debug metadata';PCF=($pcf|Select-Object mvid,ilSha256,assemblySha256);PCSS=($pcss|Select-Object mvid,ilSha256,assemblySha256)}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $repo 'Benchmarks/ShadowComparison/runtime-identity.json')
