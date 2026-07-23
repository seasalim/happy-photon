using ImageMagick;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class JpegThumbnailDecoderTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonJpegTests_{Guid.NewGuid():N}");

    public JpegThumbnailDecoderTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Decode_ConstrainsSizeAndAppliesExifOrientation()
    {
        _fixture.RequireWindows();

        Directory.CreateDirectory(_tempDirectory);
        var landscapePath = Path.Combine(_tempDirectory, "landscape.jpg");
        var orientedPath = Path.Combine(_tempDirectory, "oriented.jpg");
        WriteJpeg(landscapePath, OrientationType.Undefined);
        WriteJpeg(orientedPath, OrientationType.RightTop);

        using var landscape = JpegThumbnailDecoder.Decode(
            landscapePath, 150, CancellationToken.None);
        using var oriented = JpegThumbnailDecoder.Decode(
            orientedPath, 150, CancellationToken.None);

        Assert.Equal(150, landscape.PixelSize.Width);
        Assert.Equal(75, landscape.PixelSize.Height);
        Assert.Equal(75, oriented.PixelSize.Width);
        Assert.Equal(150, oriented.PixelSize.Height);
    }

    [Fact]
    public void Decode_ObservesCancellationBeforeOpeningTheFile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => JpegThumbnailDecoder.Decode(
            Path.Combine(_tempDirectory, "missing.jpg"), 150, cancellation.Token));
    }

    [Fact]
    public void Decode_UsesCompatibleEmbeddedExifThumbnail()
    {
        _fixture.RequireWindows();

        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "embedded-matching.jpg");
        WriteJpegWithExifThumbnail(path, 90, 60);

        using var bitmap = JpegThumbnailDecoder.Decode(path, 150, CancellationToken.None);

        Assert.Equal(90, bitmap.PixelSize.Width);
        Assert.Equal(60, bitmap.PixelSize.Height);
    }

    [Fact]
    public void Decode_RejectsEmbeddedExifThumbnailWithDifferentAspectRatio()
    {
        _fixture.RequireWindows();

        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "embedded-mismatched.jpg");
        WriteJpegWithExifThumbnail(path, 80, 60);

        using var bitmap = JpegThumbnailDecoder.Decode(path, 150, CancellationToken.None);

        Assert.Equal(150, bitmap.PixelSize.Width);
        Assert.Equal(100, bitmap.PixelSize.Height);
    }

    [Theory]
    [InlineData(1, 0, 0, 2, 1)]
    [InlineData(2, 2, 0, 0, 1)]
    [InlineData(3, 2, 1, 0, 0)]
    [InlineData(4, 0, 1, 2, 0)]
    [InlineData(5, 0, 0, 1, 2)]
    [InlineData(6, 1, 0, 0, 2)]
    [InlineData(7, 1, 2, 0, 0)]
    [InlineData(8, 0, 2, 1, 0)]
    public void MapPixel_ImplementsExifOrientation(
        int orientation,
        int firstX,
        int firstY,
        int lastX,
        int lastY)
    {
        Assert.Equal((firstX, firstY), JpegThumbnailDecoder.MapPixel(0, 0, 3, 2, orientation));
        Assert.Equal((lastX, lastY), JpegThumbnailDecoder.MapPixel(2, 1, 3, 2, orientation));
    }

    private static void WriteJpeg(string path, OrientationType orientation)
    {
        using var image = new MagickImage(MagickColors.Red, 400, 200);
        if (orientation != OrientationType.Undefined)
        {
            var profile = new ExifProfile();
            profile.SetValue(ExifTag.Orientation, (ushort)orientation);
            image.SetProfile(profile);
            image.Orientation = orientation;
        }
        image.Write(path, MagickFormat.Jpeg);
    }

    private static void WriteJpegWithExifThumbnail(string path, uint width, uint height)
    {
        using var source = new MagickImage(MagickColors.Blue, 600, 400);
        var sourceBytes = source.ToByteArray(MagickFormat.Jpeg);
        using var thumbnail = new MagickImage(MagickColors.Red, width, height);
        var thumbnailBytes = thumbnail.ToByteArray(MagickFormat.Jpeg);
        var exifPayload = CreateExifPayload(thumbnailBytes);
        var segmentLength = exifPayload.Length + 2;

        using var output = new MemoryStream();
        output.Write(sourceBytes, 0, 2);
        output.WriteByte(0xff);
        output.WriteByte(0xe1);
        output.WriteByte((byte)(segmentLength >> 8));
        output.WriteByte((byte)segmentLength);
        output.Write(exifPayload);
        output.Write(sourceBytes, 2, sourceBytes.Length - 2);
        File.WriteAllBytes(path, output.ToArray());
    }

    private static byte[] CreateExifPayload(byte[] thumbnailBytes)
    {
        const uint thumbnailOffset = 56;
        using var payload = new MemoryStream();
        payload.Write("Exif\0\0"u8);
        using var writer = new BinaryWriter(payload, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((ushort)42);
        writer.Write((uint)8);
        writer.Write((ushort)0);
        writer.Write((uint)14);
        writer.Write((ushort)3);
        WriteExifEntry(writer, 0x0103, 3, 1, 6);
        WriteExifEntry(writer, 0x0201, 4, 1, thumbnailOffset);
        WriteExifEntry(writer, 0x0202, 4, 1, (uint)thumbnailBytes.Length);
        writer.Write((uint)0);
        writer.Write(thumbnailBytes);
        return payload.ToArray();
    }

    private static void WriteExifEntry(
        BinaryWriter writer,
        ushort tag,
        ushort type,
        uint count,
        uint value)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);
        writer.Write(value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
