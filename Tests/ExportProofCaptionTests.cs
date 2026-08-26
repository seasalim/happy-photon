using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportProofCaptionTests
{
    [Fact]
    public void SizedPreview_ReportsRecipeCap()
    {
        Assert.Equal(
            "PREVIEW · JPEG · sRGB · 2048 PX",
            MainWindowViewModel.FormatExportProofCaption(
                proofIsDisplayed: false,
                ExportFormat.Jpeg,
                OutputColorSpace.Srgb,
                2048));
    }

    [Fact]
    public void UnresizedHiRes_OmitsSizeSegment()
    {
        Assert.Equal(
            "PROOF · TIFF · sRGB",
            MainWindowViewModel.FormatExportProofCaption(
                proofIsDisplayed: true,
                ExportFormat.Tiff,
                OutputColorSpace.Srgb,
                longEdge: null));
    }

    [Fact]
    public void DisplayP3Proof_PreservesDisplayCasing()
    {
        Assert.Equal(
            "PROOF · PNG · Display P3 · 1024 PX",
            MainWindowViewModel.FormatExportProofCaption(
                proofIsDisplayed: true,
                ExportFormat.Png,
                OutputColorSpace.DisplayP3,
                1024));
    }

    [Fact]
    public void ZeroArmedRecipes_OmitsSizeSegment()
    {
        Assert.Equal(
            "PREVIEW · WEBP · sRGB",
            MainWindowViewModel.FormatExportProofCaption(
                proofIsDisplayed: false,
                ExportFormat.Webp,
                OutputColorSpace.Srgb,
                longEdge: null));
    }
}
