param([string]$ProjectRoot=(Join-Path $PSScriptRoot 'SLM_Powder_Loader_MATCHED'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'loadcheck_slm_elhart.ps1') -ProjectRoot $ProjectRoot -CheckWindows
$bits=0;$pics=0;$states=0
foreach($file in Get-ChildItem (Join-Path $ProjectRoot 'HMI\Frame\Basic') -Filter '*.wcfg'){
 $reader=[Xml.XmlReader]::Create($file.FullName);try{$window=$serializer.ReadObject($reader)}finally{$reader.Close()}
 if($window.Id -gt 5){continue}
 foreach($g in $window.Graphicses){
  if($g.GetType().Name -ne 'SwitchInfoDto'){continue}
  if($g.ControlSettingsProperty.VisibilityInfo.ConditionCollection.Count -ne 0){throw ('Unexpected visibility condition: '+$g.GraphicsName)}
  $ctrl=$g.Indicator.Usage.RegisterControl
  if(!$g.Indicator.UseIndicator -or !$ctrl -or $ctrl.ControlType.ToString() -ne 'BitRegister'){continue}
  $s=$ctrl.Bit.Statuses
  if($s.Count -ne 2 -or $s[0].IsNegativeLogic -or !$s[1].IsNegativeLogic){throw ('Bit condition pair incomplete: '+$g.GraphicsName)}
  if($g.Tag.Statuses.Count -ne 2 -or $g.GraphProperty.GraphStateInfos.Count -ne 2){throw ('Missing bit display state: '+$g.GraphicsName)}
  foreach($i in 0..1){
   if(!$g.Tag.Statuses[$i].Items.Count){throw 'Missing bit state label'}
   if($g.GraphProperty.GraphStateInfos[$i].Index -ne $i){throw 'Wrong graph state index'}
  }
  $bits++
 }
}
$graphSer=New-Object System.Runtime.Serialization.DataContractSerializer($dtos.GetType('Flexem.Studio.HMI.GraphInfoDto'))
foreach($f in Get-ChildItem (Join-Path $ProjectRoot 'HMI\Lib\Graphics') -Filter '*.fsvg'){
 if($f.BaseName -notmatch '^[0-9a-f-]{36}$'){continue}
 $reader=[Xml.XmlReader]::Create($f.FullName);try{$graph=$graphSer.ReadObject($reader)}finally{$reader.Close()}
 if($graph.Name -notlike 'SLM_*'){continue}
 foreach($status in $graph.GraphStatuses){
  if(!$status.Graphicses.Count){throw 'Empty state appearance'}
  foreach($dto in $status.Graphicses){
   $model=$mapper.Invoke($mapperInstance,[object[]]@($dto));if(!$model){throw 'Native graphic model missing'}
   if($dto.GetType().Name -eq 'StaticPictureInfoDto'){
    $data=[byte[]]$dto.NormalInfo.ImageData
    if($data.Length -lt 8 -or $data[0] -ne 137 -or $data[1] -ne 80){throw 'Invalid PNG pictogram'}
    if(!$dto.NormalInfo.AlphaColor.IsUsePngAlphaChannel){throw 'Missing PNG transparency'}
    $pics++
   }
  }
  $states++
 }
}
Write-Output ('PASS: '+$bits+' native bit indicators have a complete 0/1 condition pair, matching label/graph states, and no conditional hiding.')
Write-Output ('PASS: '+$states+' gallery states loaded through native GraphicModelMapper, including '+$pics+' transparent PNG pictograms.')
