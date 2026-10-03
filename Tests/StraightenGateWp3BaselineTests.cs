using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class StraightenGateWp3BaselineTests(ITestOutputHelper output)
{
    private const int Repetitions = 3;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SkylineOracle(bool portrait)
    {
        using var basis = StraightenGateScenes.Create("skyline", portrait);
        var errors = new List<double>();
        var valid = true;

        foreach (var tilt in StraightenGateScenes.Tilts)
        {
            using var frame = StraightenGateScenes.Tilt(basis, tilt);
            var fits = Enumerable.Range(0, Repetitions).Select(_ =>
                StraightenGateWp3SkylineOracle.Evaluate(frame)).ToArray();
            var tau = FinishingGateSupport.Median(fits.Select(f => f.ContentAngle).ToArray());
            var error = Math.Abs(tau - tilt);
            errors.Add(error);
            var share = FinishingGateSupport.Median(fits.Select(f => f.Share).ToArray());
            var span = FinishingGateSupport.Median(fits.Select(f => f.Span).ToArray());
            valid &= error <= .05 && share >= .45 && span >= .8;
            Print("WP3-skyline-oracle", new { portrait, tilt, tau, error, runs = Repetitions,
                share, span, fit = fits[0], stable = fits.All(f => f == fits[0]),
                valid = error <= .05 && share >= .45 && span >= .8 });
        }

        Print("WP3-skyline-oracle-summary", new { portrait, arms = errors.Count, max = errors.Max() });
        Assert.All(errors, error => Assert.True(error <= .05, $"Skyline oracle error: {error}"));
        Assert.True(valid, "Frozen skyline share/span/angle envelope failed; owner review required");
    }

    public static IEnumerable<object[]> SyntheticFrames() =>
        StraightenGateNegativeScenes.Names.Append("skyline").SelectMany(name =>
            new[] { false, true }.Select(portrait => new object[] { name, portrait }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SkylineReferenceClosedLoop(bool portrait)
    {
        using var basis = StraightenGateScenes.Create("skyline", portrait);
        var residuals = new List<double>();

        foreach (var tilt in StraightenGateScenes.Tilts.Where(t => Math.Abs(t) >= .2))
        {
            using var frame = StraightenGateScenes.Tilt(basis, tilt);
            using var final = StraightenGateScenes.Tilt(frame, -tilt);
            var samples = Enumerable.Range(0, Repetitions).Select(_ =>
                StraightenGateOracle.Fit(final, StraightenGateSkylineScene.Region(final)).ContentAngle).ToArray();
            var residual = FinishingGateSupport.Median(samples);
            residuals.Add(Math.Abs(residual));
            Print("WP3-skyline-reference", new { portrait, tilt, residual, runs = Repetitions,
                stable = samples.All(sample => sample == samples[0]) });
        }

        Print("WP3-skyline-reference-summary", new { portrait, arms = residuals.Count, max = residuals.Max() });
        Assert.All(residuals, residual => Assert.True(residual <= .03, $"Skyline reference residual: {residual}"));
    }

    [Theory]
    [MemberData(nameof(SyntheticFrames))]
    public void SyntheticMembership(string name, bool portrait)
    {
        using var basis = name == "skyline" ? StraightenGateScenes.Create(name, portrait) :
            StraightenGateNegativeScenes.Create(name, portrait);
        var tilts = name == "skyline" ? StraightenGateScenes.Tilts : new double[] { 0 };
        var fallback = 0;

        foreach (var tilt in tilts)
        {
            using var frame = StraightenGateScenes.Tilt(basis, tilt);
            var evaluation = Measure(name == "skyline" ? "G3s" : "G4", name, frame, tilt,
                name == "skyline" ? -tilt : null, portrait);
            if (evaluation.FallbackEligible) fallback++;

            if (name == "skyline")
            {
                Assert.True(evaluation.FallbackEligible, "Frozen synthetic skyline workload must reach a fallback tier");
                Assert.False(evaluation.HorizontalCandidate || evaluation.VerticalCandidate,
                    "Frozen synthetic skyline must have no qualifying family at the 200px candidate floor");
            }
        }

        Print("WP3-membership-summary", new { name, portrait, arms = tilts.Length, fallback,
            everyArmFallbackEligible = fallback == tilts.Length });
    }

    [Theory]
    [InlineData("skyline")]
    [InlineData("fbm")]
    public void G5FrameMembership(string name)
    {
        using var frame = name == "skyline" ? StraightenGateScenes.Create(name, false) :
            StraightenGateNegativeScenes.Create(name, false);

        var evaluation = Measure("G5-membership-only", name, frame, 0, null, false);
        Assert.True(evaluation.FallbackEligible, "Frozen G5 fallback frame must remain fallback-eligible");
    }

    public static IEnumerable<object[]> PublicNegativePhotos() =>
        StraightenGateFixtures.NegativePhotos.Select(name => new object[] { name });

    [Theory]
    [InlineData("canon-eos-6d-iso-6400.cr2")]
    [InlineData("iphone-14-pro-iso-1000.heic")]
    [InlineData("nikon-d300-colorchecker.nef")]
    public void PublicG5FrameMembership(string name)
    {
        using var pair = StraightenGateFixtures.Load(name);

        Measure("G5-membership-only", name, pair.Interactive.Pixels, 0, null);
    }

    [Theory]
    [MemberData(nameof(PublicNegativePhotos))]
    public void PublicNegativeMembership(string name)
    {
        using var pair = StraightenGateFixtures.Load(name);

        Measure("G4", name, pair.Interactive.Pixels, 0, null);
    }

    private StraightenGateWp3LinePredicate.Evaluation Measure(string gate, string name,
        MagickImage frame, double tilt, double? target, bool? portrait = null)
    {
        var samples = new List<(HorizonDetection.Result Result, HorizonDetection.Diagnostics Diagnostics)>();

        for (var run = 0; run < Repetitions; run++)
        {
            var result = HorizonDetection.Detect(frame, out var diagnostics);
            samples.Add((result, diagnostics));
        }

        var first = samples[0];
        var evaluation = StraightenGateWp3LinePredicate.Evaluate(first.Diagnostics);
        var rotations = samples.Select(s => s.Result.HorizonRotation).ToArray();
        var median = rotations.Length == 0 ? (double?)null : FinishingGateSupport.Median(rotations);
        double? closedLoop = null;

        if (gate == "G3s" && name == "skyline" && Math.Abs(tilt) >= .2 && median.HasValue)
        {
            using var final = RenderGeometry.Apply(frame, new EditSettings { HorizonRotation = median.Value }, out _);
            closedLoop = StraightenGateOracle.Fit(final, StraightenGateSkylineScene.Region(final)).ContentAngle;
        }

        Print("WP3-arm", new { gate, name, portrait, tilt, target, runs = Repetitions,
            answered = rotations.Length, median, closedLoop,
            error = target.HasValue && median.HasValue ? Math.Abs(median.Value - target.Value) : (double?)null,
            predictedError = target.HasValue && evaluation.PredictedRotation.HasValue ?
                Math.Abs(evaluation.PredictedRotation.Value - target.Value) : (double?)null,
            evaluation, diagnostics = first.Diagnostics, stable = samples.All(s => s == first) });

        VerifyAnswer(gate, name, frame, tilt, target, first.Result, first.Diagnostics, evaluation);
        Assert.All(samples, sample => Assert.Equal(first, sample));

        return evaluation;
    }

    private void Print(string gate, object values) => output.WriteLine("STRAIGHTEN " +
        JsonSerializer.Serialize(new { gate, pid = Environment.ProcessId, values }));
}
