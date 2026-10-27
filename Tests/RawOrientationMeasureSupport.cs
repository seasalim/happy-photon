using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class RawOrientationMeasureSupport
{
    internal static readonly BaseDecodeSettings Off = new(
        HlReconstructionMode.Clip, Distortion: false, ChromaticAberration: false, Vignetting: false);

    internal static BaseImage Load(string path, bool preview, BaseDecodeSettings? decode = null)
    {
        SyncProfileGateSupport.RequireLocal(path);
        var loader = new GatedBaseImageLoader(new RawBaseLoader(), new SourceAvailabilityService());
        var file = new ImageFile(path);
        var result = preview ? loader.LoadPreviewBase(file, decode ?? Off, CancellationToken.None)
            : loader.LoadFullBase(file, decode ?? Off, CancellationToken.None);
        Assert.NotNull(result);

        return result;
    }
}
