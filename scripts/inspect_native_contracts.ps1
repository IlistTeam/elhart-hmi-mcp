param([string]$Bin='C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x\Bin', [string]$Type)
$ErrorActionPreference='Stop'
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve({
 param($sender,$eventArgs)
 $taskDependency=Join-Path $Bin ((New-Object Reflection.AssemblyName($eventArgs.Name)).Name+'.dll')
 if(Test-Path -LiteralPath $taskDependency) { return [Reflection.Assembly]::ReflectionOnlyLoadFrom($taskDependency) }
 return [Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name)
})
if($Type) {
 foreach($taskFile in Get-ChildItem -LiteralPath $Bin -Filter 'Flexem*.dll') {
  $taskCandidate=[Reflection.Assembly]::ReflectionOnlyLoadFrom($taskFile.FullName).GetType($Type,$false)
  if($taskCandidate) {
   [pscustomobject]@{type=$taskCandidate.FullName;assembly=$taskFile.Name;enum_values=$(if($taskCandidate.IsEnum){@($taskCandidate.GetFields([Reflection.BindingFlags]'Public,Static') | ForEach-Object {$_.Name})}else{$null});constructors=@($taskCandidate.GetConstructors() | ForEach-Object {$_.ToString()});factories=@($taskCandidate.GetMethods([Reflection.BindingFlags]'Public,Static,FlattenHierarchy') | Where-Object {$_.Name -match 'Create|Fetch'} | ForEach-Object {$_.ToString()});properties=@($taskCandidate.GetProperties() | ForEach-Object {$_.Name+': '+$_.PropertyType.FullName})} | ConvertTo-Json -Depth 4
   exit 0
  }
 }
 throw "Native type missing: $Type"
}
$taskAssembly=[Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $Bin 'Flexem.Studio.Build.dll'))
foreach($taskType in $taskAssembly.GetTypes()) {
 foreach($taskConstructor in $taskType.GetConstructors()) {
  $taskParameters=@($taskConstructor.GetParameters() | ForEach-Object { $_.ParameterType.FullName })
  if($taskParameters -contains 'Flexem.Studio.Build.IBuildMessageService') {
   [pscustomobject]@{type=$taskType.FullName;parameters=$taskParameters;interfaces=@($taskType.GetInterfaces() | ForEach-Object {$_.FullName});methods=@($taskType.GetMethods([Reflection.BindingFlags]'Public,Instance,DeclaredOnly') | ForEach-Object {$_.ToString()})} | ConvertTo-Json -Depth 5
  }
 }
}
