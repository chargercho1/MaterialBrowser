param(
  [string]$Url = "file:///D:/MaterialBrowser/tools/pages/input.html",
  [int]$SettleSeconds = 20
)
# Dumps the focus state of both threads after a click and a keystroke, so we can
# see where the keyboard input actually stops.

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
public class D {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint id, ref GUIINFO g);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)]
  public struct GUIINFO {
    public int cbSize; public uint flags;
    public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
    public RECT rcCaret;
  }
  public static string Name(IntPtr h) {
    if (h == IntPtr.Zero) return "(none)";
    var c = new StringBuilder(256); GetClassNameW(h, c, 256);
    var t = new StringBuilder(256); GetWindowTextW(h, t, 256);
    return "0x" + h.ToInt64().ToString("X") + " " + c + " \"" + t + "\" parent=0x" + GetParent(h).ToInt64().ToString("X");
  }
  public static string Info(uint thread) {
    var g = new GUIINFO(); g.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(GUIINFO));
    if (!GetGUIThreadInfo(thread, ref g)) return "  GetGUIThreadInfo failed";
    return "  active : " + Name(g.hwndActive) + "\n  focus  : " + Name(g.hwndFocus) +
           "\n  caret  : " + Name(g.hwndCaret);
  }
}
"@

Stop-Browser
$env:MBR_ENGINE = "2"
$env:MBR_NOBACKDROP = "1"
$env:MBR_TRACE = "D:\MaterialBrowser\shots\keydiag.log"
Remove-Item Env:\MBR_ENGINE -ErrorAction SilentlyContinue
$p = Start-Process -FilePath "D:\MaterialBrowser\MaterialBrowser.exe" `
        -ArgumentList "--live", "home", $Url -PassThru
Write-Host "pid=$($p.Id)"
Start-Sleep -Seconds 7
$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Host "no window handle"; Stop-Browser; exit 1 }
[D]::ShowWindow($h, 3) | Out-Null
Start-Sleep -Seconds 2
[D]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600
[D]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Seconds ($SettleSeconds - 9)

$gecko = [IntPtr]::Zero
$cb = [D+EnumProc]{
  param($w, $l)
  $c = New-Object System.Text.StringBuilder 256
  [D]::GetClassNameW($w, $c, 256) | Out-Null
  if ($c.ToString() -eq "MozillaWindowClass") { $script:gecko = $w; return $false }
  return $true
}
[D]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
Write-Host "gecko window: $([D]::Name($gecko))"

$r = New-Object D+RECT
[D]::GetWindowRect($h, [ref]$r) | Out-Null
$cx = $r.Left + [int](($r.Right - $r.Left) / 2)
$cy = $r.Top + [int](($r.Bottom - $r.Top) * 0.55)
[D]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 250
[D]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
[D]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 800

$appThread = [D]::GetWindowThreadProcessId($h, [ref]([uint32]0))
$geckoThread = [D]::GetWindowThreadProcessId($gecko, [ref]([uint32]0))
Write-Host "app thread=$appThread  gecko thread=$geckoThread"
Write-Host "BEFORE typing:"
Write-Host " app thread:"; Write-Host ([D]::Info($appThread))
Write-Host " gecko thread:"; Write-Host ([D]::Info($geckoThread))

[System.Windows.Forms.SendKeys]::SendWait("MaterialBrowser-42")
Start-Sleep -Seconds 2

Write-Host "AFTER typing:"
Write-Host " app thread:"; Write-Host ([D]::Info($appThread))
Write-Host " gecko thread:"; Write-Host ([D]::Info($geckoThread))
Write-Host "foreground: $([D]::Name([D]::GetForegroundWindow()))"

Stop-Browser
