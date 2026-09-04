using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DisplayTransformTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        GoldenTestPaths.AssetDirectory, "softproof");
    private readonly ITestOutputHelper _output;

    public DisplayTransformTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void IdentityRows_ShowCanonicalObject()
    {
        using var canonical = LoadBitmap("softproof-chart.png");
        var absent = Resolve(null, DisplayAcmState.Unavailable);
        var srgb = Resolve("softproof-srgb.icc", DisplayAcmState.Off);

        Assert.Same(canonical, absent.Derive(canonical, DisplaySourceColorSpace.Srgb));
        Assert.Equal(DisplayProfileSupport.Srgb, srgb.Support);
        Assert.Same(canonical, srgb.Derive(canonical, DisplaySourceColorSpace.Srgb));
    }

    [Theory]
    [InlineData("softproof-p3-gamma22.icc", "oracle-p3-gamma22.png")]
    [InlineData("softproof-p3-curv1024.icc", "oracle-p3-curv1024.png")]
    public void SrgbTransform_MatchesCommittedLcmsOracle(
        string profileName,
        string oracleName)
    {
        using var canonical = LoadBitmap("softproof-chart.png");
        using var displayed = Resolve(profileName, DisplayAcmState.Off)
            .Derive(canonical, DisplaySourceColorSpace.Srgb);
        using var oracle = LoadBitmap(oracleName);
        var actual = BitmapConversionService.CopyBgraPixels(displayed);
        var expected = BitmapConversionService.CopyBgraPixels(oracle);
        var input = BitmapConversionService.CopyBgraPixels(canonical);
        var errors = new List<int>(4096 * 3);
        for (var offset = 0; offset < actual.Length; offset += 4)
        for (var channel = 0; channel < 3; channel++)
            errors.Add(Math.Abs(actual[offset + channel] - expected[offset + channel]));

        Assert.True(errors.Max() <= 2,
            $"Maximum channel error was {errors.Max()}; errors: " +
            string.Join(", ", errors.GroupBy(error => error).OrderBy(group => group.Key)
                .Select(group => $"{group.Key}={group.Count()}")) + "; maxima: " +
            string.Join(", ", Enumerable.Range(0, actual.Length / 4)
                .SelectMany(pixel => Enumerable.Range(0, 3).Select(channel =>
                    (pixel, channel, error: Math.Abs(actual[pixel * 4 + channel] - expected[pixel * 4 + channel]))))
                .Where(value => value.error == errors.Max())
                .Select(value => $"pixel={value.pixel} channel={value.channel} " +
                    $"input={input[value.pixel * 4 + value.channel]} " +
                    $"actual={actual[value.pixel * 4 + value.channel]} " +
                    $"expected={expected[value.pixel * 4 + value.channel]}")));
        Assert.True(errors.Count(error => error <= 1) >= errors.Count * 0.999,
            $"{errors.Count(error => error > 1)} of {errors.Count} channels exceeded 1 LSB.");
    }

    [Theory]
    [InlineData("softproof-p3-a2b1.icc", DisplayProfileSupport.LutBased, "LUT-based")]
    [InlineData("softproof-p3-d2b0.icc", DisplayProfileSupport.LutBased, "LUT-based")]
    [InlineData("softproof-p3-mhc2.icc", DisplayProfileSupport.Mhc2, "HDR (MHC2)")]
    public void UnsupportedProfile_TreatsMonitorAsSrgb(
        string profileName,
        DisplayProfileSupport support,
        string reason)
    {
        using var canonical = LoadBitmap("softproof-chart.png");
        var transform = Resolve(profileName, DisplayAcmState.Off);

        Assert.Equal(support, transform.Support);
        Assert.Contains(profileName.Split('-')[^1][..^4], transform.ProfileName,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(reason, transform.DiagnosticText);
        Assert.Same(canonical, transform.Derive(canonical, DisplaySourceColorSpace.Srgb));
        using var p3Display = transform.Derive(canonical, DisplaySourceColorSpace.DisplayP3);
        Assert.NotSame(canonical, p3Display);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("softproof-p3-gamma22.icc")]
    public void DisplayP3SourcePath_MatchesLcmsForInGamutColors(string? destinationProfileName)
    {
        using var canonical = LoadBitmap("softproof-chart.png");
        var transform = Resolve(destinationProfileName, DisplayAcmState.Off);
        using var actual = transform.Derive(canonical, DisplaySourceColorSpace.DisplayP3);
        using var expectedImage = new MagickImage(
            Path.Combine(FixtureDirectory, "softproof-chart.png"));
        expectedImage.RenderingIntent = RenderingIntent.Relative;
        expectedImage.Settings.SetDefine("profile:black-point-compensation", "false");
        var destination = destinationProfileName == null
            ? ColorProfiles.SRGB
            : new ColorProfile(Profile(destinationProfileName));
        expectedImage.TransformColorSpace(
            OutputColorProfiles.Get(OutputColorSpace.DisplayP3), destination);
        using var expected = BitmapConversionService.ConvertToBitmap(expectedImage)!;
        var actualPixels = BitmapConversionService.CopyBgraPixels(actual);
        var expectedPixels = BitmapConversionService.CopyBgraPixels(expected);
        var sourcePixels = BitmapConversionService.CopyBgraPixels(canonical);
        var errors = Enumerable.Range(0, sourcePixels.Length / 4)
            .Where(pixel => Enumerable.Range(0, 3).All(channel =>
                sourcePixels[pixel * 4 + channel] is >= 68 and <= 187))
            .SelectMany(pixel => Enumerable.Range(0, 3).Select(channel =>
                Math.Abs(actualPixels[pixel * 4 + channel] -
                    expectedPixels[pixel * 4 + channel])))
            .ToArray();

        Assert.True(errors.Max() <= 2, $"Maximum P3-source error was {errors.Max()}.");
    }

    [Theory]
    [InlineData(2, DisplayProfileSupport.AcmManaged)]
    [InlineData(3, DisplayProfileSupport.AcmQueryFailed)]
    public void AcmSafetyPolicy_NeverAppliesMonitorProfile(
        int acmValue,
        DisplayProfileSupport expectedSupport)
    {
        using var canonical = LoadBitmap("softproof-chart.png");
        var acm = (DisplayAcmState)acmValue;
        var transform = Resolve("softproof-p3-gamma22.icc", acm);

        Assert.Equal(expectedSupport, transform.Support);
        Assert.Same(canonical, transform.Derive(canonical, DisplaySourceColorSpace.Srgb));
    }

    [Fact]
    public void Resolve_ChangesIdentityOnlyWhenMonitorPolicyChanges()
    {
        var fake = new FakePlatform(new("monitor-a", Profile("softproof-srgb.icc"),
            DisplayAcmState.Off));
        var service = new DisplayColorManagementService(fake);
        var first = service.Resolve(1);
        var repeated = service.Resolve(1, first);
        fake.Result = new("monitor-b", Profile("softproof-srgb.icc"), DisplayAcmState.Off);
        var moved = service.Resolve(1);

        Assert.Equal(first.Identity, repeated.Identity);
        Assert.Same(first, repeated);
        Assert.NotEqual(first.Identity, moved.Identity);
    }

    [Fact]
    public void Resolve_RetriesInvalidProfileReadWithUnchangedIdentity()
    {
        var path = Profile("softproof-p3-gamma22.icc");
        var fake = new FakePlatform(new("monitor", path, DisplayAcmState.Off));
        var reads = 0;
        var service = new DisplayColorManagementService(fake, profilePath =>
        {
            reads++;
            if (reads == 1) throw new IOException("Transient profile read failure.");
            return File.ReadAllBytes(profilePath);
        });

        var invalid = service.Resolve(1);
        var recovered = service.Resolve(1, invalid);

        Assert.Equal(DisplayProfileSupport.Invalid, invalid.Support);
        Assert.Equal(DisplayProfileSupport.MatrixTrc, recovered.Support);
        Assert.Equal(invalid.Identity, recovered.Identity);
        Assert.Equal(2, reads);
    }

    [Theory]
    [MemberData(nameof(PolicyCases))]
    public void ResolutionPolicy_SelectsExpectedMonitorTreatment(
        string? profileName,
        int acmValue,
        DisplayProfileSupport expectedSupport,
        bool srgbIdentity)
    {
        using var canonical = LoadBitmap("softproof-chart.png");
        var transform = Resolve(profileName, (DisplayAcmState)acmValue);

        Assert.Equal(expectedSupport, transform.Support);
        Assert.Equal(srgbIdentity, transform.IsIdentity(DisplaySourceColorSpace.Srgb));
        Assert.False(transform.IsIdentity(DisplaySourceColorSpace.DisplayP3));
        var derived = transform.Derive(canonical, DisplaySourceColorSpace.Srgb);
        if (!ReferenceEquals(derived, canonical)) derived.Dispose();
    }

    public static IEnumerable<object?[]> PolicyCases()
    {
        var profiles = new (string? Name, DisplayProfileSupport Support, bool Identity)[]
        {
            (null, DisplayProfileSupport.Absent, true),
            ("softproof-srgb.icc", DisplayProfileSupport.Srgb, true),
            ("softproof-p3-gamma22.icc", DisplayProfileSupport.MatrixTrc, false),
            ("softproof-p3-a2b1.icc", DisplayProfileSupport.LutBased, true),
            ("softproof-p3-d2b0.icc", DisplayProfileSupport.LutBased, true),
            ("softproof-p3-mhc2.icc", DisplayProfileSupport.Mhc2, true),
        };
        foreach (var profile in profiles)
        {
            yield return [profile.Name, (int)DisplayAcmState.Off,
                profile.Support, profile.Identity];
            yield return [profile.Name, (int)DisplayAcmState.Unavailable,
                profile.Support, profile.Identity];
            yield return [profile.Name, (int)DisplayAcmState.On,
                DisplayProfileSupport.AcmManaged, true];
            yield return [profile.Name, (int)DisplayAcmState.Failed,
                DisplayProfileSupport.AcmQueryFailed, true];
        }
    }

    [Fact]
    public void MacOsManagedWindow_IsIdentityForSrgbAndConvertsP3ProofsLikeSrgbMonitor()
    {
        using var canonical = LoadBitmap("softproof-chart.png");
        var managed = Resolve(null, DisplayAcmState.OsManaged);
        var untagged = Resolve(null, DisplayAcmState.OsUnmanaged);
        var incompatible = Resolve(null, DisplayAcmState.OsIncompatible);
        var treatedAsSrgb = Resolve(null, DisplayAcmState.Unavailable);

        Assert.Equal(DisplayProfileSupport.OsManaged, managed.Support);
        Assert.Equal(
            "Display profile · managed by macOS (window tagged sRGB)",
            managed.DiagnosticText);
        Assert.Same(canonical, managed.Derive(canonical, DisplaySourceColorSpace.Srgb));
        using var managedP3 = managed.Derive(canonical, DisplaySourceColorSpace.DisplayP3);
        using var expectedP3 = treatedAsSrgb.Derive(canonical, DisplaySourceColorSpace.DisplayP3);
        Assert.Equal(
            BitmapConversionService.CopyBgraPixels(expectedP3),
            BitmapConversionService.CopyBgraPixels(managedP3));

        Assert.Equal(DisplayProfileSupport.Absent, untagged.Support);
        Assert.Equal(
            "Display profile · none (sRGB) · macOS window not tagged; no Metal layer yet",
            untagged.DiagnosticText);
        Assert.Same(canonical, untagged.Derive(canonical, DisplaySourceColorSpace.Srgb));
        Assert.Equal(DisplayProfileSupport.Absent, incompatible.Support);
        Assert.Contains("non-sRGB colorspace", incompatible.DiagnosticText);
        Assert.NotEqual(managed.Identity, untagged.Identity);
    }

    // Enum arguments are ints: the enums are internal and xUnit theories must be public.
    [Theory]
    [InlineData((int)MacOsLayerKind.None, (int)MacOsLayerColorSpace.None, (int)DisplayAcmState.OsUnmanaged, false)]
    [InlineData((int)MacOsLayerKind.NotMetal, (int)MacOsLayerColorSpace.None, (int)DisplayAcmState.OsUnmanaged, false)]
    [InlineData((int)MacOsLayerKind.Metal, (int)MacOsLayerColorSpace.None, (int)DisplayAcmState.OsManaged, true)]
    [InlineData((int)MacOsLayerKind.Metal, (int)MacOsLayerColorSpace.Srgb, (int)DisplayAcmState.OsManaged, false)]
    [InlineData((int)MacOsLayerKind.Metal, (int)MacOsLayerColorSpace.Other, (int)DisplayAcmState.OsIncompatible, false)]
    public void MacOsPlatform_TagsOnlyUntaggedMetalLayers(
        int kindValue,
        int colorSpaceValue,
        int expectedValue,
        bool expectTag)
    {
        var expected = (DisplayAcmState)expectedValue;
        var layer = new FakeMetalLayer((MacOsLayerKind)kindValue, (MacOsLayerColorSpace)colorSpaceValue);
        var platform = new MacOsDisplayProfilePlatform(layer);

        var result = platform.Resolve(1);

        Assert.Equal(expected, result.AcmState);
        Assert.Equal(expectTag ? 1 : 0, layer.TagCalls);
        Assert.Equal(DisplayAcmState.OsUnmanaged, platform.Resolve(0).AcmState);
    }

    [Theory]
    [InlineData(DisplayProfileSupport.OsManaged, 0, false)]
    [InlineData(DisplayProfileSupport.Absent, 0, true)]
    [InlineData(DisplayProfileSupport.Absent, 19, true)]
    [InlineData(DisplayProfileSupport.Absent, 20, false)]
    public void MacOsRetry_StopsWhenManagedOrAtTheBound(
        DisplayProfileSupport support,
        int attempts,
        bool expected) =>
        Assert.Equal(expected, HappyPhoton.ViewModels.MainWindowViewModel
            .ShouldRetryMacOsDisplayProfile(support, attempts));

    private sealed class FakeMetalLayer(MacOsLayerKind kind, MacOsLayerColorSpace colorSpace)
        : IMacOsMetalLayer
    {
        private MacOsLayerColorSpace _colorSpace = colorSpace;
        public int TagCalls { get; private set; }
        public MacOsLayerKind GetLayerKind(nint nsView) => kind;
        public MacOsLayerColorSpace GetColorSpace(nint nsView) => _colorSpace;
        public void TagSrgb(nint nsView)
        {
            TagCalls++;
            _colorSpace = MacOsLayerColorSpace.Srgb;
        }
    }

    [Fact]
    public void DeriveCost_MeetsApprovedFullCpuGate()
    {
#if DEBUG
        Assert.Skip("Run the FULL_CPU display performance gate in Release configuration.");
#endif
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU") != "1",
            "Set HAPPY_PHOTON_FULL_CPU=1 to run the display-transform performance gate.");
        var transform = Resolve("softproof-p3-gamma22.icc", DisplayAcmState.Off);
        Measure(1600, 1067, 6, transform);
        Measure(3200, 2133, 18, transform);
    }

    private void Measure(
        int width,
        int height,
        double thresholdMilliseconds,
        DisplayTransformSnapshot transform)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = (byte)(index / 4);
            pixels[index + 1] = (byte)(index / 13);
            pixels[index + 2] = (byte)(index / 29);
            pixels[index + 3] = 255;
        }
        using var canonical = BitmapConversionService.ConvertToBitmap(pixels, width, height);
        using (transform.Derive(canonical, DisplaySourceColorSpace.Srgb)) { }
        var samples = new double[5];
        for (var run = 0; run < samples.Length; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var displayed = transform.Derive(canonical, DisplaySourceColorSpace.Srgb);
            samples[run] = stopwatch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(samples);
        _output.WriteLine($"display-derive {width}x{height}: median={samples[2]:F2} ms");
        Assert.True(samples[2] <= thresholdMilliseconds,
            $"{width} px median {samples[2]:F2} ms exceeded {thresholdMilliseconds:F0} ms.");
    }

    private static DisplayTransformSnapshot Resolve(
        string? profileName,
        DisplayAcmState acm) =>
        new DisplayColorManagementService(new FakePlatform(
            new("monitor", profileName == null ? null : Profile(profileName), acm)))
            .Resolve(1);

    private static string Profile(string name) => Path.Combine(FixtureDirectory, name);

    private static Avalonia.Media.Imaging.Bitmap LoadBitmap(string name)
    {
        using var image = new MagickImage(Path.Combine(FixtureDirectory, name));
        return BitmapConversionService.ConvertToBitmap(image)!;
    }

    private sealed class FakePlatform(DisplayPlatformResult result) : IDisplayProfilePlatform
    {
        public DisplayPlatformResult Result { get; set; } = result;
        public DisplayPlatformResult Resolve(nint windowHandle) => Result;
    }
}
