using HappyPhoton.LibRaw.Interop;

namespace HappyPhoton.Services;

/// <summary>
/// Reads source exposure compensation recorded by supported raw formats.
/// </summary>
internal static class RawExposureBias
{
    internal const double MaxAbsEv = 3.0;

    internal static double Read(LibRawContext context, string filePath)
    {
        try
        {
            var metadata = context.GetMetadata();
            if (!string.Equals(
                    metadata.Make?.Trim(),
                    "Fujifilm",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            var fuji = context.GetFujiFacts();
            if (fuji == null) return 0;
            return FromFuji(
                fuji.ExposureMidpointShift,
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
        uint developmentDynamicRange)
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
