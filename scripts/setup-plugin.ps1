param(
    [Parameter(Mandatory = $true)][string]$Workspace,
    [Parameter(Mandatory = $true)][string]$PythonPath,
    [string]$StudioRoot = 'C:\Program Files (x86)\KIP\ELHART HMI Soft 3.x'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Workspace)) { throw 'Workspace must be absolute.' }
$taskWorkspace = [IO.Path]::GetFullPath($Workspace).TrimEnd('\')
$taskPackage = [IO.Path]::GetFullPath($taskRoot).TrimEnd('\')
if ($taskWorkspace -eq $taskPackage -or $taskWorkspace.StartsWith($taskPackage + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose a workspace outside the plugin cache.'
}
if (-not [IO.Path]::IsPathRooted($PythonPath) -or -not (Test-Path -LiteralPath $PythonPath -PathType Leaf)) { throw 'Choose an absolute python.exe path.' }
& $PythonPath -c 'import sys; sys.exit(0 if sys.version_info >= (3,11) else 1)'
if ($LASTEXITCODE -ne 0) { throw 'Python 3.11+ is required.' }
if (-not $env:LOCALAPPDATA) { throw 'LOCALAPPDATA is required.' }
$taskStudio = (Resolve-Path -LiteralPath $StudioRoot).Path
$taskBin = Join-Path $taskStudio 'Bin'
if (-not (Test-Path -LiteralPath (Join-Path $taskStudio 'Device\HMI\Custom\119.xml'))) { throw 'ELHART OEM119 catalog is required.' }
New-Item -ItemType Directory -Path $taskWorkspace -Force | Out-Null
# These scripts build the bridge and local addin only; they do not launch the editor.
& (Join-Path $taskRoot 'build.ps1') -FStudioBin $taskBin
& (Join-Path $taskRoot 'install-visible-host.ps1') -FStudioRoot $taskStudio -Workspace $taskWorkspace -Profile elhart
if (Test-Path -LiteralPath (Join-Path $taskRoot 'artifacts\visible-addin\pending-update.json')) {
    throw 'Host DLL is in use. Save/close its editor, then rerun setup before reconnecting MCP.'
}
$taskConfigDirectory = Join-Path $env:LOCALAPPDATA 'IlistTeam\elhart-hmi'
New-Item -ItemType Directory -Path $taskConfigDirectory -Force | Out-Null
$taskSettings = @{ workspace = (Resolve-Path -LiteralPath $taskWorkspace).Path; python = (Resolve-Path -LiteralPath $PythonPath).Path; studio_bin = $taskBin }
$taskConfig = Join-Path $taskConfigDirectory 'config.json'
[IO.File]::WriteAllText($taskConfig, ($taskSettings | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
Write-Output "Configured $taskConfig. Reconnect MCP. No editor, simulator or panel download was started."
