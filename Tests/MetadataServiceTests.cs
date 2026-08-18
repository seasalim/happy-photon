using System.Collections.Concurrent;
using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MetadataServiceTests
{
    [Fact]
    public void ExtractMetadata_ReadsDimensionsWithoutMutatingImageFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"HappyPhotonMetadata_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "image.jpg");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 320, 240))
            {
                source.Write(path, MagickFormat.Jpeg);
            }
            var image = new ImageFile(path);

            var metadata = MetadataService.ExtractMetadata(
                image,
                new LibRawProcessingService());

            Assert.Equal(320, metadata.PixelWidth);
            Assert.Equal(240, metadata.PixelHeight);
            Assert.True(metadata.FileSize > 0);
            Assert.False(image.MetadataLoaded);
            Assert.Equal(0, image.PixelWidth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExtractMetadata_ReadsExtendedExifAndFileModifiedDate()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"HappyPhotonExtendedMetadata_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "image.jpg");
        var modified = new DateTime(2026, 8, 12, 9, 30, 0);
        try
        {
            using (var source = new MagickImage(MagickColors.Blue, 600, 400))
            {
                var profile = new ExifProfile();
                profile.SetValue(
                    ExifTag.GPSLatitude,
                    new[] { new Rational(47), new Rational(36), new Rational(30) });
                profile.SetValue(ExifTag.GPSLatitudeRef, "N");
                profile.SetValue(
                    ExifTag.GPSLongitude,
                    new[] { new Rational(122), new Rational(19), new Rational(15) });
                profile.SetValue(ExifTag.GPSLongitudeRef, "W");
                profile.SetValue(ExifTag.GPSAltitude, new Rational(12.5));
                profile.SetValue(ExifTag.GPSAltitudeRef, (byte)1);
                profile.SetValue(
                    ExifTag.ExposureBiasValue,
                    new SignedRational(2, 3));
                profile.SetValue(ExifTag.FocalLength, new Rational(70));
                profile.SetValue(ExifTag.FocalLengthIn35mmFilm, (ushort)105);
                profile.SetValue(ExifTag.ExposureTime, new Rational(10, 3200));
                source.SetProfile(profile);
                source.Write(path, MagickFormat.Jpeg);
            }
            File.SetLastWriteTime(path, modified);

            var metadata = MetadataService.ExtractMetadata(
                new ImageFile(path),
                new LibRawProcessingService());

            Assert.Equal(modified, metadata.FileModifiedDate);
            Assert.Equal(47.608333, metadata.GpsLatitude!.Value, 6);
            Assert.Equal(-122.320833, metadata.GpsLongitude!.Value, 6);
            Assert.Equal(-12.5, metadata.GpsAltitude);
            Assert.Equal(2.0 / 3.0, metadata.ExposureBias!.Value, 6);
            Assert.Equal(70, metadata.FocalLength);
            Assert.Equal(105, metadata.FocalLengthIn35mmFilm);
            Assert.Equal("1/320", metadata.ExposureTime);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RawHeaderPing_RemainsAvailableWithoutFullRasterDecode()
    {
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "canon-eos-350d.cr2");

        var metadata = MetadataService.ExtractMetadata(
            new ImageFile(path),
            new UnavailableRawService());

        Assert.True(metadata.PixelWidth > 0);
        Assert.True(metadata.PixelHeight > 0);
    }

    [Theory]
    [InlineData(0.4, "0.4")]
    [InlineData(0.8, "0.8")]
    [InlineData(0.5, "1/2")]
    [InlineData(0.0031250001, "1/320")]
    [InlineData(1.0 / 60, "1/60")]
    [InlineData(2.5, "2.5")]
    public void FormatExposureTime_KeepsNonUnitFractionsExact(
        double seconds,
        string expected)
    {
        Assert.Equal(expected, MetadataService.FormatExposureTime(seconds));
    }

    [Fact]
    public void ExtractMetadata_RealRawAssets_ReadExposureBiasAndGps()
    {
        var raw = new LibRawProcessingService();

        var pentax = MetadataService.ExtractMetadata(
            new ImageFile(Path.Combine(
                GoldenTestPaths.AssetDirectory, "pentax-k-r.dng")),
            raw);
        Assert.NotNull(pentax.ExposureBias);
        Assert.Equal(-0.7, pentax.ExposureBias.Value, 3);

        var canon = MetadataService.ExtractMetadata(
            new ImageFile(Path.Combine(
                GoldenTestPaths.AssetDirectory, "canon-eos-6d-iso-6400.cr2")),
            raw);
        Assert.Equal(0, canon.ExposureBias);
        Assert.NotNull(canon.GpsLatitude);
        Assert.Equal(47.544, canon.GpsLatitude.Value, 3);
        Assert.NotNull(canon.GpsAltitude);
        Assert.Equal(614.2, canon.GpsAltitude.Value, 1);
    }

    [Fact]
    public void ExtractMetadata_RawPathSupplementsOnlyExposureBias()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"HappyPhotonRawBias_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "image.dng");
        try
        {
            using (var source = new MagickImage(MagickColors.Green, 20, 10))
            {
                var profile = new ExifProfile();
                profile.SetValue(
                    ExifTag.ExposureBiasValue,
                    new SignedRational(-1, 3));
                profile.SetValue(ExifTag.FocalLengthIn35mmFilm, (ushort)999);
                source.SetProfile(profile);
                source.Write(path, MagickFormat.Jpeg);
            }
            var raw = new FixedRawService(new RawMetadata
            {
                PixelWidth = 6000,
                PixelHeight = 4000,
                ExposureTime = 0.0031250001,
                FocalLengthIn35mmFilm = 85,
                GpsLatitude = 10,
                GpsLongitude = 20,
                GpsAltitude = 30
            });

            var supplementReads = 0;
            var metadata = MetadataService.ExtractMetadata(
                new ImageFile(path),
                raw,
                supplementPath =>
                {
                    supplementReads++;
                    Assert.Equal(path, supplementPath);
                    return -1.0 / 3.0;
                });

            Assert.Equal(6000, metadata.PixelWidth);
            Assert.Equal("1/320", metadata.ExposureTime);
            Assert.Equal(1, supplementReads);
            Assert.Equal(-1.0 / 3.0, metadata.ExposureBias!.Value, 6);
            Assert.Equal(85, metadata.FocalLengthIn35mmFilm);
            Assert.Equal(10, metadata.GpsLatitude);
            Assert.Equal(20, metadata.GpsLongitude);
            Assert.Equal(30, metadata.GpsAltitude);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ConcurrentCallersShareOneExtraction()
    {
        var extractionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExtraction = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var extractionCount = 0;
        var service = new MetadataService(
            _ =>
            {
                Interlocked.Increment(ref extractionCount);
                extractionStarted.SetResult();
                releaseExtraction.Task.GetAwaiter().GetResult();
                return new ImageMetadata { PixelWidth = 6000, PixelHeight = 4000 };
            },
            action =>
            {
                action();
                return Task.CompletedTask;
            });
        var image = new ImageFile("test.jpg");

        var loads = Enumerable.Range(0, 40)
            .Select(_ => service.LoadAsync(image))
            .ToArray();
        await extractionStarted.Task.WaitAsync(TestWaits.Condition);
        releaseExtraction.SetResult();
        await Task.WhenAll(loads);

        Assert.Equal(1, extractionCount);
        Assert.True(image.MetadataLoaded);
        Assert.Equal(6000, image.PixelWidth);
        Assert.Equal(4000, image.PixelHeight);
    }

    [Fact]
    public async Task LoadAsync_AppliesAllObservablePropertiesOnDispatcherThread()
    {
        using var dispatcher = new SingleThreadActionDispatcher();
        var service = new MetadataService(
            _ => new ImageMetadata
            {
                FileSize = 42,
                PixelWidth = 300,
                PixelHeight = 200,
                CameraMake = "Test",
                Iso = 100
            },
            dispatcher.InvokeAsync);
        var image = new ImageFile("test.jpg");
        var notificationThreads = new ConcurrentBag<int>();
        image.PropertyChanged += (_, _) =>
            notificationThreads.Add(Environment.CurrentManagedThreadId);

        await service.LoadAsync(image);

        Assert.NotEmpty(notificationThreads);
        Assert.All(notificationThreads, id => Assert.Equal(dispatcher.ThreadId, id));
        Assert.True(image.MetadataLoaded);
    }

    [Fact]
    public async Task LoadAsync_DoesNotCompleteBeforeDispatcherApplication()
    {
        var applyStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseApply = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new MetadataService(
            _ => new ImageMetadata { DateTaken = new DateTime(2026, 7, 19) },
            async action =>
            {
                applyStarted.SetResult();
                await releaseApply.Task;
                action();
            });
        var image = new ImageFile("test.jpg");

        var load = service.LoadAsync(image);
        await applyStarted.Task.WaitAsync(TestWaits.Condition);
        Assert.False(load.IsCompleted);
        Assert.False(image.MetadataLoaded);

        releaseApply.SetResult();
        await load;

        Assert.True(image.MetadataLoaded);
        Assert.Equal(new DateTime(2026, 7, 19), image.DateTaken);
    }

    [Fact]
    public async Task HydrationDeferral_RemainsUnloadedAndRetryable()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-metadata-hydration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "photo.dng");
        await File.WriteAllBytesAsync(path, [1]);
        try
        {
            var availability = new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration);
            var raw = new CountingRawService();
            var service = new MetadataService(
                raw,
                availability,
                action =>
                {
                    action();
                    return Task.CompletedTask;
                });
            var image = new ImageFile(path);

            var deferred = await service.LoadAsync(image);
            availability.Availability = SourceAvailability.AvailableLocally;
            var loaded = await service.LoadAsync(image);

            Assert.Equal(MetadataLoadStatus.DeferredForHydration, deferred);
            Assert.Equal(MetadataLoadStatus.Loaded, loaded);
            Assert.Equal(1, raw.ExtractCount);
            Assert.True(image.MetadataLoaded);
            Assert.Equal(4000, image.PixelWidth);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SingleThreadActionDispatcher : IDisposable
    {
        private readonly BlockingCollection<(Action Action, TaskCompletionSource Completion)> _queue = new();
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _started = new();

        public SingleThreadActionDispatcher()
        {
            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
            _started.Wait();
        }

        public int ThreadId { get; private set; }

        public Task InvokeAsync(Action action)
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add((action, completion));
            return completion.Task;
        }

        private void Run()
        {
            ThreadId = Environment.CurrentManagedThreadId;
            _started.Set();
            foreach (var (action, completion) in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join();
            _queue.Dispose();
            _started.Dispose();
        }
    }

    private sealed class CountingRawService : IRawProcessingService
    {
        internal int ExtractCount { get; private set; }
        public bool IsAvailable => true;
        public RawThumbnailData? ExtractThumbnail(string filePath) => null;

        public RawMetadata? ExtractMetadata(string filePath)
        {
            ExtractCount++;
            return new RawMetadata
            {
                PixelWidth = 4000,
                PixelHeight = 3000
            };
        }
    }

    private sealed class FixedRawService(RawMetadata metadata) : IRawProcessingService
    {
        public bool IsAvailable => true;
        public RawThumbnailData? ExtractThumbnail(string filePath) => null;
        public RawMetadata? ExtractMetadata(string filePath) => metadata;
    }

    private sealed class UnavailableRawService : IRawProcessingService
    {
        public bool IsAvailable => false;
        public RawThumbnailData? ExtractThumbnail(string filePath) => null;
        public RawMetadata? ExtractMetadata(string filePath) => null;
    }
}
