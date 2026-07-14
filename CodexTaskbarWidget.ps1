[CmdletBinding()]
param()

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Drawing, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CodexWidgetNative {
  [StructLayout(LayoutKind.Sequential)]
  public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll", SetLastError=true)]
  public static extern bool SetWindowPos(
    IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint flags);
  [DllImport("user32.dll")]
  public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Auto)]
  public static extern IntPtr FindWindow(string className, string windowName);
  [DllImport("user32.dll", SetLastError=true)]
  public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
  [DllImport("user32.dll")]
  public static extern IntPtr GetParent(IntPtr hWnd);
  [DllImport("user32.dll", EntryPoint="GetWindowLongPtr")]
  public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
  [DllImport("user32.dll", EntryPoint="SetWindowLongPtr", SetLastError=true)]
  public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
  [DllImport("user32.dll", SetLastError=true)]
  public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
'@

$created = $false
$mutex = [Threading.Mutex]::new($true, 'CodexUsageTaskbarWidget', [ref]$created)
if (-not $created) { return }

$appDir = if ($MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path } else { $PWD.Path }
$stateFile = Join-Path $appDir 'taskbar-state.json'
$logFile = Join-Path $appDir 'widget-errors.log'
$script:lastLogMessage = ''

[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="235" Height="48" WindowStyle="None" AllowsTransparency="True"
        Background="Transparent" ResizeMode="NoResize" ShowInTaskbar="False"
        Topmost="False" Focusable="False" ShowActivated="False">
  <Border x:Name="Root" Padding="7,2,7,2" Cursor="SizeWE" CornerRadius="7" BorderThickness="1">
    <Border.Style>
      <Style TargetType="Border">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="BorderBrush" Value="Transparent"/>
        <Style.Triggers>
          <Trigger Property="IsMouseOver" Value="True">
            <Setter Property="Background" Value="#20FFFFFF"/>
            <Setter Property="BorderBrush" Value="#38FFFFFF"/>
          </Trigger>
        </Style.Triggers>
      </Style>
    </Border.Style>
    <Border.ContextMenu>
      <ContextMenu>
        <MenuItem Header="Move to left edge"/>
        <MenuItem Header="Place beside weather"/>
        <MenuItem Header="Move to right edge"/>
        <MenuItem Header="Move to next display"/>
        <MenuItem Header="Refresh usage"/>
        <MenuItem Header="Lock position" IsCheckable="True"/>
        <MenuItem Header="Animate icon" IsCheckable="True"/>
        <MenuItem Header="Animation speed">
          <MenuItem Header="Slow" IsCheckable="True"/>
          <MenuItem Header="Normal" IsCheckable="True"/>
          <MenuItem Header="Fast" IsCheckable="True"/>
        </MenuItem>
        <Separator/>
        <MenuItem x:Name="ExitMenu" Header="Exit Codex taskbar widget"/>
      </ContextMenu>
    </Border.ContextMenu>
    <Grid>
      <Grid.ColumnDefinitions><ColumnDefinition Width="40"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
      <Grid x:Name="IconHost" Width="27" Height="27" Margin="2,0,0,0" HorizontalAlignment="Left" VerticalAlignment="Center" Opacity="1">
        <Border Background="#FFF2F2F2" CornerRadius="6">
          <TextBlock Text="C" Foreground="#FF202020" FontFamily="Segoe UI Variable Display, Segoe UI" FontWeight="Bold" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>
        <Image x:Name="CodexIcon" Width="27" Height="27" Stretch="Uniform"/>
      </Grid>
      <Grid Grid.Column="1" VerticalAlignment="Center">
        <Grid.RowDefinitions><RowDefinition Height="21"/><RowDefinition Height="21"/></Grid.RowDefinitions>
        <Grid Grid.Row="0">
          <Grid.ColumnDefinitions><ColumnDefinition Width="52"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
          <TextBlock Text="Codex" Foreground="#FFF3F3F3" FontFamily="Segoe UI Variable Text, Segoe UI" FontWeight="SemiBold" FontSize="14" HorizontalAlignment="Left" VerticalAlignment="Center"/>
          <TextBlock x:Name="TimeText" Grid.Column="1" Text="Reset date unavailable" Foreground="#FFC7C7C7" FontFamily="Segoe UI Variable Text, Segoe UI" FontSize="10" HorizontalAlignment="Left" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
        </Grid>
        <Grid Grid.Row="1">
          <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="110"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <TextBlock Text="7D" Margin="0,0,5,0" Foreground="#FFC7C7C7" FontFamily="Segoe UI Variable Text, Segoe UI" FontWeight="SemiBold" FontSize="10" VerticalAlignment="Center"/>
          <Border Grid.Column="1" Width="110" Height="6" Background="#556A6A6A" CornerRadius="3" HorizontalAlignment="Left" VerticalAlignment="Center">
            <Border x:Name="RemainingBar" Width="0" Height="6" Background="#FF70D88B" CornerRadius="3" HorizontalAlignment="Left"/>
          </Border>
          <TextBlock x:Name="PercentText" Grid.Column="2" Text="--%" Margin="6,0,0,0" Foreground="#FFF3F3F3" FontFamily="Segoe UI Variable Text, Segoe UI" FontWeight="SemiBold" FontSize="11" VerticalAlignment="Center"/>
        </Grid>
      </Grid>
    </Grid>
  </Border>
</Window>
'@

$reader = [System.Xml.XmlNodeReader]::new($xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)
$root = $window.FindName('Root')
$iconHost = $window.FindName('IconHost')
$codexIcon = $window.FindName('CodexIcon')
$percentText = $window.FindName('PercentText')
$timeText = $window.FindName('TimeText')
$remainingBar = $window.FindName('RemainingBar')
$leftMenu = $root.ContextMenu.Items[0]
$weatherMenu = $root.ContextMenu.Items[1]
$rightMenu = $root.ContextMenu.Items[2]
$nextDisplayMenu = $root.ContextMenu.Items[3]
$refreshMenu = $root.ContextMenu.Items[4]
$lockMenu = $root.ContextMenu.Items[5]
$animateMenu = $root.ContextMenu.Items[6]
$speedMenu = $root.ContextMenu.Items[7]
$slowMenu = $speedMenu.Items[0]
$normalMenu = $speedMenu.Items[1]
$fastMenu = $speedMenu.Items[2]
$exitMenu = $root.ContextMenu.Items[9]
$script:widgetHandle = [IntPtr]::Zero
$script:workBottom = 0
$script:positionLocked = $false
$script:lastForeground = [IntPtr]::Zero
$script:taskbarHandle = [IntPtr]::Zero
$script:screenIndex = 0
$script:screenLeft = 0
$script:screenRight = [System.Windows.SystemParameters]::PrimaryScreenWidth
$script:isDragging = $false
$script:hiddenForAutoHide = $false
$script:animateIcon = $true
$script:animationSpeed = 'Normal'
$script:activeAnimationKey = ''
$script:isEmbedded = $false
$script:widgetLeft = 205.0
$script:dipScale = 1.0
$script:dragStartX = 0
$script:dragStartLeft = 0.0

function Save-TaskbarPosition {
  try {
    @{ Left=$script:widgetLeft; Locked=$script:positionLocked; Animate=$script:animateIcon; AnimationSpeed=$script:animationSpeed; ScreenIndex=$script:screenIndex } |
      ConvertTo-Json | Set-Content $stateFile -Encoding UTF8
  } catch { }
}

function Write-WidgetLog([string]$message) {
  if (-not $message -or $message -eq $script:lastLogMessage) { return }
  $script:lastLogMessage = $message
  try {
    if (Test-Path $logFile -and (Get-Item $logFile).Length -gt 262144) {
      Move-Item -LiteralPath $logFile -Destination ($logFile + '.old') -Force
    }
    [IO.File]::AppendAllText($logFile, ('{0:u} {1}{2}' -f (Get-Date),$message,[Environment]::NewLine))
  } catch { }
}

function Update-AnimationMenu {
  $animateMenu.IsChecked = $script:animateIcon
  $slowMenu.IsChecked = $script:animationSpeed -eq 'Slow'
  $normalMenu.IsChecked = $script:animationSpeed -eq 'Normal'
  $fastMenu.IsChecked = $script:animationSpeed -eq 'Fast'
}

function Set-IconAnimation {
  $allowMotion = [System.Windows.SystemParameters]::ClientAreaAnimation
  try {
    $power = [Windows.Forms.SystemInformation]::PowerStatus
    if ($power.PowerLineStatus -eq [Windows.Forms.PowerLineStatus]::Offline -and $power.BatteryLifePercent -ge 0 -and $power.BatteryLifePercent -le 0.20) {
      $allowMotion = $false
    }
  } catch { }
  $enabled = $script:animateIcon -and $allowMotion
  $key = if ($enabled) { $script:animationSpeed } else { 'Off' }
  if ($key -eq $script:activeAnimationKey) { return }
  $iconHost.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $null)
  $iconHost.Opacity = 1
  if ($enabled) {
    $seconds = switch ($script:animationSpeed) { 'Slow' { 2.4 } 'Fast' { 0.9 } default { 1.6 } }
    $animation = [Windows.Media.Animation.DoubleAnimation]::new(1.0, 0.72, [Windows.Duration]::new([TimeSpan]::FromSeconds($seconds)))
    $animation.AutoReverse = $true
    $animation.RepeatBehavior = [Windows.Media.Animation.RepeatBehavior]::Forever
    $ease = [Windows.Media.Animation.SineEase]::new()
    $ease.EasingMode = [Windows.Media.Animation.EasingMode]::EaseInOut
    $animation.EasingFunction = $ease
    $iconHost.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $animation)
  }
  $script:activeAnimationKey = $key
  Update-AnimationMenu
}

