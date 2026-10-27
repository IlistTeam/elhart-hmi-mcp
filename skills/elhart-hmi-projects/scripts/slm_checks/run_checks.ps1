param([Parameter(Mandatory=$true)][string]$ProjectRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ProjectRoot).Path
$shell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$names=@('check_slm_ecp10_profile','check_slm_editor_actions','check_slm_visual_states','check_slm_tags','check_slm_numeric_limits','check_slm_round_geometry','check_slm_caption_positions','check_slm_unit_spacing','check_slm_refresh_timers','check_slm_native_bitmap')
foreach($name in $names){
 & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot ($name+'.ps1')) -ProjectRoot $root
 if($LASTEXITCODE){throw ($name+' failed')}
}
Write-Output 'PASS: all version-specific SLM checks. Editor compilation/simulator require separate validation.'
