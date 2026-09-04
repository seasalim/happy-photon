using System.Runtime.InteropServices;

namespace HappyPhoton.Services;

internal enum MacOsLayerKind
{
    None,
    NotMetal,
    Metal,
}

internal enum MacOsLayerColorSpace
{
    None,
    Srgb,
    Other,
}

/// <summary>The few native facts the macOS policy needs, so the policy is testable.</summary>
internal interface IMacOsMetalLayer
{
    MacOsLayerKind GetLayerKind(nint nsView);
    MacOsLayerColorSpace GetColorSpace(nint nsView);
    void TagSrgb(nint nsView);
}

/// <summary>
/// macOS color management: the compositor color-matches any window whose CAMetalLayer
/// declares a color space, so tagging Avalonia's layer as sRGB makes the whole window
/// (viewers, thumbnails, chrome) display correctly with no per-frame work. The layer
/// exists only after the first frame, so callers retry until it is tagged.
/// </summary>
internal sealed class MacOsDisplayProfilePlatform(IMacOsMetalLayer? layer = null)
    : IDisplayProfilePlatform
{
    private readonly IMacOsMetalLayer _layer = layer ?? new NativeMetalLayer();

    public DisplayPlatformResult Resolve(nint nsView)
    {
        if (nsView == 0 || _layer.GetLayerKind(nsView) != MacOsLayerKind.Metal)
            return new("macos", null, DisplayAcmState.OsUnmanaged);

        if (_layer.GetColorSpace(nsView) == MacOsLayerColorSpace.None)
            _layer.TagSrgb(nsView);
        return _layer.GetColorSpace(nsView) switch
        {
            MacOsLayerColorSpace.Srgb => new("macos", null, DisplayAcmState.OsManaged),
            MacOsLayerColorSpace.Other => new("macos", null, DisplayAcmState.OsIncompatible),
            _ => new("macos", null, DisplayAcmState.OsUnmanaged),
        };
    }

    private sealed class NativeMetalLayer : IMacOsMetalLayer
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        private const string CoreGraphics =
            "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const uint Utf8Encoding = 0x08000100;

        private static readonly nint LayerSelector = Sel("layer");
        private static readonly nint ColorSpaceSelector = Sel("colorspace");
        private static readonly nint SetColorSpaceSelector = Sel("setColorspace:");
        private static readonly Lazy<nint> SrgbName = new(() =>
            CFStringCreateWithCString(0, "kCGColorSpaceSRGB", Utf8Encoding));
        private static readonly Lazy<nint> SrgbColorSpace = new(() =>
            CGColorSpaceCreateWithName(SrgbName.Value));

        public MacOsLayerKind GetLayerKind(nint nsView)
        {
            var layer = Send(nsView, LayerSelector);
            if (layer == 0) return MacOsLayerKind.None;
            return Marshal.PtrToStringAnsi(object_getClassName(layer)) == "CAMetalLayer"
                ? MacOsLayerKind.Metal
                : MacOsLayerKind.NotMetal;
        }

        public MacOsLayerColorSpace GetColorSpace(nint nsView)
        {
            var colorSpace = Send(Send(nsView, LayerSelector), ColorSpaceSelector);
            if (colorSpace == 0) return MacOsLayerColorSpace.None;
            var name = CGColorSpaceGetName(colorSpace);
            return name != 0 && CFEqual(name, SrgbName.Value)
                ? MacOsLayerColorSpace.Srgb
                : MacOsLayerColorSpace.Other;
        }

        public void TagSrgb(nint nsView) =>
            SendSet(Send(nsView, LayerSelector), SetColorSpaceSelector, SrgbColorSpace.Value);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern nint Send(nint receiver, nint selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern void SendSet(nint receiver, nint selector, nint argument);

        [DllImport(ObjC, EntryPoint = "sel_registerName")]
        private static extern nint Sel(string name);

        [DllImport(ObjC)]
        private static extern nint object_getClassName(nint obj);

        [DllImport(CoreGraphics)]
        private static extern nint CGColorSpaceCreateWithName(nint name);

        [DllImport(CoreGraphics)]
        private static extern nint CGColorSpaceGetName(nint colorSpace);

        [DllImport(CoreFoundation)]
        private static extern nint CFStringCreateWithCString(
            nint allocator,
            string value,
            uint encoding);

        [DllImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CFEqual(nint left, nint right);
    }
}
