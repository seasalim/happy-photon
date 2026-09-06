using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal static partial class EditSettingsJson
{
    private static DefaultJsonTypeInfoResolver LocalTypeResolver()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(LocalAdjustment)) return;
            foreach (var property in info.Properties.Where(p => p.Name is "rx" or "ry" or "outside"))
                property.ShouldSerialize = (owner, _) => ((LocalAdjustment)owner).IsRadial;
        });
        return resolver;
    }

    private static void ClampLocals(EditSettings settings, ref bool changed)
    {
        if (settings.Locals is not { } locals) return;
        if (locals.Count > LocalAdjustment.MaximumCount)
            throw new JsonException("Edit settings contain more than eight locals.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinals = new HashSet<int>();
        foreach (var local in locals)
        {
            if (local == null || local.Type is not ("linear" or "radial"))
                throw new JsonException("Local adjustment type is not supported.");
            if (local.Id == null || local.Id.Length != 32 ||
                !Guid.TryParseExact(local.Id, "N", out _) || !ids.Add(local.Id))
                throw new JsonException("Local adjustment IDs must be unique 32-hex GUIDs.");
            if (local.Ordinal < 1 || local.Ordinal == int.MaxValue || !ordinals.Add(local.Ordinal))
                throw new JsonException("Local adjustment ordinals must be unique positive integers.");
            local.Cu = Clamp(local.Cu, -1, 2, ref changed);
            local.Cv = Clamp(local.Cv, -1, 2, ref changed);
            local.Feather = Clamp(local.Feather, local.IsRadial ? 0 : .001, local.IsRadial ? 1 : 2, ref changed);
            if (local.IsRadial)
            {
                local.Rx = Clamp(local.Rx, .001, 1, ref changed);
                local.Ry = Clamp(local.Ry, .001, 1, ref changed);
            }
            local.Exposure = Clamp(local.Exposure, -4, 4, ref changed);
            if (!double.IsFinite(local.Angle))
                throw new JsonException("Local adjustment angle must be finite.");
            var angle = (local.Angle % 360 + 360) % 360;
            changed |= angle != local.Angle;
            local.Angle = angle;
        }
    }
}
