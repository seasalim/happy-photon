using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace HappyPhoton.Services;

internal sealed class LensfunDatabase
{
    private readonly LensfunCamera[] _cameras;
    private readonly LensfunLens[] _lenses;
    private readonly IReadOnlyDictionary<string, HashSet<string>> _mounts;

    internal LensfunDatabase(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var cameras = new List<LensfunCamera>();
        var lenses = new List<LensfunLens>();
        var mounts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(directory, "*.xml")
            .Order(StringComparer.OrdinalIgnoreCase)
            .AsParallel()
            .AsOrdered()
            .Select(ParseFile)
            .ToArray();
        foreach (var file in files)
        {
            cameras.AddRange(file.Cameras);
            lenses.AddRange(file.Lenses);
            foreach (var (mount, compatible) in file.Mounts)
            {
                if (!mounts.TryGetValue(mount, out var combined))
                    mounts[mount] = combined = new HashSet<string>(StringComparer.Ordinal);
                combined.UnionWith(compatible);
            }
        }
        _cameras = cameras.ToArray();
        _lenses = lenses.ToArray();
        _mounts = mounts;
    }

    private static LensfunFile ParseFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null
        });
        var root = XDocument.Load(reader).Root ??
            throw new InvalidDataException($"Lensfun XML has no root: {path}");
        var mounts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        ParseMounts(root, mounts);
        return new LensfunFile(
            root.Elements("camera").Select(ParseCamera).ToArray(),
            root.Elements("lens").Select(ParseLens).ToArray(),
            mounts);
    }

    internal int CameraCount => _cameras.Length;
    internal int LensCount => _lenses.Length;

    internal LensfunResolvedProfile? Resolve(
        string? make,
        string? model,
        string? lensName,
        double focalLength,
        double? aperture,
        int sensorWidth,
        int sensorHeight)
    {
        if (string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(model) ||
            !double.IsFinite(focalLength) || focalLength <= 0 ||
            sensorWidth <= 0 || sensorHeight <= 0)
            return null;
        var makeKey = Normalize(make);
        var modelKey = Normalize(model);
        var cameras = _cameras.Where(camera =>
            camera.MakerKey == makeKey && CameraModelMatches(
                camera.ModelKey, makeKey, modelKey)).ToArray();
        if (cameras.Length != 1) return null;
        var camera = cameras[0];
        var allowedMounts = CompatibleMounts(camera.Mount);
        var mounted = _lenses.Where(lens => lens.Mounts.Any(allowedMounts.Contains))
            .ToArray();
        LensfunLens[] matchingLenses;
        if (string.IsNullOrWhiteSpace(lensName))
        {
            matchingLenses = mounted.Length == 1 ? mounted : [];
        }
        else
        {
            var lensKey = Normalize(lensName);
            matchingLenses = mounted.Where(lens => ModelMatches(
                lens.ModelKey, lens.MakerKey, lensKey)).ToArray();
        }
        var lens = SelectLensCalibration(matchingLenses, camera.CropFactor);
        if (lens == null) return null;
        var distortion = InterpolateFocal(lens.Distortions, focalLength);
        var tca = InterpolateFocal(lens.Tcas, focalLength);
        var vignette = aperture is > 0 && double.IsFinite(aperture.Value)
            ? InterpolateVignette(lens.Vignettes, focalLength, aperture.Value)
            : null;
        if (distortion == null && tca == null && vignette == null) return null;

        var actualAspect = Math.Max(sensorWidth, sensorHeight) /
            (double)Math.Min(sensorWidth, sensorHeight);
        var calibrationCrop = lens.CropFactor ?? camera.CropFactor;
        if (!double.IsFinite(calibrationCrop) || calibrationCrop <= 0 ||
            !double.IsFinite(camera.CropFactor) || camera.CropFactor <= 0)
            return null;
        var radiusScale = calibrationCrop / camera.CropFactor *
            Math.Sqrt(lens.AspectRatio * lens.AspectRatio + 1) /
            Math.Sqrt(actualAspect * actualAspect + 1);
        // Vignetting normalizes r=1 at the frame corner (half-diagonal), so
        // the sensor rescale is the pure crop ratio; verified differentially
        // against liblensfun. Distortion/TCA keep the half-height convention.
        var vignetteRadiusScale = calibrationCrop / camera.CropFactor;
        if (!double.IsFinite(radiusScale) || radiusScale <= 0) return null;
        var centerX = 0.5 + lens.CenterX /
            (2 * radiusScale * actualAspect);
        var centerY = 0.5 + lens.CenterY / (2 * radiusScale);
        return new LensfunResolvedProfile(
            lens.Model, distortion, tca, vignette,
            radiusScale, vignetteRadiusScale, centerX, centerY);
    }

    private HashSet<string> CompatibleMounts(string mount)
    {
        var key = Normalize(mount);
        var result = new HashSet<string>(StringComparer.Ordinal) { key };
        if (_mounts.TryGetValue(key, out var compatible))
            result.UnionWith(compatible);
        return result;
    }

    private static bool CameraModelMatches(
        string databaseModel,
        string make,
        string suppliedModel) => ModelMatches(databaseModel, make, suppliedModel);

    private static bool ModelMatches(
        string databaseModel,
        string maker,
        string suppliedModel) =>
        databaseModel == suppliedModel ||
        databaseModel == maker + suppliedModel ||
        maker + databaseModel == suppliedModel;

    private static LensfunLens? SelectLensCalibration(
        IReadOnlyList<LensfunLens> matches,
        double cameraCrop)
    {
        if (matches.Count == 1) return matches[0];
        if (matches.Count == 0 || matches.Select(item => item.ModelKey).Distinct().Count() != 1)
            return null;
        var ranked = matches.Select(item => new
            {
                Lens = item,
                Distance = Math.Abs(Math.Log((item.CropFactor ?? cameraCrop) / cameraCrop))
            })
            .OrderBy(item => item.Distance)
            .ToArray();
        return ranked.Length > 1 && Math.Abs(ranked[0].Distance - ranked[1].Distance) < 1e-12
            ? null
            : ranked[0].Lens;
    }

    internal static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                result.Append(char.ToUpperInvariant(character));
        }
        return result.ToString();
    }

    private static void ParseMounts(
        XElement root,
        IDictionary<string, HashSet<string>> mounts)
    {
        foreach (var element in root.Elements("mount"))
        {
            var name = Text(element, "name");
            if (name == null) continue;
            var key = Normalize(name);
            if (!mounts.TryGetValue(key, out var compatible))
                mounts[key] = compatible = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in element.Elements("compat").Select(item => item.Value))
                compatible.Add(Normalize(value));
        }
    }

    private static LensfunCamera ParseCamera(XElement element) => new(
        Text(element, "maker") ?? string.Empty,
        Text(element, "model") ?? string.Empty,
        Text(element, "mount") ?? string.Empty,
        Number(element.Element("cropfactor"), 0));

    private static LensfunLens ParseLens(XElement element)
    {
        var calibration = element.Element("calibration");
        return new LensfunLens(
            Text(element, "maker") ?? string.Empty,
            Text(element, "model") ?? string.Empty,
            element.Elements("mount").Select(item => Normalize(item.Value)).ToArray(),
            OptionalNumber(element.Element("cropfactor")),
            Aspect(element.Element("aspect-ratio")?.Value),
            AttributeNumber(element.Element("center"), "x", 0),
            AttributeNumber(element.Element("center"), "y", 0),
            ParseCalibrations(calibration, "distortion", DistortionParameters),
            ParseCalibrations(calibration, "tca", TcaParameters),
            ParseCalibrations(calibration, "vignetting", VignetteParameters));
    }

    private static LensfunCalibration[] ParseCalibrations(
        XElement? calibration,
        string name,
        Func<XElement, double[]> parameters) => calibration?.Elements(name)
        .Select(element => new LensfunCalibration(
            (string?)element.Attribute("model") ?? "none",
            AttributeNumber(element, "focal", 0),
            AttributeNumber(element, "aperture", 0),
            AttributeNumber(element, "distance", 0),
            parameters(element)))
        .Where(item => item.Focal > 0 && item.Parameters.All(double.IsFinite))
        .OrderBy(item => item.Focal)
        .ToArray() ?? [];

    private static double[] DistortionParameters(XElement element) =>
        ((string?)element.Attribute("model")) switch
        {
            "poly3" => [AttributeNumber(element, "k1", 0)],
            "poly5" => [AttributeNumber(element, "k1", 0),
                AttributeNumber(element, "k2", 0)],
            "ptlens" => [AttributeNumber(element, "a", 0),
                AttributeNumber(element, "b", 0),
                AttributeNumber(element, "c", 0)],
            _ => []
        };

    private static double[] TcaParameters(XElement element) =>
        ((string?)element.Attribute("model")) switch
        {
            "linear" => [AttributeNumber(element, "kr", 1),
                AttributeNumber(element, "kb", 1)],
            "poly3" => [AttributeNumber(element, "br", 0),
                AttributeNumber(element, "cr", 0),
                AttributeNumber(element, "vr", 1),
                AttributeNumber(element, "bb", 0),
                AttributeNumber(element, "cb", 0),
                AttributeNumber(element, "vb", 1)],
            _ => []
        };

    private static double[] VignetteParameters(XElement element) =>
        (string?)element.Attribute("model") == "pa"
            ? [AttributeNumber(element, "k1", 0),
                AttributeNumber(element, "k2", 0),
                AttributeNumber(element, "k3", 0)]
            : [];

    private static LensfunCalibration? InterpolateFocal(
        IReadOnlyList<LensfunCalibration> values,
        double focal)
    {
        if (values.Count == 0) return null;
        var upper = values.FirstOrDefault(item => item.Focal >= focal) ?? values[^1];
        var lower = values.LastOrDefault(item => item.Focal <= focal) ?? values[0];
        return Interpolate(lower, upper, LogFraction(lower.Focal, upper.Focal, focal));
    }

    private static LensfunCalibration? InterpolateVignette(
        IReadOnlyList<LensfunCalibration> values,
        double focal,
        double aperture)
    {
        if (values.Count == 0) return null;
        var maximumDistance = values.Max(item => item.Distance);
        var atDistance = values.Where(item => item.Distance == maximumDistance).ToArray();
        var focals = atDistance.Select(item => item.Focal).Distinct().Order().ToArray();
        var lowerFocal = focals.LastOrDefault(value => value <= focal);
        if (lowerFocal == 0) lowerFocal = focals[0];
        var upperFocal = focals.FirstOrDefault(value => value >= focal);
        if (upperFocal == 0) upperFocal = focals[^1];
        var lower = AtAperture(atDistance, lowerFocal, aperture);
        var upper = AtAperture(atDistance, upperFocal, aperture);
        return lower == null || upper == null ? null : Interpolate(
            lower, upper, LogFraction(lowerFocal, upperFocal, focal));
    }

    private static LensfunCalibration? AtAperture(
        IReadOnlyList<LensfunCalibration> values,
        double focal,
        double aperture)
    {
        var atFocal = values.Where(item => item.Focal == focal)
            .OrderBy(item => item.Aperture).ToArray();
        if (atFocal.Length == 0) return null;
        var lower = atFocal.LastOrDefault(item => item.Aperture <= aperture) ?? atFocal[0];
        var upper = atFocal.FirstOrDefault(item => item.Aperture >= aperture) ?? atFocal[^1];
        var fraction = upper.Aperture == lower.Aperture ? 0 :
            Math.Clamp((aperture - lower.Aperture) /
                (upper.Aperture - lower.Aperture), 0, 1);
        return Interpolate(lower, upper, fraction);
    }

    private static LensfunCalibration? Interpolate(
        LensfunCalibration lower,
        LensfunCalibration upper,
        double fraction)
    {
        if (!string.Equals(lower.Model, upper.Model, StringComparison.Ordinal) ||
            lower.Parameters.Length != upper.Parameters.Length)
            return fraction <= 0.5 ? Supported(lower) : Supported(upper);
        if (Supported(lower) == null) return null;
        var parameters = lower.Parameters.Zip(upper.Parameters,
            (first, second) => first + (second - first) * fraction).ToArray();
        return lower with { Parameters = parameters };
    }

    private static LensfunCalibration? Supported(LensfunCalibration value) =>
        value.Parameters.Length == 0 ? null : value;

    private static double LogFraction(double lower, double upper, double value) =>
        lower == upper ? 0 : Math.Clamp(
            Math.Log(value / lower) / Math.Log(upper / lower), 0, 1);

    private static string? Text(XElement parent, string name) =>
        parent.Elements(name).FirstOrDefault(element => element.Attribute("lang") == null)
            ?.Value.Trim();

    private static double Aspect(string? value)
    {
        if (value?.Split(':') is [var left, var right])
            return Number(left, 0) / Number(right, 1);
        return Number(value, 1.5);
    }

    private static double AttributeNumber(
        XElement? element, string name, double fallback) =>
        Number((string?)element?.Attribute(name), fallback);
    private static double Number(XElement? element, double fallback) =>
        Number(element?.Value, fallback);
    private static double? OptionalNumber(XElement? element) => element == null
        ? null
        : Number(element.Value, 0) is > 0 and var value ? value : null;
    private static double Number(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var result) && double.IsFinite(result) ? result : fallback;
}

