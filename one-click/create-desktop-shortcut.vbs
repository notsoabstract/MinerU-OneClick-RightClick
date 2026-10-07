Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
appDir = fso.GetParentFolderName(WScript.ScriptFullName)
desktop = shell.SpecialFolders("Desktop")
shortcutPath = desktop & "\MinerU-OneClick.lnk"
targetPath = appDir & "\start-silent.vbs"

Set shortcut = shell.CreateShortcut(shortcutPath)
shortcut.TargetPath = targetPath
shortcut.WorkingDirectory = appDir
shortcut.Description = "MinerU One Click"
If fso.FileExists(appDir & "\node_modules\electron\dist\electron.exe") Then
  shortcut.IconLocation = appDir & "\node_modules\electron\dist\electron.exe,0"
End If
shortcut.Save

MsgBox "Desktop shortcut created: MinerU-OneClick", 64, "Done"
