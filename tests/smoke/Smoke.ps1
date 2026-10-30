# Launches the published Nyvora.exe on the runner's real desktop and drives it with the mouse and keyboard:
# it appears, opens on click, takes typed text and saves it, closes on Esc and on a click elsewhere, opens and
# closes the Settings side panel, and quits from its own button. Screenshots of the notch are written as files
# and also printed to the log as base64 (SHOT lines) so they can be read without downloading the artifact.
param(
  [Parameter(Mandatory)][string]$Exe,
  [Parameter(Mandatory)][string]$OutDir
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Win {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public static List<RECT> Visible(uint pid) {
    var result = new List<RECT>();
    EnumWindows(delegate (IntPtr h, IntPtr l) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { RECT r; if (GetWindowRect(h, out r)) result.Add(r); }
      return true;
    }, IntPtr.Zero);
    return result;
  }
}
'@
[void][Win]::SetProcessDPIAware()
$scale = [Win]::GetDpiForSystem() / 96.0
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$results = New-Object System.Collections.Generic.List[object]
$logPath = Join-Path $env:LOCALAPPDATA 'Nyvora\nyvora.log'
$tasksPath = Join-Path $env:LOCALAPPDATA 'Nyvora\tasks.json'

function Check([bool]$ok, [string]$what, [string]$detail = '') {
  $results.Add([pscustomobject]@{ ok = $ok; check = $what; detail = $detail })
  Write-Host ($(if ($ok) { '  ok    ' } else { '  FAIL  ' }) + $what + $(if ($detail) { '  (' + $detail + ')' } else { '' }))
}

function Get-Pill($proc) {
  $r = [Win]::Visible([uint32]$proc.Id) | Sort-Object { ($_.Right - $_.Left) * ($_.Bottom - $_.Top) } -Descending | Select-Object -First 1
  if ($null -eq $r) { return $null }
  return [pscustomobject]@{ L = $r.Left; T = $r.Top; R = $r.Right; B = $r.Bottom; W = $r.Right - $r.Left; H = $r.Bottom - $r.Top }
}

function Wait-Pill($proc, [scriptblock]$until, [int]$seconds = 6) {
  $end = (Get-Date).AddSeconds($seconds)
  do {
    $p = Get-Pill $proc
    if ($null -ne $p -and (& $until $p)) { return $p }
    Start-Sleep -Milliseconds 150
  } while ((Get-Date) -lt $end)
  return (Get-Pill $proc)
}

