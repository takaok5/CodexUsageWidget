Option Explicit

Dim shell, fso, folder, launcher, startupFolder, shortcutPath, oldShortcutPath, shortcut
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

folder = fso.GetParentFolderName(WScript.ScriptFullName)
launcher = fso.BuildPath(folder, "Start Taskbar Delayed.vbs")
startupFolder = shell.SpecialFolders("Startup")
shortcutPath = fso.BuildPath(startupFolder, "ChatGPT Codex Usage Widget.lnk")
oldShortcutPath = fso.BuildPath(startupFolder, "Codex Taskbar Widget.lnk")

If Not fso.FileExists(launcher) Then
  MsgBox "Start Taskbar Delayed.vbs was not found.", 16, "ChatGPT Codex Usage Widget"
  WScript.Quit 2
End If

On Error Resume Next
If fso.FileExists(oldShortcutPath) Then fso.DeleteFile oldShortcutPath, True
Set shortcut = shell.CreateShortcut(shortcutPath)
shortcut.TargetPath = WScript.FullName
shortcut.Arguments = "//B //Nologo """ & launcher & """"
shortcut.WorkingDirectory = folder
shortcut.WindowStyle = 7
shortcut.Description = "Start ChatGPT Codex Usage Widget after Windows sign-in"
shortcut.Save

If Err.Number <> 0 Then
  MsgBox "Windows could not create the Startup shortcut." & vbCrLf & Err.Description, 16, "ChatGPT Codex Usage Widget"
  WScript.Quit 3
End If
On Error GoTo 0

On Error Resume Next
shell.RegDelete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexTaskbarWidget"
shell.RegDelete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ChatGPTCodexUsageWidget"
On Error GoTo 0

If fso.FileExists(shortcutPath) Then
  MsgBox "Automatic startup is installed." & vbCrLf & _
         "The widget will open about 15 seconds after you sign in to Windows.", _
         64, "ChatGPT Codex Usage Widget"
Else
  MsgBox "The Startup shortcut could not be verified.", 16, "ChatGPT Codex Usage Widget"
  WScript.Quit 4
End If
