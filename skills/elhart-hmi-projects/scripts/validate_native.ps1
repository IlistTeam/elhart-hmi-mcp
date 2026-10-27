param([Parameter(Mandatory=$true)][string]$ProjectRoot,[string]$InstallRoot='C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.Serialization
$bin=Join-Path $InstallRoot 'Bin'
$asm=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Studio.Dtos.dll'))
$root=(Resolve-Path -LiteralPath $ProjectRoot).Path
$cases=@(
 @('HMI\Frame','*.wcfg','Flexem.Studio.HMI.Window.HMIWindowInfoDto'),
 @('HMI\Lib\Graphics','*.fsvg','Flexem.Studio.HMI.GraphInfoDto'),
 @('HMI\Macro','*.cfg','Flexem.Studio.Macro.MacroInfoDto'))
$bad=0;$total=0;$windows=@{};$globals=$null
$controlCount=0
$controlType=$asm.GetType('Flexem.Studio.Components.Parts.ControlSettingsPropertyInfoDto')
if(!$controlType){throw 'Installed control DTO unavailable'}
$members=@($controlType.GetProperties() | ForEach-Object {
 $attribute=$_.GetCustomAttributes([Runtime.Serialization.DataMemberAttribute],$true)|Select-Object -First 1
 if($attribute){[pscustomobject]@{Order=$attribute.Order;Name=$(if($attribute.Name){$attribute.Name}else{$_.Name})}}
})
$controlRanks=@{};$rank=0
foreach($group in $members|Group-Object Order|Sort-Object {[int]$_.Name}){
 $names=[Collections.Generic.List[string]]::new()
 foreach($member in $group.Group){$names.Add($member.Name)}
 $names.Sort([StringComparer]::Ordinal)
 foreach($name in $names){$controlRanks[$name]=$rank;$rank++}
}
function Check-ControlSettings($file,$window){
 $xml=New-Object Xml.XmlDocument;$xml.Load($file)
 $ns=New-Object Xml.XmlNamespaceManager($xml.NameTable)
 $ns.AddNamespace('m','http://schemas.flexem.com/fs/model/1')
 $graphics=@($xml.SelectNodes('/m:HMIWindowInfo/m:Graphicses/m:GraphicsBase',$ns))
 if($graphics.Count -ne $window.Graphicses.Count){throw 'Window graphics XML/DTO count mismatch'}
 for($index=0;$index -lt $graphics.Count;$index++){
  $node=$graphics[$index];$controlNode=$node.SelectSingleNode('m:ControlSettingsProperty',$ns)
  if(!$controlNode){continue}
  $dto=$window.Graphicses[$index];$label=$window.Name+'/'+$dto.GraphicsName
  $previous=-1;$seen=@{}
  foreach($field in $controlNode.ChildNodes){
   if($field.NodeType -ne 'Element' -or $field.NamespaceURI -ne $ns.LookupNamespace('m') -or !$controlRanks.ContainsKey($field.LocalName)){continue}
   $current=$controlRanks[$field.LocalName]
   if($current -le $previous -or $seen.ContainsKey($field.LocalName)){throw ('ControlSettingsProperty DataContract order: '+$label+' / '+$field.LocalName)}
   $previous=$current;$seen[$field.LocalName]=$true
  }
  $control=$dto.ControlSettingsProperty
  if(!$control){throw ('ControlSettingsProperty was lost: '+$label)}
  foreach($name in @('EnableType','IsEnabled','IsDisableWhenNotMatch','IsHideWhenNotMatch','IsTriggerMacroBefore','IsTriggerMacroAfter','TriggerMacroBefore','TriggerMacroAfter')){
   $field=$controlNode.SelectSingleNode('m:'+$name,$ns)
   if($field -and ![string]::Equals($field.InnerText,$control.$name.ToString(),[StringComparison]::OrdinalIgnoreCase)){throw ('ControlSettingsProperty value lost: '+$label+' / '+$name)}
  }
  $visibility=$controlNode.SelectSingleNode('m:VisibilityInfo',$ns)
  if($visibility -and $visibility.GetAttribute('nil','http://www.w3.org/2001/XMLSchema-instance') -ne 'true' -and !$control.VisibilityInfo){throw ('ControlSettingsProperty VisibilityInfo was lost: '+$label)}
  if($control.IsEnabled -and $control.EnableType.ToString() -eq 'Condition'){
   $v=$control.VisibilityInfo
   if(!$v){throw ('Condition control requires VisibilityInfo: '+$label)}
   if($v.IsControlLogic -and (!$v.ConditionCollection -or $v.ConditionCollection.Count -eq 0)){throw ('Logic control requires ConditionCollection: '+$label)}
  }
  $script:controlCount++
 }
}
function Read-NativeFile($file,$type){
 $reader=[Xml.XmlReader]::Create($file)
 try{
  $reader.MoveToContent()|Out-Null
  $serializer=New-Object Runtime.Serialization.DataContractSerializer($type,$reader.LocalName,$reader.NamespaceURI)
  return $serializer.ReadObject($reader)
 }finally{$reader.Close()}
}
foreach($case in $cases){
 $dir=Join-Path $root $case[0]
 if(!(Test-Path -LiteralPath $dir -PathType Container)){throw ('Missing required folder '+$dir)}
 $files=@(Get-ChildItem -LiteralPath $dir -Filter $case[1] -Recurse -File)
 if($case[0] -ne 'HMI\Macro' -and !$files.Count){throw ('No native files in '+$dir)}
 $type=$asm.GetType($case[2]);if(!$type){throw ('Installed DTO unavailable '+$case[2])}
 foreach($file in $files){
  try{
   $o=Read-NativeFile $file.FullName $type
   if($case[0] -eq 'HMI\Frame'){
    $id=$o.UniqueId.ToString()
    if($id -eq [guid]::Empty.ToString() -or $windows.ContainsKey($id)){throw ('Empty or duplicate window UniqueId '+$id)}
    $windows[$id]=$o
    Check-ControlSettings $file.FullName $o
   }
   $total++
  }
  catch{$bad++;$e=$_.Exception;while($e.InnerException){$e=$e.InnerException};Write-Output ('FAIL '+$file.FullName+' '+$e.Message)}
 }
 Write-Output ($case[0]+' checked='+$files.Count)
}
$systemCases=@(
 @('GlobalConfigInfo','Flexem.Studio.HMI.GlobalConfigInfoDto'),
 @('ExtendedPropertyInfo','Flexem.Studio.HMI.ExtendedPropertyInfoDto'),
 @('LanguageConfigInfo','Flexem.Studio.HMI.LanguageConfigInfoDto'),
 @('PLCControlConfigInfo','Flexem.Studio.HMI.PLCControlConfigInfoDto'),
 @('AlertAndEventConfigInfo','Flexem.Studio.HMI.AlertAndEventConfigInfoDto'),
 @('DataSampleConfigInfo','Flexem.Studio.HMI.DataSampleConfigInfoDto'),
 @('PlanTaskConfigInfo','Flexem.Studio.HMI.PlanTaskConfigInfoDto'),
 @('UserLevelConfigInfo','Flexem.Studio.HMI.UserLevelConfigInfoDto'),
 @('UserPermissionConfigInfo','Flexem.Studio.HMI.UserPermissionConfigInfoDto'),
 @('EmailConfigInfo','Flexem.Studio.Hmi.EmailConfigInfoDto'),
 @('FontTemplateConfigInfo','Flexem.Studio.HMI.FontTemplateConfigInfoDto'),
 @('MonitoringRegisterConfigInfo','Flexem.Studio.HMI.MonitoringRegisterConfigInfoDto'),
 @('VideoOutputInfo','Flexem.Studio.HMI.VideoOutputInfoDto'))
$systemCount=0
foreach($case in $systemCases){
 $file=Join-Path $root ('HMI\System\'+$case[0]+'.cfg')
 if(!(Test-Path -LiteralPath $file)){
  if($case[0] -in @('GlobalConfigInfo','ExtendedPropertyInfo')){$bad++;Write-Output ('FAIL missing system configuration '+$file)}
  continue
 }
 try{
  $type=$asm.GetType($case[1]);if(!$type){throw ('Installed DTO unavailable '+$case[1])}
  $o=Read-NativeFile $file $type
  if($case[0] -eq 'GlobalConfigInfo'){$globals=$o}
  $total++;$systemCount++
 }catch{$bad++;$e=$_.Exception;while($e.InnerException){$e=$e.InnerException};Write-Output ('FAIL '+$file+' '+$e.Message)}
}
Write-Output ('HMI\System configuration DTOs checked='+$systemCount)
if($globals){
 try{
  foreach($member in @('InitWindow','HomeWindowId')){
   $id=$globals.$member.ToString()
   if(!$windows.ContainsKey($id)){throw ('GlobalConfigInfo.'+$member+' points to a missing window '+$id)}
  }
  if($globals.ScreensaverIsEnabled -and !$windows.ContainsKey($globals.ScreensaverWindow.ToString())){throw 'Enabled screensaver points to a missing window'}
  foreach($pair in @(@('BacklitTurnDownIsEnabled','BacklitTurnDownTime'),@('BacklitConservationIsEnabled','BacklitConservationTime'),@('ScreensaverIsEnabled','ScreensaverTime'))){
   if($globals.($pair[0]) -and ($null -eq $globals.($pair[1]) -or $globals.($pair[1]) -le 0)){throw ('Enabled '+$pair[0]+' requires a positive '+$pair[1])}
  }
  Write-Output 'PASS: system initial/home/screensaver window references and enabled timeout values.'
 }catch{$bad++;Write-Output ('FAIL system settings '+$_.Exception.Message)}
}
foreach($case in @(
 @('HMI\System\ConnectInfo.cfg',$asm.GetType('Flexem.Studio.HMIWizard.HMIProjectInfoDto')),
 @('HMI\Lib\Graphics\Thumbnails\ThumbnailConfigs.xml',[Collections.Generic.Dictionary[guid,string]]))){
 try{$o=Read-NativeFile (Join-Path $root $case[0]) $case[1];$total++}
 catch{$bad++;$e=$_.Exception;while($e.InnerException){$e=$e.InnerException};Write-Output ('FAIL '+$case[0]+' '+$e.Message)}
}
Write-Output ('Native DTO files: '+$total+' passed, '+$bad+' failed')
Write-Output ('Control settings checked='+$controlCount)
Write-Output 'Scope: DTO deserialization, control order/values and system references/timeouts; full HMI compilation and simulator remain separate checks.'
if($bad){exit 1}
