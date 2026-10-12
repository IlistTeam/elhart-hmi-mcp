param([string]$HostDll = (Join-Path $PSScriptRoot '..\artifacts\FStudioMcp.Visible.dll'))
$ErrorActionPreference = 'Stop'
$taskFramework = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'
$taskOutput = Join-Path $PSScriptRoot '..\artifacts\drawing-contract-tests.exe'
$taskSource = Join-Path $PSScriptRoot 'DrawingContractsHarness.cs'
# 测试只加载宿主类型与内存模型；不打开、关闭或修改任何用户工程。
& "$taskFramework\csc.exe" /nologo /platform:x86 /r:System.Core.dll "/out:$taskOutput" $taskSource
if ($LASTEXITCODE -ne 0) { throw 'Drawing contracts harness build failed' }
& $taskOutput ([IO.Path]::GetFullPath($HostDll)) (Join-Path $PSScriptRoot '..\projects')
if ($LASTEXITCODE -ne 0) { throw 'Drawing contracts harness failed' }
