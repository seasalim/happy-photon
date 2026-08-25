#:project ../HappyPhoton.csproj
#:property PublishAot=false
#:property SelfContained=false
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
var inputs = new List<string>();
var outputDirectory = Path.Combine(
    Path.GetTempPath(), "happy-photon-raf-lens-evaluation");
var source = "embedded";
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--output" && index + 1 < args.Length)
        outputDirectory = Path.GetFullPath(args[++index]);
    else if (args[index] == "--source" && index + 1 < args.Length)
        source = args[++index].ToLowerInvariant();
    else
        inputs.Add(Path.GetFullPath(args[index]));
}
if (inputs.Count == 0 || source is not ("embedded" or "lensfun"))
{
    Console.Error.WriteLine(
        "Usage: dotnet run --file scripts/evaluate-raf-lens-corrections.cs -- " +
        "<raw-file-or-directory> [...] [--source embedded|lensfun] " +
        "[--output <directory>]");
    return 2;
}
var paths = inputs.SelectMany(path => Directory.Exists(path)
        ? Directory.EnumerateFiles(path).Where(file =>
            new[] { ".raf", ".cr2", ".cr3", ".nef", ".dng", ".arw", ".orf" }
                .Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
        : [path])
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Order(StringComparer.OrdinalIgnoreCase)
    .ToArray();
var readerType = typeof(RawBaseLoader).Assembly.GetType(
    "HappyPhoton.Services.FujiLensPrescriptionReader", throwOnError: true)!;
readerType.GetProperty("IncludeUnqualifiedTables",
    BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
var lensfunType = typeof(RawBaseLoader).Assembly.GetType(
    "HappyPhoton.Services.LensfunPrescriptionReader", throwOnError: true)!;
lensfunType.GetProperty("ForceSource",
    BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, source == "lensfun");
Environment.SetEnvironmentVariable("OMP_NUM_THREADS", "1");
Directory.CreateDirectory(outputDirectory);
var reports = new List<FileReport>();
var failed = false;
foreach (var path in paths)
{
    Console.WriteLine($"Evaluating {Path.GetFileName(path)}...");
    try
    {
        var reference = SelectReference(path);
        using var referenceImage = reference.Image;
        using var inactive = Render(path, false, false, false);
        using var distortion = Render(path, true, false, false);
        using var ca = Render(path, false, true, false);
        using var vignetting = Render(path, false, false, true);
        var stem = Path.GetFileNameWithoutExtension(path);
        referenceImage.Write(Path.Combine(outputDirectory, $"{stem}-reference.jpg"));
        inactive.Write(Path.Combine(outputDirectory, $"{stem}-inactive.jpg"));
        distortion.Write(Path.Combine(outputDirectory, $"{stem}-distortion.jpg"));
        ca.Write(Path.Combine(outputDirectory, $"{stem}-ca.jpg"));
        vignetting.Write(Path.Combine(outputDirectory, $"{stem}-vignetting.jpg"));
        var target = Raster.From(inactive);
        var referenceRaster = Raster.From(referenceImage, target.Width, target.Height);
        var inactiveRegistration = Register(target, referenceRaster);
        var distortionRaster = Raster.From(distortion, target.Width, target.Height);
        var distortionRegistration = Register(distortionRaster, referenceRaster);
        var geometryBefore = GeometryResiduals(
            target, referenceRaster, inactiveRegistration);
        var geometryAfter = GeometryResiduals(
            distortionRaster, referenceRaster, distortionRegistration);
        var caBefore = ChromaticResiduals(target);
        var caAfter = ChromaticResiduals(Raster.From(ca, target.Width, target.Height));
        var vignetteBefore = PhotometricResiduals(
            target, referenceRaster, inactiveRegistration);
        var vignetteRaster = Raster.From(vignetting, target.Width, target.Height);
        var vignetteAfter = PhotometricResiduals(
            vignetteRaster, referenceRaster, Register(vignetteRaster, referenceRaster));
        var gates = new[]
        {
            Gate("distortion", geometryBefore, geometryAfter, 0.40,
                PixelEqual(inactive, distortion)),
            Gate("chromatic-aberration", caBefore, caAfter, 0.40,
                PixelEqual(inactive, ca)),
            Gate("vignetting", vignetteBefore, vignetteAfter, 0.30,
                PixelEqual(inactive, vignetting))
        };
        failed |= gates.Any(gate => !gate.Passed);
        var report = new FileReport(
            path, source,
            reference.Offset,
            reference.Width,
            reference.Height,
            reference.Sha256,
            inactiveRegistration.Score,
            gates);
        reports.Add(report);
        foreach (var gate in gates)
            Console.WriteLine(
                $"  {gate.Name}: {gate.Before:F5} -> {gate.After:F5}; " +
                $"reduction={gate.Reduction:P1}; floor={gate.ThreeSigma:F5}; " +
                $"{(gate.NotApplicable ? "IDENTITY" : gate.Passed ? "PASS" : "FAIL")}");
    }
    catch (Exception exception)
    {
        failed = true;
        reports.Add(new FileReport(path, source, 0, 0, 0, "", 0, [], exception.Message));
        Console.WriteLine($"  FAILURE: {exception.Message}");
    }
}
var reportPath = Path.Combine(outputDirectory, $"{source}-lens-qualification.json");
File.WriteAllText(reportPath, JsonSerializer.Serialize(reports,
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Report: {reportPath}");
return failed ? 1 : 0;
static ReferenceCandidate SelectReference(string path)
{
    using var context = LibRawContext.Open(path);
    var dimensions = context.GetDimensions();
    var rawWidth = dimensions.OutputWidth;
    var rawHeight = dimensions.OutputHeight;
    var rawAspect = rawWidth / (double)rawHeight;
    var bytes = File.ReadAllBytes(path);
    var candidates = new List<ReferenceCandidate>();
    var observed = new List<string>();
    for (var start = 0; start + 3 < bytes.Length; start++)
    {
        if (bytes[start] != 0xff || bytes[start + 1] != 0xd8 || bytes[start + 2] != 0xff)
            continue;
        var end = FindJpegEnd(bytes, start + 2);
        if (end < 0) continue;
        try
        {
            var data = bytes.AsSpan(start, end + 2 - start).ToArray();
            var image = new MagickImage(data);
            image.AutoOrient();
            var width = checked((int)image.Width);
            var height = checked((int)image.Height);
            observed.Add($"{start}:{width}x{height}");
            var aspect = width / (double)height;
            if (Math.Max(width, height) >= 1024 &&
                Math.Abs(aspect / rawAspect - 1) <= 0.02)
            {
                candidates.Add(new ReferenceCandidate(
                    start, width, height,
                    Convert.ToHexString(SHA256.HashData(data)), image));
            }
            else image.Dispose();
        }
        catch (MagickException) { }
    }
    var selected = candidates
        .OrderByDescending(candidate => (long)candidate.Width * candidate.Height)
        .ThenBy(candidate => candidate.Offset)
        .FirstOrDefault() ?? throw new InvalidOperationException(
            "No embedded JPEG has a >=1024 px long edge and aspect within 2% " +
            $"of {rawWidth}x{rawHeight}; decoded: {string.Join(", ", observed)}.");
    foreach (var candidate in candidates)
        if (!ReferenceEquals(candidate, selected)) candidate.Image.Dispose();
    return selected;
}
static int FindJpegEnd(byte[] bytes, int start)
{
    var index = start;
    while (index + 1 < bytes.Length)
    {
        while (index < bytes.Length && bytes[index] != 0xff) index++;
        while (index < bytes.Length && bytes[index] == 0xff) index++;
        if (index >= bytes.Length) break;
        var marker = bytes[index++];
        if (marker == 0xd9) return index - 2;
        if (marker is >= 0xd0 and <= 0xd8 or 0x01) continue;
        if (index + 1 >= bytes.Length) break;
        var length = (bytes[index] << 8) | bytes[index + 1];
        if (length < 2 || index + length > bytes.Length) break;
        index += length;
        if (marker != 0xda) continue;
        while (index + 1 < bytes.Length)
        {
            if (bytes[index++] != 0xff) continue;
            while (index < bytes.Length && bytes[index] == 0xff) index++;
            if (index >= bytes.Length) break;
            var entropyMarker = bytes[index];
            if (entropyMarker == 0x00 || entropyMarker is >= 0xd0 and <= 0xd7)
            {
                index++;
                continue;
            }
            if (entropyMarker == 0xd9) return index - 1;
            index--;
            break;
        }
    }
    return -1;
}

static MagickImage Render(string path, bool distortion, bool ca, bool vignette)
{
    var settings = new BaseDecodeSettings(
        HlReconstructionMode.Clip, distortion, ca, vignette);
    using var baseImage = new RawBaseLoader().LoadPreviewBase(
        new ImageFile(path), settings, CancellationToken.None) ??
        throw new InvalidOperationException("RAW decode failed.");
    using var result = new RenderPipeline().Render(new RenderRequest(
        baseImage, new EditSettings(), RenderIntent.Export, 1200,
        new RenderOptions(false, false)));
    return new MagickImage(result.Image);
}
static bool PixelEqual(MagickImage first, MagickImage second)
{
    if (first.Width != second.Width || first.Height != second.Height) return false;
    using var firstPixels = first.GetPixelsUnsafe();
    using var secondPixels = second.GetPixelsUnsafe();
    return firstPixels.ToShortArray(PixelMapping.RGB)!
        .AsSpan().SequenceEqual(secondPixels.ToShortArray(PixelMapping.RGB)!);
}
static Registration Register(Raster source, Raster reference)
{
    var transform = new Registration(1, 1, 0, 0, double.NegativeInfinity);
    foreach (var step in new[]
             {
                 (Scale: 0.04, Shift: 16d),
                 (Scale: 0.015, Shift: 5d),
                 (Scale: 0.005, Shift: 1.5d),
                 (Scale: 0.0015, Shift: 0.5d)
             })
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var candidate in Neighbors(transform, step.Scale, step.Shift))
            {
                var score = RegistrationScore(source, reference, candidate);
                if (score <= transform.Score) continue;
                transform = candidate with { Score = score };
                changed = true;
            }
        }
    }
    if (transform.Score < 0.15)
        throw new InvalidOperationException(
            $"Insufficient reliable registration correspondences ({transform.Score:F3}).");
    return transform;
}

static IEnumerable<Registration> Neighbors(
    Registration value, double scale, double shift)
{
    yield return value;
    yield return value with { ScaleX = value.ScaleX - scale };
    yield return value with { ScaleX = value.ScaleX + scale };
    yield return value with { ScaleY = value.ScaleY - scale };
    yield return value with { ScaleY = value.ScaleY + scale };
    yield return value with { OffsetX = value.OffsetX - shift };
    yield return value with { OffsetX = value.OffsetX + shift };
    yield return value with { OffsetY = value.OffsetY - shift };
    yield return value with { OffsetY = value.OffsetY + shift };
}

static double RegistrationScore(Raster source, Raster reference, Registration value)
{
    double sumX = 0, sumY = 0, sumX2 = 0, sumY2 = 0, sumXY = 0;
    var count = 0;
    for (var y = 24; y < source.Height - 24; y += 8)
    for (var x = 24; x < source.Width - 24; x += 8)
    {
        var mapped = value.Map(x, y, source.Width, source.Height);
        if (!reference.Contains(mapped.X, mapped.Y, 2)) continue;
        var a = source.Gradient(x, y);
        var b = reference.Gradient(mapped.X, mapped.Y);
        sumX += a; sumY += b; sumX2 += a * a; sumY2 += b * b; sumXY += a * b;
        count++;
    }
    if (count < 1000) return double.NegativeInfinity;
    var numerator = sumXY - sumX * sumY / count;
    var denominator = Math.Sqrt(
        Math.Max(0, sumX2 - sumX * sumX / count) *
        Math.Max(0, sumY2 - sumY * sumY / count));
    return denominator > 0 ? numerator / denominator : double.NegativeInfinity;
}

static CellValues GeometryResiduals(
    Raster source, Raster reference, Registration registration) =>
    Cells(source, (x, y) =>
    {
        var mapped = registration.Map(x, y, source.Width, source.Height);
        var best = PatchOffset(source, reference, x, y, mapped.X, mapped.Y, 5);
        return Math.Sqrt(best.X * best.X + best.Y * best.Y);
    });

static CellValues ChromaticResiduals(Raster image) => Cells(image, (x, y) =>
{
    var red = PatchOffset(image, image, x, y, x, y, 2, 1, 0);
    var blue = PatchOffset(image, image, x, y, x, y, 2, 1, 2);
    return Math.Sqrt(red.X * red.X + red.Y * red.Y) +
           Math.Sqrt(blue.X * blue.X + blue.Y * blue.Y);
});

static CellValues PhotometricResiduals(
    Raster source, Raster reference, Registration registration)
{
    var sourceMeans = new List<double>();
    var referenceMeans = new List<double>();
    var parity = new List<int>();
    const int columns = 14;
    const int rows = 10;
    for (var row = 1; row < rows - 1; row++)
    for (var column = 1; column < columns - 1; column++)
    {
        var x = (column * source.Width + source.Width / 2) / columns;
        var y = (row * source.Height + source.Height / 2) / rows;
        var mapped = registration.Map(x, y, source.Width, source.Height);
        sourceMeans.Add(source.Mean(x, y, 15));
        referenceMeans.Add(reference.Mean(mapped.X, mapped.Y, 15));
        parity.Add((row + column) & 1);
    }
    var meanX = referenceMeans.Average();
    var meanY = sourceMeans.Average();
    var covariance = referenceMeans.Zip(sourceMeans)
        .Sum(pair => (pair.First - meanX) * (pair.Second - meanY));
    var variance = referenceMeans.Sum(value => (value - meanX) * (value - meanX));
    var slope = variance > 0 ? covariance / variance : 1;
    var intercept = meanY - slope * meanX;
    return new CellValues(sourceMeans.Zip(referenceMeans)
        .Select(pair => Math.Abs(pair.First - (slope * pair.Second + intercept)))
        .ToArray(), parity.ToArray());
}

static CellValues Cells(Raster image, Func<int, int, double> metric)
{
    const int columns = 14;
    const int rows = 10;
    var values = new List<double>();
    var parity = new List<int>();
    for (var row = 1; row < rows - 1; row++)
    for (var column = 1; column < columns - 1; column++)
    {
        var x = (column * image.Width + image.Width / 2) / columns;
        var y = (row * image.Height + image.Height / 2) / rows;
        values.Add(metric(x, y));
        parity.Add((row + column) & 1);
    }
    return new CellValues(values.ToArray(), parity.ToArray());
}

static PatchMatch PatchOffset(
    Raster first, Raster second, double x1, double y1, double x2, double y2,
    int search, int channel1 = -1, int channel2 = -1)
{
    var best = (X: 0d, Y: 0d, Score: double.NegativeInfinity);
    for (var dy = -search; dy <= search; dy++)
    for (var dx = -search; dx <= search; dx++)
    {
        double sumA = 0, sumB = 0, aa = 0, bb = 0, ab = 0;
        var count = 0;
        for (var py = -5; py <= 5; py += 2)
        for (var px = -5; px <= 5; px += 2)
        {
            var a = first.Sample(x1 + px, y1 + py, channel1);
            var b = second.Sample(x2 + px + dx, y2 + py + dy, channel2);
            sumA += a; sumB += b;
            aa += a * a; bb += b * b; ab += a * b; count++;
        }
        var covariance = ab - sumA * sumB / count;
        var score = covariance / Math.Sqrt(Math.Max(1e-12,
            (aa - sumA * sumA / count) * (bb - sumB * sumB / count)));
        if (score > best.Score) best = (dx, dy, score);
    }
    return new PatchMatch(best.X, best.Y, best.Score);
}

static GateReport Gate(
    string name, CellValues before, CellValues after, double target, bool identity)
{
    if (identity) return new GateReport(name, 0, 0, 0, 0, true, [], true);
    var halfReports = new List<HalfGate>();
    for (var parity = 0; parity < 2; parity++)
    {
        var indices = Enumerable.Range(0, before.Values.Length)
            .Where(index => before.Parity[index] == parity).ToArray();
        var beforeMean = indices.Average(index => before.Values[index]);
        var afterMean = indices.Average(index => after.Values[index]);
        var sigma = BootstrapSigma(indices, before.Values, after.Values);
        halfReports.Add(new HalfGate(
            parity, beforeMean, afterMean, sigma,
            afterMean < beforeMean && beforeMean - afterMean > 3 * sigma));
    }
    var beforeAll = before.Values.Average();
    var afterAll = after.Values.Average();
    return new GateReport(
        name, beforeAll, afterAll,
        (beforeAll - afterAll) / Math.Max(beforeAll, 1e-12),
        halfReports.Max(half => 3 * half.Sigma),
        halfReports.All(half => half.Passed) &&
            (beforeAll - afterAll) / Math.Max(beforeAll, 1e-12) >= target,
        halfReports,
        false);
}

static double BootstrapSigma(
    int[] indices, double[] before, double[] after)
{
    var random = new Random(183 + indices.Length);
    var samples = new double[1000];
    for (var sample = 0; sample < samples.Length; sample++)
    {
        var total = 0d;
        for (var index = 0; index < indices.Length; index++)
        {
            var selected = indices[random.Next(indices.Length)];
            total += before[selected] - after[selected];
        }
        samples[sample] = total / indices.Length;
    }
    var mean = samples.Average();
    return Math.Sqrt(samples.Sum(value => (value - mean) * (value - mean)) /
        (samples.Length - 1));
}

sealed class Raster
{
    private readonly float[] _rgb;
    public int Width { get; }
    public int Height { get; }

    private Raster(int width, int height, float[] rgb) =>
        (Width, Height, _rgb) = (width, height, rgb);

    public static Raster From(MagickImage source, int? width = null, int? height = null)
    {
        using var image = new MagickImage(source);
        image.AutoOrient();
        if (width != null && height != null)
            image.Resize(new MagickGeometry((uint)width, (uint)height)
                { IgnoreAspectRatio = true });
        else if (Math.Max(image.Width, image.Height) > 720)
            image.Resize(new MagickGeometry(720, 720)
                { IgnoreAspectRatio = false, Greater = true });
        image.ColorSpace = ColorSpace.sRGB;
        using var pixels = image.GetPixelsUnsafe();
        var values = pixels.ToShortArray(PixelMapping.RGB)!;
        return new Raster(checked((int)image.Width), checked((int)image.Height),
            values.Select(value => value / (float)ushort.MaxValue).ToArray());
    }

    public bool Contains(double x, double y, int margin) =>
        x >= margin && y >= margin && x < Width - margin && y < Height - margin;
    public double Gradient(double x, double y) => Math.Sqrt(
        Math.Pow(Sample(x + 1, y) - Sample(x - 1, y), 2) +
        Math.Pow(Sample(x, y + 1) - Sample(x, y - 1), 2));
    public double Sample(double x, double y, int channel = -1)
    {
        var ix = Math.Clamp((int)Math.Round(x), 0, Width - 1);
        var iy = Math.Clamp((int)Math.Round(y), 0, Height - 1);
        var offset = (iy * Width + ix) * 3;
        return channel >= 0 ? _rgb[offset + channel] :
            0.2126 * _rgb[offset] + 0.7152 * _rgb[offset + 1] + 0.0722 * _rgb[offset + 2];
    }
    public double Mean(double x, double y, int radius)
    {
        var total = 0d;
        var count = 0;
        for (var dy = -radius; dy <= radius; dy += 3)
        for (var dx = -radius; dx <= radius; dx += 3)
        {
            total += Sample(x + dx, y + dy);
            count++;
        }
        return total / count;
    }
}

sealed record ReferenceCandidate(long Offset, int Width, int Height,
    string Sha256, MagickImage Image);
sealed record Registration(double ScaleX, double ScaleY, double OffsetX,
    double OffsetY, double Score)
{
    public (double X, double Y) Map(double x, double y, int width, int height) =>
        ((x - width * 0.5) * ScaleX + width * 0.5 + OffsetX,
         (y - height * 0.5) * ScaleY + height * 0.5 + OffsetY);
}
sealed record CellValues(double[] Values, int[] Parity);
readonly record struct PatchMatch(double X, double Y, double Score);
sealed record HalfGate(int Parity, double Before, double After, double Sigma,
    bool Passed);
sealed record GateReport(string Name, double Before, double After, double Reduction,
    double ThreeSigma, bool Passed, IReadOnlyList<HalfGate> Halves,
    bool NotApplicable);
sealed record FileReport(string Path, string Source, long ReferenceOffset,
    int ReferenceWidth, int ReferenceHeight,
    string ReferenceSha256, double RegistrationScore,
    IReadOnlyList<GateReport> Gates, string? Error = null);
