namespace HappyPhoton.LibRaw.Interop;

public static class LibRawOrientation
{
    // LibRaw 0.22.2 src/metadata/tiff.cpp:620-621, indexed by EXIF minus one.
    private static readonly int[] NativeFlips = [0, 1, 3, 2, 4, 6, 7, 5];

    public static int FromNativeFlip(int flip) =>
        flip is >= 0 and <= 7 ? Array.IndexOf(NativeFlips, flip) + 1 : 1;

    public static int ToNativeFlip(int orientation) =>
        NativeFlips[orientation is >= 1 and <= 8 ? orientation - 1 : 0];
}
