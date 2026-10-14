param([string]$Bin='C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x\Bin')
$ErrorActionPreference='Stop'
[AppDomain]::CurrentDomain.add_AssemblyResolve({
 param($sender,$args)
 $taskDependency=Join-Path $Bin ((New-Object Reflection.AssemblyName($args.Name)).Name+'.dll')
 if(Test-Path -LiteralPath $taskDependency) { return [Reflection.Assembly]::LoadFrom($taskDependency) }
 return $null
})
$taskAssembly=[Reflection.Assembly]::LoadFrom((Join-Path $Bin 'Flexem.Studio.Build.dll'))
foreach($taskType in $taskAssembly.GetTypes()) {
 foreach($taskConstructor in $taskType.GetConstructors()) {
  $taskParameters=@($taskConstructor.GetParameters() | ForEach-Object { $_.ParameterType.FullName })
  if($taskParameters -contains 'Flexem.Studio.Build.IBuildMessageService') {
   [pscustomobject]@{type=$taskType.FullName;parameters=$taskParameters;interfaces=@($taskType.GetInterfaces() | ForEach-Object {$_.FullName});methods=@($taskType.GetMethods([Reflection.BindingFlags]'Public,Instance,DeclaredOnly') | ForEach-Object {$_.ToString()})} | ConvertTo-Json -Depth 5
  }
 }
}
