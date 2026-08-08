using System.Globalization;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public sealed class ExportMetadataService
{
    private readonly string _software;
    private readonly ISourceAvailabilityService _availabilityService;
    private readonly Func<string, ExifProfile?> _readSourceProfile;

    public ExportMetadataService()
        : this(
            $"Happy Photon {AppBuildInfo.Version.ToString(3)}",
            new SourceAvailabilityService())
    {
    }

    internal ExportMetadataService(string software)
        : this(software, new SourceAvailabilityService())
    {
    }

    internal ExportMetadataService(
        string software,
        ISourceAvailabilityService availabilityService,
        Func<string, ExifProfile?>? readSourceProfile = null)
    {
        _software = software;
        _availabilityService = availabilityService ??
            throw new ArgumentNullException(nameof(availabilityService));
        _readSourceProfile = readSourceProfile ?? ReadSourceProfileCore;
    }

    public void Apply(
        ImageFile sourceFile,
        MagickImage destination,
        bool stripLocationData) => Apply(
            sourceFile,
            destination,
            stripLocationData,
            SourceReadIntent.Background);

    internal void Apply(
        ImageFile sourceFile,
        MagickImage destination,
        bool stripLocationData,
        SourceReadIntent intent)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(destination);

        var profile = ReadSourceProfile(sourceFile.FilePath, intent) ??
            new ExifProfile();
        ApplyFallbackMetadata(profile, sourceFile);
        destination.Orientation = OrientationType.TopLeft;
        profile.SetValue(ExifTag.Orientation, (ushort)1);
        profile.RemoveThumbnail();
        profile.RemoveValue(ExifTag.JPEGInterchangeFormat);
        profile.RemoveValue(ExifTag.JPEGInterchangeFormatLength);
        profile.RemoveValue(ExifTag.PixelXDimension);
        profile.RemoveValue(ExifTag.PixelYDimension);
        profile.SetValue(ExifTag.Software, _software);

        if (stripLocationData)
        {
            StripGps(profile);
        }

        destination.SetProfile(profile);
    }

    private ExifProfile? ReadSourceProfile(
        string sourcePath,
        SourceReadIntent intent)
    {
        var availability = _availabilityService.GetAvailability(sourcePath);
        return SourceAccessPolicy.CanRead(availability, intent)
            ? _readSourceProfile(sourcePath)
            : null;
    }

    private static ExifProfile? ReadSourceProfileCore(string sourcePath)
    {
        try
        {
            using var source = new MagickImage();
            source.Ping(sourcePath);
            var profile = source.GetExifProfile();
            return profile == null
                ? null
                : new ExifProfile(profile.ToByteArray());
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyFallbackMetadata(
        ExifProfile profile,
        ImageFile source)
    {
        SetStringIfMissing(profile, ExifTag.Make, source.CameraMake);
        SetStringIfMissing(profile, ExifTag.Model, source.CameraModel);
        SetStringIfMissing(profile, ExifTag.LensModel, source.LensModel);

        if (profile.GetValue(ExifTag.DateTimeOriginal) == null &&
            source.DateTaken is { } dateTaken)
        {
            profile.SetValue(
                ExifTag.DateTimeOriginal,
                dateTaken.ToString(
                    "yyyy:MM:dd HH:mm:ss",
                    CultureInfo.InvariantCulture));
        }

        if (profile.GetValue(ExifTag.ISOSpeedRatings) == null &&
            source.Iso is > 0 and <= ushort.MaxValue)
        {
            profile.SetValue(
                ExifTag.ISOSpeedRatings,
                new[] { (ushort)source.Iso.Value });
        }

        if (profile.GetValue(ExifTag.FNumber) == null &&
            source.FNumber is > 0)
        {
            profile.SetValue(
                ExifTag.FNumber,
                new Rational(source.FNumber.Value));
        }

        if (profile.GetValue(ExifTag.ExposureTime) == null &&
            ParseExposure(source.ExposureTime) is { } exposure)
        {
            profile.SetValue(ExifTag.ExposureTime, exposure);
        }

        if (profile.GetValue(ExifTag.FocalLength) == null &&
            source.FocalLength is > 0)
        {
            profile.SetValue(
                ExifTag.FocalLength,
                new Rational(source.FocalLength.Value));
        }
    }

    private static Rational? ParseExposure(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split('/');
        if (parts.Length == 2 &&
            uint.TryParse(parts[0], out var numerator) &&
            uint.TryParse(parts[1], out var denominator) &&
            denominator != 0)
        {
            return new Rational(numerator, denominator);
        }

        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var seconds) && seconds > 0
            ? new Rational(seconds)
            : null;
    }

    private static void SetStringIfMissing(
        ExifProfile profile,
        ExifTag<string> tag,
        string? value)
    {
        if (profile.GetValue(tag) == null &&
            !string.IsNullOrWhiteSpace(value))
        {
            profile.SetValue(tag, value.Trim());
        }
    }

    private static void StripGps(ExifProfile profile)
    {
        var gpsTags = profile.Values
            .Where(value => value.Tag.Ifd == ExifIfds.Gps)
            .Select(value => value.Tag)
            .ToList();
        foreach (var tag in gpsTags)
        {
            profile.RemoveValue(tag);
        }

        profile.RemoveValue(ExifTag.GPSIFDOffset);
        profile.AllowedIfds &= ~ExifIfds.Gps;
    }
}
