param()
$ErrorActionPreference='Stop'
$taskRepository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskOutput=Join-Path $taskRepository ('artifacts\capture-api-probe\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskWindows=[Environment]::GetFolderPath('Windows')
$taskFramework=Join-Path $taskWindows 'Microsoft.NET\Framework64\v4.0.30319'
$taskReferences=@(
    'System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll',
    (Join-Path $taskFramework 'System.Runtime.WindowsRuntime.dll'),
    (Join-Path $taskFramework 'System.Runtime.InteropServices.WindowsRuntime.dll'),
    (Join-Path $taskWindows 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll'),
    (Join-Path $taskWindows 'System32\WinMetadata\Windows.Foundation.winmd'),
    (Join-Path $taskWindows 'System32\WinMetadata\Windows.Graphics.winmd')
)
$taskExecutable=Join-Path $taskOutput 'CaptureApiProbe.exe'
$taskResponse=Join-Path $taskOutput 'compile.rsp'
$taskLines=@('/nologo','/platform:x64',('/out:"'+$taskExecutable+'"'))
$taskLines+=@($taskReferences | ForEach-Object {'/r:"'+$_+'"'})
$taskLines+=('"'+(Join-Path $PSScriptRoot 'WindowsCaptureApiProbe.cs')+'"')
$taskLines | Set-Content -LiteralPath $taskResponse -Encoding UTF8
& (Join-Path $taskFramework 'csc.exe') ('@'+$taskResponse)
if($LASTEXITCODE -ne 0){throw 'Capture diagnostic compilation failed'}
# Native program closes its own generated form in finally. It never reads
# frame pixels, saves a screenshot, or accepts a user-app window handle.
$taskResult=& $taskExecutable
$taskExitCode=$LASTEXITCODE
$taskResult | Set-Content -LiteralPath (Join-Path $taskOutput 'result.json') -Encoding UTF8
$taskResult
if($taskExitCode -ne 0){throw 'Capture API diagnostic failed; inspect result.json'}
