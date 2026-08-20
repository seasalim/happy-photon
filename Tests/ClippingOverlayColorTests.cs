using Avalonia.Media;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ClippingOverlayColorTests
{
    [Fact]
    public void OverlayColorsArePinnedThemeIndependentInvariants()
    {
        Assert.Equal(
            Color.FromArgb(235, 0xff, 0x3b, 0x30),
            HappyPhotonColors.SceneHighlightClipColor);
        Assert.Equal(
            Color.FromArgb(235, 0x2f, 0x6f, 0xed),
            HappyPhotonColors.DisplayFloorClipColor);
    }
}
