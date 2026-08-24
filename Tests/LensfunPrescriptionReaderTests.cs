using System.Diagnostics;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LensfunPrescriptionReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"happy-photon-lensfun-{Guid.NewGuid():N}");

    [Fact]
    public void SnapshotParsesEveryShippedXmlAndShipsInOutput()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "lensfun");
        var expected = Directory.EnumerateFiles(path, "*.xml").Count();

        var database = new LensfunDatabase(path);

        Assert.Equal(56, expected);
        Assert.True(database.CameraCount > 1000);
        Assert.True(database.LensCount > 1500);
        Assert.True(File.Exists(Path.Combine(path, "COPYING.CC_BY-SA_3.0")));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ColdSnapshotResolutionStaysWithinSupplementaryGate()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run the Lensfun cold-resolution gate.");

        var path = Path.Combine(AppContext.BaseDirectory, "data", "lensfun");
        var times = new List<double>();
        LensfunDatabase? retained = null;
        for (var run = 0; run < 5; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            retained = new LensfunDatabase(path);
            var match = retained.Resolve(
                "Canon", "EOS 6D", "Sigma 8mm f/3.5 EX DG Circular",
                8, 3.5, 5472, 3648);
            stopwatch.Stop();
            Assert.NotNull(match);
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        retained = null;
        Collect();
        var before = GC.GetTotalMemory(true);
        retained = new LensfunDatabase(path);
        Assert.NotNull(retained.Resolve(
            "Canon", "EOS 6D", "Sigma 8mm f/3.5 EX DG Circular",
            8, 3.5, 5472, 3648));
        Collect();
        var retainedBytes = GC.GetTotalMemory(true) - before;
        var median = times.Order().ElementAt(2);
        Console.Error.WriteLine(
            $"Lensfun G4 median={median:F1}ms retained={retainedBytes / 1048576d:F1}MB");

        Assert.True(median <= 250,
            $"Cold Lensfun resolution median was {median:F1} ms.");
        Assert.True(retainedBytes <= 20 * 1024 * 1024,
            $"Retained Lensfun memory was {retainedBytes / 1048576d:F1} MB.");
        GC.KeepAlive(retained);

        static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    [Fact]
    public void MatcherNormalizesPunctuationAndChecksCameraMount()
    {
        WriteDatabase(Lens("Exact Lens 24-70 f/2.8", "Mount A"));
        var database = new LensfunDatabase(_directory);

        var match = database.Resolve(
            "Camera Co.", "Model-One", "exact_lens 24 70 f2.8",
            35, 4, 6000, 4000);
        var wrongMount = database.Resolve(
            "Camera Co.", "Model-One", "Wrong Mount Lens",
            35, 4, 6000, 4000);

        Assert.NotNull(match);
        Assert.Null(wrongMount);
    }

    [Fact]
    public void MatcherAllowsLensMakerPrefixOnEitherIdentity()
    {
        WriteDatabase(
            Lens("Lens Co Wide Lens", "Mount A") +
            Lens("Tele Lens", "Mount A"));
        var database = new LensfunDatabase(_directory);

        var databasePrefix = database.Resolve(
            "Camera Co", "Model One", "Wide Lens", 35, 4, 6000, 4000);
        var suppliedPrefix = database.Resolve(
            "Camera Co", "Model One", "Lens Co Tele Lens", 35, 4, 6000, 4000);

        Assert.Equal("Lens Co Wide Lens", databasePrefix?.LensName);
        Assert.Equal("Tele Lens", suppliedPrefix?.LensName);
    }

    [Fact]
    public void MatcherRejectsAmbiguousMakerPrefixEquivalence()
    {
        WriteDatabase(
            Lens("Lens Co Duplicate Lens", "Mount A") +
            Lens("Duplicate Lens", "Mount A"));
        var database = new LensfunDatabase(_directory);

        Assert.Null(database.Resolve(
            "Camera Co", "Model One", "Duplicate Lens", 35, 4, 6000, 4000));
    }

    [Fact]
    public void MatcherRejectsAmbiguousLensAndMissingFocalLength()
    {
        WriteDatabase(
            Lens("Duplicate Lens", "Mount A") + Lens("Duplicate-Lens", "Mount A"));
        var database = new LensfunDatabase(_directory);

        Assert.Null(database.Resolve(
            "Camera Co", "Model One", "Duplicate Lens", 35, 4, 6000, 4000));
        Assert.Null(database.Resolve(
            "Camera Co", "Model One", "Duplicate Lens", 0, 4, 6000, 4000));
    }

    [Fact]
    public void MatcherRejectsCameraVariantCollision()
    {
        WriteDatabase(Lens("Exact Lens", "Mount A"), secondCamera: true);
        var database = new LensfunDatabase(_directory);

        Assert.Null(database.Resolve(
            "Camera Co", "Model One", "Exact Lens", 35, 4, 6000, 4000));
    }

    [Fact]
    public void AcmOnlyCalibrationIsNoData()
    {
        WriteDatabase(Lens("Exact Lens", "Mount A",
            "<distortion model=\"acm\" focal=\"35\" k1=\"0.1\"/>"));
        var database = new LensfunDatabase(_directory);

        Assert.Null(database.Resolve(
            "Camera Co", "Model One", "Exact Lens", 35, 4, 6000, 4000));
    }

    [Fact]
    public void MalformedSnapshotReturnsInvalidInsteadOfEscaping()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "broken.xml"), "<lensdatabase>");
        var metadata = new LibRawMetadata(
            "Camera Co", "Model One", "Camera Co", "Model One", "Exact Lens",
            100, 0.01f, 4, 35, null, null, 1,
            new LibRawGpsFacts(false, null, null, null));

        var result = new LensfunPrescriptionReader(_directory).Read(
            metadata, 6000, 4000);

        Assert.Equal(LensPrescriptionStatus.Invalid, result.Status);
    }

    [Fact]
    public void FixedLensCameraMatchesOnlyItsUniqueMountLens()
    {
        WriteDatabase(Lens("Built-in Lens", "fixedMount"), mount: "fixedMount");
        var database = new LensfunDatabase(_directory);

        var match = database.Resolve(
            "Camera Co", "Model One", null, 24, 4, 4000, 3000);

        Assert.Equal("Built-in Lens", match?.LensName);
    }

    [Fact]
    public void InterchangeableCameraRejectsMissingLensIdentity()
    {
        WriteDatabase(
            Lens("First Lens", "Mount A") + Lens("Second Lens", "Mount A"));
        var database = new LensfunDatabase(_directory);

        Assert.Null(database.Resolve(
            "Camera Co", "Model One", null, 24, 4, 6000, 4000));
    }

    [Fact]
    public void InterpolationUsesLogFocalAndLargestFocusDistance()
    {
        WriteDatabase(Lens(
            "Exact Lens", "Mount A",
            """
            <distortion model="poly5" focal="20" k1="0.1" k2="0.2"/>
            <distortion model="poly5" focal="80" k1="0.5" k2="0.6"/>
            <vignetting model="pa" focal="20" aperture="2" distance="10" k1="-9"/>
            <vignetting model="pa" focal="20" aperture="2" distance="1000" k1="-0.2"/>
            <vignetting model="pa" focal="20" aperture="6" distance="1000" k1="-0.6"/>
            <vignetting model="pa" focal="80" aperture="2" distance="1000" k1="-1.0"/>
            <vignetting model="pa" focal="80" aperture="6" distance="1000" k1="-1.4"/>
            """));
        var database = new LensfunDatabase(_directory);

        var match = database.Resolve(
            "Camera Co", "Model One", "Exact Lens", 40, 4, 6000, 4000);

        Assert.NotNull(match);
        Assert.Equal(0.3, match.Distortion!.Parameters[0], 12);
        Assert.Equal(-0.8, match.Vignette!.Parameters[0], 12);
    }

    [Theory]
    [InlineData("poly3", 0.7)]
    [InlineData("poly5", 1.05625)]
    [InlineData("ptlens", 0.6)]
    public void DistortionModelsMapDocumentedDestinationToSourceRadius(
        string modelName,
        double expectedFactor)
    {
        var model = modelName switch
        {
            "poly3" => LensfunDistortionModel.Poly3,
            "poly5" => LensfunDistortionModel.Poly5,
            _ => LensfunDistortionModel.Ptlens
        };
        var coefficients = model switch
        {
            LensfunDistortionModel.Poly3 => new[] { 0.4 },
            LensfunDistortionModel.Poly5 => new[] { 0.2, 0.1 },
            _ => new[] { 0.2, 0.1, 0.3 }
        };
        var prescription = Prescription(
            distortion: new LensfunDistortion(
                model, coefficients, 1, 0.5, 0.5));
        var plan = Plan(prescription);

        var mapped = Normalize(plan.Map(new LensPoint(0.75, 0.5), 1));

        Assert.Equal(0.5 + 0.25 * expectedFactor, mapped.X, 12);
        Assert.Equal(0.5, mapped.Y, 12);
    }

    [Fact]
    public void Poly3TcaAppliesOddRadiusTermAfterDistortion()
    {
        var prescription = Prescription(
            distortion: new LensfunDistortion(
                LensfunDistortionModel.Poly5, [0.2, 0], 1, 0.5, 0.5),
            tca: new LensfunTca(
                LensfunTcaModel.Poly3, [0.4, 0.2, 1.1], [0, 0, 1],
                1, 0.5, 0.5));
        var plan = Plan(prescription);
        var distortedRadius = 0.5 * (1 + 0.2 * 0.25);
        var tcaFactor = 1.1 + 0.2 * distortedRadius +
            0.4 * distortedRadius * distortedRadius;

        var mapped = Normalize(plan.Map(new LensPoint(0.75, 0.5), 0));

        Assert.Equal(0.5 + distortedRadius * tcaFactor / 2, mapped.X, 12);
    }

    [Fact]
    public void LinearTcaMapsRedAndBlueAroundUnchangedGreen()
    {
        var prescription = Prescription(tca: new LensfunTca(
            LensfunTcaModel.Linear, [1.01], [0.98], 1, 0.5, 0.5));
        var plan = Plan(prescription);

        Assert.Equal(0.7525,
            Normalize(plan.Map(new LensPoint(0.75, 0.5), 0)).X, 12);
        Assert.Equal(0.75,
            Normalize(plan.Map(new LensPoint(0.75, 0.5), 1)).X, 12);
        Assert.Equal(0.745,
            Normalize(plan.Map(new LensPoint(0.75, 0.5), 2)).X, 12);
    }

    [Fact]
    public void CropFactorAndAspectRescaleCalibrationRadius()
    {
        WriteDatabase(Lens("Exact Lens", "Mount A").Replace(
            "<cropfactor>1.5</cropfactor>", "<cropfactor>2</cropfactor>"));
        var database = new LensfunDatabase(_directory);

        var match = database.Resolve(
            "Camera Co", "Model One", "Exact Lens", 24, 4, 6000, 4000);

        Assert.Equal(4.0 / 3, match!.RadiusScale, 12);
    }

    [Fact]
    public void VignetteScaleIsPureCropRatioAcrossAspects()
    {
        // Calibration sensor 3:2 (default), actual frame 4:3: the
        // distortion scale carries the aspect correction while the pa
        // vignette scale is the bare crop ratio.
        WriteDatabase(Lens("Exact Lens", "Mount A",
            "<distortion model=\"poly3\" focal=\"24\" k1=\"0.1\"/>" +
            "<vignetting model=\"pa\" focal=\"24\" aperture=\"4\" " +
            "distance=\"1000\" k1=\"-0.3\" k2=\"0\" k3=\"0\"/>").Replace(
            "<cropfactor>1.5</cropfactor>", "<cropfactor>2</cropfactor>"));
        var database = new LensfunDatabase(_directory);

        var match = database.Resolve(
            "Camera Co", "Model One", "Exact Lens", 24, 4, 6000, 4500);

        var expectedDistortion = 4.0 / 3 *
            Math.Sqrt(1.5 * 1.5 + 1) / Math.Sqrt(4.0 / 3 * (4.0 / 3) + 1);
        Assert.Equal(expectedDistortion, match!.RadiusScale, 12);
        Assert.Equal(4.0 / 3, match.VignetteRadiusScale, 12);
        Assert.NotEqual(match.RadiusScale, match.VignetteRadiusScale);

        var reader = new LensfunPrescriptionReader(_directory);
        var metadata = new LibRawMetadata(
            "Camera Co", "Model One", "Camera Co", "Model One",
            "Exact Lens", 100, 0.01f, 4f, 24,
            null, null, 1, new LibRawGpsFacts(false, null, null, null));
        var prescription = reader.Read(metadata, 6000, 4500).Prescription!;
        Assert.Equal(expectedDistortion,
            prescription.LensfunDistortion!.RadiusScale, 12);
        Assert.Equal(4.0 / 3, prescription.LensfunVignette!.RadiusScale, 12);
    }

    [Fact]
    public void PaVignettingUsesReciprocalDocumentedGain()
    {
        var prescription = Prescription(vignette: new LensfunVignette(
            -0.4, 0.2, -0.1, 1, 0.5, 0.5));
        var plan = Plan(prescription, vignetting: true);
        // Vignetting normalizes r=1 at the frame corner; on the square test
        // frame that halves the r-squared of the half-height convention.
        const double r2 = 0.125;
        var expected = 1 / (1 + r2 * (-0.4 + r2 * (0.2 + r2 * -0.1)));
        var point = new LensPoint(0.75, 0.5);
        plan.Map(point, 1, out var greenPostGeometry);

        Assert.Equal(expected,
            plan.GetVignetteGain(point, greenPostGeometry), 12);
    }

    [Fact]
    public void PaVignettingUsesGreenPostDistortionRadius()
    {
        var prescription = Prescription(
            distortion: new LensfunDistortion(
                LensfunDistortionModel.Poly3, [0.4], 1, 0.5, 0.5),
            vignette: new LensfunVignette(-0.4, 0, 0, 1, 0.5, 0.5));
        var plan = Plan(prescription, vignetting: true);
        const double postDistortionRadius = 0.35;
        // Corner-normalized vignette radius on the square frame is the
        // post-distortion half-height radius divided by sqrt(2).
        var expected =
            1 / (1 - 0.4 * postDistortionRadius * postDistortionRadius / 2);
        var point = new LensPoint(0.75, 0.5);
        plan.Map(point, 1, out var greenPostGeometry);

        Assert.Equal(expected,
            plan.GetVignetteGain(point, greenPostGeometry), 12);
    }

    [Fact]
    public void EmbeddedClassesWinAndSummaryJoinsEnabledSources()
    {
        LensfunPrescriptionReader.ForceSource = true;
        var embedded = new LensPrescription(
            LensPrescriptionSource.FujifilmMakerNote, "Embedded", [], [],
            LensFrameWindow.Full, LensFrameWindow.Full,
            TableWarps: [new LensTableWarp(new LensRadialTable(
                1, [0, 1], [0, 1], 1, 1), null)]);
        var lensfun = Prescription(
            distortion: new LensfunDistortion(
                LensfunDistortionModel.Poly3, [0.1], 1, 0.5, 0.5),
            tca: new LensfunTca(
                LensfunTcaModel.Linear, [1.001], [0.999], 1, 0.5, 0.5),
            vignette: new LensfunVignette(-0.2, 0, 0, 1, 0.5, 0.5));

        var merged = LensfunPrescriptionReader.Merge(embedded, lensfun)!;
        var summary = merged.GetSummary(new BaseDecodeSettings(
            HlReconstructionMode.Clip, FbddMode.Off,
            Distortion: true, ChromaticAberration: true, Vignetting: false));

        Assert.Null(merged.LensfunDistortion);
        Assert.NotNull(merged.LensfunTca);
        Assert.Equal("FUJIFILM MAKER NOTE + LENSFUN", summary.Source);
    }

    [Fact]
    public void CommittedCanon6dRejectsAmbiguousShortLensIdentity()
    {
        var path = GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2");
        using var context = LibRawContext.Open(path);
        var metadata = context.GetMetadata();
        var dimensions = context.GetDimensions();

        var result = new LensfunPrescriptionReader().Read(
            metadata, (int)dimensions.VisibleWidth, (int)dimensions.VisibleHeight);

        Assert.Equal("8mm", metadata.Lens);
        Assert.Equal(LensPrescriptionStatus.None, result.Status);
    }

    [Fact]
    public void Canon6dMakerPrefixAndExactLensResolveFullFrameCalibration()
    {
        var metadata = new LibRawMetadata(
            "Canon", "EOS 6D", "Canon", "EOS 6D",
            "Sigma 8mm f/3.5 EX DG Circular", 100, 0.01f, 3.5f, 8, null,
            null, 1, new LibRawGpsFacts(false, null, null, null));

        var result = new LensfunPrescriptionReader().Read(metadata, 5472, 3648);

        Assert.Equal(LensPrescriptionStatus.Available, result.Status);
        Assert.Equal("Sigma 8mm f/3.5 EX DG Circular",
            result.Prescription!.LensName);
        Assert.Equal("LENSFUN", result.Prescription.Summary.Source);
    }

    private static LensCorrectionPlan Plan(
        LensPrescription prescription,
        bool vignetting = false) => new(
            1001, 1001, 1001, 1001, 1, prescription,
            new BaseDecodeSettings(
                HlReconstructionMode.Clip, FbddMode.Off,
                true, true, vignetting), 1);

    private static LensPoint Normalize(LensPoint point) => new(
        (point.X + 0.5) / 1001,
        (point.Y + 0.5) / 1001);

    private static LensPrescription Prescription(
        string lensName = "Exact Lens",
        LensfunDistortion? distortion = null,
        LensfunTca? tca = null,
        LensfunVignette? vignette = null) => new(
            LensPrescriptionSource.Lensfun, lensName, [], [],
            LensFrameWindow.Full, LensFrameWindow.Full,
            LensfunDistortion: distortion,
            LensfunTca: tca,
            LensfunVignette: vignette);

    private void WriteDatabase(
        string lenses,
        string mount = "Mount A",
        bool secondCamera = false)
    {
        Directory.CreateDirectory(_directory);
        var duplicate = secondCamera
            ? "<camera><maker>Camera Co</maker><model>Camera Co Model One</model>" +
              $"<mount>{mount}</mount><cropfactor>1.5</cropfactor></camera>"
            : string.Empty;
        File.WriteAllText(Path.Combine(_directory, "fixture.xml"), $$"""
            <lensdatabase version="2">
              <mount><name>{{mount}}</name></mount>
              <camera><maker>Camera Co</maker><model>Camera Co Model One</model>
                <mount>{{mount}}</mount><cropfactor>1.5</cropfactor></camera>
              {{duplicate}}
              {{lenses}}
            </lensdatabase>
            """);
    }

    private static string Lens(
        string model,
        string mount,
        string? calibration = null) => $$"""
        <lens><maker>Lens Co</maker><model>{{model}}</model><mount>{{mount}}</mount>
          <cropfactor>1.5</cropfactor><aspect-ratio>3:2</aspect-ratio>
          <calibration>{{calibration ?? "<distortion model=\"poly3\" focal=\"24\" k1=\"0.1\"/>"}}</calibration>
        </lens>
        """;

    public void Dispose()
    {
        LensfunPrescriptionReader.ForceSource = false;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
