using System.Text;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MagickProfileLimitTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    public void PreviewDecode_CapsProfilesAndKeepsImageReadable(int profileCount)
    {
        var warnings = new List<string>();
        var retainedProfiles = -1;
        var fixture = CreateMiff(profileCount);
        var loader = new StandardBaseLoader((_, _) =>
        {
            var image = new MagickImage();
            image.Warning += (_, warning) => warnings.Add(warning.Message);

            try
            {
                image.Read(fixture, MagickFormat.Miff);
                retainedProfiles = image.ProfileNames.Count();

                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        });
        // MIFF is an in-memory policy fixture, not a claimed JPEG/TIFF file.
        // The injected read feeds the normal standard-image normalization path.
        var outcome = loader.LoadPreviewBaseWithOutcome(
            new ImageFile("profile-limit.png"),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var pair = outcome.Pair;

        Assert.Equal(BaseImageLoadFailure.None, outcome.Failure);
        Assert.NotNull(pair);
        Assert.Equal(32, retainedProfiles);
        Assert.Equal(1u, pair.Interactive.Pixels.Width);
        Assert.Equal(1u, pair.Interactive.Pixels.Height);
        Assert.Empty(pair.Interactive.Pixels.ProfileNames);

        if (profileCount > 32)
        {
            Assert.Contains(warnings, warning => warning.Contains("MaximumProfilesExceeded"));
        }
        else
        {
            Assert.Empty(warnings);
        }
    }

    private static byte[] CreateMiff(int profileCount)
    {
        // Write the bytes directly: creating profiles through Magick would cap
        // the fixture before the read under test could encounter profile 33.
        using var stream = new MemoryStream();
        var header = new StringBuilder(
            "id=ImageMagick version=1.0\nclass=DirectClass\n" +
            "columns=1 rows=1 depth=8\ncolorspace=sRGB\n");

        for (var index = 0; index < profileCount; index++)
        {
            header.Append($"profile=test{index:D2}\n");
        }

        header.Append('\f').Append('\n').Append(':').Append('\u001a');
        stream.Write(Encoding.ASCII.GetBytes(header.ToString()));

        for (var index = 0; index < profileCount; index++)
        {
            // MIFF profiles have a big-endian 32-bit length then their payload.
            stream.Write([0, 0, 0, 1, (byte)index]);
        }

        stream.Write([128, 128, 128]);

        return stream.ToArray();
    }
}
