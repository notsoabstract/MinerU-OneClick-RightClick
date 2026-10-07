Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
appDir = fso.GetParentFolderName(WScript.ScriptFullName)
shell.Run Chr(34) & appDir & "\MinerURightClick.exe" & Chr(34) & " --uninstall", 1, False
