param([string]$ProjectRoot=(Join-Path $PSScriptRoot 'SLM_Powder_Loader_DEMO'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'loadcheck_slm_elhart.ps1') -ProjectRoot $ProjectRoot -CheckWindows
$build=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Studio.Build.dll'))
$adapter=$build.GetType('Flexem.Studio.Build.Adapter.TagPropertyInfoBuildAdapter')
$getItem=$adapter.GetMethod('GetItemById')
$count=0
foreach($file in Get-ChildItem (Join-Path $ProjectRoot 'HMI\Frame\Basic') -Filter '*.wcfg'){
 $reader=[Xml.XmlReader]::Create($file.FullName);try{$window=$serializer.ReadObject($reader)}finally{$reader.Close()}
 if($window.Id -gt 5){continue}
 foreach($dto in $window.Graphicses){
  if($dto.GetType().Name -ne 'SwitchInfoDto'){continue}
  $control=$dto.Indicator.Usage.RegisterControl
  if(!$dto.Indicator.UseIndicator -or !$control -or $control.ControlType.ToString() -ne 'WordRegister'){continue}
  $model=$mapper.Invoke($mapperInstance,[object[]]@($dto))
  $tag=[Activator]::CreateInstance($adapter,[object[]]@($model.NewTag))
  # The compiler requests an extra error/fallback state for every word indicator.
  foreach($id in 0..$control.Word.Statuses.Count){
   try{$item=$getItem.Invoke($tag,[object[]]@([int]$id));if(!$item){throw 'Tag state missing'}}
   catch{$e=$_.Exception;while($e.InnerException){$e=$e.InnerException};Write-Output ('FAIL: '+$window.Name+'/'+$dto.GraphicsName+' state='+$id+' '+$e.ToString());throw}
  }
  $count++
 }
}
Write-Output ('PASS: '+$count+' word indicator tags, including error states, loaded through native compiler TagPropertyInfoBuildAdapter')
$dictionaryType=[Collections.Generic.Dictionary[guid,string]]
$gallerySerializer=New-Object Runtime.Serialization.DataContractSerializer($dictionaryType)
$galleryRoot=Join-Path $ProjectRoot 'HMI\Lib\Graphics'
$reader=[Xml.XmlReader]::Create((Join-Path $galleryRoot 'Thumbnails\ThumbnailConfigs.xml'))
try{$galleryIndex=$gallerySerializer.ReadObject($reader)}finally{$reader.Close()}
$graphSerializer=New-Object Runtime.Serialization.DataContractSerializer($dtos.GetType('Flexem.Studio.HMI.GraphInfoDto'))
$graphics=0
foreach($file in Get-ChildItem $galleryRoot -Filter '*.fsvg'){
 $reader=[Xml.XmlReader]::Create($file.FullName);try{$graph=$graphSerializer.ReadObject($reader)}finally{$reader.Close()}
 if($graph.Name -notlike 'SLM_*'){continue}
 if(!$galleryIndex.ContainsKey($graph.Id)){throw ('Vector gallery missing from native index '+$graph.Name)}
 $indexed=Join-Path $galleryRoot ($galleryIndex[$graph.Id]+'.fsvg')
 if(!(Test-Path -LiteralPath $indexed)){throw ('Indexed vector file not found '+$indexed)}
 if(!(Test-Path -LiteralPath (Join-Path $galleryRoot ('Thumbnails\'+$galleryIndex[$graph.Id]+'\Status0.png')))){throw 'Missing gallery thumbnail'}
 $graphics++
}
Write-Output ('PASS: '+$graphics+' vector galleries indexed and parsed through native DataContractSerializer')
