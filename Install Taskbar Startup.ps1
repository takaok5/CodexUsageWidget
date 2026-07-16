$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'CodexTaskbarWidget.exe'
$delayedLauncher = Join-Path $PSScriptRoot 'Start Taskbar Delayed.vbs'

if (-not (Test-Path $exe)) {
  throw 'CodexTaskbarWidget.exe was not found. Extract the complete release ZIP first.'
}

$startupCommand = if (Test-Path $delayedLauncher) {
  'wscript.exe //B //Nologo "{0}"' -f $delayedLauncher
} else {
  '"{0}"' -f $exe
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
Remove-ItemProperty -Path $runKey -Name 'CodexTaskbarWidget' -ErrorAction SilentlyContinue
New-ItemProperty -Path $runKey -Name 'ChatGPTCodexUsageWidget' -PropertyType String -Value $startupCommand -Force | Out-Null
Write-Host 'Installed. ChatGPT Codex Usage Widget will start 15 seconds after you sign in to Windows.'
