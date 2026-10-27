param([string]$ProjectRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'loadcheck_slm_elhart.ps1') -ProjectRoot $ProjectRoot -CheckWindows
$build=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Studio.Build.dll'))
$adapter=$build.GetType('Flexem.Studio.Build.Adapter.TagPropertyInfoBuildAdapter')
$count=0
foreach($file in Get-ChildItem (Join-Path $ProjectRoot 'HMI\Frame\Basic') -Filter '*.wcfg'){
 $reader=[Xml.XmlReader]::Create($file.FullName);try{$window=$serializer.ReadObject($reader)}finally{$reader.Close()}
 if($window.Id -gt 5){continue}
 foreach($dto in $window.Graphicses){
  if($dto.GetType().Name -ne 'SwitchInfoDto' -or $dto.Tag.Margin.Left -lt 40){continue}
  $model=$mapper.Invoke($mapperInstance,[object[]]@($dto))
  $tag=[Activator]::CreateInstance($adapter,[object[]]@($model.NewTag))
  $position=$tag.TagHorizontalPosition
  if($position.TagHorizontalAlignmentType.ToString() -ne 'Left' -or $position.Margin -ne $dto.Tag.Margin.Left){throw ('Compiler ignored caption margin: '+$dto.GraphicsName)}
  if($tag.IsDecisionTagOverflow){throw 'Caption could expand bitmap'}
  $count++
 }
}
if($count -ne 31){throw ('Unexpected caption position count: '+$count)}
Write-Output ('PASS: '+$count+' caption positions have effective LEFT margins 40/60 px in the installed native compiler adapter.')
