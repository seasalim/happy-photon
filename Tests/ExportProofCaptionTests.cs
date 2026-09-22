using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportProofCaptionTests
{
    [Theory]
    [InlineData(false, "Web", 2048, OutputColorSpace.DisplayP3, "PREVIEW · edits applied")]
    [InlineData(true, "Full size", null, OutputColorSpace.Srgb, "PROOF · Full size · No resizing · sRGB")]
    [InlineData(true, "Web", 2048, OutputColorSpace.Srgb, "PROOF · Web · 2048 PX · sRGB")]
    [InlineData(true, "Small", 1024, OutputColorSpace.DisplayP3, "PROOF · Small · 1024 PX · Display P3")]
    public void CaptionNamesAcceptedSizeCapAndColorSpace(
        bool displayed, string name, int? cap, OutputColorSpace colorSpace, string expected) =>
        Assert.Equal(expected, MainWindowViewModel.FormatExportProofCaption(
            displayed, new ExportProofSize(name, cap), colorSpace));
}
