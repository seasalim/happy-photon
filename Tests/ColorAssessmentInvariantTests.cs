using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ColorAssessmentInvariantTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-assessment-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task ToggleCommand_IsGatedToDevelopAndFullScreen()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await using var vm = CreateViewModel(catalog);
        var notifications = 0;
        vm.ToggleColorAssessmentModeCommand.CanExecuteChanged +=
            (_, _) => notifications++;

        Assert.False(vm.ToggleColorAssessmentModeCommand.CanExecute(null));
        vm.ToggleColorAssessmentModeCommand.Execute(null);
        Assert.False(vm.IsColorAssessmentMode);

        vm.IsDevelopMode = true;
        Assert.True(vm.ToggleColorAssessmentModeCommand.CanExecute(null));
        vm.ToggleColorAssessmentModeCommand.Execute(null);
        Assert.True(vm.IsColorAssessmentMode);
        Assert.Equal("Reference field is complete at Fit", vm.TransientStatus);

        vm.IsDevelopMode = false;
        vm.IsFullScreenMode = true;
        Assert.True(vm.ToggleColorAssessmentModeCommand.CanExecute(null));
        vm.ToggleColorAssessmentModeCommand.Execute(null);
        Assert.False(vm.IsColorAssessmentMode);
        Assert.True(notifications >= 3);
    }

    [Fact]
    public async Task Toggle_DoesNotChangePipelineStateOrPersistence()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root, "photo.jpg"))
        {
            EditSettings = new EditSettings
            {
                Exposure = 0.75,
                Contrast = 12,
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Custom,
                    Kelvin = 6100,
                    Tint = -8
                }
            }
        };
        var histogram = new HistogramData();
        vm.SelectedImage = image;
        vm.Histogram = histogram;
        vm.IsDevelopMode = true;
        vm.IsCropMode = true;
        vm.IsWhiteBalancePicking = true;
        vm.IsShowingOriginal = true;
        var settingsJson = EditSettingsJson.Serialize(image.EditSettings);
        var settingsHash = RenderSettingsHash.Compute(image.EditSettings);
        var persistCalls = 0;
        vm.PersistAppSettingsAsync = () =>
        {
            persistCalls++;
            return Task.CompletedTask;
        };

        vm.ToggleColorAssessmentModeCommand.Execute(null);

        Assert.Same(histogram, vm.Histogram);
        Assert.True(vm.IsCropMode);
        Assert.True(vm.IsWhiteBalancePicking);
        Assert.True(vm.IsShowingOriginal);
        Assert.Equal(settingsJson, EditSettingsJson.Serialize(image.EditSettings));
        Assert.Equal(settingsHash, RenderSettingsHash.Compute(image.EditSettings));
        Assert.Equal(0, persistCalls);
        Assert.DoesNotContain(
            typeof(AppSettings).GetProperties(),
            property => property.Name.Contains(
                "Assessment",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Toggle_DoesNotChangeExportBytes()
    {
        var sourcePath = Path.Combine(_root, "source.png");
        using (var source = new MagickImage(MagickColors.Orange, 160, 100))
        {
            source.Write(sourcePath);
        }

        using var catalog = new CatalogService(Path.Combine(_root, "catalog-export"));
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(sourcePath)
        {
            EditSettings = new EditSettings { Exposure = 0.25, Saturation = 8 }
        };
        vm.SelectedImage = image;
        vm.IsDevelopMode = true;
        var service = new ImageExportService(
            new RenderPipeline(),
            new StandardBaseLoader(),
            new ExportMetadataService());
        var beforeFolder = Path.Combine(_root, "before");
        var afterFolder = Path.Combine(_root, "after");

        await service.ExportBatchAsync(
            [image],
            new ExportSettings
            {
                OutputFolder = beforeFolder,
                Format = ExportFormat.Png
            });
        vm.ToggleColorAssessmentModeCommand.Execute(null);
        await service.ExportBatchAsync(
            [image],
            new ExportSettings
            {
                OutputFolder = afterFolder,
                Format = ExportFormat.Png
            });

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(beforeFolder, "source.png")),
            File.ReadAllBytes(Path.Combine(afterFolder, "source.png")));
    }

    [Fact]
    public async Task EscapeLadder_DoesNotConsumeAssessmentMode()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog-escape"));
        await using var vm = CreateViewModel(catalog);
        vm.IsDevelopMode = true;
        vm.ToggleColorAssessmentModeCommand.Execute(null);
        vm.IsFullScreenMode = true;

        vm.HandleEscapeCommand.Execute(null);
        Assert.False(vm.IsFullScreenMode);
        Assert.True(vm.IsDevelopMode);
        Assert.True(vm.IsColorAssessmentMode);

        vm.HandleEscapeCommand.Execute(null);
        Assert.False(vm.IsDevelopMode);
        Assert.True(vm.IsColorAssessmentMode);
    }

    [Fact]
    public void ShortcutCatalog_ListsColorAssessmentMode()
    {
        Assert.Contains(
            ShortcutCatalog.Groups.SelectMany(group => group.Entries),
            entry => entry.Keys == "Ctrl+B" &&
                     entry.Action.Contains(
                         "color assessment",
                         StringComparison.OrdinalIgnoreCase));
    }

    private static MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        new(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
