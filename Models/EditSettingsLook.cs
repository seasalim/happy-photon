using System.Reflection;
using System.Text.Json.Serialization;
using HappyPhoton.Services;

namespace HappyPhoton.Models;

/// <summary>The aesthetic subset of SYNC's look groups; everything else is a correction.</summary>
public static class EditSettingsLook
{
    public static IReadOnlyList<string> Fields { get; } = Array.AsReadOnly(new[]
    {
        "contrast", "saturation", "vibrance", "curve", "curveRed", "curveGreen",
        "curveBlue", "mixer", "effects", "texture", "clarity"
    });

    private static readonly PropertyInfo[] Properties = typeof(EditSettings).GetProperties()
        .Where(property => Fields.Contains(property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name))
        .ToArray();

    public static void Apply(EditSettings source, EditSettings target)
    {
        EditSettingsJson.EnsureCurrent(source);
        EditSettingsJson.EnsureCurrent(target);
        var owned = source.Clone();

        foreach (var property in Properties)
        {
            property.SetValue(target, property.GetValue(owned));
        }
    }

    public static void Reset(EditSettings target) => Apply(new EditSettings(), target);
}
