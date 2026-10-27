param([string]$ProjectRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'loadcheck_slm_elhart.ps1') -ProjectRoot $ProjectRoot -CheckWindows
$file=Join-Path $ProjectRoot 'HMI\Frame\Basic\ad16075d-af58-45b3-8b28-deba24a30061.wcfg'
$reader=[Xml.XmlReader]::Create($file);try{$window=$serializer.ReadObject($reader)}finally{$reader.Close()}
$count=0
foreach($dto in $window.Graphicses){
 if($dto.GetType().Name -ne 'NumericCharInfoDto' -or $dto.NormalData.OperationAttrib.ToString() -eq 'CharDisplay'){continue}
 $model=$mapper.Invoke($mapperInstance,[object[]]@($dto))
 $fontPosition=$model.FontData.FontPostion
 if($fontPosition.HorizontalAlignmentType.ToString() -ne 'Right' -or $fontPosition.HorizontalMargin -ne 0){throw 'Numeric model lost right alignment'}
 $p=$dto.DisplaySettings.Position
 $labels=@($window.Graphicses|Where-Object{ $_.GetType().Name -eq 'StaticTextInfoDto' -and $_.NormalInfo.Position.Y -eq $p.Y+7 -and $_.NormalInfo.Position.X -eq $p.X+$p.Width+8 })
 if($labels.Count -ne 1){throw 'Unit caption missing or gap incorrect'}
 $lm=$mapper.Invoke($mapperInstance,[object[]]@($labels[0].PSObject.BaseObject))
 if($lm.NormalInfo.TagPosition.LeftRightPosition.ToString() -ne 'Left'){throw 'Unit model lost left alignment'}
 $count++
}
if($count -ne 5){throw 'Expected 5 numeric/unit pairs on the first screen'}
Write-Output 'PASS: 5 native numeric models are right aligned, with zero margin; 5 unit models left aligned, at a fixed 8 px gap.'
