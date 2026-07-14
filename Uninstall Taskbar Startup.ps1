$shortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Taskbar Widget.lnk'
if (Test-Path $shortcut) { Remove-Item -LiteralPath $shortcut }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'CodexTaskbarWidget' -ErrorAction SilentlyContinue
Write-Host 'Codex taskbar automatic startup removed.'
