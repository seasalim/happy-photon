using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed partial class RawBaseLoader
{
    internal static LibRawOutputConfiguration ConfigureOutput(
        BaseDecodeSettings decode,
        bool preview)
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
        return LibRawOutputConfiguration.LinearCameraNative(
            highlight,
            noiseReduction,
            preview);
    }

    private sealed record LoadedBases(
        PreviewBasePair? Pair,
        BaseImage? Full);
}