function Set-TaskbarPosition([double]$left) {
  $minLeft = $script:screenLeft
  $maxLeft = [math]::Max($minLeft, $script:screenRight - $window.Width)
  $script:widgetLeft = [math]::Max($minLeft, [math]::Min($maxLeft, $left))
  if ($script:isEmbedded) {
    Position-EmbeddedWidget
  } else {
    $window.Left = $script:widgetLeft
    $window.Top = $script:workBottom
  }
  Save-TaskbarPosition
}

function Position-EmbeddedWidget {
  if (-not $script:isEmbedded -or $script:widgetHandle -eq [IntPtr]::Zero -or $script:taskbarHandle -eq [IntPtr]::Zero) { return }
  $rect = New-Object 'CodexWidgetNative+RECT'
  if (-not [CodexWidgetNative]::GetWindowRect($script:taskbarHandle, [ref]$rect)) { return }
  $scale = if ($script:dipScale -gt 0) { $script:dipScale } else { 1 }
  $screenLeftPx = [int][math]::Round($script:widgetLeft / $scale)
  $childX = $screenLeftPx - $rect.Left
  $childWidth = [int][math]::Round($window.Width / $scale)
  $childHeight = [math]::Max(1, $rect.Bottom - $rect.Top)
  [CodexWidgetNative]::SetWindowPos(
    $script:widgetHandle, [IntPtr]::Zero, $childX, 0, $childWidth, $childHeight, 0x0030) | Out-Null
}

