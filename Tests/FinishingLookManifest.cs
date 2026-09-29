using HappyPhoton.Models;

namespace HappyPhoton.Tests;

internal static class FinishingLookManifest
{
    internal static string[] Derive(EditSettings settings)
    {
        var fields = new List<string>();

        if (settings.Contrast > 0) fields.Add("contrast");
        if (settings.Texture > 0) fields.Add("texture");
        if (settings.Clarity > 0) fields.Add("clarity");

        foreach (var (name, curve) in new[]
        {
            ("curve", settings.Curve), ("curveRed", settings.CurveRed),
            ("curveGreen", settings.CurveGreen), ("curveBlue", settings.CurveBlue)
        })
        {
            if (AboveIdentity(curve)) fields.Add(name);
        }

        return fields.ToArray();
    }

    internal static bool AboveIdentity(CurveData? curve)
    {
        if (curve == null || curve.IsIdentity()) return false;

        // Production interpolates linearly between the 256 quantized LUT
        // entries, so its difference from identity has extrema at the knots.
        var table = curve.Clone().LookupTable;

        return table.Where((value, index) => value > index).Any();
    }
}

