using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed partial class RawBaseLoader
{
    internal static LibRawOutputConfiguration ConfigureOutput(
        BaseDecodeSettings decode,
        bool preview,
        bool isMonochrome = false)
    {
        var highlight = decode.HlReconstruction switch
        {
            HlReconstructionMode.Blend => LibRawHighlightMode.Blend,
            HlReconstructionMode.Clip => LibRawHighlightMode.Clip,
            _ => throw new InvalidOperationException(
                $"Unsupported highlight reconstruction mode: {decode.HlReconstruction}.")
        };
        var noiseReduction = decode.NoiseReduction switch
        {
            FbddMode.Off => LibRawFbddMode.Off,
            FbddMode.Light => LibRawFbddMode.Light,
            FbddMode.Full => LibRawFbddMode.Full,
            _ => throw new InvalidOperationException(
                $"Unsupported FBDD mode: {decode.NoiseReduction}.")
        };
        var configuration = LibRawOutputConfiguration.LinearCameraNative(
            highlight,
            noiseReduction,
            preview);
        return !isMonochrome ? configuration : configuration with
        {
            HalfSize = false,
            UseCameraWhiteBalance = false,
            UseAutoWhiteBalance = false,
            UserMultiplier0 = 1,
            UserMultiplier1 = 1,
            UserMultiplier2 = 1,
            UserMultiplier3 = 1,
            UseCameraMatrix = false
        };
    }

    internal static bool IsMonochromeSensor(LibRawSensorIdentity identity) =>
        identity.Colors == 1;

    internal static bool HasExpectedProcessedLayout(
        bool isMonochrome,
        uint channels) => channels == (isMonochrome ? 1u : 3u);

    private sealed record LoadedBases(
        PreviewBasePair? Pair,
        BaseImage? Full,
        PreviewSourceAnalysis Analysis);
}
