using System.Text.Json;

namespace HappyPhoton.Tests;

internal sealed class ColorScienceOracleData
{
    public int SchemaVersion { get; set; }
    public OracleGenerator Generator { get; set; } = new();
    public List<OracleColorSpace> Spaces { get; set; } = [];
    public List<OracleAdaptation> Adaptations { get; set; } = [];
    public List<OracleCameraCharacterization> CameraCharacterizations { get; set; } = [];
    public OracleTransferFunctions TransferFunctions { get; set; } = new();
    public OracleColorChecker ColorChecker { get; set; } = new();

    public static ColorScienceOracleData Load()
    {
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "color-science-oracle.json");
        return JsonSerializer.Deserialize<ColorScienceOracleData>(
            File.ReadAllText(path),
            JsonOptions) ?? throw new InvalidOperationException(
                $"Could not read colour-science oracle: {path}");
    }

    public OracleColorSpace Space(string id) => Spaces.Single(value => value.Id == id);

    public OracleAdaptation Adaptation(string id) =>
        Adaptations.Single(value => value.Id == id);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

internal sealed class OracleGenerator
{
    public string Script { get; set; } = "";
    public string ColourScienceVersion { get; set; } = "";
    public string NumpyVersion { get; set; } = "";
}

internal sealed class OracleColorSpace
{
    public string Id { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public double[][] Primaries { get; set; } = [];
    public double[] WhitePoint { get; set; } = [];
    public double[][] MatrixRgbToXyz { get; set; } = [];
    public double[][] MatrixXyzToRgb { get; set; } = [];
    public List<OracleRoundTrip> RoundTrips { get; set; } = [];
}

internal sealed class OracleRoundTrip
{
    public double[] Rgb { get; set; } = [];
    public double[] Xyz { get; set; } = [];
    public double[] RecoveredRgb { get; set; } = [];
}

internal sealed class OracleAdaptation
{
    public string Id { get; set; } = "";
    public double[] SourceWhite { get; set; } = [];
    public double[] DestinationWhite { get; set; } = [];
    public double[][] Matrix { get; set; } = [];
    public double[] SourceWhiteXyz { get; set; } = [];
    public double[] AdaptedWhiteXyz { get; set; } = [];
}

internal sealed class OracleTransferFunctions
{
    public List<OracleEotfSample> SrgbEotf { get; set; } = [];
}

internal sealed class OracleEotfSample
{
    public double Encoded { get; set; }
    public double Linear { get; set; }
}

internal sealed class OracleCameraCharacterization
{
    public string Id { get; set; } = "";
    public double[][] CameraToSrgb { get; set; } = [];
    public double[][] CameraToRec2020 { get; set; } = [];
    public List<OracleCameraSample> Samples { get; set; } = [];
}

internal sealed class OracleCameraSample
{
    public double[] CameraRgb { get; set; } = [];
    public double[] Rec2020 { get; set; } = [];
}

internal sealed class OracleColorChecker
{
    public string Dataset { get; set; } = "";
    public string Observer { get; set; } = "";
    public double[] Illuminant { get; set; } = [];
    public double[] ReferenceWhiteXyz { get; set; } = [];
    public int Rows { get; set; }
    public int Columns { get; set; }
    public List<OracleColorCheckerPatch> Patches { get; set; } = [];
}

internal sealed class OracleColorCheckerPatch
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public double[] XyY { get; set; } = [];
    public double[] Xyz { get; set; } = [];
    public double[] Lab { get; set; } = [];
}
