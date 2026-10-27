param([string]$ProjectRoot=(Join-Path $PSScriptRoot 'SLM_Powder_Loader_DEMO'))
$ErrorActionPreference='Stop'
# Uses installed .NET Framework editor libraries, including the legacy CSLA loader.
. (Join-Path $PSScriptRoot 'loadcheck_slm_elhart.ps1') -ProjectRoot $ProjectRoot -CheckWindows
$parser=[Activator]::CreateInstance([ElhartLoadStringParser])
$register.MakeGenericMethod($core.GetType('Flexem.Services.IStringParser')).Invoke($resolver,[object[]]@($parser)) | Out-Null
$editor=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Studio.HMI.Window.Library.dll'))
$actionType=$editor.GetType('Flexem.Studio.HMI.Window.HMIWindowAction')
$actionMethods=$actionType.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static') | Where-Object {$_.Name -eq 'FetchAsChild'}
$actionMethods | ForEach-Object {Write-Output $_.ToString()}
$fetch=$actionMethods | Where-Object {$_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.FullName -eq 'Flexem.Studio.HMI.Window.HMIWindowActionDto'} | Select-Object -First 1
if(!$fetch){throw 'Editor action FetchAsChild factory not found'}
$count=0
$componentLibrary=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Studio.Components.Library.dll'))
$timerType=$componentLibrary.GetType('Flexem.Studio.Components.Parts.TimerPart.TimerListInfo')
$timerFetch=$timerType.GetMethod('FetchAsChild',[Reflection.BindingFlags]'Public,NonPublic,Static')
foreach($file in (Get-ChildItem (Join-Path $ProjectRoot 'HMI\Frame\Basic') -Filter '*.wcfg')) {
 $reader=[Xml.XmlReader]::Create($file.FullName);try{$window=$serializer.ReadObject($reader)}finally{$reader.Close()}
 if($window.Id -gt 5){continue}
 foreach($action in $window.Actions){
  try{
   $loaded=$fetch.Invoke($null,[object[]]@($action))
   if(!$loaded){throw 'Editor action loader returned null'}
   Write-Output ('EDITOR ACTION PASS: '+$window.Name+' / '+$loaded.ExecuteType)
   $count++
  }catch{
   $e=$_.Exception;while($e.InnerException){$e=$e.InnerException}
   Write-Output $e.ToString();throw
  }
 }
 if($window.TimerList){
  try{$timers=$timerFetch.Invoke($null,[object[]]@($window.TimerList));Write-Output ('EDITOR TIMER PASS: '+$window.Name)}
  catch{$e=$_.Exception;while($e.InnerException){$e=$e.InnerException};Write-Output $e.ToString();throw}
 }
}
Write-Output ('PASS: '+$count+' actions loaded through actual HMIWindowAction.FetchAsChild/Init')
