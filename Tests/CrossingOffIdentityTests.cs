using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CrossingOffIdentityTests
{
    private const string GenerateVariable =
        "HAPPY_PHOTON_GENERATE_CROSSING_OFF_IDENTITY";

    private static readonly (string Key, string FileName, bool RequiresHeic)[]
        Classes =
        [
            ("display-p3", "display-p3-reference.jpg", false),
            ("adobe-rgb", "adobe-rgb-reference.jpg", false),
            ("heic", "reference.heic", true),
            ("tiff-16bit", "reference-16bit.tiff", false)
        ];

    public static TheoryData<string, string, bool> ReferenceClasses
    {
        get
        {
            var data = new TheoryData<string, string, bool>();
            foreach (var row in Classes)
            {
                data.Add(row.Key, row.FileName, row.RequiresHeic);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ReferenceClasses))]
    public void UneditedFullResolutionSrgb_MatchespreAgxReference(
        string key,
        string fileName,
        bool requiresHeic)
    {
        Assert.SkipWhen(
            RuntimeInformation.RuntimeIdentifier != "win-x64",
            "Checkpoint-C crossing-off identities gate on win-x64 until E.");
        Assert.SkipWhen(
            requiresHeic && !SupportsHeic(),
            "HEIC identity skipped because this ImageMagick build has no HEIC reader.");

        using var document = JsonDocument.Parse(File.ReadAllText(BaselinePath));
        var observations = document.RootElement
            .GetProperty("observations")
            .GetProperty("win-x64");
        AssertObservation(fileName, observations.GetProperty(key));
    }

    [Fact]
    public void GeneratepreAgxReferences_WhenEnabled()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable(GenerateVariable) != "1",
            $"Set {GenerateVariable}=1 to generate checkpoint-C references.");
        Assert.Equal("win-x64", RuntimeInformation.RuntimeIdentifier);

        var observations = new Dictionary<string, object>();
        foreach (var row in Classes)
        {
            if (row.RequiresHeic && !SupportsHeic())
            {
                observations[row.Key] = new
                {
                    skipped = "ImageMagick HEIC reader unavailable"
                };
                continue;
            }

            observations[row.Key] = Measure(RenderPreAgx(row.FileName));
        }

        var payload = new
        {
            schemaVersion = 1,
            baseCommit = "878903f",
            renderPath = "full base -> unedited export render -> sRGB RGB8, full resolution, output sharpening off",
            observations = new Dictionary<string, object>
            {
                ["win-x64"] = observations
            }
        };
        File.WriteAllText(
            BaselinePath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }) + Environment.NewLine);
    }

    private static void AssertObservation(
        string fileName,
        JsonElement expected)
    {
        var preAgx = RenderPreAgx(fileName);
        var frozen = Measure(preAgx);
        Assert.Equal(expected.GetProperty("width").GetUInt32(), frozen.Width);
        Assert.Equal(expected.GetProperty("height").GetUInt32(), frozen.Height);
        Assert.Equal(expected.GetProperty("bytes").GetInt32(), frozen.Bytes);
        Assert.Equal(
            expected.GetProperty("sha256").GetString(),
            frozen.Sha256);

        var current = RenderCurrent(fileName);
        Assert.Equal(preAgx.Width, current.Width);
        Assert.Equal(preAgx.Height, current.Height);
        Assert.Equal(preAgx.Pixels.Length, current.Pixels.Length);
        var maximumDifference = preAgx.Pixels.Zip(current.Pixels)
            .Max(pair => Math.Abs(pair.First - pair.Second));
        Assert.True(
            maximumDifference <= 1,
            $"Unedited RGB8 output moved by {maximumDifference} codes; limit is 1.");
    }

    private static PixelResult RenderCurrent(string fileName)
    {
        var path = Path.Combine(GoldenTestPaths.AssetDirectory, fileName);
        using var baseImage = new StandardBaseLoader().LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                $"Identity fixture did not decode: {path}");
        using var rendered = new RenderPipeline().Render(new RenderRequest(
            baseImage,
            new EditSettings(),
            RenderIntent.Export,
            null,
            new RenderOptions(false, false),
            OutputColorSpace.Srgb));
        var bytes = rendered.Image.GetPixelsUnsafe()
            .ToByteArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Unable to read RGB8 pixels.");
        return new PixelResult(
            rendered.Image.Width,
            rendered.Image.Height,
            bytes);
    }

    private static PixelResult RenderPreAgx(string fileName)
    {
        var path = Path.Combine(GoldenTestPaths.AssetDirectory, fileName);
        using var baseImage = new StandardBaseLoader().LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                $"Identity fixture did not decode: {path}");
        using var image = PreAgxRenderReference.Render(
            baseImage,
            new EditSettings(),
            maxDimension: null);
        var bytes = image.GetPixelsUnsafe().ToByteArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Unable to read pre-AgX RGB8 pixels.");
        return new PixelResult(image.Width, image.Height, bytes);
    }

    private static Observation Measure(PixelResult result)
    {
        return new Observation(
            result.Width,
            result.Height,
            result.Pixels.Length,
            Convert.ToHexString(SHA256.HashData(result.Pixels)).ToLowerInvariant());
    }

    private static bool SupportsHeic() =>
        MagickFormatInfo.Create(MagickFormat.Heic) is { SupportsReading: true };

    private static string BaselinePath => Path.Combine(
        GoldenTestPaths.AssetDirectory,
        "crossing-off-identity.json");

    private sealed record Observation(
        uint Width,
        uint Height,
        int Bytes,
        string Sha256);

    private sealed record PixelResult(uint Width, uint Height, byte[] Pixels);
}
