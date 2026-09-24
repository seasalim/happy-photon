using System.Globalization;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public partial class AppSettingsService
{
    private async Task<WatermarkSpec> LoadWatermarkAsync()
    {
        Task<string?> Read(string name) => _catalogService.GetAppSettingAsync("Watermark" + name);
        async Task<bool> Flag(string name, bool fallback = false) =>
            bool.TryParse(await Read(name), out var value) ? value : fallback;
        async Task<double> Number(string name, double min, double max, double fallback) =>
            double.TryParse(await Read(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            double.IsFinite(value) && value >= min && value <= max ? value : fallback;
        async Task<T> Choice<T>(string name, T fallback) where T : struct, Enum =>
            Enum.TryParse<T>(await Read(name), true, out var value) && Enum.IsDefined(value) ? value : fallback;
        return new WatermarkSpec(
            (await Read("Text") ?? "").Replace("\r", "").Replace("\n", ""),
            await Read("FontFamily") ?? "", await Flag("Bold"), await Flag("Italic"),
            await Number("Size", 1, 20, 3), await Choice("Color", WatermarkColor.White),
            await Number("Opacity", 5, 100, 60), await Choice("Edge", WatermarkEdge.Bottom),
            await Choice("Alignment", WatermarkAlignment.End), await Flag("RotateAlongEdge", true),
            await Number("Margin", 0, 20, 2));
    }

    private static Dictionary<string, string?> WithWatermark(AppSettings settings, Dictionary<string, string?> values)
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
        return values;
    }
}
