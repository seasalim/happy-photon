using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CloudSelectionStateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-cloud-selection-{Guid.NewGuid():N}");

    [AvaloniaFact]
    public async Task LibrarySelection_CloudAfterLocalClearsDerivedEditUi()
    {
        Directory.CreateDirectory(_root);
        var localPath = WriteJpeg("local.jpg");
        var cloudPath = WriteJpeg("cloud.jpg");
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally)
        {
            Resolver = path => path == cloudPath
                ? SourceAvailability.RequiresHydration
                : SourceAvailability.AvailableLocally
        };
        var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: availability);
        try
        {
            var local = new ImageFile(
                localPath,
                SourceAvailability.AvailableLocally)
            {
                EditSettings = new EditSettings { Exposure = 1 }
            };
            var cloud = new ImageFile(
                cloudPath,
                SourceAvailability.AvailableLocally)
            {
                EditSettings = CreateNonDefaultSettings()
            };
            viewModel.Library.SetImages([local, cloud]);
            viewModel.SelectedImage = local;
            viewModel.Histogram = new HistogramData();
            viewModel.IsWhiteBalanceReady = true;
            viewModel.IsWhiteBalancePicking = true;
            Assert.True(viewModel.CopyEditSettingsCommand.CanExecute(null));
            viewModel.CopyEditSettingsCommand.Execute(null);

            viewModel.SelectedImage = cloud;

            Assert.True(cloud.SourceRequiresHydration);
            Assert.Equal(1, viewModel.OnlineOnlyPhotoCount);
            Assert.False(viewModel.CanEditSelectedImage);
            Assert.Null(viewModel.Histogram);
            Assert.Equal(0, viewModel.Exposure);
            Assert.Equal(0, viewModel.Brightness);
            Assert.Equal(0, viewModel.Contrast);
            Assert.Equal(0, viewModel.Saturation);
            Assert.Equal(0, viewModel.Vibrance);
            Assert.Equal(0, viewModel.Shadows);
            Assert.Equal(0, viewModel.Highlights);
            Assert.Equal(0, viewModel.Rotation);
            Assert.Equal(0, viewModel.HorizonRotation);
            Assert.Equal("As Shot", viewModel.SelectedWhiteBalanceMode);
            Assert.Equal(0, viewModel.WhiteBalanceTint);
            Assert.Equal(HlReconstructionMode.Clip, viewModel.HlReconstruction);
            Assert.Null(viewModel.ActivePresetId);
            Assert.Null(viewModel.CurrentCrop);
            Assert.True(viewModel.CurrentCurve!.IsIdentity());
            Assert.False(viewModel.IsWhiteBalanceReady);
            Assert.False(viewModel.IsWhiteBalancePicking);
            Assert.False(viewModel.CopyEditSettingsCommand.CanExecute(null));
            Assert.False(viewModel.PasteEditSettingsCommand.CanExecute(null));
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task LibrarySelection_CloudUsesCachedThumbnailHistogram()
    {
        Directory.CreateDirectory(_root);
        var cloudPath = WriteJpeg("cached-cloud.jpg");
        using var catalog = new CatalogService(Path.Combine(_root, "cached-catalog"));
        await catalog.InitializeAsync();
        var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration));
        try
        {
            var cloud = new ImageFile(
                cloudPath,
                SourceAvailability.RequiresHydration);
            using var source = new MagickImage(MagickColors.Orange, 16, 16);
            viewModel.Library.SetImages([cloud]);
            viewModel.Library.ReplaceThumbnail(
                cloud,
                BitmapConversionService.ConvertToBitmap(source));

            viewModel.SelectedImage = cloud;
            await TestWaits.UntilAsync(() => viewModel.Histogram != null);

            Assert.True(cloud.SourceRequiresHydration);
            Assert.NotNull(cloud.Thumbnail);
            Assert.NotNull(viewModel.Histogram);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task CloudStateChange_NotifiesEditStateOutsideLibrary()
    {
        Directory.CreateDirectory(_root);
        var imagePath = WriteJpeg("outside-library.jpg");
        using var catalog = new CatalogService(Path.Combine(_root, "outside-catalog"));
        await catalog.InitializeAsync();
        var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        try
        {
            var image = new ImageFile(imagePath);
            viewModel.SelectedImage = image;
            var notifications = new List<string?>();
            viewModel.PropertyChanged += (_, args) =>
                notifications.Add(args.PropertyName);

            viewModel.ApplyThumbnailLoadStatus(
                image,
                ThumbnailLoadStatus.DeferredForHydration);

            Assert.False(viewModel.CanEditSelectedImage);
            Assert.Contains(nameof(viewModel.CanEditSelectedImage), notifications);
            Assert.Equal(0, viewModel.OnlineOnlyPhotoCount);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task BatchPaste_RejectsCloudOnlyTargets()
    {
        Directory.CreateDirectory(_root);
        var localPath = WriteJpeg("paste-local.jpg");
        var cloudPath = WriteJpeg("paste-cloud.jpg");
        using var catalog = new CatalogService(Path.Combine(_root, "paste-catalog"));
        await catalog.InitializeAsync();
        var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally)
            {
                Resolver = path => path == cloudPath
                    ? SourceAvailability.RequiresHydration
                    : SourceAvailability.AvailableLocally
            });
        try
        {
            var local = new ImageFile(localPath)
            {
                EditSettings = new EditSettings { Exposure = 1 }
            };
            var cloud = new ImageFile(
                cloudPath,
                SourceAvailability.RequiresHydration)
            {
                EditSettings = new EditSettings { Exposure = 2 }
            };
            viewModel.Library.SetImages([local, cloud]);
            viewModel.SelectedImage = local;
            viewModel.CopyEditSettingsCommand.Execute(null);
            viewModel.ToggleImageSelection(local);
            viewModel.ToggleImageSelection(cloud);
            var confirmations = 0;
            viewModel.ConfirmBatchApplyAsync = _ =>
            {
                confirmations++;
                return Task.FromResult(true);
            };

            await viewModel.PasteEditSettingsCommand.ExecuteAsync(null);

            Assert.Equal(0, confirmations);
            Assert.Equal(2, cloud.EditSettings.Exposure);
            Assert.Equal(
                "Download online-only originals before applying edit settings",
                viewModel.TransientStatus);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WriteJpeg(string name)
    {
        var path = Path.Combine(_root, name);
        TestImages.WriteJpeg(path);
        return path;
    }

    private static EditSettings CreateNonDefaultSettings() => new()
    {
        Exposure = 2,
        Brightness = 20,
        Contrast = 30,
        Saturation = 40,
        Vibrance = 50,
        Shadows = 60,
        Highlights = -70,
        Rotation = 90,
        HorizonRotation = 2,
        Curve = new CurveData
        {
            Points =
            [
                new CurvePoint(0, 0),
                new CurvePoint(0.5, 0.7),
                new CurvePoint(1, 1)
            ]
        }
    };
}
