param(
    [string]$ApplicationDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MinerURightClick\app')
)
$ErrorActionPreference = 'Stop'

# This optional installer registers only the current user's classic menu verb.
# It does not install certificates, packages, or change Explorer preferences.
$ApplicationDirectory = [IO.Path]::GetFullPath($ApplicationDirectory)
$executablePath = Join-Path $ApplicationDirectory 'MinerURightClick.exe'
if (!(Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Cannot find the installed application: $executablePath"
}
$executablePath = (Get-Item -LiteralPath $executablePath).FullName
if ($executablePath.Contains('"')) { throw 'The application path contains an invalid quotation mark.' }

$extensions = @('.pdf', '.doc', '.docx', '.ppt', '.pptx', '.xls', '.xlsx', '.png', '.jpg', '.jpeg', '.jp2', '.webp', '.gif', '.bmp')
$appliesTo = '(' + (($extensions | ForEach-Object { 'System.FileExtension:="' + $_ + '"' }) -join ' OR ') + ')'
$verbSubKey = 'Software\Classes\*\shell\MinerURightClick'
$verbKey = $null
$commandKey = $null
try {
    $verbKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($verbSubKey)
    $verbKey.SetValue('', '用 MinerU 识别', [Microsoft.Win32.RegistryValueKind]::String)
    $verbKey.SetValue('MUIVerb', '用 MinerU 识别', [Microsoft.Win32.RegistryValueKind]::String)
    $verbKey.SetValue('Icon', ('"' + $executablePath + '",0'), [Microsoft.Win32.RegistryValueKind]::String)
    $verbKey.SetValue('MultiSelectModel', 'Single', [Microsoft.Win32.RegistryValueKind]::String)
    $verbKey.SetValue('AppliesTo', $appliesTo, [Microsoft.Win32.RegistryValueKind]::String)
    $commandKey = $verbKey.CreateSubKey('command')
    # Explorer starts the executable directly; no cmd, PowerShell, or script intermediary.
    $commandKey.SetValue('', ('"' + $executablePath + '" "%1"'), [Microsoft.Win32.RegistryValueKind]::String)
}
finally {
    if ($commandKey) { $commandKey.Dispose() }
    if ($verbKey) { $verbKey.Dispose() }
}

if (!('MinerURightClickClassic.NativeMethods' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace MinerURightClickClassic {
    public static class NativeMethods {
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
'@
}
[MinerURightClickClassic.NativeMethods]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
Write-Output '已添加右键菜单：右键文件 → 显示更多选项 → 用 MinerU 识别。'