function Click([int]$x, [int]$y) {
  [void][Win]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 120
  [Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
  [Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Shot([string]$name) {
  $bmp = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
  $g.Dispose()
  $bmp.Save((Join-Path $OutDir ($name + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
  # The notch area only (top centre), as a small JPEG that fits in a log.
  $w = [Math]::Min($screen.Width, [int](1000 * $scale)); $h = [Math]::Min($screen.Height, [int](560 * $scale))
  $crop = $bmp.Clone((New-Object System.Drawing.Rectangle ([int](($screen.Width - $w) / 2)), 0, $w, $h), $bmp.PixelFormat)
  $jpg = Join-Path $OutDir ($name + '.jpg')
  $crop.Save($jpg, [System.Drawing.Imaging.ImageFormat]::Jpeg)
  $crop.Dispose(); $bmp.Dispose()
  $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($jpg))
  $i = 0
  for ($o = 0; $o -lt $b64.Length; $o += 3000) { Write-Host ('SHOT:' + $name + ':' + $i + ':' + $b64.Substring($o, [Math]::Min(3000, $b64.Length - $o))); $i++ }
}

$proc = $null
try {
  Write-Host ("screen " + $screen.Width + 'x' + $screen.Height + ' scale ' + $scale)
  $logStart = if (Test-Path $logPath) { (Get-Content -LiteralPath $logPath).Count } else { 0 }
  $proc = Start-Process -FilePath $Exe -PassThru

  Write-Host 'Launch'
  # Wait for the pill itself, not whatever window exists first: a default-sized window before it is a startup flaw.
  $pill = Wait-Pill $proc { param($p) $p.W -gt 40 -and $p.W -lt 260 * $scale -and $p.H -lt 80 * $scale } 25
  Check (-not $proc.HasExited) 'the app is running'
  if ($null -eq $pill) { Check $false 'a window appeared'; throw 'no window' }
  Check $true 'a window appeared' ("{0}x{1} at {2},{3}" -f $pill.W, $pill.H, $pill.L, $pill.T)
  Check ($pill.W -ge 100 * $scale -and $pill.W -le 260 * $scale -and $pill.H -le 60 * $scale) 'it starts as a small pill'
  Check ($pill.T -le 40 * $scale -and [Math]::Abs((($pill.L + $pill.R) / 2) - ($screen.Width / 2)) -le 24 * $scale) 'it sits at the top centre'
  Start-Sleep -Milliseconds 700
  Shot 'resting'

  Write-Host 'Expand'
  Click ([int](($pill.L + $pill.R) / 2)) ([int](($pill.T + $pill.B) / 2))
  $open = Wait-Pill $proc { param($p) $p.W -gt 330 * $scale -and $p.H -gt 300 * $scale } 6
  Check ($open.W -gt 330 * $scale -and $open.H -gt 300 * $scale) 'clicking the pill expands it' ("{0}x{1}" -f $open.W, $open.H)
  Start-Sleep -Milliseconds 900
  Shot 'expanded'

  Write-Host 'Type a task'
  [System.Windows.Forms.SendKeys]::SendWait('smoke test task{ENTER}')
  Start-Sleep -Milliseconds 900
  $saved = (Test-Path $tasksPath) -and ((Get-Content -LiteralPath $tasksPath -Raw) -match 'smoke test task')
  $how = 'typed straight away (the input had focus)'
  if (-not $saved) {
    Click ([int](($open.L + $open.R) / 2)) ([int]($open.T + 96 * $scale))   # click into the input, then try again
    [System.Windows.Forms.SendKeys]::SendWait('smoke test task{ENTER}')
    Start-Sleep -Milliseconds 900
    $saved = (Test-Path $tasksPath) -and ((Get-Content -LiteralPath $tasksPath -Raw) -match 'smoke test task')
    $how = 'needed a click into the input first'
  }
  Check $saved 'typed text becomes a saved task' $how
  Shot 'with-task'

  Write-Host 'Settings side panel'
  $open = Get-Pill $proc
  $baseW = $open.W
  $iconY = [int]($open.T + 32 * $scale)
  Click ([int]($open.R - 96 * $scale)) $iconY                      # the gear
  $wide = Wait-Pill $proc { param($p) $p.W -gt $baseW + 200 * $scale } 5
  Check ($wide.W -gt $baseW + 200 * $scale) 'the gear opens Settings beside the tasks' ("{0} -> {1}" -f $baseW, $wide.W)
  Start-Sleep -Milliseconds 900
  $wide = Get-Pill $proc                                           # settled: the window may have shifted left to fit the screen
  Shot 'settings'
  Click ([int]($wide.R - 38 * $scale)) $iconY                      # the close button of the Settings panel
  $back = Wait-Pill $proc { param($p) $p.W -le $baseW + 10 * $scale } 4
  if ($back.W -gt $baseW + 10 * $scale) {
    Click ([int]($wide.L + 324 * $scale)) $iconY                   # fall back to the gear, which toggles it
    $back = Wait-Pill $proc { param($p) $p.W -le $baseW + 10 * $scale } 4
  }
  Check ($back.W -le $baseW + 10 * $scale) 'Settings closes again' ("{0}" -f $back.W)

  Write-Host 'Esc and click away'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  $small = Wait-Pill $proc { param($p) $p.W -lt 260 * $scale } 4
  Check ($small.W -lt 260 * $scale) 'Esc collapses the pill' ("{0}" -f $small.W)
  Click ([int](($small.L + $small.R) / 2)) ([int](($small.T + $small.B) / 2))
  $open = Wait-Pill $proc { param($p) $p.W -gt 330 * $scale } 5
  Check ($open.W -gt 330 * $scale) 'it expands again'
  Start-Sleep -Milliseconds 700
  Click 40 ([int]($screen.Height - 120))
  $small = Wait-Pill $proc { param($p) $p.W -lt 260 * $scale } 4
  Check ($small.W -lt 260 * $scale) 'clicking elsewhere collapses the pill' ("{0}" -f $small.W)
  Start-Sleep -Milliseconds 500
  Shot 'collapsed'

  Write-Host 'Second launch'
  $second = Start-Process -FilePath $Exe -PassThru
  $secondExited = $second.WaitForExit(10000)
  Check $secondExited 'a second copy exits instead of opening another pill'
  Check (-not $proc.HasExited) 'the first copy keeps running'
  $open = Wait-Pill $proc { param($p) $p.W -gt 330 * $scale } 5
  Check ($open.W -gt 330 * $scale) 'launching again opens the running pill'
  Start-Sleep -Milliseconds 700
  Click 40 ([int]($screen.Height - 120))
  $small = Wait-Pill $proc { param($p) $p.W -lt 260 * $scale } 4
  if (-not $second.HasExited) { Stop-Process -Id $second.Id -Force -ErrorAction SilentlyContinue }

  Write-Host 'Quit'
  Click ([int](($small.L + $small.R) / 2)) ([int](($small.T + $small.B) / 2))
  [void](Wait-Pill $proc { param($p) $p.W -gt 330 * $scale } 5)
  Start-Sleep -Milliseconds 900
  $open = Get-Pill $proc                                           # settled, so the right edge is final
  Click ([int]($open.R - 36 * $scale)) ([int]($open.T + 32 * $scale))   # the Quit icon
  $exited = $proc.WaitForExit(6000)
  Check $exited 'the Quit button closes the app'
}
catch {
  Write-Host ('smoke test stopped early: ' + $_.Exception.Message)
  try { Shot 'failure' } catch { }
  $results.Add([pscustomobject]@{ ok = $false; check = 'ran to the end'; detail = $_.Exception.Message })
}
finally {
  if ($null -ne $proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}

if (Test-Path $logPath) {
  $lines = Get-Content -LiteralPath $logPath | Select-Object -Skip $logStart
  Write-Host '--- nyvora.log (this run) ---'
  $lines | ForEach-Object { Write-Host $_ }
  Write-Host '--- end ---'
  $errors = @($lines | Where-Object { $_ -match ' ERROR ' })
  Check ($errors.Count -eq 0) 'no errors in the app log' ($errors -join ' | ')
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutDir 'results.json')
$failed = @($results | Where-Object { -not $_.ok }).Count
Write-Host ("`n{0} of {1} checks passed" -f ($results.Count - $failed), $results.Count)
if ($failed -gt 0) { exit 1 }
