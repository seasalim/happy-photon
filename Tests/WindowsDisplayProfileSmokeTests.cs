using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WindowsDisplayProfileSmokeTests
{
    [Fact]
    public void DesktopResolution_MatchesIndependentProfileOracleWithoutLeaks()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows-only display profile smoke.");
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_COMPAT") != "1",
            "Set HAPPY_PHOTON_COMPAT=1 to run the desktop display profile smoke.");
        var window = GetDesktopWindow();
        var expected = ResolveProfileIndependently(window);
        var platform = new WindowsDisplayProfilePlatform();
        var actual = platform.Resolve(window);

        Assert.Equal(expected ?? string.Empty, actual.ProfilePath ?? string.Empty,
            ignoreCase: true);
        Assert.NotEqual(DisplayAcmState.Failed, actual.AcmState);

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var handlesBefore = process.HandleCount;
        var gdiBefore = GetGuiResources(process.Handle, 0);
        for (var index = 0; index < 200; index++)
            _ = platform.Resolve(window);
        process.Refresh();
        var handlesAfter = process.HandleCount;
        var gdiAfter = GetGuiResources(process.Handle, 0);

        Assert.InRange(handlesAfter - handlesBefore, -2, 2);
        Assert.InRange((int)gdiAfter - (int)gdiBefore, -2, 2);
    }

    private static string? ResolveProfileIndependently(nint window)
    {
        var monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        Assert.NotEqual(0, monitor);
        Assert.True(GetMonitorInfo(monitor, ref info));
        var dc = CreateDC("DISPLAY", info.DeviceName, null, 0);
        Assert.NotEqual(0, dc);
        try
        {
            uint length = 0;
            _ = GetICMProfile(dc, ref length, null);
            if (length == 0) return null;
            var path = new StringBuilder(checked((int)length));
            return GetICMProfile(dc, ref length, path) ? path.ToString() : null;
        }
        finally
        {
            Assert.True(DeleteDC(dc));
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateDC(
        string driver, string device, string? output, nint initData);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetICMProfile(
        nint dc, ref uint bufferSize, StringBuilder? fileName);

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(nint process, uint flags);
}
