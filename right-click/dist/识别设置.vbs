Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
appDir = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%") & "\MinerURightClick\app"
If Not fso.FileExists(appDir & "\MinerURightClick.exe") Then appDir = fso.GetParentFolderName(WScript.ScriptFullName)
shell.Run Chr(34) & appDir & "\MinerURightClick.exe" & Chr(34) & " --settings", 1, False
