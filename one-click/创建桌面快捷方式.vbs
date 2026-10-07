Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
appDir = fso.GetParentFolderName(WScript.ScriptFullName)
desktop = shell.SpecialFolders("Desktop")
shortcutPath = desktop & "\MinerU一键识别.lnk"
targetPath = appDir & "\启动MinerU一键识别-无黑窗.vbs"

Set shortcut = shell.CreateShortcut(shortcutPath)
shortcut.TargetPath = targetPath
shortcut.WorkingDirectory = appDir
shortcut.Description = "MinerU one-click recognition"
If fso.FileExists(appDir & "\node_modules\electron\dist\electron.exe") Then
  shortcut.IconLocation = appDir & "\node_modules\electron\dist\electron.exe,0"
End If
shortcut.Save

MsgBox "Desktop shortcut created: MinerU一键识别", 64, "Done"
