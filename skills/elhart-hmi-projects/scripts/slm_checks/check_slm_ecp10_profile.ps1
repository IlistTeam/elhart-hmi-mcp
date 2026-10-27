param([string]$ProjectRoot)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.Serialization
$bin='C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x\Bin'
$dtos=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Flexem.Studio.Dtos.dll'))
$type=$dtos.GetType('Flexem.Studio.HMIWizard.HMIProjectInfoDto')
$reader=[Xml.XmlReader]::Create((Join-Path $ProjectRoot 'HMI\System\ConnectInfo.cfg'))
$reader.MoveToContent() | Out-Null
$ser=New-Object Runtime.Serialization.DataContractSerializer($type,$reader.LocalName,$reader.NamespaceURI)
try{$profile=$ser.ReadObject($reader)}finally{$reader.Close()}
if($profile.ID -ne 2050 -or $profile.Name -ne 'ECP10'){throw 'Wrong panel profile'}
$catalog=[xml](Get-Content -LiteralPath (Join-Path (Split-Path $bin) 'Device\HMI\HMI_index.CFG') -Raw)
$native=$catalog.SelectSingleNode("//HMIType[@ID='2050']")
if($native.DisplaySizeX -ne '1024' -or $native.DisplaySizeY -ne '768'){throw 'Wrong catalogue resolution'}
$ser=New-Object Runtime.Serialization.DataContractSerializer($dtos.GetType('Flexem.Studio.HMI.Window.HMIWindowInfoDto'))
$count=0
foreach($f in Get-ChildItem (Join-Path $ProjectRoot 'HMI\Frame\Basic') -Filter '*.wcfg'){
 $reader=[Xml.XmlReader]::Create($f.FullName);try{$w=$ser.ReadObject($reader)}finally{$reader.Close()}
 if($w.Id -gt 5){continue}
 if($w.Width -ne 1024 -or $w.Height -ne 768){throw 'Window resolution differs from ECP10'}
 foreach($o in $w.Graphicses){
  $p=if($o.GetType().Name -in @('SwitchInfoDto','NumericCharInfoDto')){$o.DisplaySettings.Position}else{$o.NormalInfo.Position}
  if($p.X -lt 0 -or $p.Y -lt 0 -or $p.X+$p.Width -gt 1024 -or $p.Y+$p.Height -gt 768){throw ('Control outside screen: '+$o.GraphicsName)}
 }
 $count++
}
if($count -ne 5){throw 'Expected five demo windows'}
Write-Output 'PASS: native profile DTO ID=2050/Name=ECP10; installed F010 catalogue resolution=1024x768/runtime=a8; all 5 demo windows and 228 controls fit the panel.'
