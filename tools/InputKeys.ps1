param(
  [string]$Url = "file:///D:/MaterialBrowser/tools/pages/input.html",
  [string]$ReportLog = "D:\MaterialBrowser\shots\input-report.log",
  [int]$SettleSeconds = 20,
  [switch]$Cyrillic
)
# Types into a real page with physical keystrokes (scan codes), which is what a
# keyboard actually produces - SendKeys would send Unicode packets instead.

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
public class K {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] i, int size);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  public delegate bool EnumProc(IntPtr w, IntPtr l);

  // Where the page actually is. After tabs come and go the panel can be a
  // different rectangle, and a blind click at the window centre can miss it.
  static IntPtr _page;
  public static RECT PageRect;
  public static bool FindPage(IntPtr app) {
    _page = IntPtr.Zero;
    EnumChildWindows(app, delegate (IntPtr w, IntPtr l) {
      var c = new StringBuilder(256); GetClassNameW(w, c, 256);
      if (c.ToString() == "MozillaWindowClass") { _page = w; return false; }
      return true;
    }, IntPtr.Zero);
    if (_page == IntPtr.Zero) return false;
    return GetWindowRect(_page, out PageRect);
  }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)]
  public struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
  [StructLayout(LayoutKind.Explicit, Size = 40)]
  public struct INPUT {
    [FieldOffset(0)] public uint type;
    [FieldOffset(8)] public KEYBDINPUT ki;
  }
  const uint SCANCODE = 0x0008, KEYUP = 0x0002;
  const ushort SHIFT = 0x2A;

  static void Send(INPUT i) {
    uint sent = SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
    if (sent == 0) Console.WriteLine("  SendInput FAILED err=" + Marshal.GetLastWin32Error());
  }
  static INPUT Make(ushort scan, bool up) {
    return new INPUT { type = 1, ki = new KEYBDINPUT { scan = scan,
      flags = SCANCODE | (up ? KEYUP : 0u) } };
  }
  public static void Tap(ushort scan) { Send(Make(scan, false)); Send(Make(scan, true)); }
  public static void Hold(ushort scan, bool down) { Send(Make(scan, !down)); }
  // Scan codes of the US layout, so the same table works for any text.
  public static void Type(string text) {
    foreach (char c in text) {
      ushort scan = ScanOf(c);
      if (scan == 0) continue;
      bool shifted = c >= 'A' && c <= 'Z' || "!@#$%^&*()_+{}:\"|<>?".IndexOf(c) >= 0;
      if (shifted) Send(Make(SHIFT, false));
      Tap(scan);
      if (shifted) Send(Make(SHIFT, true));
      System.Threading.Thread.Sleep(25);
    }
  }
  // Real US scan codes, listed explicitly: an index-based shortcut would be
  // wrong, because Backspace (0x0E) and Tab (0x0F) sit between '=' and 'Q'.
  static readonly string Chars = "1234567890-=qwertyuiop[]asdfghjkl;'zxcvbnm,./";
  static readonly ushort[] Scans = {
    0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,
    0x10,0x11,0x12,0x13,0x14,0x15,0x16,0x17,0x18,0x19,0x1A,0x1B,
    0x1E,0x1F,0x20,0x21,0x22,0x23,0x24,0x25,0x26,0x27,0x28,
    0x2C,0x2D,0x2E,0x2F,0x30,0x31,0x32,0x33,0x34,0x35
  };
  static ushort ScanOf(char c) {
    int i = Chars.IndexOf(char.ToLowerInvariant(c));
    if (i >= 0) return Scans[i];
    if (c == ' ') return 0x39;
    if (c == '\n') return 0x1C;
    return 0;
  }
}
"@

Stop-Browser
Remove-Item $ReportLog -ErrorAction SilentlyContinue
$env:MBR_ENGINE = "2"
$env:MBR_NOBACKDROP = "1"
$env:MBR_TRACE = "D:\MaterialBrowser\shots\inputkeys.log"
Remove-Item $env:MBR_TRACE -ErrorAction SilentlyContinue
Remove-Item Env:\MBR_ENGINE -ErrorAction SilentlyContinue
$p = Start-Process -FilePath "D:\MaterialBrowser\MaterialBrowser.exe" `
        -ArgumentList "--live", "home", $Url -PassThru
Write-Host "pid=$($p.Id)"
Start-Sleep -Seconds 7
$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Host "no window handle"; Stop-Browser; exit 1 }
[K]::ShowWindow($h, 3) | Out-Null
Start-Sleep -Seconds 2
[K]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600
[K]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Seconds ($SettleSeconds - 9)

$r = New-Object K+RECT
[K]::GetWindowRect($h, [ref]$r) | Out-Null
# Click inside the page panel itself, wherever it currently sits.
function Click-Page([IntPtr]$app) {
  $p = New-Object K+RECT
  if ([K]::FindPage($app)) {
    $p = [K]::PageRect
  } else {
    Write-Host "  no page window found, falling back to the window centre"
    [K]::GetWindowRect($app, [ref]$p) | Out-Null
  }
  $x = $p.Left + [int](($p.Right - $p.Left) / 2)
  $y = $p.Top + [int](($p.Bottom - $p.Top) / 2)
  Write-Host "  clicking page at $x,$y (page $(( $p.Right - $p.Left ))x$(($p.Bottom - $p.Top)))"
  [K]::SetCursorPos($x, $y) | Out-Null
  Start-Sleep -Milliseconds 250
  [K]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  [K]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 800
}
Click-Page $h

$text = "Hello-42"
[K]::Type($text)
Start-Sleep -Seconds 3
Write-Host "--- page reports ---"
if (Test-Path $ReportLog) { Get-Content $ReportLog | Select-Object -Last 5 | ForEach-Object { Write-Host $_ } }
else { Write-Host "NO REPORTS" }

# Same again after opening and closing a tab: switching tabs tears the shared
# input state down and rebuilds it, and typing has to keep working.
$ctrl = 0x1D
[K]::Hold($ctrl, $true)
[K]::Tap(0x14)               # Ctrl+T
[K]::Hold($ctrl, $false)
Start-Sleep -Seconds 4
[K]::Hold($ctrl, $true)
[K]::Tap(0x11)               # Ctrl+W
[K]::Hold($ctrl, $false)
Start-Sleep -Seconds 4

Click-Page $h
Write-Host "--- typing again after tab churn ---"
# Clear the field first: Firefox restores what was typed here before, so the
# length after typing must be counted from a known empty state.
[K]::Hold($ctrl, $true)
[K]::Tap(0x1E)               # Ctrl+A
[K]::Hold($ctrl, $false)
Start-Sleep -Milliseconds 400
[K]::Tap(0x53)               # Delete
Start-Sleep -Milliseconds 600
[K]::Type("AfterTabs")
Start-Sleep -Seconds 3
if (Test-Path $ReportLog) { Get-Content $ReportLog | Select-Object -Last 4 | ForEach-Object { Write-Host $_ } }
else { Write-Host "NO REPORTS" }
Stop-Browser