internal sealed record LensfunCamera(
    string Maker, string Model, string Mount, double CropFactor)
{
    internal string MakerKey { get; } = LensfunDatabase.Normalize(Maker);
    internal string ModelKey { get; } = LensfunDatabase.Normalize(Model);
}

internal sealed record LensfunLens(
    string Maker,
    string Model,
    IReadOnlyList<string> Mounts,
    double? CropFactor,
    double AspectRatio,
    double CenterX,
    double CenterY,
    IReadOnlyList<LensfunCalibration> Distortions,
    IReadOnlyList<LensfunCalibration> Tcas,
    IReadOnlyList<LensfunCalibration> Vignettes)
{
    internal string MakerKey { get; } = LensfunDatabase.Normalize(Maker);
    internal string ModelKey { get; } = LensfunDatabase.Normalize(Model);
}

internal sealed record LensfunCalibration(
    string Model,
    double Focal,
    double Aperture,
    double Distance,
    double[] Parameters);

internal sealed record LensfunResolvedProfile(
    string LensName,
    LensfunCalibration? Distortion,
    LensfunCalibration? Tca,
    LensfunCalibration? Vignette,
    double RadiusScale,
    double VignetteRadiusScale,
    double CenterX,
    double CenterY);

internal sealed record LensfunFile(
    IReadOnlyList<LensfunCamera> Cameras,
    IReadOnlyList<LensfunLens> Lenses,
    IReadOnlyDictionary<string, HashSet<string>> Mounts);
