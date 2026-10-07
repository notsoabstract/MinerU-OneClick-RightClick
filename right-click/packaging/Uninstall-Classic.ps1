$ErrorActionPreference = 'Stop'

# The key is fixed, so a wildcard cannot broaden the deletion scope.
# Application files, settings, packages, and certificates are untouched.
$verbSubKey = 'Software\Classes\*\shell\MinerURightClick'
[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($verbSubKey, $false)

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
Write-Output '已移除本工具的传统右键菜单。'
