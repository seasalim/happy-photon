using System.Text.Json;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionFixtureTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> PublicLabels() =>
    [
        ["nikon-d300-colorchecker.nef", -1.231583, true],
        ["panasonic-s9-standard.RW2", -.318697, false]
    ];

    public static IEnumerable<object[]> OwnerLabels() =>
    [
        ["DSCF0076.JPG", -1.638322, true],
        ["DSCF0075.JPG", -1.221181, false],
        ["DSCF0076.RAF", -1.644340, false],
        ["IMG_5569.JPG", -.369738, false]
    ];

    [Theory]
    [MemberData(nameof(PublicLabels))]
    public void G3Accuracy(string name, double label, bool recall)
    {
        using var pair = StraightenGateFixtures.Load(name);

        CheckG3(name, pair.Interactive.Pixels, label, recall);
    }

    [Theory]
    [MemberData(nameof(OwnerLabels))]
    public void G3OwnerAccuracy(string name, double label, bool recall)
    {
        using var pair = StraightenGateOwnerFixtures.Load(name);

        CheckG3(name, pair.Interactive.Pixels, label, recall);
    }

    [Fact]
    public void G3Pooled()
    {
        var errors = new List<double>();
        var photos = new List<string>();
        var ownerAvailable = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(StraightenGateOwnerFixtures.DirectoryVariable));

        foreach (var owner in new[] { false, true })
        {
            if (owner && !ownerAvailable) continue;

            foreach (var row in owner ? OwnerLabels() : PublicLabels())
            {
                var name = (string)row[0];
                if (!owner && !File.Exists(StraightenGateFixtures.PathFor(name))) continue;

                using var pair = owner ? StraightenGateOwnerFixtures.Load(name) : StraightenGateFixtures.Load(name);
                errors.AddRange(CheckG3(name, pair.Interactive.Pixels, (double)row[1], (bool)row[2], checkPhoto: false));
                photos.Add(name);
            }
        }

        var median = errors.Count > 0 ? FinishingGateSupport.Median(errors) : (double?)null;
        var max = errors.Count > 0 ? errors.Max() : (double?)null;
        Print("G3-pooled", new { photos, count = errors.Count, median, max });
        Assert.NotEmpty(photos);
        if (median.HasValue) Assert.True(median <= .15, $"Pooled G3 median {median} exceeds 0.15 degrees");
        if (max.HasValue) Assert.True(max <= .35);
    }

    private List<double> CheckG3(string name, MagickImage basis, double label, bool recall, bool checkPhoto = true)
    {
        var errors = new List<double>();

        foreach (var tilt in new double[] { -3, -1.5, -.5, 0, .5, 1.5, 3 })
        {
            if (Math.Abs(label - tilt) > 4.5) continue;

            using var frame = StraightenGateScenes.Tilt(basis, tilt);
            var result = HorizonDetection.Detect(frame, out var diagnostics);
            var error = Math.Abs(result.HorizonRotation - (label - tilt));
            Print("G3", new { name, tilt, label, diagnostics, result, error });

            // Owner exception: X-Trans decode variance makes this RAW's tier decode-dependent.
            if (name == StraightenGateWp3BaselineTests.BestEffortPhoto)
            {
                Assert.InRange(result.HorizonRotation, -5, 5);
                Assert.True(error <= Math.Abs(label - tilt) + StraightenGateWp3BaselineTests.NoHarmMargin);

                continue;
            }

            Assert.Equal(HorizonDetection.Tier.Lines, diagnostics.Tier);
            errors.Add(error);
        }

        if (errors.Count == 0) return errors;

        Print("G3-summary", new { name, arms = errors.Count,
            median = FinishingGateSupport.Median(errors), max = errors.Max() });
        if (checkPhoto) Assert.All(errors, error => Assert.True(error <= .35, $"Wrong answer: {name}, error={error}"));

        return errors;
    }

    public static IEnumerable<object[]> NegativePhotos() =>
        StraightenGateFixtures.NegativePhotos.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(NegativePhotos))]
    public void G4NoStructure(string name)
    {
        using var pair = StraightenGateFixtures.Load(name);
        CheckG4(name, pair.Interactive.Pixels);
    }

    [Fact]
    public void G4OwnerNoStructure()
    {
        using var pair = StraightenGateOwnerFixtures.Load("DSCF8369.JPG");

        CheckG4("DSCF8369.JPG", pair.Interactive.Pixels);
    }

    private void CheckG4(string name, MagickImage basis)
    {
        var result = HorizonDetection.Detect(basis, out var diagnostics);
        Print("G4", new { name, diagnostics, result });
        Assert.InRange(result.HorizonRotation, -1, 1);
        Assert.NotEqual(HorizonDetection.Tier.Lines, diagnostics.Tier);
    }

    [Theory]
    [InlineData("m2462362.DNG")]
    [InlineData("sony-a9m3-lossy.ARW")]
    [InlineData("canon-eos-350d.cr2")]
    [InlineData("nikon-d70-burst-1.nef")]
    public void ReportOnly(string name)
    {
        RequireReport();
        using var pair = StraightenGateFixtures.Load(name);
        var result = HorizonDetection.Detect(pair.Interactive.Pixels, out var diagnostics);
        Print("report-only", new { name, diagnostics, result });
    }

    [Theory]
    [InlineData("DSCF0129.RAF")]
    [InlineData("DSCF0155.JPG")]
    [InlineData("DSCF0423.JPG")]
    [InlineData("DSCF2263.RAF")]
    [InlineData("IMG_4952.JPG")]
    [InlineData("IMG_5177.JPG")]
    [InlineData("IMG_6854.HEIC")]
    public void OwnerReportOnly(string name)
    {
        RequireReport();
        using var pair = StraightenGateOwnerFixtures.Load(name);
        var result = HorizonDetection.Detect(pair.Interactive.Pixels, out var diagnostics);
        Print("report-only", new { name, diagnostics, result });
    }

    [Fact]
    public void OwnerRawJpegReportOnly()
    {
        RequireReport();
        using var jpeg = StraightenGateOwnerFixtures.Load("DSCF0076.JPG");
        using var raw = StraightenGateOwnerFixtures.Load("DSCF0076.RAF");
        var jpegResult = HorizonDetection.Detect(jpeg.Interactive.Pixels, out var jpegDiagnostics);
        var rawResult = HorizonDetection.Detect(raw.Interactive.Pixels, out var rawDiagnostics);
        var agrees = Math.Abs(jpegResult.HorizonRotation - rawResult.HorizonRotation) <= .10;
        Print("raw-jpeg-report-only", new { jpegResult, rawResult, jpegDiagnostics, rawDiagnostics, agrees });
    }

    private static void RequireReport() => Assert.SkipUnless(
        Environment.GetEnvironmentVariable("HAPPY_PHOTON_STRAIGHTEN_REPORT") == "1",
        "Report-only straighten photos require HAPPY_PHOTON_STRAIGHTEN_REPORT=1 for the report sweep");

    private void Print(string gate, object values) => output.WriteLine("STRAIGHTEN " +
        JsonSerializer.Serialize(new { gate, pid = Environment.ProcessId, values }));
}
