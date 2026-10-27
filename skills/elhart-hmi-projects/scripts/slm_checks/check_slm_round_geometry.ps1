param([string]$ProjectRoot=(Join-Path $PSScriptRoot 'SLM_Powder_Loader_MATCHED'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'loadcheck_slm_elhart.ps1') -ProjectRoot $ProjectRoot -CheckWindows
$graphSer=New-Object Runtime.Serialization.DataContractSerializer($dtos.GetType('Flexem.Studio.HMI.GraphInfoDto'))
$graphs=@{}
foreach($f in Get-ChildItem (Join-Path $ProjectRoot 'HMI\Lib\Graphics') -Filter '*.fsvg'){
 $reader=[Xml.XmlReader]::Create($f.FullName);try{$g=$graphSer.ReadObject($reader)}finally{$reader.Close()};$graphs[$g.Id.ToString()]=$g
}
$switches=0;$round=0;$badgeCount=0
$gpLoader=$models.GetType('Flexem.Studio.Hmi.Models.Components.Parts.SwitchPart.GraphProperty.GraphPropertyModel').GetMethod('Load')
foreach($f in Get-ChildItem (Join-Path $ProjectRoot 'HMI\Frame\Basic') -Filter '*.wcfg'){
 $reader=[Xml.XmlReader]::Create($f.FullName);try{$window=$serializer.ReadObject($reader)}finally{$reader.Close()}
 if($window.Id -gt 5){continue}
 foreach($dto in $window.Graphicses){
  if($dto.GetType().Name -ne 'SwitchInfoDto' -or !$dto.GraphProperty.UseGraph){continue}
  $p=$dto.DisplaySettings.Position;$gp=$dto.GraphProperty
  $native=$gpLoader.Invoke($null,[object[]]@($gp))
  if($native.GraphWidth -ne $p.Width -or $native.GraphHeight -ne $p.Height){throw 'Graph/component dimension mismatch'}
  $graph=$graphs[$gp.Id.ToString()]
  if($graph.Width -ne $p.Width -or $graph.Height -ne $p.Height){throw 'Gallery/component dimension mismatch'}
  $switches++
  if($graph.Name -like 'SLM_stage*' -or $graph.Name -like 'SLM_checkcircle*' -or $graph.Name -like 'SLM_dot*'){
   if($dto.Tag.IsDecisionTagOverflow){throw 'Pure graphic is allowed to expand for captions'}
   $nativeSwitch=$mapper.Invoke($mapperInstance,[object[]]@($dto))
   if($nativeSwitch.NewTag.IsDecisionTagOverflow){throw 'Native tag model re-enabled caption overflow'}
   foreach($s in $dto.Tag.Statuses){foreach($lang in $s.Items){if($lang.Text){throw 'A hidden/error caption can enlarge the circle sprite'}}}
   if($p.Width -ne $p.Height){throw 'Round indicator component is not square'}
   foreach($s in $graph.GraphStatuses){foreach($pic in $s.Graphicses){
    if($pic.GetType().Name -ne 'StaticPictureInfoDto'){continue}
    $normal=$pic.NormalInfo
    if($normal.Position.Width -ne $normal.Position.Height){throw 'Round image is not square'}
    if(!$normal.SizeZoom.IsFixLatitudeScale -or !$normal.SizeZoom.IsUseOriginalSize){throw 'Image aspect ratio is not locked'}
    $data=[byte[]]$normal.ImageData
    $bw=($data[16]*16777216)+($data[17]*65536)+($data[18]*256)+$data[19]
    $bh=($data[20]*16777216)+($data[21]*65536)+($data[22]*256)+$data[23]
    if($bw -ne $bh -or $bw -ne $normal.Position.Width){throw 'Round PNG size mismatch'}
   }}
   $round++
  }
  if($window.Id -eq 1 -and $p.X -eq 816 -and $p.Width -eq 190 -and $p.Height -eq 46){
   foreach($s in $gp.GraphStateInfos){if($s.Index -ge $graph.GraphStatuses.Count){throw 'Missing status badge appearance'}}
   if($dto.Tag.HorizontalAlignment.ToString() -ne 'Left' -or $dto.Tag.Margin.Left -lt 40){throw 'Badge needs an effective global caption margin'}
   foreach($s in $gp.GraphStateInfos){foreach($lang in $dto.Tag.Statuses[$s.Index].Items){if($lang.Margin.Left -lt 40){throw 'Badge text overlaps dot area'}}}
   if($graph.GraphStatuses[0].Graphicses.Count -ne 1 -or $graph.GraphStatuses[0].Graphicses[0].GetType().Name -ne 'StaticPictureInfoDto'){throw 'Badge should use one bitmap containing its background and dot'}
   $badgeCount++
  }
 }
 if($window.Id -eq 1){foreach($dto in $window.Graphicses){if($dto.GetType().Name -eq 'SwitchInfoDto' -and $dto.DisplaySettings.Position.X -eq 826 -and $dto.DisplaySettings.Position.Width -eq 16){throw 'Old floating status dot still present'}}}
}
if($badgeCount -ne 1){throw 'Header badge is missing or duplicated'}
Write-Output ('PASS: '+$switches+' native component/gallery sizing pairs; '+$round+' square round indicators with matching PNG dimensions and locked image proportions; integral header dot and reserved text area.')
