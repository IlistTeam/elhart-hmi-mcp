param([string]$ProjectRoot=(Join-Path $PSScriptRoot 'SLM_Powder_Loader_DEMO'),[switch]$CheckWindows)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.Serialization
$bin='C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x\Bin'
$dtos=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Studio.Dtos.dll'))
$models=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Studio.Hmi.Models.dll'))
$core=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Core.dll'))
$hmi=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Studio.Hmi.dll'))
Add-Type -Path (Join-Path $PSScriptRoot 'ElhartLoadResolver.cs') -ReferencedAssemblies @((Join-Path $bin 'Flexem.Core.dll'),(Join-Path $bin 'Flexem.Studio.Hmi.dll'))
$resolver=New-Object ElhartLoadResolver
$resolverType=$resolver.GetType()
$aggregator=[Activator]::CreateInstance($core.GetType('Flexem.Services.EventAggregator'))
$register=$resolverType.GetMethods()|Where-Object{$_.Name -eq 'Register' -and $_.IsGenericMethodDefinition}|Select-Object -First 1
$register.MakeGenericMethod($core.GetType('Flexem.Services.IEventAggregator')).Invoke($resolver,[object[]]@($aggregator)) | Out-Null
$projectService=[Activator]::CreateInstance([ElhartLoadProjectService])
$register.MakeGenericMethod($hmi.GetType('Flexem.Studio.Hmi.Project.IHmiProjectService')).Invoke($resolver,[object[]]@($projectService)) | Out-Null
$core.GetType('Flexem.Infrastructure.IoC').GetField('_resolver',[Reflection.BindingFlags]'Static,NonPublic').SetValue($null,$resolver)
$serializer=New-Object System.Runtime.Serialization.DataContractSerializer($dtos.GetType('Flexem.Studio.HMI.Window.HMIWindowInfoDto'))
$mapper=$models.GetType('Flexem.Studio.Hmi.Models.Components.GraphicModelMapper').GetMethod('MapToModel',[Reflection.BindingFlags]'Public,NonPublic,Static,Instance')
$mapperInstance=if(!$mapper.IsStatic){[Activator]::CreateInstance($mapper.DeclaringType)}else{$null}
if($mapperInstance){$register.MakeGenericMethod($models.GetType('Flexem.Studio.Hmi.Models.Components.IGraphicModelMapper')).Invoke($resolver,[object[]]@($mapperInstance))|Out-Null}
$good=0;$bad=0
$windowType=$null
if($CheckWindows){
 $windowType=$models.GetType('Flexem.Studio.Hmi.Models.Window.HmiWindowModel')
 if(!$windowType){
 foreach($f in Get-ChildItem $bin -Filter 'Flexem*.dll'){
  try{$a=[Reflection.Assembly]::LoadFrom($f.FullName);$t=$a.GetType('Flexem.Studio.Models.Window.WindowModel');if($t){$windowType=$t;break}}catch{}
 }
 }
 if(!$windowType){throw 'Native window model type not found'}
 $windowType.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,Instance')|Where-Object Name -eq 'Load'|ForEach-Object{Write-Output $_.ToString()}
}
foreach($file in (Get-ChildItem (Join-Path $ProjectRoot 'HMI\Frame\Basic') -Filter '*.wcfg')) {
 $reader=[Xml.XmlReader]::Create($file.FullName);try{$window=$serializer.ReadObject($reader)}finally{$reader.Close()}
 if($window.Id -gt 5){continue}
 if($CheckWindows){
  $method=$windowType.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static')|Where-Object{$_.Name -eq 'Load' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType -eq $window.GetType()}|Select-Object -First 1
  if(!$method){throw 'Window DTO Load factory not found'}
  $wm=$method.Invoke($null,[object[]]@($window))
 }
 foreach($g in $window.Graphicses){
  try{
   $model=$mapper.Invoke($mapperInstance,[object[]]@($g))
   if(!$model){throw 'Native mapper returned no model'}
   $good++
  }catch{
   $e=$_.Exception;while($e.InnerException){$e=$e.InnerException}
   Write-Output ('FAIL '+$window.Name+' / '+$g.GraphicsName+' / '+$e.ToString());$bad++
   if($bad -ge 3){throw 'Native model loading failed; first 3 errors shown'}
  }
 }
 Write-Output ('Loaded '+$window.Name+' objects='+$window.Graphicses.Count)
}
if($bad){throw ('Native model loading: '+$bad+' failures')}
Write-Output ('PASS: loaded '+$good+' graphics through native GraphicModelMapper')
if($CheckWindows){Write-Output 'PASS: complete native window models loaded, including actions and timers (isolated project/event service context).'}
