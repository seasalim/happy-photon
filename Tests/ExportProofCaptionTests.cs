using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportProofCaptionTests
{
    [Theory]
    [InlineData(false, "Web", 2048, OutputColorSpace.DisplayP3, "Preview · edits applied")]
    [InlineData(true, "Full size", null, OutputColorSpace.Srgb, "Proof · Full size · No resizing · sRGB")]
    [InlineData(true, "Web", 2048, OutputColorSpace.Srgb, "Proof · Web · 2048 px · sRGB")]
    [InlineData(true, "Small", 1024, OutputColorSpace.DisplayP3, "Proof · Small · 1024 px · Display P3")]
    public void CaptionNamesAcceptedSizeCapAndColorSpace(
        bool displayed, string name, int? cap, OutputColorSpace colorSpace, string expected) =>
        Assert.Equal(expected, MainWindowViewModel.FormatExportProofCaption(
            displayed, new ExportProofSize(name, cap), colorSpace));
}
