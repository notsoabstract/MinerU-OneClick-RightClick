$ErrorActionPreference = 'Stop'
$env:PSModulePath = (Join-Path $PSHOME 'Modules') + ';' + $env:PSModulePath
$taskSource = [IO.Path]::GetFullPath($PSScriptRoot)
$taskTarget = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'MinerURightClick\app'))
$taskRequired = @('MinerURightClick.exe', 'MinerUContextMenu.dll', 'MinerURightClick.identity.msix', 'MinerURightClick.cer', 'package-identity.json', 'Register-Menu.ps1', 'Unregister-Menu.ps1', 'Machine-Certificate.ps1')
foreach ($taskFile in $taskRequired) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskSource $taskFile) -PathType Leaf)) { throw ('安装文件不完整：' + $taskFile) }
}
$taskAssetNames = @('StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png')
foreach ($taskAssetName in $taskAssetNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskSource ('Assets\' + $taskAssetName)) -PathType Leaf)) { throw ('安装文件不完整：Assets\' + $taskAssetName) }
}
if (-not $taskSource.Equals($taskTarget, [StringComparison]::OrdinalIgnoreCase)) {
    New-Item -ItemType Directory -Path $taskTarget -Force | Out-Null
    foreach ($taskItem in Get-ChildItem -LiteralPath $taskSource -File) {
        if ($taskItem.Extension -in @('.exe', '.dll', '.msix', '.cer', '.json', '.ps1', '.vbs', '.md')) {
            Copy-Item -LiteralPath $taskItem.FullName -Destination (Join-Path $taskTarget $taskItem.Name) -Force
        }
    }
    New-Item -ItemType Directory -Path (Join-Path $taskTarget 'Assets') -Force | Out-Null
    foreach ($taskAssetName in $taskAssetNames) {
        Copy-Item -LiteralPath (Join-Path $taskSource ('Assets\' + $taskAssetName)) -Destination (Join-Path $taskTarget ('Assets\' + $taskAssetName)) -Force
    }
}
& (Join-Path $taskTarget 'Register-Menu.ps1')
if (-not (Get-AppxPackage -Name 'Local.MinerURightClick')) { throw '系统没有确认右键菜单注册，请重新运行安装。' }
$taskShortcutDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\MinerU右键识别'
New-Item -ItemType Directory -Path $taskShortcutDirectory -Force | Out-Null
$taskShell = New-Object -ComObject WScript.Shell
foreach ($taskEntry in @(@('识别设置.lnk', '--settings'), @('移除右键菜单.lnk', '--uninstall'))) {
    $taskShortcut = $taskShell.CreateShortcut((Join-Path $taskShortcutDirectory $taskEntry[0]))
    $taskShortcut.TargetPath = Join-Path $taskTarget 'MinerURightClick.exe'
    $taskShortcut.Arguments = $taskEntry[1]
    $taskShortcut.WorkingDirectory = $taskTarget
    $taskShortcut.IconLocation = (Join-Path $taskTarget 'MinerURightClick.exe') + ',0'
    $taskShortcut.Save()
}
Write-Output '已添加当前账号的 MinerU 识别右键入口。'