function Sync-DisplayGeometry {
  if ($script:isDragging) { return }
  $screens = @([Windows.Forms.Screen]::AllScreens)
  if ($screens.Count -eq 0) { return }
  if ($script:screenIndex -lt 0 -or $script:screenIndex -ge $screens.Count) { $script:screenIndex = 0 }
  $primaryPixels = [Windows.Forms.Screen]::PrimaryScreen.Bounds.Width
  $dipScale = if ($primaryPixels -gt 0) { [System.Windows.SystemParameters]::PrimaryScreenWidth / $primaryPixels } else { 1 }
  $script:dipScale = $dipScale
  $screen = $screens[$script:screenIndex]
  $bounds = $screen.Bounds
  $work = $screen.WorkingArea
  $script:screenLeft = $bounds.Left * $dipScale
  $script:screenRight = $bounds.Right * $dipScale
  if ($script:isEmbedded) {
    $script:widgetLeft = [math]::Max($script:screenLeft, [math]::Min($script:screenRight - $window.Width, $script:widgetLeft))
    Position-EmbeddedWidget
    return
  }
  $bottomGap = ($bounds.Bottom - $work.Bottom) * $dipScale
  $topGap = ($work.Top - $bounds.Top) * $dipScale
  if ($bottomGap -ge 20) {
    if ($script:hiddenForAutoHide) { $window.Show(); $script:hiddenForAutoHide=$false }
    $script:workBottom = $work.Bottom * $dipScale
    $targetHeight = $bottomGap
  } elseif ($topGap -ge 20) {
    if ($script:hiddenForAutoHide) { $window.Show(); $script:hiddenForAutoHide=$false }
    $script:workBottom = $bounds.Top * $dipScale
    $targetHeight = $topGap
  } else {
    # Auto-hidden taskbar: appear only while the pointer or active taskbar is near the edge.
    $nearEdge = [Windows.Forms.Cursor]::Position.Y -ge ($bounds.Bottom - 64)
    $taskbarActive = [CodexWidgetNative]::GetForegroundWindow() -eq $script:taskbarHandle
    if (-not $nearEdge -and -not $taskbarActive) { if ($window.IsVisible) { $window.Hide(); $script:hiddenForAutoHide=$true }; return }
    if ($script:hiddenForAutoHide) { $window.Show(); $script:hiddenForAutoHide=$false }
    $targetHeight = 48
    $script:workBottom = ($bounds.Bottom * $dipScale) - $targetHeight
  }
  if ([math]::Abs($window.Height - $targetHeight) -gt 0.25) { $window.Height = $targetHeight }
  if ([math]::Abs($window.Top - $script:workBottom) -gt 0.25) { $window.Top = $script:workBottom }
  $maxLeft = [math]::Max($script:screenLeft, $script:screenRight - $window.Width)
  $targetLeft = [math]::Max($script:screenLeft, [math]::Min($maxLeft, $script:widgetLeft))
  if ([math]::Abs($window.Left - $targetLeft) -gt 0.25) { $window.Left = $targetLeft }
}

