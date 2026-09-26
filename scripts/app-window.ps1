# Drives a running Happy Photon window on Windows for screenshots, without touching
# the user's cursor, keyboard focus, or foreground window.
#
# Dot-source it from Windows PowerShell 5.1 (powershell.exe), which ships System.Drawing:
#   . ./scripts/app-window.ps1
#   Resize-AppWindow 2222 1272        # client area 2200x1260 at 150% scaling
#   Send-AppClick 1576 1012; Send-AppKeys 0x50, 0x35   # select a tile, then P and 5
#   Save-AppShot docs/screenshots/Screenshot_Browse.png
#
# Input is posted with PostMessage straight to the window, so it works while the app is
# in the background. Coordinates are physical client pixels; read them off a capture.
# Notes from use:
# - Title-bar menus open as separate popup windows; find them with Get-AppPopup and
#   capture or click them by handle.
# - A click on a Develop slider track focuses it without changing the value; arrow keys
#   then step by SmallChange and coalesce into one history entry per slider.
# - Posted keys carry no modifier state, so Ctrl/Shift shortcuts do not work this way.

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class AppWindow {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

  public static void Capture(IntPtr h, string path) {
    RECT r; GetWindowRect(h, out r);
    RECT f; DwmGetWindowAttribute(h, 9, out f, Marshal.SizeOf(typeof(RECT)));
    using (var bmp = new Bitmap(r.Right - r.Left, r.Bottom - r.Top, PixelFormat.Format32bppArgb)) {
      using (var g = Graphics.FromImage(bmp)) {
        IntPtr hdc = g.GetHdc();
        PrintWindow(h, hdc, 2);
        g.ReleaseHdc(hdc);
      }
      // Trim the invisible resize border so only the visible frame remains.
      var crop = new Rectangle(f.Left - r.Left, f.Top - r.Top, f.Right - f.Left, f.Bottom - f.Top);
      using (var visible = bmp.Clone(crop, bmp.PixelFormat)) visible.Save(path, ImageFormat.Png);
    }
  }

  static IntPtr Pt(int x, int y) { return (IntPtr)((y << 16) | (x & 0xFFFF)); }
  public static void Key(IntPtr h, int vk) {
    uint sc = MapVirtualKey((uint)vk, 0);
    PostMessage(h, 0x0100, (IntPtr)vk, (IntPtr)(1 | (sc << 16)));
    PostMessage(h, 0x0101, (IntPtr)vk, (IntPtr)(1 | (sc << 16) | (1u << 30) | (1u << 31)));
  }
  public static void Move(IntPtr h, int x, int y, bool down) { PostMessage(h, 0x0200, down ? (IntPtr)1 : IntPtr.Zero, Pt(x, y)); }
  public static void Down(IntPtr h, int x, int y) { PostMessage(h, 0x0201, (IntPtr)1, Pt(x, y)); }
  public static void Up(IntPtr h, int x, int y) { PostMessage(h, 0x0202, IntPtr.Zero, Pt(x, y)); }
  public static void Wheel(IntPtr h, int x, int y, int delta) {
    var p = new POINT { X = x, Y = y }; ClientToScreen(h, ref p);
    PostMessage(h, 0x020A, (IntPtr)(delta << 16), Pt(p.X, p.Y));
  }
  public static List<IntPtr> ProcessWindows(uint pid) {
    var list = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
    return list;
  }
}
"@

[void][AppWindow]::SetProcessDPIAware()
$script:AppProcess = Get-Process HappyPhoton -ErrorAction Stop | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
$script:AppHwnd = [IntPtr]$script:AppProcess.MainWindowHandle

function Save-AppShot([string]$Path, [IntPtr]$Window = $script:AppHwnd) {
  [AppWindow]::Capture($Window, [IO.Path]::GetFullPath($Path))
}

function Resize-AppWindow([int]$Width, [int]$Height) {
  $r = New-Object AppWindow+RECT
  [void][AppWindow]::GetWindowRect($script:AppHwnd, [ref]$r)
  # SWP_NOZORDER | SWP_NOACTIVATE keeps the window where it is in the stack.
  [void][AppWindow]::SetWindowPos($script:AppHwnd, [IntPtr]::Zero, $r.Left, $r.Top, $Width, $Height, 0x14)
}

function Send-AppClick([int]$X, [int]$Y, [IntPtr]$Window = $script:AppHwnd) {
  [AppWindow]::Move($Window, $X, $Y, $false); [AppWindow]::Down($Window, $X, $Y); [AppWindow]::Up($Window, $X, $Y)
  Start-Sleep -Milliseconds 400
}

function Send-AppDrag([int]$X0, [int]$Y0, [int]$X1, [int]$Y1, [int]$Steps = 8) {
  [AppWindow]::Move($script:AppHwnd, $X0, $Y0, $false); Start-Sleep -Milliseconds 60
  [AppWindow]::Down($script:AppHwnd, $X0, $Y0); Start-Sleep -Milliseconds 80
  for ($i = 1; $i -le $Steps; $i++) {
    [AppWindow]::Move($script:AppHwnd, [int]($X0 + ($X1 - $X0) * $i / $Steps), [int]($Y0 + ($Y1 - $Y0) * $i / $Steps), $true)
    Start-Sleep -Milliseconds 40
  }
  [AppWindow]::Up($script:AppHwnd, $X1, $Y1); Start-Sleep -Milliseconds 400
}

function Send-AppKeys([int[]]$VirtualKeys, [int]$Repeat = 1) {
  foreach ($vk in $VirtualKeys) {
    for ($i = 0; $i -lt $Repeat; $i++) { [AppWindow]::Key($script:AppHwnd, $vk); Start-Sleep -Milliseconds 80 }
  }
}

function Send-AppWheel([int]$X, [int]$Y, [int]$Notches = -1) {
  for ($i = 0; $i -lt [Math]::Abs($Notches); $i++) {
    [AppWindow]::Wheel($script:AppHwnd, $X, $Y, 120 * [Math]::Sign($Notches)); Start-Sleep -Milliseconds 150
  }
}

function Get-AppPopup {
  [AppWindow]::ProcessWindows([uint32]$script:AppProcess.Id) | Where-Object { $_ -ne $script:AppHwnd } | Select-Object -First 1
}
