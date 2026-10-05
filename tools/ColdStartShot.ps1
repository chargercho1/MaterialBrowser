param(
  [string]$Out = "D:\MaterialBrowser\shots\coldstart.png",
  [string]$Trace = "D:\MaterialBrowser\shots\coldstart.log",
  [int]$SettleSeconds = 28
)

Get-Process MaterialBrowser, zen -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700

$env:MBR_TRACE = $Trace
Remove-Item Env:\MBR_ENGINE -ErrorAction SilentlyContinue
Remove-Item $Trace -ErrorAction SilentlyContinue

# Cold launch: no arguments, so the startup path (session restore -> about:newtab)
# is exactly what a real user gets.
Start-Process -FilePath "D:\MaterialBrowser\MaterialBrowser.exe" | Out-Null
Start-Sleep -Seconds 6

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class C {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
}
"@
$p = Get-Process MaterialBrowser -ErrorAction SilentlyContinue | Select-Object -First 1
[C]::ShowWindow($p.MainWindowHandle, 3) | Out-Null
Start-Sleep -Milliseconds 400
[C]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Seconds ($SettleSeconds - 6)

$r = New-Object C+R
[C]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.Rt - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved $Out"

Get-Process MaterialBrowser, zen -ErrorAction SilentlyContinue | Stop-Process -Force