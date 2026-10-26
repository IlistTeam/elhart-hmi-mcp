param([Parameter(Mandatory=$true)][string]$ProjectDirectory,
      [Parameter(Mandatory=$true)][string]$OwnedRoot,
      [Parameter(Mandatory=$true)][string]$VendorBin)
$ErrorActionPreference='Stop'
if([Environment]::Is64BitProcess){throw 'Native packaging requires x86 Windows PowerShell'}
$target=[IO.Path]::GetFullPath($ProjectDirectory).TrimEnd('\')
$owner=[IO.Path]::GetFullPath($OwnedRoot).TrimEnd('\')
if(-not $target.StartsWith($owner+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Expected a project inside the owned snapshot'}
if(-not (Test-Path -LiteralPath (Join-Path $target 'Bin\fshmi.bin'))){throw 'Compiled project missing'}
$taskBin=[IO.Path]::GetFullPath($VendorBin)
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ElhartPackPaths { [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] public static extern bool SetDllDirectory(string path); }'
if(-not [ElhartPackPaths]::SetDllDirectory($taskBin)){throw 'Cannot set process-local vendor DLL path'}
[AppDomain]::CurrentDomain.add_AssemblyResolve({param($s,$e) $p=Join-Path $taskBin ((New-Object Reflection.AssemblyName($e.Name)).Name+'.dll');if(Test-Path -LiteralPath $p){return [Reflection.Assembly]::LoadFrom($p)};return $null})
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $taskBin 'Flexem.Studio.Build.dll'))
$core=[Reflection.Assembly]::LoadFrom((Join-Path $taskBin 'Flexem.Core.dll'))
$directoryType=$core.GetType('Flexem.DirectoryName',$true)
$directory=[Activator]::CreateInstance($directoryType,[object[]]@($target))
# This is the installed editor's own packaging wrapper. It obtains its native
# pack password internally; no password/key is supplied, exported or persisted.
$type=$assembly.GetType('iJggZe7XNeRX9g53BK.wlgIQCka66eEdi6HGP',$true)
$method=$type.GetMethod('BWeVVHUpjb',[Reflection.BindingFlags]'Public,NonPublic,Static')
if(-not $method){throw 'Installed packaging contract missing'}
$method.Invoke($null,[object[]]@($directory)) | Out-Null
[pscustomobject]@{packaging_returned=$true;info_exists=(Test-Path -LiteralPath (Join-Path $target 'Bin\info.dat'))} | ConvertTo-Json -Compress