function Attach-WidgetToTaskbar {
  if ($script:widgetHandle -eq [IntPtr]::Zero) { return }
  $taskbar = [CodexWidgetNative]::FindWindow('Shell_TrayWnd', $null)
  if ($taskbar -eq [IntPtr]::Zero) { return }
  $style = [uint32]([CodexWidgetNative]::GetWindowLongPtr($script:widgetHandle, -16).ToInt64() -band 0xFFFFFFFFL)
  $childStyle = [uint32](($style -bor [uint32]0x40000000) -band [uint32]0x7FFFFFFF)
  [CodexWidgetNative]::SetWindowLongPtr($script:widgetHandle, -16, [IntPtr]([long]$childStyle)) | Out-Null
  [CodexWidgetNative]::SetParent($script:widgetHandle, $taskbar) | Out-Null
  $script:taskbarHandle = $taskbar
  $script:isEmbedded = [CodexWidgetNative]::GetParent($script:widgetHandle) -eq $taskbar
  if ($script:isEmbedded) {
    $window.Topmost = $false
    Position-EmbeddedWidget
  } else {
    Write-WidgetLog 'Could not embed widget into the Windows taskbar'
    $window.Topmost = $true
  }
}

function Set-CodexAppIcon {
  try {
    $exe = Get-Process ChatGPT -ErrorAction SilentlyContinue | Where-Object Path | Select-Object -First 1 -ExpandProperty Path
    if (-not $exe) {
      $package = Get-AppxPackage -Name 'OpenAI.Codex' -ErrorAction SilentlyContinue
      if ($package) { $exe = Join-Path $package.InstallLocation 'app\ChatGPT.exe' }
    }
    if (-not $exe -or -not (Test-Path $exe)) { return }
    $icon = [Drawing.Icon]::ExtractAssociatedIcon($exe)
    if (-not $icon) { return }
    $codexIcon.Source = [System.Windows.Interop.Imaging]::CreateBitmapSourceFromHIcon(
      $icon.Handle, [Windows.Int32Rect]::Empty,
      [Windows.Media.Imaging.BitmapSizeOptions]::FromEmptyOptions())
    $icon.Dispose()
  } catch { }
}

