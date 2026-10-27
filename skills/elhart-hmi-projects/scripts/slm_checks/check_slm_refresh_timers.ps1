param([string]$ProjectRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'loadcheck_slm_elhart.ps1') -ProjectRoot $ProjectRoot -CheckWindows
$count=0
foreach($f in Get-ChildItem (Join-Path $ProjectRoot 'HMI\Frame\Basic') -Filter '*.wcfg'){
 $reader=[Xml.XmlReader]::Create($f.FullName);try{$w=$serializer.ReadObject($reader)}finally{$reader.Close()}
 if($w.Id -gt 5){continue}
 foreach($t in $w.TimerList.Timers){
  if($t.TimerTriggerAndBreakInfo.ExecutionCycle -ne 1 -or !$t.TimerTriggerAndBreakInfo.AutoReset -or !$t.TimerFunctionInfo.RunMacro){throw 'Expected a repeating 0.1 s macro timer'}
  $count++
 }
 if($w.Id -eq 1){
  $char=@($w.Graphicses|Where-Object{ $_.GetType().Name -eq 'NumericCharInfoDto' -and $_.NormalData.ReadAddress.Raw.MainAddress -eq 1060 })
  if($char.Count -ne 1 -or $char[0].CharSettingData.CharCount -ne 7 -or $char[0].NormalData.ReadAddress.Raw.Length -ne 4){throw 'Elapsed MM:SS.d native field needs 7 characters / 4 words'}
 }
}
if($count -ne 5){throw 'Expected five active-window macro timers'}
Write-Output 'PASS: five repeating 100 ms native timers and the 7-character MM:SS.d elapsed display are loaded correctly.'
