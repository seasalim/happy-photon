using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportDialogViewModelTests
{
    [Fact]
    public void Constructor_NormalizesDesktopExportToOneSize()
    {
        var settings = new ExportSettings
        {
            ExportHiRes = true,
            ExportWeb = true,
            ExportSmall = true
        };

        using var viewModel = new ExportDialogViewModel(settings, 4);

        Assert.Equal(ExportSizePreset.HiRes, viewModel.SelectedSize);
        Assert.True(settings.ExportHiRes);
        Assert.False(settings.ExportWeb);
        Assert.False(settings.ExportSmall);
        Assert.Single(settings.GetActiveVariants());
    }

    [Fact]
    public void SelectingWeb_ActivatesOnlyWebVariant()
    {
        var settings = new ExportSettings { WebMaxSize = 2560 };
        using var viewModel = new ExportDialogViewModel(settings, 2);

        viewModel.IsWebSelected = true;

        Assert.False(settings.ExportHiRes);
        Assert.True(settings.ExportWeb);
        Assert.False(settings.ExportSmall);
        Assert.Equal("web", viewModel.SelectedVariant.Name);
        Assert.Equal(2560, viewModel.SelectedVariant.MaxDimension);
        Assert.Single(settings.GetActiveVariants());
    }

    [Fact]
    public void Png_DisplaysLosslessStateAndUpdatesPreview()
    {
        var settings = new ExportSettings();
        using var viewModel = new ExportDialogViewModel(settings, 1);

        viewModel.SelectedFormatOption = viewModel.FormatOptions.Single(
            option => option.Format == ExportFormat.Png);

        Assert.False(viewModel.IsQualityAvailable);
        Assert.True(viewModel.IsLosslessFormat);
        Assert.Equal("example_photo.png", viewModel.PreviewFileName);
    }

    [Fact]
    public void CustomNaming_UpdatesFlatFilenamePreview()
    {
        var settings = new ExportSettings();
        using var viewModel = new ExportDialogViewModel(settings, 1);

        viewModel.SelectedNamingOption = "Custom…";
        settings.NamingPattern = "{name}_delivery";

        Assert.True(viewModel.IsCustomNaming);
        Assert.Equal("example_photo_delivery.jpg", viewModel.PreviewFileName);
        Assert.DoesNotContain("web", viewModel.PreviewFileName);
    }

    [Fact]
    public void ZeroSelection_CannotExport()
    {
        var settings = new ExportSettings { OutputFolder = "exports" };
        using var viewModel = new ExportDialogViewModel(settings, 0);

        Assert.True(viewModel.HasNoImages);
        Assert.False(viewModel.CanExport);
        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.ShowConfiguration);
        Assert.Equal("Export 0 Images", viewModel.HeaderText);
    }

    [Fact]
    public void TourPreview_ShowsFullFormAndReturnActionWithoutImages()
    {
        var settings = new ExportSettings { OutputFolder = "exports" };
        using var viewModel = new ExportDialogViewModel(
            settings,
            0,
            ExportDialogMode.TourPreview);

        Assert.True(viewModel.ShowConfiguration);
        Assert.False(viewModel.ShowEmptyState);
        Assert.True(viewModel.ShowFooterOptions);
        Assert.True(viewModel.ShowPrimaryAction);
        Assert.True(viewModel.CanPrimaryAction);
        Assert.Equal("Return to Library", viewModel.PrimaryActionText);
        Assert.False(viewModel.CanExport);
    }

    [Fact]
    public void ExportProgress_ReplacesIdleActionsUntilExportEnds()
    {
        var settings = new ExportSettings { OutputFolder = "exports" };
        using var viewModel = new ExportDialogViewModel(settings, 3);

        viewModel.BeginExport();
        viewModel.UpdateProgress(2, 3, "photo.jpg");

        Assert.False(viewModel.ShowIdleImageActions);
        Assert.Equal(2, viewModel.ProgressValue);
        Assert.Equal("Exporting 3/3 — photo.jpg", viewModel.ProgressText);

        viewModel.EndExport();

        Assert.True(viewModel.ShowIdleImageActions);
        Assert.True(viewModel.CanExport);
    }
}
