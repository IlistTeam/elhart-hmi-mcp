param(
 [string]$FStudioRoot='C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x',
 [string]$Workspace,
 [ValidateSet('auto','elhart','fstudio')][string]$Profile='auto'
)
$ErrorActionPreference='Stop'
$taskBin=Join-Path $FStudioRoot 'Bin'
$taskDestination=Join-Path $PSScriptRoot 'artifacts\visible-addin'
$taskFramework='C:\Windows\Microsoft.NET\Framework\v4.0.30319'
New-Item -ItemType Directory -Force -Path $taskDestination | Out-Null
Push-Location $PSScriptRoot
try {
 & "$taskFramework\csc.exe" /nologo /target:library /platform:x86 /out:artifacts\FStudioMcp.Visible.dll "/r:$taskBin\ICSharpCode.Core.dll" /r:System.Core.dll /r:System.Xaml.dll /r:System.Web.Extensions.dll "/r:$taskFramework\WPF\WindowsBase.dll" "/r:$taskFramework\WPF\PresentationCore.dll" "/r:$taskFramework\WPF\PresentationFramework.dll" src\VisibleHost.cs src\LiveModels.cs src\ProjectCreation.cs src\NativeDrawing.cs src\BatchDrawing.cs src\BackgroundBuild.cs src\BuildLogCapture.cs src\NativePages.cs src\Simulation.cs src\NativeIndicators.cs
 if($LASTEXITCODE -ne 0){throw 'Visible host build failed'}
 $taskLoadedDestination=Join-Path $taskDestination 'FStudioMcp.Visible.dll'
 try {
  Copy-Item -LiteralPath 'artifacts\FStudioMcp.Visible.dll' -Destination $taskLoadedDestination -Force -ErrorAction Stop
  Remove-Item -LiteralPath (Join-Path $taskDestination 'FStudioMcp.Visible.next.dll') -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath (Join-Path $taskDestination 'pending-update.json') -Force -ErrorAction SilentlyContinue
 } catch [System.IO.IOException] {
  $taskPending=Join-Path $taskDestination 'FStudioMcp.Visible.next.dll'
  Copy-Item -LiteralPath 'artifacts\FStudioMcp.Visible.dll' -Destination $taskPending -Force
  @{status='pending_host_restart';source=(Join-Path $PSScriptRoot 'artifacts\FStudioMcp.Visible.dll');destination=$taskLoadedDestination;staged=$taskPending;sha256=(Get-FileHash -LiteralPath $taskPending -Algorithm SHA256).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskDestination 'pending-update.json') -Encoding utf8
 }
 # 更新 DLL 时保留现有工作区和端点目录，避免升级后误连默认工程目录。
 $taskSettingsPath=Join-Path $taskDestination 'host-settings.json'
 $taskSettings = if(Test-Path -LiteralPath $taskSettingsPath) { Get-Content -LiteralPath $taskSettingsPath -Raw -Encoding utf8 | ConvertFrom-Json } else { $null }
 $taskWorkspace = if($Workspace) { [IO.Path]::GetFullPath($Workspace) } elseif($taskSettings) { $taskSettings.workspace } else { Join-Path $PSScriptRoot 'projects' }
 $taskState = if($taskSettings) { $taskSettings.state_directory } else { Join-Path $PSScriptRoot 'artifacts\visible-host' }
 $taskIsElhart = Test-Path -LiteralPath (Join-Path $FStudioRoot 'Device\HMI\Custom\119.xml')
 if($Profile -eq 'elhart' -and -not $taskIsElhart) { throw 'Elhart OEM catalog is missing' }
 $taskProfile = if($Profile -eq 'auto') { if($taskIsElhart) {'elhart'} else {'fstudio'} } else { $Profile }
 $taskLog = if($taskProfile -eq 'elhart') { Join-Path $env:APPDATA 'KIP\ELHART HMI Soft3\Logs\Log.txt' } else { Join-Path $env:APPDATA 'Flexem\FStudio3\Logs\Log.txt' }
 @{workspace=$taskWorkspace;state_directory=$taskState;bin=[IO.Path]::GetFullPath($taskBin);profile=$taskProfile;log_file=$taskLog} | ConvertTo-Json | Set-Content -LiteralPath $taskSettingsPath -Encoding utf8
 @'
<AddIn name="FStudio MCP Visible Host" author="Local MCP" description="Local visible MCP bridge">
 <Runtime><Import assembly="FStudioMcp.Visible.dll" /></Runtime>
 <Path name="/Workspace/AutostartAfterWorkbenchInitialized"><Class id="FStudioMcpStart" class="FStudioMcp.StartHost" /></Path>
</AddIn>
'@ | Set-Content -LiteralPath (Join-Path $taskDestination 'FStudioMcp.addin') -Encoding utf8
}finally{Pop-Location}
Write-Output "Installed visible host at $taskDestination; load on next FStudio start."
