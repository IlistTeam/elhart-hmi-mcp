param([string]$ProjectRoot=(Join-Path $PSScriptRoot 'SLM_Powder_Loader_DEMO'))
$ErrorActionPreference='Stop'
$compiler='C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x\Macro\Compiler'
$stage=Join-Path $env:TEMP ('slm_elhart_build_'+[Guid]::NewGuid().ToString('N'))
$project=(Resolve-Path -LiteralPath $ProjectRoot).Path
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'demo_expanded.c') -Destination (Join-Path $stage 'demo.c') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test_slm_demo.c') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test_slm_runtime.c') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test_slm_10hz.c') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test_slm_reentry.c'),(Join-Path $PSScriptRoot 'test_slm_reentry_module.c') -Destination $stage -Force
$env:PATH=(Join-Path $compiler 'MinGW\bin')+';'+$env:PATH
Push-Location $compiler
try {
 & '.\build_win32.cmd' "$stage\demo.c" "$stage\demo.dll" "C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x\Macro\Config" "$stage\demo.o" "$stage\gcc.log"
 if($LASTEXITCODE){Get-Content "$stage\gcc.log";throw 'Win32 compiler failed'}
 & '.\build_arm.cmd' "$stage\demo.c" "$stage\demo.so" "C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x\Macro\Config" "$stage\arm.log"
 if($LASTEXITCODE){Get-Content "$stage\arm.log";throw 'ARM compiler failed'}
 & '.\MinGW\bin\gcc.exe' '-I' 'C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x\Macro\Config' "$stage\test_slm_demo.c" '-o' "$stage\test.exe" '-lm'
 if($LASTEXITCODE){throw 'Tests failed to compile'}
 & "$stage\test.exe"
 if($LASTEXITCODE){throw 'Behavior tests failed'}
 & '.\MinGW\bin\gcc.exe' "$stage\test_slm_runtime.c" '-o' "$stage\runtime_test.exe"
 if($LASTEXITCODE){throw 'Runtime test failed to compile'}
 Push-Location $stage
 try{& "$stage\runtime_test.exe";if($LASTEXITCODE){throw 'Runtime callback tests failed'}}finally{Pop-Location}
 if ((Get-Content -LiteralPath (Join-Path $project 'demo_expanded.c') -Raw) -match 'elapsed_tenths') {
  & '.\MinGW\bin\gcc.exe' "$stage\test_slm_10hz.c" '-o' "$stage\refresh_test.exe"
  if($LASTEXITCODE){throw '100 ms test failed to compile'}
  & "$stage\refresh_test.exe"
  if($LASTEXITCODE){throw '100 ms timing test failed'}
  & '.\MinGW\bin\gcc.exe' '-shared' "$stage\test_slm_reentry_module.c" '-o' "$stage\reentry_module.dll"
  if($LASTEXITCODE){throw 'Reentry module failed to compile'}
  & '.\MinGW\bin\gcc.exe' "$stage\test_slm_reentry.c" '-o' "$stage\reentry_test.exe"
  if($LASTEXITCODE){throw 'Reentry driver failed to compile'}
  Push-Location $stage
  try{& "$stage\reentry_test.exe";if($LASTEXITCODE){throw 'Recreated-context timing test failed'}}finally{Pop-Location}
 }
}finally{Pop-Location}
New-Item -ItemType Directory -Path (Join-Path $project 'Validation') -Force | Out-Null
Copy-Item -LiteralPath "$stage\gcc.log","$stage\arm.log" -Destination (Join-Path $project 'Validation') -Force
# Elhart rebuilds these from the native macro source on project compilation.
Copy-Item -LiteralPath "$stage\demo.dll","$stage\demo.so" -Destination (Join-Path $project 'Validation') -Force
Write-Output 'Native macro compiled for simulator (Win32) and panel (ARM).'
