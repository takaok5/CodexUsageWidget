$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'CodexTaskbarWidget.exe'
$source = if (Test-Path $exe) { $exe } else { Join-Path $PSScriptRoot 'Start Taskbar Mode.vbs' }
$delayedLauncher = Join-Path $PSScriptRoot 'Start Taskbar Delayed.vbs'
$startupCommand = if (Test-Path $delayedLauncher) {
  'wscript.exe //B //Nologo "{0}"' -f $delayedLauncher
} else {
  '"{0}"' -f $source
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
New-ItemProperty -Path $runKey -Name 'CodexTaskbarWidget' -PropertyType String -Value $startupCommand -Force | Out-Null
Write-Host 'Installed. Embedded taskbar mode will start 15 seconds after you sign in to Windows.'
