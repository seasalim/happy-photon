#:project ../HappyPhoton.csproj

using HappyPhoton.Services;
using ImageMagick;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: dotnet run scripts/generate-pipeline-test-assets.cs -- <asset-directory> <DisplayP3-profile>");
    return 1;
}

var assetDirectory = Path.GetFullPath(args[0]);
var displayP3ProfilePath = Path.GetFullPath(args[1]);
var rawPath = Path.Combine(assetDirectory, "canon-eos-350d.cr2");
var rawService = new LibRawProcessingService();
using var source = rawService.DecodeFull(rawPath)
    ?? throw new InvalidOperationException($"Could not decode {rawPath}.");

Resize(source, 1200);
source.Strip();
source.SetProfile(ColorProfiles.SRGB);

WriteJpeg(source, "srgb-reference.jpg");
WriteOrientedGpsJpeg(source, "srgb-exif-gps-orientation-6.jpg");
WriteColorManagedJpeg(source, ColorProfiles.AdobeRGB1998, "adobe-rgb-reference.jpg");
var displayP3 = new ColorProfile(displayP3ProfilePath);
WriteColorManagedJpeg(source, displayP3, "display-p3-reference.jpg");

using (var tiff = (MagickImage)source.Clone())
{
    tiff.Depth = 16;
    tiff.Format = MagickFormat.Tiff;
    tiff.Write(Path.Combine(assetDirectory, "reference-16bit.tiff"));
}

return 0;

void WriteJpeg(MagickImage image, string fileName)
{
    using var output = (MagickImage)image.Clone();
    output.Format = MagickFormat.Jpeg;
    output.Quality = 92;
    output.Write(Path.Combine(assetDirectory, fileName));
}

void WriteOrientedGpsJpeg(MagickImage image, string fileName)
{
    using var output = (MagickImage)image.Clone();
    output.Rotate(-90);
    output.Orientation = OrientationType.RightTop;

    var exif = new ExifProfile();
    exif.SetValue(ExifTag.Orientation, (ushort)OrientationType.RightTop);
    exif.SetValue(ExifTag.Make, "Happy Photon");
    exif.SetValue(ExifTag.Model, "WP0.1 fixture generator");
    exif.SetValue(ExifTag.DateTimeOriginal, "2026:01:02 03:04:05");
    exif.SetValue(ExifTag.GPSLatitudeRef, "N");
    exif.SetValue(ExifTag.GPSLatitude,
        [new Rational(47), new Rational(36), new Rational(21.6)]);
    exif.SetValue(ExifTag.GPSLongitudeRef, "W");
    exif.SetValue(ExifTag.GPSLongitude,
        [new Rational(122), new Rational(19), new Rational(48.0)]);
    output.SetProfile(exif);
    WriteJpeg(output, fileName);
}

void WriteColorManagedJpeg(MagickImage image, IColorProfile target, string fileName)
{
    using var output = (MagickImage)image.Clone();
    output.TransformColorSpace(ColorProfiles.SRGB, target);
    WriteJpeg(output, fileName);
}

void Resize(MagickImage image, uint longEdge)
{
    image.Resize(new MagickGeometry(longEdge, longEdge)
    {
        IgnoreAspectRatio = false,
        Greater = true
    });
}