function Get-LatestSnapshot {
  $sessionRoot = Join-Path $HOME '.codex\sessions'
  $files = Get-ChildItem $sessionRoot -Recurse -Filter '*.jsonl' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 6
  foreach ($file in $files) {
    $stream = $null
    $reader = $null
    try {
      $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
      $tailBytes = [math]::Min($stream.Length, 2MB)
      $stream.Seek(-$tailBytes, [IO.SeekOrigin]::End) | Out-Null
      $reader = [IO.StreamReader]::new($stream)
      $text = $reader.ReadToEnd()
      $lines = $text -split "`r?`n"
      for ($i=$lines.Count-1; $i -ge 0; $i--) {
        if ($lines[$i].IndexOf('"type":"token_count"') -lt 0) { continue }
        try {
          $j = $lines[$i] | ConvertFrom-Json -ErrorAction Stop
          if ($j.payload.type -eq 'token_count' -and $j.payload.rate_limits) {
            $limits = @($j.payload.rate_limits.primary, $j.payload.rate_limits.secondary) | Where-Object { $_ }
            $long = $limits | Where-Object window_minutes -ge 1440 | Sort-Object window_minutes -Descending | Select-Object -First 1
            if (-not $long) { $long = $limits | Sort-Object window_minutes -Descending | Select-Object -First 1 }
            $short = $limits | Where-Object window_minutes -lt 1440 | Sort-Object window_minutes | Select-Object -First 1
            $eventTime = if ($j.timestamp) { [DateTimeOffset]::Parse($j.timestamp).ToLocalTime() } else { [DateTimeOffset]$file.LastWriteTime }
            return [pscustomobject]@{ Long=$long; Short=$short; EventTime=$eventTime }
          }
        } catch { }
      }
    } catch { }
    finally {
      if ($reader) { $reader.Dispose() }
      elseif ($stream) { $stream.Dispose() }
    }
  }
  return $null
}

function Refresh-Usage {
  $snapshot = Get-LatestSnapshot
  $limit = $snapshot.Long
  if (-not $limit) {
    $percentText.Text = '--%'
    $timeText.Text = 'Reset date unavailable'
    $remainingBar.Width = 0
    $root.ToolTip = 'No Codex usage data found'
    Write-WidgetLog 'No Codex usage data found'
    return
  }
  $left = [math]::Max(0, [math]::Round(100 - [double]$limit.used_percent))
  $percentText.Text = "$left%"
  $remainingBar.Width = 110 * ($left / 100)
  $age = [DateTimeOffset]::Now - $snapshot.EventTime
  $isStale = $age.TotalMinutes -gt 5
  $freshness = if ($isStale) { 'Data may be stale' } else { 'Live' }
  if ($isStale) {
    $remainingBar.Background = '#FF777777'
    $percentText.Foreground = '#FFAAAAAA'
  } else {
    $remainingBar.Background = if ($left -le 10) { '#FFEF4444' } elseif ($left -le 30) { '#FFF59E0B' } else { '#FF70D88B' }
    $percentText.Foreground = '#FFF3F3F3'
  }
  $root.ToolTip = '{0} - updated {1:HH:mm:ss}' -f $freshness,$snapshot.EventTime
  $codexRunning = Get-Process codex,ChatGPT -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $codexRunning) { $root.ToolTip = '{0}; Codex is not running' -f $root.ToolTip }
  if ($limit.resets_at) {
    $reset = [DateTimeOffset]::FromUnixTimeSeconds([long]$limit.resets_at).ToLocalTime()
    $timeText.Text = 'Reset {0}' -f $reset.ToString('ddd d MMM HH:mm', [Globalization.CultureInfo]::InvariantCulture)
  } else { $timeText.Text = 'Reset date unavailable' }
}

