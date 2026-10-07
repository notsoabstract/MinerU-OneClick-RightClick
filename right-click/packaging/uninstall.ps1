$ErrorActionPreference = 'Stop'
# All public uninstall entry points use this script. Confirm before touching
# package registration, certificates, or the installation record.
Add-Type -AssemblyName System.Windows.Forms
$taskMessage = '确定移除“用 MinerU 识别”右键入口吗？' + "`r`n`r`n" + '移除后，右键菜单将不再显示此工具。API 设置和已识别的文件会保留。'
$taskChoice = [System.Windows.Forms.MessageBox]::Show(
    $taskMessage,
    '移除 MinerU 右键菜单',
    [System.Windows.Forms.MessageBoxButtons]::YesNoCancel,
    [System.Windows.Forms.MessageBoxIcon]::Question,
    [System.Windows.Forms.MessageBoxDefaultButton]::Button2)
if ($taskChoice -ne [System.Windows.Forms.DialogResult]::Yes) { exit 2 }

$env:PSModulePath = (Join-Path $PSHOME 'Modules') + ';' + $env:PSModulePath
$taskInstalledDirectory = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'MinerURightClick\app'))
$taskRegistered = Join-Path $taskInstalledDirectory 'Unregister-Menu.ps1'
if (-not (Test-Path -LiteralPath $taskRegistered)) { $taskRegistered = Join-Path $PSScriptRoot 'Unregister-Menu.ps1' }
if (Test-Path -LiteralPath $taskRegistered) { & $taskRegistered }
if (Get-AppxPackage -Name 'Local.MinerURightClick') { throw '右键入口尚未移除，请关闭识别窗口后重试。' }
Write-Output '已移除 MinerU 右键入口，保留识别设置。'
