using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class DcpRenderedLabel
{
    // The profile name a render hands the picker's relabel, from the real calculator.
    internal static string For(RawProfileSelection selection)
    {
        var reader = new DcpProfileReader();
        var profile = reader.ParseExternal(reader.ReadExternalSnapshot(selection.Location!), "fallback");
        var facts = new RawCameraFactSnapshot([1, 1, 1], ChromaticAdaptation.Identity(), [1, 1, 1], null, false);
        var result = DcpMatrixCalculator.Create(DcpProfileResolution.Success(selection, profile),
            DcpCameraData.Defaults, facts, 5500);

        return result.Payload!.Name;
    }
}
