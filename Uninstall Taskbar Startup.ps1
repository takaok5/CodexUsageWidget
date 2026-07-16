$startup = [Environment]::GetFolderPath('Startup')
@('ChatGPT Codex Usage Widget.lnk', 'Codex Taskbar Widget.lnk') | ForEach-Object {
  $shortcut = Join-Path $startup $_
  if (Test-Path $shortcut) { Remove-Item -LiteralPath $shortcut }
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'ChatGPTCodexUsageWidget' -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $runKey -Name 'CodexTaskbarWidget' -ErrorAction SilentlyContinue
Write-Host 'ChatGPT Codex Usage Widget automatic startup removed.'
