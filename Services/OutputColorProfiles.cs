using System.Reflection;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class OutputColorProfiles
{
    private const string DisplayP3ResourceName =
        "HappyPhoton.Assets.DisplayP3-v4.icc";

    private static readonly Lazy<IColorProfile> DisplayP3Profile = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(DisplayP3ResourceName) ??
            throw new InvalidOperationException(
                $"Embedded profile is missing: {DisplayP3ResourceName}.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return new ColorProfile(memory.ToArray());
    });

    public static IColorProfile Get(OutputColorSpace outputColorSpace) =>
        outputColorSpace switch
        {
            OutputColorSpace.Srgb => ColorProfiles.SRGB,
            OutputColorSpace.DisplayP3 => DisplayP3Profile.Value,
            _ => throw new ArgumentOutOfRangeException(
                nameof(outputColorSpace), outputColorSpace, null)
        };
}
