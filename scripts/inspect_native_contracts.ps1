param([string]$Bin='C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x\Bin', [string]$Type, [string]$Pattern)
$ErrorActionPreference='Stop'
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve({
 param($sender,$eventArgs)
 $taskDependency=Join-Path $Bin ((New-Object Reflection.AssemblyName($eventArgs.Name)).Name+'.dll')
 if(Test-Path -LiteralPath $taskDependency) { return [Reflection.Assembly]::ReflectionOnlyLoadFrom($taskDependency) }
 return [Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name)
})
if($Pattern) {
 foreach($taskFile in Get-ChildItem -LiteralPath $Bin -Filter 'Flexem*.dll') {
  $taskAssembly=[Reflection.Assembly]::ReflectionOnlyLoadFrom($taskFile.FullName)
  try { $taskTypes=$taskAssembly.GetTypes() } catch [Reflection.ReflectionTypeLoadException] { $taskTypes=$_.Exception.Types | Where-Object { $_ } }
  foreach($taskMatch in $taskTypes | Where-Object { $_.FullName -match $Pattern }) {
   [pscustomobject]@{type=$taskMatch.FullName;assembly=$taskFile.Name} | ConvertTo-Json -Compress
  }
 }
 exit 0
}
function Get-MethodContracts($NativeType) {
 foreach($NativeMethod in $NativeType.GetMethods([Reflection.BindingFlags]'Public,Instance,Static,FlattenHierarchy') | Where-Object { -not $_.IsSpecialName -and ($_.DeclaringType -eq $NativeType -or $_.Name -match '^Save') }) {
  [pscustomobject]@{signature=$NativeMethod.ToString();parameters=@($NativeMethod.GetParameters() | ForEach-Object {[pscustomobject]@{name=$_.Name;type=$_.ParameterType.FullName}})}
 }
}
if($Type) {
 foreach($taskFile in Get-ChildItem -LiteralPath $Bin -Filter 'Flexem*.dll') {
  $taskCandidate=[Reflection.Assembly]::ReflectionOnlyLoadFrom($taskFile.FullName).GetType($Type,$false)
  if($taskCandidate) {
   [pscustomobject]@{type=$taskCandidate.FullName;assembly=$taskFile.Name;enum_values=$(if($taskCandidate.IsEnum){@($taskCandidate.GetFields([Reflection.BindingFlags]'Public,Static') | ForEach-Object {$_.Name})}else{$null});constructors=@($taskCandidate.GetConstructors() | ForEach-Object {$_.ToString()});factories=@($taskCandidate.GetMethods([Reflection.BindingFlags]'Public,Static,FlattenHierarchy') | Where-Object {$_.Name -match 'Create|Fetch'} | ForEach-Object {$_.ToString()});factory_details=@($taskCandidate.GetMethods([Reflection.BindingFlags]"Public,Static,FlattenHierarchy") | Where-Object {$_.Name -match "Create|Fetch"} | ForEach-Object {[pscustomobject]@{signature=$_.ToString();parameters=@($_.GetParameters() | ForEach-Object {[pscustomobject]@{name=$_.Name;type=$_.ParameterType.FullName}})}});creation_contracts=@($taskCandidate.GetMethods([Reflection.BindingFlags]"NonPublic,Instance,DeclaredOnly") | Where-Object {$_.Name -match "Child_Create|DataPortal_Create"} | ForEach-Object {$_.ToString()+" "+(($_.GetParameters() | ForEach-Object {$_.Name}) -join ",")});interfaces=@($taskCandidate.GetInterfaces() | ForEach-Object {$_.FullName});method_details=@(Get-MethodContracts $taskCandidate);properties=@($taskCandidate.GetProperties() | ForEach-Object {$_.Name+': '+$_.PropertyType.FullName});members=@($taskCandidate.GetProperties() | ForEach-Object {[pscustomobject]@{name=$_.Name;type=$_.PropertyType.FullName;readable=($null -ne $_.GetGetMethod());writable=($null -ne $_.GetSetMethod())}});methods=@($taskCandidate.GetMethods([Reflection.BindingFlags]'Public,Instance,DeclaredOnly') | Where-Object { -not $_.IsSpecialName } | ForEach-Object {$_.ToString()})} | ConvertTo-Json -Depth 6
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
