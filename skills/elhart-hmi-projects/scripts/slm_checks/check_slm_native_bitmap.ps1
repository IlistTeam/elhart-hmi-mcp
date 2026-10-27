param([string]$ProjectRoot=(Join-Path $PSScriptRoot 'SLM_Powder_Loader_MATCHED'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'loadcheck_slm_elhart.ps1') -ProjectRoot $ProjectRoot -CheckWindows
$build=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Studio.Build.dll'))
$maker=$build.GetType('ajB2n0PpuFJLZOePMsU.SrbPDMP3iGXuNTLFXk0').GetMethod('uXVDGw8yhH',[Reflection.BindingFlags]'NonPublic,Public,Static,Instance')
$target=if($maker.IsStatic){$null}else{[Activator]::CreateInstance($maker.DeclaringType,$true)}
$graphSer=New-Object Runtime.Serialization.DataContractSerializer($dtos.GetType('Flexem.Studio.HMI.GraphInfoDto'))
$count=0
foreach($f in Get-ChildItem (Join-Path $ProjectRoot 'HMI\Lib\Graphics') -Filter '*.fsvg'){
 $reader=[Xml.XmlReader]::Create($f.FullName);try{$g=$graphSer.ReadObject($reader)}finally{$reader.Close()}
 if($g.Name -notlike 'SLM_*'){continue}
 foreach($state in $g.GraphStatuses){foreach($dto in $state.Graphicses){
  if($dto.GetType().Name -ne 'StaticPictureInfoDto'){continue}
  $model=$mapper.Invoke($mapperInstance,[object[]]@($dto))
  $result=$maker.Invoke($target,[object[]]@([double]1,[double]1,$model))
  if(!$result){throw 'Native bitmap maker returned no picture'}
  $getter=$result.GetType().GetMethod('OMaDdTkOic',[Reflection.BindingFlags]'NonPublic,Public,Static,Instance')
  $picture=$getter.Invoke($result,@())
  if($picture.Width -ne $dto.NormalInfo.Position.Width -or $picture.Height -ne $dto.NormalInfo.Position.Height){throw ('Native bitmap dimensions mismatch '+$g.Name)}
  $count++
 }}
}
Write-Output ('PASS: '+$count+' PNG state decorations accepted by the installed native bitmap builder with their original width/height.')
Write-Output 'LIMITATION: this checks bitmap assets; full component compilation and simulator display still require a fresh editor build.'
