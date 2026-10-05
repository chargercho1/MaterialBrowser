param(
  [string]$Url = "file:///D:/MaterialBrowser/tools/pages/input.html",
  [int]$SettleSeconds = 20
)
# The browser now owns the keyboard, so the app's own shortcuts must still arrive.
# Keys are injected by physical scan code, not through SendKeys: SendKeys sends
# Unicode packets (VK_PACKET), which no real keyboard produces and which carry no
# usable key identity.

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
public class S {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint id, ref GUIINFO g);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)]
  public struct GUIINFO {
    public int cbSize; public uint flags;
    public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
    public RECT rcCaret;
  }
  [StructLayout(LayoutKind.Sequential)]
  public struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
  // INPUT is 40 bytes on x64: a DWORD plus padding, then a union whose largest
  // member is MOUSEINPUT. SendInput rejects anything smaller with error 87.
  [StructLayout(LayoutKind.Explicit, Size = 40)]
  public struct INPUT {
    [FieldOffset(0)] public uint type;
    [FieldOffset(8)] public KEYBDINPUT ki;
  }
  const uint KEYEVENTF_SCANCODE = 0x0008, KEYEVENTF_KEYUP = 0x0002;

  public static void Tap(uint scan) {
    Send(new INPUT { type = 1, ki = new KEYBDINPUT { scan = (ushort)scan, flags = KEYEVENTF_SCANCODE } });
    Send(new INPUT { type = 1, ki = new KEYBDINPUT { scan = (ushort)scan, flags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP } });
  }
  public static void Hold(uint scan, bool down) {
    Send(new INPUT { type = 1, ki = new KEYBDINPUT { scan = (ushort)scan,
      flags = KEYEVENTF_SCANCODE | (down ? 0u : KEYEVENTF_KEYUP) } });
  }
  static void Send(INPUT i) {
    int size = Marshal.SizeOf(typeof(INPUT));
    uint sent = SendInput(1, new INPUT[] { i }, size);
    Console.WriteLine("  SendInput sent=" + sent + " size=" + size +
                      (sent == 0 ? " err=" + Marshal.GetLastWin32Error() : ""));
  }
  public static string Name(IntPtr h) {
    if (h == IntPtr.Zero) return "(none)";
    var c = new StringBuilder(256); GetClassNameW(h, c, 256);
    uint pid; uint th = GetWindowThreadProcessId(h, out pid);
    return "0x" + h.ToInt64().ToString("X") + " pid=" + pid + " " + c + " parent=0x" + GetParent(h).ToInt64().ToString("X");
  }
  public static string Children(IntPtr parent) {
    var sb = new StringBuilder();
    EnumChildWindows(parent, delegate (IntPtr w, IntPtr l) { sb.AppendLine(Name(w)); return true; }, IntPtr.Zero);
    return sb.ToString();
  }
  public delegate bool EnumProc(IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
  public static string Focus(uint thread) {
    var g = new GUIINFO(); g.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(GUIINFO));
    if (!GetGUIThreadInfo(thread, ref g)) return "GetGUIThreadInfo failed";
    return Name(g.hwndFocus);
  }
}
"@

Stop-Browser
$env:MBR_ENGINE = "2"
$env:MBR_NOBACKDROP = "1"
$env:MBR_TRACE = "D:\MaterialBrowser\shots\shortcut.log"
Remove-Item $env:MBR_TRACE -ErrorAction SilentlyContinue
Remove-Item Env:\MBR_ENGINE -ErrorAction SilentlyContinue
$p = Start-Process -FilePath "D:\MaterialBrowser\MaterialBrowser.exe" `
        -ArgumentList "--live", "home", $Url -PassThru
Write-Host "pid=$($p.Id)"
Start-Sleep -Seconds 7
$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Host "no window handle"; Stop-Browser; exit 1 }
[S]::ShowWindow($h, 3) | Out-Null
Start-Sleep -Seconds 2
[S]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600
[S]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Seconds ($SettleSeconds - 9)

$appThread = [S]::GetWindowThreadProcessId($h, [ref]([uint32]0))
$r = New-Object S+RECT
[S]::GetWindowRect($h, [ref]$r) | Out-Null
$cx = $r.Left + [int](($r.Right - $r.Left) / 2)
$cy = $r.Top + [int](($r.Bottom - $r.Top) * 0.55)
[S]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 250
[S]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
[S]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 900
Write-Host "focus after page click : $([S]::Focus($appThread))"

$ctrl = 0x1D
[S]::Hold($ctrl, $true)      # Ctrl down
[S]::Tap(0x26)               # physical L key
[S]::Hold($ctrl, $false)     # Ctrl up
Start-Sleep -Seconds 2
Write-Host "focus after Ctrl+L     : $([S]::Focus($appThread))"

# Ctrl+W while the address bar holds the keyboard is the app's own business.
# Return the focus to the page, which is the case the hook has to cover.
[S]::SetCursorPos($cx, $cy) | Out-Null
[S]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
[S]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 900
Write-Host "focus back on page    : $([S]::Focus($appThread))"

# Every tab brings its own surface window, so the child count tracks tab count.
$before = ([S]::Children($h) | Out-String).Trim().Split("`n").Count
[S]::Hold($ctrl, $true)
[S]::Tap(0x14)               # physical T key
[S]::Hold($ctrl, $false)
Start-Sleep -Seconds 3
$after = ([S]::Children($h) | Out-String).Trim().Split("`n").Count
Write-Host "child windows: $before -> $after (Ctrl+T should open a tab)"

[S]::Hold($ctrl, $true)
[S]::Tap(0x11)               # physical W key
[S]::Hold($ctrl, $false)
Start-Sleep -Seconds 3
$closed = ([S]::Children($h) | Out-String).Trim().Split("`n").Count
Write-Host "child windows after Ctrl+W: $closed (should be near $before)"

# The tab that is left must get the keyboard back - closing a tab tears the
# shared input state down, so typing has to recover without a restart.
Write-Host "focus after Ctrl+W     : $([S]::Focus($appThread))"
[S]::SetCursorPos($cx, $cy) | Out-Null
[S]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
[S]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 900
[S]::Tap(0x23)               # X key: one more probe keystroke after the churn
Start-Sleep -Seconds 2
Write-Host "focus after page click : $([S]::Focus($appThread))"

$bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$bmp.Save("D:\MaterialBrowser\shots\shortcut.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved D:\MaterialBrowser\shots\shortcut.png"
Stop-Browser
