using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal static class AgentExportSettingsFactory
{
    public static ExportSettings Create(
        string outputFolder,
        AgentExportOptions options,
        ExportFormat format,
        bool stripLocationData,
        bool outputSharpening) => new()
        {
            OutputFolder = outputFolder,
            Quality = Math.Clamp(options.Quality, 1, 100),
            NamingPattern = options.NamingPattern,
            Format = format,
            StripLocationData = stripLocationData,
            OutputSharpening = outputSharpening
        };
}
