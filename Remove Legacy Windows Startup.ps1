$startup = [Environment]::GetFolderPath('Startup')
@('ChatGPT Codex Usage Widget.lnk', 'Codex Taskbar Widget.lnk') | ForEach-Object {
  $shortcut = Join-Path $startup $_
  if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'ChatGPTCodexUsageWidget' -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $runKey -Name 'CodexTaskbarWidget' -ErrorAction SilentlyContinue
Write-Host 'Legacy Windows startup entries for ChatGPT Codex Usage Widget were removed.'
