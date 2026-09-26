using System.Text.Json;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal static partial class EditSettingsJson
{
    private static void ClampRepairs(EditSettings settings, ref bool changed)
    {
        if (settings.Repairs is not { } repairs) return;
        if (repairs.Count > Repair.MaximumCount)
            throw new JsonException("Edit settings contain more than 64 repairs.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repair in repairs)
        {
            if (repair == null || repair.Type is not ("heal" or "clone"))
                throw new JsonException("Repair type is not supported.");
            if (repair.Id == null || repair.Id.Length != 32 ||
                !Guid.TryParseExact(repair.Id, "N", out _) || !ids.Add(repair.Id))
                throw new JsonException("Repair IDs must be unique 32-hex GUIDs.");
            repair.U = ClampRepairCoordinate(repair.U, ref changed);
            repair.V = ClampRepairCoordinate(repair.V, ref changed);
            repair.Su = ClampRepairCoordinate(repair.Su, ref changed);
            repair.Sv = ClampRepairCoordinate(repair.Sv, ref changed);
            repair.Radius = Clamp(repair.Radius, Repair.MinimumRadius, Repair.MaximumRadius, ref changed);
            repair.Feather = Clamp(repair.Feather, 0, 1, ref changed);
            repair.Opacity = Clamp(repair.Opacity, .05, 1, ref changed);
        }
        if (RepairArea.Sum(repairs) > Repair.MaximumArea * (1 + RepairArea.RelativeTolerance))
            throw new JsonException("Repairs exceed the total disc area limit.");
    }

    private static double ClampRepairCoordinate(double value, ref bool changed)
    {
        var clamped = Clamp(value, 0, 1, ref changed);
        var quantized = Math.Round(clamped * Repair.CoordinateScale) / Repair.CoordinateScale;
        changed |= quantized != value;
        return quantized;
    }
}
