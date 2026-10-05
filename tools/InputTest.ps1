param(
  [string]$Url = "file:///D:/MaterialBrowser/tools/pages/input.html",
  [string]$ReportLog = "D:\MaterialBrowser\shots\input-report.log",
  [string]$Shot = "D:\MaterialBrowser\shots\input.png",
  [string]$Text = "MaterialBrowser-42",
  [int]$SettleSeconds = 20
)
# Reproduces a real user typing into a page: launches the browser with the
# Firefox engine, clicks inside the page, types text through the keyboard input
# queue, and reports what the page actually received.

function Stop-Browser {
  Get-Process -Name MaterialBrowser, firefox -ErrorAction SilentlyContinue |
    ForEach-Object { try { $_.Kill() } catch { } }
  Start-Sleep -Milliseconds 800
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Win {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

Stop-Browser
Remove-Item $ReportLog -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Split-Path $ReportLog) | Out-Null

$env:MBR_ENGINE = "2"
$env:MBR_NOBACKDROP = "1"
$env:MBR_TRACE = "D:\MaterialBrowser\shots\input.log"
Remove-Item Env:\MBR_ENGINE -ErrorAction SilentlyContinue
$p = Start-Process -FilePath "D:\MaterialBrowser\MaterialBrowser.exe" `
        -ArgumentList "--live", "home", $Url -PassThru
Write-Host "pid=$($p.Id) waiting for the engine"
Start-Sleep -Seconds 7
$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Host "no window handle"; Stop-Browser; exit 1 }
[Win]::ShowWindow($h, 3) | Out-Null
Start-Sleep -Seconds 2
[Win]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600
[Win]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Seconds ($SettleSeconds - 9)

# Click in the middle of the page area (below our toolbar).
$r = New-Object Win+RECT
[Win]::GetWindowRect($h, [ref]$r) | Out-Null
$cx = $r.Left + [int](($r.Right - $r.Left) / 2)
$cy = $r.Top + [int](($r.Bottom - $r.Top) * 0.55)
Write-Host "clicking at $cx,$cy"
[Win]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 250
[Win]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
[Win]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 600

$fg = [Win]::GetForegroundWindow()
$title = New-Object System.Text.StringBuilder 256
$cls = New-Object System.Text.StringBuilder 256
[Win]::GetWindowTextW($fg, $title, 256) | Out-Null
[Win]::GetClassNameW($fg, $cls, 256) | Out-Null
Write-Host "foreground: 0x$($fg.ToInt64().ToString('X')) class=$($cls.ToString()) title=$($title.ToString())"

Write-Host "typing: $Text"
[System.Windows.Forms.SendKeys]::SendWait($Text)
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
Start-Sleep -Seconds 2

$bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$bmp.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved $Shot"

if (Test-Path $ReportLog) {
  Write-Host "--- page reports ---"
  Get-Content $ReportLog | Select-Object -Last 12 | ForEach-Object { Write-Host $_ }
} else {
  Write-Host "NO REPORTS: the page received nothing"
}
Stop-Browser
