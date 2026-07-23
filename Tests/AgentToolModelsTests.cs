using System.Text.Json;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgentToolModelsTests
{
    private static readonly JsonSerializerOptions Options = AgentToolJson.Options;

    [Fact]
    public void ImageSummary_RoundTripsWithCamelCaseAndStringEnums()
    {
        var summary = new AgentImageSummary(
            Id: @"C:\photos\a.jpg", FileName: "a.jpg", Rating: 3, Flag: "picked",
            HasEdits: true, MetadataLoaded: true, PixelWidth: 6000, PixelHeight: 4000,
            DateTaken: new DateTime(2026, 7, 1, 10, 30, 0), Camera: "Canon EOS R5",
            Iso: 100, FNumber: 2.8, ExposureTime: "1/250", FocalLength: 35,
            LensModel: "RF 35mm F1.8", BurstId: "burst_1", BurstIndex: 2,
            BurstSize: 4);

        var json = JsonSerializer.Serialize(summary, Options);
        Assert.Contains("\"fileName\"", json);
        Assert.Contains("\"picked\"", json);
        Assert.Contains("\"burstId\":\"burst_1\"", json);

        var back = JsonSerializer.Deserialize<AgentImageSummary>(json, Options);
        Assert.Equal(summary, back);
    }

    [Fact]
    public void ImageSummary_OmitsNullBurstFields()
    {
        var summary = new AgentImageSummary(
            Id: "a", FileName: "a.jpg", Rating: 0, Flag: "unflagged",
            HasEdits: false, MetadataLoaded: false, PixelWidth: 0, PixelHeight: 0,
            DateTaken: null, Camera: null, Iso: null, FNumber: null,
            ExposureTime: null, FocalLength: null, LensModel: null);

        var json = JsonSerializer.Serialize(summary, Options);

        Assert.DoesNotContain("burstId", json);
        Assert.DoesNotContain("burstIndex", json);
        Assert.DoesNotContain("burstSize", json);
    }

    [Fact]
    public void LibraryState_SerializesBurstAnalysisStatus()
    {
        var state = new AgentLibraryState(
            "photos", 3, 2, new AgentFilterState("all", "all", 0), "a", true);

        var json = JsonSerializer.Serialize(state, Options);

        Assert.Contains("\"burstsComputed\":true", json);
    }

    [Fact]
    public void BatchResult_SerializesFailures()
    {
        var result = new AgentBatchResult(
            Succeeded: new List<string> { "a" },
            Failed: new List<AgentBatchFailure> { new("b", "unknown image id") });

        var json = JsonSerializer.Serialize(result, Options);
        Assert.Contains("unknown image id", json);
    }

    [Fact]
    public void BatchCap_ValidatesLimits()
    {
        Assert.Null(AgentToolValidation.CheckBatchCap(new string[500], 500));
        Assert.NotNull(AgentToolValidation.CheckBatchCap(new string[501], 500));
        Assert.Contains("500", AgentToolValidation.CheckBatchCap(new string[501], 500));
    }

    [Fact]
    public void FlagParsing_AcceptsKnownValuesCaseInsensitive()
    {
        Assert.Equal(ImageFlag.Picked, AgentToolValidation.ParseFlag("Picked"));
        Assert.Equal(ImageFlag.Rejected, AgentToolValidation.ParseFlag("rejected"));
        Assert.Equal(ImageFlag.Unflagged, AgentToolValidation.ParseFlag("unflagged"));
        Assert.Throws<AgentToolException>(() => AgentToolValidation.ParseFlag("maybe"));
    }

    [Fact]
    public void JsonOptions_SerializeEnumsAsCamelCaseStrings()
    {
        var json = JsonSerializer.Serialize(ImageFlag.Unflagged, Options);

        Assert.Equal("\"unflagged\"", json);
    }

    [Fact]
    public void ExportOptions_RoundTripsFormatAndVariants()
    {
        var options = new AgentExportOptions(
            OutputFolder: "exports", Quality: 90, MaxDimension: null,
            NamingPattern: "{name}_edited", Format: "webp",
            Variants: new List<AgentExportVariant>
            {
                new("Hi Res!", null),
                new("web", 1200)
            });

        var json = JsonSerializer.Serialize(options, Options);
        Assert.Contains("\"format\":\"webp\"", json);
        Assert.Contains("\"variants\"", json);
        Assert.Contains("\"maxDimension\":1200", json);

        var back = JsonSerializer.Deserialize<AgentExportOptions>(json, Options);
        Assert.NotNull(back);
        Assert.Equal(options.OutputFolder, back.OutputFolder);
        Assert.Equal(options.Quality, back.Quality);
        Assert.Equal(options.NamingPattern, back.NamingPattern);
        Assert.Equal(options.Format, back.Format);
        Assert.Equal(options.Variants, back.Variants);
    }

    [Theory]
    [InlineData("jpeg", ExportFormat.Jpeg)]
    [InlineData("JPG", ExportFormat.Jpeg)]
    [InlineData("PNG", ExportFormat.Png)]
    [InlineData("WebP", ExportFormat.Webp)]
    public void ExportFormatParsing_AcceptsKnownValuesCaseInsensitive(
        string value, ExportFormat expected)
    {
        Assert.Equal(expected, AgentToolValidation.ParseExportFormat(value));
    }

    [Fact]
    public void ExportFormatParsing_RejectsUnknownValue()
    {
        Assert.Throws<AgentToolException>(() =>
            AgentToolValidation.ParseExportFormat("tiff"));
    }

    [Theory]
    [InlineData(" Hi Res! ", "hi-res-")]
    [InlineData("SOCIAL_1", "social_1")]
    [InlineData("web/mobile", "web-mobile")]
    public void VariantNameSanitizing_NormalizesSafeFolderName(
        string value, string expected)
    {
        Assert.Equal(expected, AgentToolValidation.SanitizeVariantName(value));
    }

    [Fact]
    public void VariantNameSanitizing_RejectsEmptyName()
    {
        Assert.Throws<AgentToolException>(() =>
            AgentToolValidation.SanitizeVariantName("   "));
    }
}
