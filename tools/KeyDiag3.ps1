param(
  [string]$Url = "file:///D:/MaterialBrowser/tools/pages/input.html",
  [string]$ReportLog = "D:\MaterialBrowser\shots\input-report.log",
  [int]$SettleSeconds = 20
)
# Experiment: prove that AttachThreadInput + SetFocus hands the keyboard over to
# the cross-process Gecko window, then type and see what the page receives.

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
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SetActiveWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetFocus();
  [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
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
  public static IntPtr FocusOf(uint thread) {
    var g = new GUIINFO(); g.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(GUIINFO));
    if (!GetGUIThreadInfo(thread, ref g)) return IntPtr.Zero;
    return g.hwndFocus;
  }
  public static string Info(uint thread) {
    var g = new GUIINFO(); g.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(GUIINFO));
    if (!GetGUIThreadInfo(thread, ref g)) return "  GetGUIThreadInfo failed";
    return "  active : " + Name(g.hwndActive) + "\n  focus  : " + Name(g.hwndFocus) +
           "\n  caret  : " + Name(g.hwndCaret);
  }
  public static IntPtr FindGecko(IntPtr app) {
    IntPtr found = IntPtr.Zero;
    EnumChildWindows(app, delegate (IntPtr w, IntPtr l) {
      var c = new StringBuilder(256); GetClassNameW(w, c, 256);
      if (c.ToString() == "MozillaWindowClass") { found = w; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@

Stop-Browser
Remove-Item $ReportLog -ErrorAction SilentlyContinue
$env:MBR_ENGINE = "2"
$env:MBR_NOBACKDROP = "1"
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

$gecko = [D]::FindGecko($h)
Write-Host "gecko: $([D]::Name($gecko))"
$appThread = [D]::GetWindowThreadProcessId($h, [ref]([uint32]0))
$geckoThread = [D]::GetWindowThreadProcessId($gecko, [ref]([uint32]0))
Write-Host "app thread=$appThread gecko thread=$geckoThread me=$([D]::GetCurrentThreadId())"

$r = New-Object D+RECT
[D]::GetWindowRect($h, [ref]$r) | Out-Null
$cx = $r.Left + [int](($r.Right - $r.Left) / 2)
$cy = $r.Top + [int](($r.Bottom - $r.Top) * 0.55)
[D]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 250
[D]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
[D]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 800

Write-Host "gecko thread focus after click: $([D]::Name([D]::FocusOf($geckoThread)))"
Write-Host "app thread focus after click : $([D]::Name([D]::FocusOf($appThread)))"

$attached = [D]::AttachThreadInput($appThread, $geckoThread, $true)
Write-Host "AttachThreadInput(app,gecko) = $attached"
[D]::SetActiveWindow($gecko) | Out-Null
[D]::SetFocus($gecko) | Out-Null
Start-Sleep -Milliseconds 400
Write-Host "app thread focus after SetFocus: $([D]::Name([D]::FocusOf($appThread)))"
Write-Host "gecko thread focus after SetFocus: $([D]::Name([D]::FocusOf($geckoThread)))"

[System.Windows.Forms.SendKeys]::SendWait("MaterialBrowser-42")
Start-Sleep -Seconds 3
Write-Host "--- page reports ---"
if (Test-Path $ReportLog) { Get-Content $ReportLog | Select-Object -Last 8 | ForEach-Object { Write-Host $_ } }
else { Write-Host "NO REPORTS" }

[D]::AttachThreadInput($appThread, $geckoThread, $false) | Out-Null
Stop-Browser
