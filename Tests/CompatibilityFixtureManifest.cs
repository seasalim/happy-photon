using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyPhoton.Tests;

internal sealed class CompatibilityFixtureManifest
{
    internal const long MaximumFixtureBytes = 30L * 1024 * 1024;

    public int SchemaVersion { get; init; }
    public string? CommittedFixtureProvenance { get; init; }
    public List<CompatibilityFixture>? Fixtures { get; init; }

    internal IReadOnlyList<CompatibilityFixture> SelectedFixtures =>
        Fixtures!.Where(fixture => fixture.SelectionStatus == "selected").ToArray();

    internal static CompatibilityFixtureManifest Load(string path)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        var manifest = JsonSerializer.Deserialize<CompatibilityFixtureManifest>(
            File.ReadAllText(path), options) ??
            throw new InvalidDataException("Compatibility manifest is empty.");
        manifest.Validate();
        return manifest;
    }

    internal void Validate()
    {
        Require(SchemaVersion == 1, "schemaVersion must be 1.");
        Require(
            CommittedFixtureProvenance == "assets/README.md",
            "committedFixtureProvenance must link to assets/README.md.");
        Require(Fixtures is { Count: > 0 }, "fixtures must not be empty.");

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fixture in Fixtures!)
        {
            fixture.Validate();
            Require(
                slugs.Add(fixture.Slug!),
                $"Duplicate compatibility fixture slug: {fixture.Slug}.");
        }
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class CompatibilityFixture
{
    public string? Slug { get; init; }
    public string? CameraMake { get; init; }
    public string? CameraModel { get; init; }
    public string? CaptureMode { get; init; }
    public string? Extension { get; init; }
    public string? ProvenanceUrl { get; init; }
    public string? License { get; init; }
    public long? SizeBytes { get; init; }
    public string? Sha256 { get; init; }
    public string? SelectionStatus { get; init; }
    public string? ExpectationStatus { get; init; }
    public CompatibilityExpectation? Expected { get; init; }
    public List<JsonElement>? PlatformOverrides { get; init; }
    public string? TestLevel { get; init; }

    internal string FileName => $"{Slug}{Extension}";

    internal void Validate()
    {
        RequireText(Slug, "slug");
        CompatibilityFixtureManifest.Require(
            Slug!.Length <= 80 &&
            Slug[0] != '-' && Slug[^1] != '-' &&
            Slug.All(character =>
                char.IsAsciiLetterLower(character) ||
                char.IsAsciiDigit(character) || character == '-'),
            $"{Slug}: slug must use lowercase ASCII letters, digits, and hyphens.");
        CompatibilityFixtureManifest.Require(
            SelectionStatus is "pending" or "selected",
            $"{Slug}: invalid selectionStatus.");
        if (SelectionStatus == "pending")
        {
            CompatibilityFixtureManifest.Require(
                CameraMake == null && CameraModel == null && CaptureMode == null &&
                Extension == null && ProvenanceUrl == null && License == null &&
                SizeBytes == null && Sha256 == null && ExpectationStatus == null &&
                Expected == null && PlatformOverrides == null && TestLevel == null,
                $"{Slug}: pending entries must omit fixture-specific fields.");
            return;
        }

        RequireText(CameraMake, "cameraMake");
        RequireText(CameraModel, "cameraModel");
        RequireText(CaptureMode, "captureMode");
        CompatibilityFixtureManifest.Require(
            Extension is { Length: > 1 and <= 10 } && Extension[0] == '.' &&
            Extension.Skip(1).All(char.IsAsciiLetterOrDigit),
            $"{Slug}: extension must be a short alphanumeric suffix.");
        CompatibilityFixtureManifest.Require(
            Uri.TryCreate(ProvenanceUrl, UriKind.Absolute, out var source) &&
            source.Scheme == Uri.UriSchemeHttps,
            $"{Slug}: provenanceUrl must be an absolute HTTPS URL.");
        CompatibilityFixtureManifest.Require(
            License == "CC0-1.0",
            $"{Slug}: license must be CC0-1.0.");
        CompatibilityFixtureManifest.Require(
            SizeBytes is > 0 and <= CompatibilityFixtureManifest.MaximumFixtureBytes,
            $"{Slug}: sizeBytes must be within the 30 MiB fixture cap.");
        CompatibilityFixtureManifest.Require(
            Sha256 is { Length: 64 } && Sha256.All(character =>
                char.IsAsciiHexDigit(character) && !char.IsAsciiLetterUpper(character)),
            $"{Slug}: sha256 must be 64 lowercase hexadecimal characters.");
        CompatibilityFixtureManifest.Require(
            ExpectationStatus is "candidate" or "reviewed",
            $"{Slug}: invalid expectationStatus.");
        CompatibilityFixtureManifest.Require(
            PlatformOverrides is { Count: 0 },
            $"{Slug}: this Windows-only slice does not permit platform overrides.");
        CompatibilityFixtureManifest.Require(
            TestLevel is "smoke" or "golden",
            $"{Slug}: invalid testLevel.");

        if (ExpectationStatus == "candidate")
        {
            CompatibilityFixtureManifest.Require(
                Expected == null,
                $"{Slug}: candidate entries must omit expected.");
        }
        else
        {
            CompatibilityFixtureManifest.Require(
                Expected != null,
                $"{Slug}: reviewed entries require expected.");
            Expected!.Validate(Slug!);
        }
    }

    private void RequireText(string? value, string field) =>
        CompatibilityFixtureManifest.Require(
            !string.IsNullOrWhiteSpace(value),
            $"{Slug ?? "fixture"}: {field} is required.");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class CompatibilityExpectation
{
    public string? Overall { get; init; }
    public CapabilityExpectation? Capabilities { get; init; }
    public MetadataExpectation? Metadata { get; init; }
    public CameraFactExpectation? CameraFacts { get; init; }
    public UnpackErrorExpectation? UnpackError { get; init; }
    public List<string>? Limitations { get; init; }

    internal void Validate(string slug)
    {
        CompatibilityFixtureManifest.Require(
            Overall is "supported" or "degraded" or "unsupported",
            $"{slug}: invalid expected overall outcome.");
        CompatibilityFixtureManifest.Require(
            Capabilities != null && Metadata != null && Limitations != null,
            $"{slug}: reviewed expectations require capabilities, metadata, and limitations.");
        Capabilities!.Validate(slug);
        Metadata!.Validate(slug);

        if (Overall == "supported")
        {
            CompatibilityFixtureManifest.Require(
                Capabilities.Values.All(value => value == "pass"),
                $"{slug}: supported fixtures require every capability to pass.");
            CompatibilityFixtureManifest.Require(
                CameraFacts != null && UnpackError == null && Limitations!.Count == 0,
                $"{slug}: supported expectations require camera facts and no limitations.");
            CameraFacts!.Validate(slug);
        }
        else if (Overall == "unsupported")
        {
            CompatibilityFixtureManifest.Require(
                UnpackError != null && Limitations!.Any(value =>
                    !string.IsNullOrWhiteSpace(value)),
                $"{slug}: unsupported expectations require an error and limitation.");
            UnpackError!.Validate(slug);
        }
        else
        {
            CompatibilityFixtureManifest.Require(
                Capabilities.Values.Any(value => value == "degraded") &&
                Limitations!.Any(value => !string.IsNullOrWhiteSpace(value)),
                $"{slug}: degraded expectations require a degraded capability and limitation.");
            CameraFacts?.Validate(slug);
        }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class CapabilityExpectation
{
    public string? BrowseThumbnail { get; init; }
    public string? Metadata { get; init; }
    public string? Preview { get; init; }
    public string? FullDecode { get; init; }
    public string? CameraColor { get; init; }
    public string? Export { get; init; }

    internal IEnumerable<string?> Values =>
        [BrowseThumbnail, Metadata, Preview, FullDecode, CameraColor, Export];

    internal void Validate(string slug) =>
        CompatibilityFixtureManifest.Require(
            Values.All(value => value is "pass" or "degraded" or "unsupported"),
            $"{slug}: invalid capability outcome.");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class MetadataExpectation
{
    public int VisibleWidth { get; init; }
    public int VisibleHeight { get; init; }
    public int NativeOrientation { get; init; }
    public int AppliedOrientation { get; init; }
    public bool RequiresIso { get; init; }
    public bool RequiresExposure { get; init; }
    public bool RequiresCaptureTimestamp { get; init; }

    internal void Validate(string slug) =>
        CompatibilityFixtureManifest.Require(
            VisibleWidth > 0 && VisibleHeight > 0 &&
            NativeOrientation is >= 0 and <= 8 &&
            AppliedOrientation is >= 1 and <= 8,
            $"{slug}: invalid metadata dimensions or orientation.");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class CameraFactExpectation
{
    public SensorExpectation? Sensor { get; init; }
    public VectorExpectation? CamMul { get; init; }
    public MatrixExpectation? CamToSrgb { get; init; }
    public string? ToleranceBasis { get; init; }
    public string? ReviewBasis { get; init; }

    internal void Validate(string slug)
    {
        CompatibilityFixtureManifest.Require(
            Sensor != null && CamMul != null && CamToSrgb != null &&
            !string.IsNullOrWhiteSpace(ToleranceBasis) &&
            !string.IsNullOrWhiteSpace(ReviewBasis),
            $"{slug}: complete camera facts and review bases are required.");
        Sensor!.Validate(slug);
        CamMul!.Validate(slug);
        CamToSrgb!.Validate(slug, CamMul.Values!.Count);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SensorExpectation
{
    public int Colors { get; init; }
    public uint Filters { get; init; }
    public uint DngVersion { get; init; }
    public string? ColorDescription { get; init; }

    internal void Validate(string slug) =>
        CompatibilityFixtureManifest.Require(
            Colors is 3 or 4 && !string.IsNullOrWhiteSpace(ColorDescription),
            $"{slug}: invalid sensor facts.");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class VectorExpectation
{
    public List<double>? Values { get; init; }
    public double AbsoluteTolerance { get; init; }
    public double RelativeTolerance { get; init; }

    internal void Validate(string slug)
    {
        CompatibilityFixtureManifest.Require(
            Values is { Count: 3 or 4 } && Values.All(double.IsFinite),
            $"{slug}: CamMul must contain three or four finite values.");
        ValidateTolerances(slug, AbsoluteTolerance, RelativeTolerance);
    }

    internal static void ValidateTolerances(
        string slug,
        double absoluteTolerance,
        double relativeTolerance) =>
        CompatibilityFixtureManifest.Require(
            double.IsFinite(absoluteTolerance) && absoluteTolerance >= 0 &&
            double.IsFinite(relativeTolerance) && relativeTolerance >= 0 &&
            (absoluteTolerance > 0 || relativeTolerance > 0),
            $"{slug}: camera-fact tolerances must be finite, non-negative, and nonzero.");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class MatrixExpectation
{
    public int Rows { get; init; }
    public int Columns { get; init; }
    public List<double>? RowMajorValues { get; init; }
    public double AbsoluteTolerance { get; init; }
    public double RelativeTolerance { get; init; }

    internal void Validate(string slug, int multiplierCount)
    {
        CompatibilityFixtureManifest.Require(
            Rows == 3 && Columns == multiplierCount &&
            RowMajorValues?.Count == Rows * Columns &&
            RowMajorValues.All(double.IsFinite),
            $"{slug}: CamToSrgb shape must be 3 by the CamMul length.");
        VectorExpectation.ValidateTolerances(
            slug, AbsoluteTolerance, RelativeTolerance);
        for (var row = 0; row < Rows; row++)
        {
            var sum = RowMajorValues!.Skip(row * Columns).Take(Columns).Sum();
            CompatibilityFixtureManifest.Require(
                Math.Abs(sum - 1) <= 1e-5,
                $"{slug}: CamToSrgb row {row} does not sum to one.");
        }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class UnpackErrorExpectation
{
    public int NativeCode { get; init; }
    public string? NativeText { get; init; }

    internal void Validate(string slug) =>
        CompatibilityFixtureManifest.Require(
            NativeCode < 0 && !string.IsNullOrWhiteSpace(NativeText),
            $"{slug}: invalid expected LibRaw error.");
}
