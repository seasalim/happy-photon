using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportJobContractTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-export-job-{Guid.NewGuid():N}");

    public ExportJobContractTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ExportBatch_SettingsMutationAfterStartCannotChangeOutputs()
    {
        var sourcePath = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "srgb-exif-gps-orientation-6.jpg");
        var expectedFolder = Path.Combine(_root, "expected");
        var actualFolder = Path.Combine(_root, "actual");
        var expectedSettings = CreateSnapshotSettings(expectedFolder);
        var actualSettings = CreateSnapshotSettings(actualFolder);

        var expected = await CreateService(new StandardBaseLoader())
            .ExportBatchAsync([new ImageFile(sourcePath)], expectedSettings);
        var loader = new CallbackBaseLoader(new StandardBaseLoader(), () =>
        {
            actualSettings.OutputFolder = Path.Combine(_root, "mutated");
            actualSettings.Quality = 1;
            actualSettings.Format = ExportFormat.Png;
            actualSettings.OutputColorSpace = OutputColorSpace.Srgb;
            actualSettings.ExportHiRes = false;
            actualSettings.ExportWeb = false;
            actualSettings.ExportSmall = false;
            actualSettings.WebMaxSize = 600;
            actualSettings.SmallMaxSize = 500;
            actualSettings.NamingPattern = "{name}_mutated";
            actualSettings.StripLocationData = false;
            actualSettings.OutputSharpening = OutputSharpeningMode.Off;
        });

        var actual = await CreateService(loader)
            .ExportBatchAsync([new ImageFile(sourcePath)], actualSettings);

        Assert.Equal(3, actual.SuccessfulTargetCount);
        Assert.All(actual.Outcomes, outcome => Assert.True(outcome.Succeeded));
        Assert.Equal(
            expected.Outcomes.Select(RelativeTarget),
            actual.Outcomes.Select(RelativeTarget));
        foreach (var relativePath in actual.Outcomes.Select(RelativeTarget))
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(expectedFolder, relativePath)),
                File.ReadAllBytes(Path.Combine(actualFolder, relativePath)));
        }

        Assert.False(Directory.Exists(Path.Combine(_root, "mutated")));
        using var hiRes = new MagickImage(Path.Combine(
            actualFolder,
            "hi-res",
            "srgb-exif-gps-orientation-6_before.jpg"));
        Assert.Equal(MagickFormat.Jpeg, hiRes.Format);
        Assert.Equal(92u, hiRes.Quality);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(
                GoldenTestPaths.AssetDirectory,
                "DisplayP3-v4.icc")),
            hiRes.GetColorProfile()!.ToByteArray());
        Assert.DoesNotContain(
            hiRes.GetExifProfile()!.Values,
            value => value.Tag.Ifd == ExifIfds.Gps);
        AssertLongEdge(actualFolder, "web", 48);
        AssertLongEdge(actualFolder, "small", 24);
    }

    [Fact]
    public async Task ExportBatch_ReportsEachTargetAndSharesCaptureRender()
    {
        var sourcePath = WriteSourceImage("source.png");
        var outputFolder = Path.Combine(_root, "partial");
        Directory.CreateDirectory(outputFolder);
        File.WriteAllText(Path.Combine(outputFolder, "web"), "blocks folder");
        var capture = new ImageFile(sourcePath);
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Png,
            ExportWeb = true,
            WebMaxSize = 32
        };
        var loader = new CountingBaseLoader();
        var renderCount = 0;
        var pipeline = new RenderPipeline();
        var service = new ImageExportService(
            pipeline,
            loader,
            new ExportMetadataService(),
            new DcpProfileService(new SourceAvailabilityService()),
            request =>
            {
                renderCount++;
                return pipeline.RenderDisplayRec2020(request);
            });

        var result = await service.ExportBatchAsync([capture], settings);

        Assert.Equal(1, loader.FullLoadCount);
        Assert.Equal(1, renderCount);
        Assert.Equal(2, result.Outcomes.Count);
        Assert.Equal(1, result.SuccessfulTargetCount);
        Assert.Equal(0, result.ExportedCount);
        Assert.Equal(capture, Assert.Single(result.FailedImages));
        var succeeded = Assert.Single(result.Outcomes, outcome => outcome.Succeeded);
        Assert.Equal("hi-res", succeeded.Recipe.Name);
        Assert.True(File.Exists(succeeded.ResolvedPath));
        var failed = Assert.Single(result.FailedTargets);
        Assert.Equal(capture, failed.Capture);
        Assert.Equal("web", failed.Recipe.Name);
        Assert.Equal(Path.Combine(outputFolder, "web", "source.png"), failed.ResolvedPath);
        Assert.False(string.IsNullOrWhiteSpace(failed.FailureReason));
    }

    [Fact]
    public async Task ExportBatch_ZeroArmedRecipesDoesNoPixelWork()
    {
        var loader = new CountingBaseLoader();
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(_root, "none"),
            ExportHiRes = false
        };

        var result = await CreateService(loader).ExportBatchAsync(
            [new ImageFile(WriteSourceImage("none.png"))],
            settings);

        Assert.Empty(result.Outcomes);
        Assert.Equal(0, result.ExportedCount);
        Assert.Equal(0, loader.FullLoadCount);
        Assert.False(Directory.Exists(settings.OutputFolder));
    }

    [Fact]
    public async Task Progress_ReportsEveryTargetThroughCompletion()
    {
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(_root, "progress"),
            Format = ExportFormat.Png,
            ExportWeb = true,
            ExportSmall = true
        };
        var progress = new RecordingProgress();

        await CreateService(new CountingBaseLoader()).ExportBatchAsync(
            [
                new ImageFile(WriteSourceImage("progress-a.png")),
                new ImageFile(WriteSourceImage("progress-b.png"))
            ],
            settings,
            progress);

        Assert.Equal(
            Enumerable.Range(0, 7),
            progress.Values.Select(value => value.current));
        Assert.All(progress.Values, value =>
        {
            Assert.Equal(6, value.total);
            Assert.InRange(value.current, 0, value.total);
        });
    }

    [Fact]
    public async Task FileCreatedAfterPreflight_IsNotOverwritten()
    {
        var outputFolder = Path.Combine(_root, "race");
        var targetPath = Path.Combine(outputFolder, "race.png");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Png
        };
        var loader = new CallbackBaseLoader(new StandardBaseLoader(), () =>
        {
            Directory.CreateDirectory(outputFolder);
            File.WriteAllText(targetPath, "appeared after preflight");
        });

        var result = await CreateService(loader).ExportBatchAsync(
            [new ImageFile(WriteSourceImage("race.png"))],
            settings);

        Assert.Equal("appeared after preflight", File.ReadAllText(targetPath));
        Assert.False(Assert.Single(result.Outcomes).Succeeded);
    }

    [Fact]
    public void FailedTargetProjection_PreservesOnlyRequestedSnapshots()
    {
        var first = new ImageFile(Path.Combine(_root, "a.jpg"))
        {
            EditSettings = new EditSettings { Exposure = 1 }
        };
        var second = new ImageFile(Path.Combine(_root, "b.jpg"))
        {
            EditSettings = new EditSettings { Exposure = 2 }
        };
        var job = new ExportSettings
        {
            OutputFolder = Path.Combine(_root, "subset")
        }.CreateJob(
            [first, second],
            [new ExportVariant("web", 2048), new ExportVariant("small", 1024)],
            useSubfolders: true);

        var subset = job.ProjectTargets([job.Targets[1], job.Targets[2]]);
        first.EditSettings.Exposure = 8;
        second.EditSettings.Exposure = 9;

        Assert.Equal(2, subset.Targets.Count);
        Assert.Equal(["small", "web"], subset.Targets.Select(t => t.Recipe.Name));
        Assert.Equal(1, subset.GetEditSettings(first).Exposure);
        Assert.Equal(2, subset.GetEditSettings(second).Exposure);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ExportSettings CreateSnapshotSettings(string outputFolder) => new()
    {
        OutputFolder = outputFolder,
        Quality = 92,
        Format = ExportFormat.Jpeg,
        OutputColorSpace = OutputColorSpace.DisplayP3,
        ExportHiRes = true,
        ExportWeb = true,
        ExportSmall = true,
        WebMaxSize = 48,
        SmallMaxSize = 24,
        NamingPattern = "{name}_before",
        StripLocationData = true,
        OutputSharpening = OutputSharpeningMode.Print
    };

    private static string RelativeTarget(ExportTargetOutcome outcome) =>
        Path.Combine(outcome.Recipe.Name, Path.GetFileName(outcome.ResolvedPath));

    private static ImageExportService CreateService(IBaseImageLoader loader) => new(
        new RenderPipeline(),
        loader,
        new ExportMetadataService());

    private static void AssertLongEdge(string root, string recipe, uint expected)
    {
        using var image = new MagickImage(Path.Combine(
            root,
            recipe,
            "srgb-exif-gps-orientation-6_before.jpg"));
        Assert.Equal(expected, Math.Max(image.Width, image.Height));
    }

    private string WriteSourceImage(string name)
    {
        var path = Path.Combine(_root, name);
        using var image = new MagickImage(MagickColors.Orange, 64, 48);
        image.Write(path);
        return path;
    }

    private sealed class CallbackBaseLoader(
        IBaseImageLoader inner,
        Action callback) : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => inner.CanLoad(file);

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            inner.LoadPreviewBaseWithOutcome(file, decode, cancellationToken);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            inner.LoadPreviewBase(file, decode, cancellationToken);

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            callback();
            return inner.LoadFullBase(file, decode, cancellationToken);
        }
    }

    private sealed class CountingBaseLoader : IBaseImageLoader
    {
        public int FullLoadCount { get; private set; }
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
            FullLoadCount++;
            return new BaseImage(
                new MagickImage(MagickColors.Orange, 64, 48),
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
                    64,
                    48));
        }
    }

    private sealed class RecordingProgress :
        IProgress<(int current, int total, string fileName)>
    {
        public List<(int current, int total, string fileName)> Values { get; } = [];

        public void Report((int current, int total, string fileName) value) =>
            Values.Add(value);
    }
}
