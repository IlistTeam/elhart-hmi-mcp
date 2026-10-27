param([string]$ProjectRoot=(Join-Path $PSScriptRoot 'SLM_Powder_Loader_DEMO'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'loadcheck_slm_elhart.ps1') -ProjectRoot $ProjectRoot -CheckWindows
$build=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Studio.Build.dll'))
$compiler=$build.GetType('GA6f9nlBJS34XHRy2rN.HoDpibl0IBES7naWRlk')
$getVariable=$compiler.GetMethod('EBVor8AhlhZYMbpoJLvM',[Reflection.BindingFlags]'Public,NonPublic,Static,Instance')
$setDataType=$compiler.GetMethod('zBcMlNAh8srt4phNnnfQ',[Reflection.BindingFlags]'Public,NonPublic,Static,Instance')
$count=0
foreach($file in Get-ChildItem (Join-Path $ProjectRoot 'HMI\Frame\Basic') -Filter '*.wcfg'){
 $r=[Xml.XmlReader]::Create($file.FullName);try{$w=$serializer.ReadObject($r)}finally{$r.Close()};if($w.Id -gt 5){continue}
 foreach($g in $w.Graphicses){
  if($g.GetType().Name -ne 'NumericCharInfoDto' -or $g.NormalData.OperationAttrib.ToString() -eq 'CharDisplay'){continue}
  $m=$mapper.Invoke($mapperInstance,[object[]]@($g))
  foreach($data in @($m.NormalData,$m.NumericSetingData)){
   if(!$data.NumericLimit.UseLimit){throw ('Compiler requires populated limit models: '+$g.GraphicsName)}
   foreach($bound in @($data.NumericLimit.MinLimit,$data.NumericLimit.MaxLimit)){
    if(!$bound -or !$bound.IsConstant){throw ('Missing constant bound: '+$g.GraphicsName)}
    # These are the exact native compiler accessors from the failing numeric payload path.
    $address=$getVariable.Invoke($null,[object[]]@($bound))
    if(!$address){throw ('Missing bound address metadata: '+$g.GraphicsName)}
    $setDataType.Invoke($null,[object[]]@($address,$data.DataType)) | Out-Null
   }
   if([double]$data.NumericLimit.MinLimit.Constant -gt [double]$data.NumericLimit.MaxLimit.Constant){throw 'Reversed numeric bounds'}
  }
  $count++
 }
}
Write-Output ('PASS: '+$count+' numeric fields / '+($count*4)+' bounds through native compiler variable getter and data-type setter. Constant values and original input limits preserved.')
Write-Output 'LIMITATION: this validates the numeric limit failure path, not a complete editor build or simulator run.'
