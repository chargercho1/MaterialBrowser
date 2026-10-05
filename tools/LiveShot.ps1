param(
  [string]$Url = "http://127.0.0.1:8731/gecko.html",
  [string]$Out = "D:\MaterialBrowser\shots\gecko.png",
  [int]$SettleSeconds = 14,
  [int]$Engine = 2
)

function Stop-Browser {
  foreach ($name in @("zen", "MaterialBrowser")) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
      try { $_.Kill() } catch { }
    }
  }
  Start-Sleep -Milliseconds 700
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

Stop-Browser

$env:MBR_ENGINE = "$Engine"
$env:MBR_NOBACKDROP = "1"
$p = Start-Process -FilePath "D:\MaterialBrowser\MaterialBrowser.exe" `
        -ArgumentList "--live", "home", $Url -PassThru
Write-Host "pid=$($p.Id) waiting $SettleSeconds s"
Start-Sleep -Seconds 6
$p.Refresh()
if ($p.MainWindowHandle -ne [IntPtr]::Zero) { [Win]::ShowWindow($p.MainWindowHandle, 3) | Out-Null }
Start-Sleep -Seconds ($SettleSeconds - 6)

$p.Refresh()
$h = $p.MainWindowHandle
Write-Host "hwnd=$h exited=$($p.HasExited)"
if ($h -ne [IntPtr]::Zero) {
  [Win]::ShowWindow($h, 3) | Out-Null      # maximize so the panel is as large as possible
  Start-Sleep -Seconds 1
  [Win]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 400
  [Win]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Seconds 3
  $r = New-Object Win+RECT
  [Win]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
  Write-Host "window $w x $ht at $($r.Left),$($r.Top)"
  $bmp = New-Object System.Drawing.Bitmap $w, $ht
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $ht))
  New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
  $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Host "saved $Out"
} else {
  Write-Host "no window handle"
}

Stop-Browser