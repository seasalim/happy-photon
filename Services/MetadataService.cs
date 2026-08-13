using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using ImageMagick;
using HappyPhoton.Models;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

internal sealed class MetadataService
{
    private readonly ConditionalWeakTable<ImageFile, Lazy<Task<MetadataLoadStatus>>>
        _loads = new();
    private readonly Func<ImageFile, MetadataExtractionResult> _extractMetadata;
    private readonly Func<Action, Task> _applyAsync;
    private int _inFlightCount;

    public int InFlightCount => Volatile.Read(ref _inFlightCount);

    public MetadataService(IRawProcessingService rawService) : this(
        rawService,
        new SourceAvailabilityService())
    {
    }

    internal MetadataService(
        IRawProcessingService rawService,
        ISourceAvailabilityService availabilityService,
        Func<Action, Task>? applyAsync = null)
    {
        ArgumentNullException.ThrowIfNull(rawService);
        ArgumentNullException.ThrowIfNull(availabilityService);
        _extractMetadata = imageFile => ExtractMetadataCore(
            imageFile,
            rawService,
            availabilityService);
        _applyAsync = applyAsync ?? ApplyOnUiThreadAsync;
    }

    internal MetadataService(
        Func<ImageFile, ImageMetadata> extractMetadata,
        Func<Action, Task> applyAsync)
    {
        _extractMetadata = image => MetadataExtractionResult.Loaded(
            extractMetadata(image));
        _applyAsync = applyAsync;
    }

