using System.Runtime.InteropServices;
using System.Text;

namespace HappyPhoton.Services;

internal sealed class WindowsDisplayProfilePlatform : IDisplayProfilePlatform
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint QueryOnlyActivePaths = 2;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNotSupported = 50;
    private const int ErrorInvalidParameter = 87;

    public DisplayPlatformResult Resolve(nint windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == 0)
            return new("none", null, DisplayAcmState.Unavailable);

        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == 0) return new("none", null, DisplayAcmState.Failed);
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfo(monitor, ref info))
            return new(monitor.ToString("X"), null, DisplayAcmState.Failed);

        var profile = GetProfilePath(info.DeviceName);
        var acm = GetAcmState(info.DeviceName);
        return new(info.DeviceName, profile, acm);
    }

    private static string? GetProfilePath(string deviceName)
    {
        var dc = CreateDC("DISPLAY", deviceName, null, 0);
        if (dc == 0) return null;
        try
        {
            uint length = 0;
            _ = GetICMProfile(dc, ref length, null);
            if (length == 0) return null;
            var path = new StringBuilder(checked((int)length));
            return GetICMProfile(dc, ref length, path)
                ? path.ToString()
                : null;
        }
        finally
        {
            _ = DeleteDC(dc);
        }
    }

    private static DisplayAcmState GetAcmState(string deviceName)
    {
        if (!TryFindTarget(deviceName, out var adapterId, out var targetId))
            return DisplayAcmState.Failed;

        var info2 = new AdvancedColorInfo2
        {
            Header = DeviceInfoHeader.Create(15, Marshal.SizeOf<AdvancedColorInfo2>(),
                adapterId, targetId)
        };
        var result = DisplayConfigGetDeviceInfo(ref info2);
        if (result == ErrorSuccess)
            return info2.ActiveColorMode == 0 ? DisplayAcmState.Off : DisplayAcmState.On;
        if (result is not ErrorNotSupported and not ErrorInvalidParameter)
            return DisplayAcmState.Failed;

        var info = new AdvancedColorInfo
        {
            Header = DeviceInfoHeader.Create(9, Marshal.SizeOf<AdvancedColorInfo>(),
                adapterId, targetId)
        };
        result = DisplayConfigGetDeviceInfo(ref info);
        if (result == ErrorSuccess)
            return (info.Value & 0b10) != 0 ? DisplayAcmState.On : DisplayAcmState.Off;
        return result is ErrorNotSupported or ErrorInvalidParameter
            ? DisplayAcmState.Unavailable
            : DisplayAcmState.Failed;
    }

    private static bool TryFindTarget(
        string deviceName,
        out Luid adapterId,
        out uint targetId)
    {
        adapterId = default;
        targetId = 0;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = GetDisplayConfigBufferSizes(
                QueryOnlyActivePaths, out var pathCount, out var modeCount);
            if (result != ErrorSuccess) return false;
            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            result = QueryDisplayConfig(
                QueryOnlyActivePaths, ref pathCount, paths,
                ref modeCount, modes, 0);
            if (result == ErrorInsufficientBuffer) continue;
            if (result != ErrorSuccess) return false;

            for (var index = 0; index < pathCount; index++)
            {
                var sourceName = new SourceDeviceName
                {
                    Header = DeviceInfoHeader.Create(
                        1, Marshal.SizeOf<SourceDeviceName>(),
                        paths[index].SourceInfo.AdapterId,
                        paths[index].SourceInfo.Id)
                };
                if (DisplayConfigGetDeviceInfo(ref sourceName) == ErrorSuccess &&
                    string.Equals(sourceName.ViewGdiDeviceName, deviceName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    adapterId = paths[index].TargetInfo.AdapterId;
                    targetId = paths[index].TargetInfo.Id;
                    return true;
                }
            }
            return false;
        }
        return false;
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

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Luid
    {
        public readonly uint LowPart;
        public readonly int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;

        public static DeviceInfoHeader Create(
            uint type, int size, Luid adapterId, uint id) =>
            new() { Type = type, Size = checked((uint)size), AdapterId = adapterId, Id = id };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName
    {
        public DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo
    {
        public DeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo2
    {
        public DeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
        public uint ActiveColorMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rational { public uint Numerator, Denominator; }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct DisplayConfigModeInfo { }

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
    private static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        nint topologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo2 request);
}
