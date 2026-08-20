using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportFailureReportingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-export-failures-{Guid.NewGuid():N}");

    public ExportFailureReportingTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task MixedBatch_SurfacesEveryFailureWithoutCompleteProgress()
    {
        var exported = new ImageFile(Path.Combine(_root, "exported.dng"));
        var failedA = new ImageFile(Path.Combine(_root, "failed-a.dng"));
        var failedB = new ImageFile(Path.Combine(_root, "failed-b.dng"));
        var service = new ImageExportService(
            new RenderPipeline(),
            new SelectiveBaseLoader(exported),
            new ExportMetadataService());
        var progress = new RecordingProgress();
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(_root, "output"),
            Format = ExportFormat.Png
        };

        var result = await service.ExportBatchAsync(
            [exported, failedA, failedB],
            settings,
            progress);
        using var viewModel = new ExportDialogViewModel(settings, 3);
        viewModel.BeginExport();
        foreach (var value in progress.Values)
        {
            viewModel.UpdateProgress(
                value.current,
                value.total,
                value.fileName);
        }
        viewModel.ShowPartialExport(result);

        Assert.Equal(1, result.ExportedCount);
        Assert.Equal([failedA, failedB], result.FailedImages);
        Assert.DoesNotContain(
            progress.Values,
            value => value.fileName == "Complete");
        Assert.False(viewModel.IsExporting);
        Assert.True(viewModel.HasError);
        Assert.Contains("Exported 1 of 3 images", viewModel.ErrorMessage);
        Assert.Contains(failedA.FilePath, viewModel.ErrorMessage);
        Assert.Contains(failedB.FilePath, viewModel.ErrorMessage);
        Assert.DoesNotContain("Complete", viewModel.ProgressText);
    }

    [Fact]
    public async Task MissingSelectedProfile_ExportsBuiltInFallbackWithWarning()
    {
        var image = new ImageFile(Path.Combine(_root, "source.dng"));
        var missingProfile = Path.Combine(_root, "missing.dcp");
        image.EditSettings.RawProfile = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = missingProfile,
            ContentHash = new string('a', 64)
        };
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        var service = new ImageExportService(
            new RenderPipeline(),
            new ProfileStatusBaseLoader(),
            new ExportMetadataService("test", availability),
            new DcpProfileService(availability));
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(_root, "profile-output"),
            Format = ExportFormat.Png
        };

        var result = await service.ExportBatchAsync([image], settings);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(1, result.ExportedCount);
        Assert.Equal(image, warning.Image);
        Assert.Equal("profile_missing", warning.Code);
        Assert.Contains("no longer exists", warning.Message);
        Assert.True(File.Exists(Path.Combine(settings.OutputFolder, "source.png")));
    }

    [Fact]
    public async Task VariantCountOverload_PropagatesProfileWarning()
    {
        var image = new ImageFile(Path.Combine(_root, "variant.dng"));
        image.EditSettings.RawProfile = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = Path.Combine(_root, "missing-variant.dcp"),
            ContentHash = new string('b', 64)
        };
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        var service = new ImageExportService(
            new RenderPipeline(),
            new ProfileStatusBaseLoader(),
            new ExportMetadataService("test", availability),
            new DcpProfileService(availability));
        var warningProgress = new RecordingWarningProgress();
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(_root, "variant-output")
        };

        var count = await service.ExportBatchAsync(
            [image],
            settings,
            [new ExportVariant("hi-res", null)],
            useSubfolders: false,
            cancellationToken: CancellationToken.None,
            warningProgress: warningProgress);

        Assert.Equal(1, count);
        Assert.Equal("profile_missing", Assert.Single(warningProgress.Values).Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class SelectiveBaseLoader(ImageFile successfulImage) :
        IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.FromImage(
                LoadPreviewBase(file, decode, cancellationToken),
                BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            file != successfulImage
                ? null
                : new BaseImage(
                    new MagickImage(MagickColors.Orange, 16, 16),
                    new BaseImageInfo(
                        BaseSourceKind.Standard,
                        false,
                        decode,
                        null,
                        null,
                        6504,
                        0,
                        false,
                        null,
                        1,
                        16,
                        16));
    }

    private sealed class RecordingProgress :
        IProgress<(int current, int total, string fileName)>
    {
        public List<(int current, int total, string fileName)> Values { get; } = [];

        public void Report((int current, int total, string fileName) value) =>
            Values.Add(value);
    }

    private sealed class RecordingWarningProgress : IProgress<ExportWarning>
    {
        internal List<ExportWarning> Values { get; } = [];

        public void Report(ExportWarning value) => Values.Add(value);
    }

    private sealed class ProfileStatusBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var resolution = decode.ProfileResolution ??
                DcpProfileResolution.BuiltIn;
            return new BaseImage(
                new MagickImage(MagickColors.Orange, 16, 16),
                new BaseImageInfo(
                    BaseSourceKind.RawLibRaw,
                    true,
                    decode,
                    [1, 1, 1],
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    16,
                    16)
                {
                    ProfileToken = resolution.Token,
                    ProfileStatus = resolution.Status,
                    ProfileMessage = resolution.Message
                });
        }
    }
}
