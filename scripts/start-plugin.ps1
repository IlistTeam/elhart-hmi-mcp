$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskConfig = $env:ILIST_ELHART_CONFIG
if (-not $taskConfig) {
    if (-not $env:LOCALAPPDATA) { throw 'LOCALAPPDATA is required.' }
    $taskConfig = Join-Path $env:LOCALAPPDATA 'IlistTeam\elhart-hmi\config.json'
}
if (-not (Test-Path -LiteralPath $taskConfig -PathType Leaf)) {
    throw 'ELHART plugin is not configured. Run scripts/setup-plugin.ps1 from the installed plugin; see docs/PLUGIN.md.'
}
$taskSettings = Get-Content -LiteralPath $taskConfig -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($taskProperty in @('python', 'workspace', 'studio_bin')) {
    $taskValue = $taskSettings.$taskProperty
    if (-not $taskValue -or -not [IO.Path]::IsPathRooted($taskValue) -or -not (Test-Path -LiteralPath $taskValue)) {
        throw "Configure an existing absolute $taskProperty path."
    }
}
$taskWorkspace = (Resolve-Path -LiteralPath $taskSettings.workspace).Path.TrimEnd('\')
$taskPackage = [IO.Path]::GetFullPath($taskRoot).TrimEnd('\')
if ($taskWorkspace -eq $taskPackage -or $taskWorkspace.StartsWith($taskPackage + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Workspace must be outside the plugin cache.'
}
foreach ($taskArtifact in @('artifacts\FStudioBridge.exe', 'artifacts\visible-addin\host-settings.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskRoot $taskArtifact))) {
        throw 'Native bridge is not prepared. Run scripts/setup-plugin.ps1 for this installed version.'
    }
}
$taskHost = Get-Content -LiteralPath (Join-Path $taskRoot 'artifacts\visible-addin\host-settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ([IO.Path]::GetFullPath($taskHost.workspace).TrimEnd('\') -ne $taskWorkspace -or
    [IO.Path]::GetFullPath($taskHost.bin).TrimEnd('\') -ne [IO.Path]::GetFullPath($taskSettings.studio_bin).TrimEnd('\') -or
    $taskHost.profile -ne 'elhart') { throw 'Server and native host settings differ. Repeat setup for the same workspace and installation.' }
$env:PYTHONUTF8 = '1'
& $taskSettings.python -u (Join-Path $taskRoot 'server.py') --workspace $taskWorkspace --profile elhart --studio-bin $taskSettings.studio_bin
exit $LASTEXITCODE
