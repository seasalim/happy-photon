using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportFailureReportingTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [Fact]
    public async Task MixedBatch_SurfacesEveryFailureWithoutCompleteProgress()
    {
        var exported = new ImageFile(Path.Combine(_root.Path, "exported.dng"));
        var failedA = new ImageFile(Path.Combine(_root.Path, "failed-a.dng"));
        var failedB = new ImageFile(Path.Combine(_root.Path, "failed-b.dng"));
        var service = new ImageExportService(
            new RenderPipeline(),
            new SelectiveBaseLoader(exported),
            new ExportMetadataService());
        var progress = new RecordingProgress();
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(_root.Path, "output"),
            Format = ExportFormat.Png
        };

        var result = await service.ExportBatchAsync(
            [exported, failedA, failedB],
            settings,
            progress);
        var report = ExportRunReport.FromResult(result);

        Assert.Equal(1, result.ExportedCount);
        Assert.Equal([failedA, failedB], result.FailedImages);
        Assert.Equal("Export finished with failures", report.Heading);
        Assert.Equal("1 of 3 files exported.", report.Summary);
        Assert.Equal(2, report.FailedTargets.Count);
    }

    [Fact]
    public async Task MissingSelectedProfile_ExportsBuiltInFallbackWithWarning()
    {
        var image = new ImageFile(Path.Combine(_root.Path, "source.dng"));
        var missingProfile = Path.Combine(_root.Path, "missing.dcp");
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
            OutputFolder = Path.Combine(_root.Path, "profile-output"),
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
        var image = new ImageFile(Path.Combine(_root.Path, "variant.dng"));
        image.EditSettings.RawProfile = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = Path.Combine(_root.Path, "missing-variant.dcp"),
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
            OutputFolder = Path.Combine(_root.Path, "variant-output")
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

    public void Dispose() => _root.Dispose();

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
