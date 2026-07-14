Option Explicit

Dim shell, fso, folder, launcher, startupFolder, shortcutPath, shortcut
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

folder = fso.GetParentFolderName(WScript.ScriptFullName)
launcher = fso.BuildPath(folder, "Start Taskbar Delayed.vbs")
startupFolder = shell.SpecialFolders("Startup")
shortcutPath = fso.BuildPath(startupFolder, "Codex Taskbar Widget.lnk")

If Not fso.FileExists(launcher) Then
  MsgBox "Start Taskbar Delayed.vbs was not found.", 16, "Codex Taskbar Widget"
  WScript.Quit 2
End If

On Error Resume Next
Set shortcut = shell.CreateShortcut(shortcutPath)
shortcut.TargetPath = WScript.FullName
shortcut.Arguments = "//B //Nologo """ & launcher & """"
shortcut.WorkingDirectory = folder
shortcut.WindowStyle = 7
shortcut.Description = "Start Codex Embedded Taskbar Widget after Windows sign-in"
shortcut.Save

If Err.Number <> 0 Then
  MsgBox "Windows could not create the Startup shortcut." & vbCrLf & Err.Description, 16, "Codex Taskbar Widget"
  WScript.Quit 3
End If
On Error GoTo 0

' Remove the older registry startup method so only one launcher is used.
On Error Resume Next
shell.RegDelete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexTaskbarWidget"
On Error GoTo 0

If fso.FileExists(shortcutPath) Then
  MsgBox "Automatic startup is installed." & vbCrLf & _
         "The widget will open about 15 seconds after you sign in to Windows.", _
         64, "Codex Taskbar Widget"
Else
  MsgBox "The Startup shortcut could not be verified.", 16, "Codex Taskbar Widget"
  WScript.Quit 4
End If