$window.Add_SourceInitialized({
  $script:widgetHandle = [System.Windows.Interop.WindowInteropHelper]::new($window).Handle
  $initialLeft = 205
  if (Test-Path $stateFile) {
    try {
      $saved = Get-Content $stateFile -Raw | ConvertFrom-Json
      if ($saved.Left -ne $null) { $initialLeft = [double]$saved.Left }
      if ($saved.Locked -ne $null) { $script:positionLocked = [bool]$saved.Locked }
      if ($saved.Animate -ne $null) { $script:animateIcon = [bool]$saved.Animate }
      if ($saved.AnimationSpeed -in @('Slow','Normal','Fast')) { $script:animationSpeed = [string]$saved.AnimationSpeed }
      if ($saved.ScreenIndex -ne $null) { $script:screenIndex = [int]$saved.ScreenIndex }
    } catch { }
  }
  $lockMenu.IsChecked = $script:positionLocked
  $root.Cursor = if ($script:positionLocked) { 'Arrow' } else { 'SizeWE' }
  Update-AnimationMenu
  Sync-DisplayGeometry
  Set-TaskbarPosition $initialLeft
  Attach-WidgetToTaskbar
})
$root.Add_MouseLeftButtonDown({
  if (-not $script:positionLocked -and $_.ChangedButton -eq [Windows.Input.MouseButton]::Left) {
    $script:isDragging = $true
    $script:dragStartX = [Windows.Forms.Cursor]::Position.X
    $script:dragStartLeft = $script:widgetLeft
    $root.CaptureMouse() | Out-Null
    $_.Handled = $true
  }
})
$root.Add_MouseMove({
  if ($script:isDragging -and $_.LeftButton -eq [Windows.Input.MouseButtonState]::Pressed) {
    $deltaPixels = [Windows.Forms.Cursor]::Position.X - $script:dragStartX
    $target = $script:dragStartLeft + ($deltaPixels * $script:dipScale)
    $script:widgetLeft = [math]::Max($script:screenLeft, [math]::Min($script:screenRight - $window.Width, $target))
    if ($script:isEmbedded) { Position-EmbeddedWidget } else { $window.Left = $script:widgetLeft }
    $_.Handled = $true
  }
})
$root.Add_MouseLeftButtonUp({
  if ($script:isDragging) {
    $script:isDragging = $false
    $root.ReleaseMouseCapture()
    Set-TaskbarPosition $script:widgetLeft
    $_.Handled = $true
  }
})
$leftMenu.Add_Click({ Set-TaskbarPosition $script:screenLeft })
$weatherMenu.Add_Click({ Set-TaskbarPosition ($script:screenLeft + 205) })
$rightMenu.Add_Click({ Set-TaskbarPosition ($script:screenRight - $window.Width) })
$nextDisplayMenu.Add_Click({
  $count = @([Windows.Forms.Screen]::AllScreens).Count
  if ($count -gt 0) { $script:screenIndex = ($script:screenIndex + 1) % $count; Sync-DisplayGeometry; Set-TaskbarPosition $script:screenLeft }
})
$refreshMenu.Add_Click({ Refresh-Usage })
$lockMenu.Add_Click({
  $script:positionLocked = $lockMenu.IsChecked
  $root.Cursor = if ($script:positionLocked) { 'Arrow' } else { 'SizeWE' }
  Save-TaskbarPosition
})
$animateMenu.Add_Click({
  $script:animateIcon = $animateMenu.IsChecked
  Set-IconAnimation
  Save-TaskbarPosition
})
$slowMenu.Add_Click({ $script:animationSpeed='Slow'; Set-IconAnimation; Save-TaskbarPosition })
$normalMenu.Add_Click({ $script:animationSpeed='Normal'; Set-IconAnimation; Save-TaskbarPosition })
$fastMenu.Add_Click({ $script:animationSpeed='Fast'; Set-IconAnimation; Save-TaskbarPosition })
$exitMenu.Add_Click({ $window.Close() })

$timer = [Windows.Threading.DispatcherTimer]::new()
$timer.Interval = [TimeSpan]::FromSeconds(30)
$timer.Add_Tick({ Refresh-Usage; Set-IconAnimation; Sync-DisplayGeometry })
$timer.Start()
$zOrderTimer = [Windows.Threading.DispatcherTimer]::new()
$zOrderTimer.Interval = [TimeSpan]::FromSeconds(1)
$zOrderTimer.Add_Tick({
  $taskbar = [CodexWidgetNative]::FindWindow('Shell_TrayWnd', $null)
  if ($taskbar -ne $script:taskbarHandle) {
    Sync-DisplayGeometry
    Attach-WidgetToTaskbar
  }
})
$zOrderTimer.Start()
$script:lastSessionWrite = [datetime]::MinValue
$changeTimer = [Windows.Threading.DispatcherTimer]::new()
$changeTimer.Interval = [TimeSpan]::FromSeconds(2)
$changeTimer.Add_Tick({
  try {
    $latest = Get-ChildItem (Join-Path $HOME '.codex\sessions') -Recurse -Filter '*.jsonl' -ErrorAction SilentlyContinue |
      Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest -and $latest.LastWriteTime -gt $script:lastSessionWrite) {
      $script:lastSessionWrite = $latest.LastWriteTime
      Refresh-Usage
    }
  } catch { }
})
$changeTimer.Start()
Set-CodexAppIcon
Set-IconAnimation
Refresh-Usage
$window.ShowDialog() | Out-Null
$mutex.ReleaseMutex()
$mutex.Dispose()