    public Task<MetadataLoadStatus> LoadAsync(ImageFile imageFile)
    {
        if (imageFile.MetadataLoaded)
        {
            return Task.FromResult(MetadataLoadStatus.Loaded);
        }

        var load = _loads.GetValue(
            imageFile,
            image => new Lazy<Task<MetadataLoadStatus>>(
                () => LoadCoreAsync(image),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return load.Value;
    }

    private async Task<MetadataLoadStatus> LoadCoreAsync(ImageFile imageFile)
    {
        Interlocked.Increment(ref _inFlightCount);
        MetadataLoadStatus status = MetadataLoadStatus.Failed;
        try
        {
            var result = await Task.Run(() => _extractMetadata(imageFile));
            status = result.Status;
            if (status == MetadataLoadStatus.Loaded)
            {
                await _applyAsync(() => imageFile.ApplyMetadata(result.Metadata));
            }
            return status;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightCount);
            if (status != MetadataLoadStatus.Loaded)
            {
                _loads.Remove(imageFile);
            }
        }
    }

    private static async Task ApplyOnUiThreadAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    internal static ImageMetadata ExtractMetadata(
        ImageFile imageFile,
        IRawProcessingService rawService) =>
        ExtractMetadataCore(
            imageFile,
            rawService,
            new SourceAvailabilityService()).Metadata;

    private static MetadataExtractionResult ExtractMetadataCore(
        ImageFile imageFile,
        IRawProcessingService rawService,
        ISourceAvailabilityService availabilityService)
    {
        var swTotal = Stopwatch.StartNew();
        var builder = new MetadataBuilder();
        string? source = null;
        LogDebug(nameof(MetadataService), "Loading metadata", imageFile.FilePath);

        try
        {
            builder.FileSize = new FileInfo(imageFile.FilePath).Length;
            var availability = availabilityService.GetAvailability(
                imageFile.FilePath);
            if (!SourceAccessPolicy.CanRead(
                availability,
                SourceReadIntent.Background))
            {
                source = availability == SourceAvailability.RequiresHydration
                    ? "Deferred"
                    : "Unavailable";
                return new MetadataExtractionResult(
                    availability == SourceAvailability.RequiresHydration
                        ? MetadataLoadStatus.DeferredForHydration
                        : MetadataLoadStatus.Failed,
                    builder.ToMetadata());
            }

            if (imageFile.IsRaw && rawService.IsAvailable)
            {
                var rawMetadata = rawService.ExtractMetadata(imageFile.FilePath);
                if (rawMetadata != null)
                {
                    source = "LibRaw";
                    builder.Apply(rawMetadata);
                    return MetadataExtractionResult.Loaded(
                        builder.ToMetadata());
                }
            }

            try
            {
                using var image = new MagickImage();
                image.Ping(imageFile.FilePath);
                builder.PixelWidth = (int)image.Width;
                builder.PixelHeight = (int)image.Height;
                var exifProfile = image.GetExifProfile();
                if (exifProfile != null)
                {
                    ApplyExifMetadata(builder, exifProfile);
                }
                source = "Ping";
            }
            catch (Exception ex)
            {
                LogDebug(nameof(MetadataService), $"Ping failed: {ex.Message}", imageFile.FilePath);
                using var image = new MagickImage(imageFile.FilePath);
                builder.PixelWidth = (int)image.Width;
                builder.PixelHeight = (int)image.Height;
                var exifProfile = image.GetExifProfile();
                if (exifProfile != null)
                {
                    ApplyExifMetadata(builder, exifProfile);
                }
                source = "FullDecode";
            }
        }
        catch (Exception ex)
        {
            source = "Error";
            LogDebug(nameof(MetadataService), $"Failed: {ex.Message}", imageFile.FilePath);
            HandleImageLoadError(ex, imageFile.FilePath);
        }
        finally
        {
            LogPerformance(
                nameof(MetadataService),
                "Total",
                swTotal.ElapsedMilliseconds,
                imageFile.FilePath,
                $"source={source ?? "None"}");
        }

        return MetadataExtractionResult.Loaded(builder.ToMetadata());
    }

    private static void ApplyExifMetadata(MetadataBuilder metadata, IExifProfile exifProfile)
    {
        var dateTakenValue = exifProfile.GetValue(ExifTag.DateTimeOriginal);
        if (dateTakenValue?.Value != null && DateTime.TryParseExact(
            dateTakenValue.Value.ToString(),
            "yyyy:MM:dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var dateTaken))
        {
            metadata.DateTaken = dateTaken;
        }

        metadata.CameraMake = exifProfile.GetValue(ExifTag.Make)?.Value?.ToString()?.Trim();
        metadata.CameraModel = exifProfile.GetValue(ExifTag.Model)?.Value?.ToString()?.Trim();
        var lens = exifProfile.GetValue(ExifTag.LensModel)?.Value?.ToString()?.Trim();
        metadata.LensModel = string.IsNullOrEmpty(lens) ? null : lens;

        if (exifProfile.GetValue(ExifTag.FNumber)?.Value is Rational fNumber)
        {
            metadata.FNumber = fNumber.ToDouble();
        }

        if (exifProfile.GetValue(ExifTag.ExposureTime)?.Value is Rational exposure)
        {
            var seconds = exposure.ToDouble();
            metadata.ExposureTime = seconds < 1 ? exposure.ToString() : $"{seconds:F1}";
        }

        if (exifProfile.GetValue(ExifTag.ISOSpeedRatings)?.Value is ushort[] iso && iso.Length > 0)
        {
            metadata.Iso = iso[0];
        }

        if (exifProfile.GetValue(ExifTag.FocalLength)?.Value is Rational focalLength)
        {
            metadata.FocalLength = focalLength.ToDouble();
        }

        var latitude = exifProfile.GetValue(ExifTag.GPSLatitude)?.Value as Rational[];
        var longitude = exifProfile.GetValue(ExifTag.GPSLongitude)?.Value as Rational[];
        if (latitude != null && longitude != null)
        {
            metadata.GpsLatitude = ConvertGpsToDouble(
                latitude,
                exifProfile.GetValue(ExifTag.GPSLatitudeRef)?.Value?.ToString());
            metadata.GpsLongitude = ConvertGpsToDouble(
                longitude,
                exifProfile.GetValue(ExifTag.GPSLongitudeRef)?.Value?.ToString());
        }
    }

    private static double? ConvertGpsToDouble(Rational[] coordinates, string? reference)
    {
        if (coordinates.Length < 3) return null;
        var result = coordinates[0].ToDouble() +
                     coordinates[1].ToDouble() / 60.0 +
                     coordinates[2].ToDouble() / 3600.0;
        return reference is "S" or "W" ? -result : result;
    }

    private sealed class MetadataBuilder
    {
        public long FileSize { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public DateTime? DateTaken { get; set; }
        public string? CameraMake { get; set; }
        public string? CameraModel { get; set; }
        public double? FNumber { get; set; }
        public string? ExposureTime { get; set; }
        public int? Iso { get; set; }
        public double? FocalLength { get; set; }
        public string? LensModel { get; set; }
        public double? GpsLatitude { get; set; }
        public double? GpsLongitude { get; set; }

        public void Apply(RawMetadata metadata)
        {
            PixelWidth = metadata.PixelWidth;
            PixelHeight = metadata.PixelHeight;
            DateTaken = metadata.DateTaken;
            CameraMake = metadata.CameraMake;
            CameraModel = metadata.CameraModel;
            FNumber = metadata.FNumber;
            ExposureTime = FormatExposureTime(metadata.ExposureTime);
            Iso = metadata.Iso;
            FocalLength = metadata.FocalLength;
            LensModel = metadata.LensModel;
        }

        public ImageMetadata ToMetadata() => new()
        {
            FileSize = FileSize,
            PixelWidth = PixelWidth,
            PixelHeight = PixelHeight,
            DateTaken = DateTaken,
            CameraMake = CameraMake,
            CameraModel = CameraModel,
            FNumber = FNumber,
            ExposureTime = ExposureTime,
            Iso = Iso,
            FocalLength = FocalLength,
            LensModel = LensModel,
            GpsLatitude = GpsLatitude,
            GpsLongitude = GpsLongitude
        };

        private static string? FormatExposureTime(double? exposureTime)
        {
            if (!exposureTime.HasValue) return null;
            var value = exposureTime.Value;
            return value is > 0 and < 1 ? $"1/{(int)(1 / value)}" : $"{value:F1}";
        }
    }

    private sealed record MetadataExtractionResult(
        MetadataLoadStatus Status,
        ImageMetadata Metadata)
    {
        internal static MetadataExtractionResult Loaded(
            ImageMetadata metadata) =>
            new(MetadataLoadStatus.Loaded, metadata);
    }
}
