using System.Globalization;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public partial class AppSettingsService
{
    private static WatermarkSpec LoadWatermark(Func<string, string?> readSetting)
    {
        string? Read(string name) => readSetting("Watermark" + name);
        bool Flag(string name, bool fallback = false) =>
            bool.TryParse(Read(name), out var value) ? value : fallback;
        double Number(string name, double min, double max, double fallback) =>
            double.TryParse(Read(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            double.IsFinite(value) && value >= min && value <= max ? value : fallback;
        T Choice<T>(string name, T fallback) where T : struct, Enum =>
            Enum.TryParse<T>(Read(name), true, out var value) && Enum.IsDefined(value) ? value : fallback;

        return new WatermarkSpec(
            (Read("Text") ?? "").Replace("\r", "").Replace("\n", ""),
            Read("FontFamily") ?? "", Flag("Bold"), Flag("Italic"),
            Number("Size", 1, 20, 3), Choice("Color", WatermarkColor.White),
            Number("Opacity", 5, 100, 60), Choice("Edge", WatermarkEdge.Bottom),
            Choice("Alignment", WatermarkAlignment.End), Flag("RotateAlongEdge", true),
            Number("Margin", 0, 20, 2));
    }

    private static void AddWatermark(AppSettings settings, Dictionary<string, string?> values)
    {
        var mark = settings.Watermark;
        values["WatermarkEnabled"] = settings.WatermarkEnabled.ToString();
        values["WatermarkText"] = mark.Text;
        values["WatermarkFontFamily"] = mark.FontFamily;
        values["WatermarkBold"] = mark.Bold.ToString();
        values["WatermarkItalic"] = mark.Italic.ToString();
        values["WatermarkSize"] = mark.Size.ToString(CultureInfo.InvariantCulture);
        values["WatermarkColor"] = mark.Color.ToString();
        values["WatermarkOpacity"] = mark.Opacity.ToString(CultureInfo.InvariantCulture);
        values["WatermarkEdge"] = mark.Edge.ToString();
        values["WatermarkAlignment"] = mark.Alignment.ToString();
        values["WatermarkRotateAlongEdge"] = mark.RotateAlongEdge.ToString();
        values["WatermarkMargin"] = mark.Margin.ToString(CultureInfo.InvariantCulture);
    }
}
