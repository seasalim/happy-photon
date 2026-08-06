using System.Runtime.InteropServices;
using Sdcb.LibRaw;
using Sdcb.LibRaw.Natives;

namespace HappyPhoton.Services;

/// <summary>
/// Reads source exposure compensation recorded by supported raw formats.
/// </summary>
internal static class RawExposureBias
{
    internal const double MaxAbsEv = 3.0;

    internal static double Read(RawContext context, string filePath)
    {
        try
        {
            if (!string.Equals(
                    context.ImageParams.Make?.Trim(),
                    "Fujifilm",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            var fujiOffset =
                (int)Marshal.OffsetOf<LibRawData>(nameof(LibRawData.MakerNotes)) +
                (int)Marshal.OffsetOf<LibRawMakerNotes>(nameof(LibRawMakerNotes.Fuji));
            var fuji = Marshal.PtrToStructure<LibRawFujiInfo>(
                context.UnsafeGetHandle() + fujiOffset);
            return FromFuji(
                fuji.ExpoMidPointShift,
                fuji.DevelopmentDynamicRange);
        }
        catch (Exception exception)
        {
            ImageServiceHelpers.LogDebug(
                nameof(RawExposureBias),
                $"Bias read failed: {exception.Message}",
                filePath);
            return 0;
        }
    }

    internal static double FromFuji(
        float expoMidPointShift,
        ushort developmentDynamicRange)
    {
        if (float.IsFinite(expoMidPointShift) &&
            Math.Abs(expoMidPointShift) <= 10)
        {
            return Math.Clamp(
                -expoMidPointShift,
                -MaxAbsEv,
                MaxAbsEv);
        }

        return developmentDynamicRange is 200 or 400
            ? Math.Log2(developmentDynamicRange / 100.0)
            : 0;
    }
}
