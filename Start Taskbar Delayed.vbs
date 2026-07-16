Option Explicit

Dim shell, fso, folder, exePath, logDir, logPath, logFile, result
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
folder = fso.GetParentFolderName(WScript.ScriptFullName)
exePath = fso.BuildPath(folder, "CodexTaskbarWidget.exe")
logDir = fso.BuildPath(shell.ExpandEnvironmentStrings("%APPDATA%"), "CodexUsageWidget")

On Error Resume Next
If Not fso.FolderExists(logDir) Then fso.CreateFolder(logDir)
logPath = fso.BuildPath(logDir, "startup-launcher.log")
Set logFile = fso.OpenTextFile(logPath, 8, True)
logFile.WriteLine Now & " Startup launcher invoked"
logFile.Close
On Error GoTo 0

' Let Windows Explorer and the taskbar finish loading before embedding the widget.
WScript.Sleep 15000

If fso.FileExists(exePath) Then
  result = shell.Run("""" & exePath & """", 0, False)
  On Error Resume Next
  Set logFile = fso.OpenTextFile(logPath, 8, True)
  logFile.WriteLine Now & " Widget launch requested, result=" & result
  logFile.Close
  On Error GoTo 0
End If
