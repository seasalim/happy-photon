using System.Text.RegularExpressions;

namespace HappyPhoton.Services;

internal sealed record DcpDiscoveryHints(string? LensModel, double FocalLength35mm);

internal static class DcpCameraModule
{
    private static readonly string[] Positions = ["REAR", "FRONT", "BACK", "INNER", "UNDER DISPLAY"];

    private static readonly string[] Lenses =
    [
        "MAIN", "WIDE", "ULTRAWIDE", "ULTRA WIDE", "WIDE ANGLE", "ULTRA WIDE ANGLE",
        "TELEPHOTO", "SUPER TELEPHOTO", "ZOOM", "MACRO", "STANDARD"
    ];

    private static readonly Regex AndroidOptics = new(
        @"\A(?: \d+(?: \d+)?MM)?(?: F \d+(?: \d+)?)?\z", RegexOptions.CultureInvariant);

    internal static (string Base, string Position, string Lens)? Parse(string model)
    {
        for (var space = model.IndexOf(' '); space >= 0; space = model.IndexOf(' ', space + 1))
        {
            var suffix = model[(space + 1)..];
            var position = Positions.FirstOrDefault(word => suffix.StartsWith(word + " ", StringComparison.Ordinal));
            if (position != null) suffix = suffix[(position.Length + 1)..];

            if (position is "BACK" or "FRONT" && suffix.StartsWith("CAMERA", StringComparison.Ordinal) &&
                AndroidOptics.IsMatch(suffix[6..]))
            {
                return (model[..space], position, string.Empty);
            }

            if (!suffix.EndsWith(" CAMERA", StringComparison.Ordinal) && suffix != "CAMERA") continue;

            var lens = suffix[..^6].TrimEnd();

            if ((position != null && lens.Length == 0) || Lenses.Contains(lens))
            {
                return (model[..space], position ?? string.Empty, lens);
            }
        }

        return null;
    }

    internal static int Rank(string? model, DcpDiscoveryHints? hints)
    {
        var module = Parse(DcpProfileDiscovery.NormalizeCameraIdentity(null, model, preservePlus: true));
        if (module == null || hints == null) return 1;

        var (_, position, lens) = module.Value;
        var preferred = hints.LensModel?.Contains("front", StringComparison.OrdinalIgnoreCase) == true
            ? position == "FRONT"
            : position != "FRONT" && hints.FocalLength35mm switch
            {
                > 0 and <= 18 => lens is "ULTRAWIDE" or "ULTRA WIDE" or "ULTRA WIDE ANGLE",
                > 18 and <= 40 => lens is "WIDE" or "MAIN" or "WIDE ANGLE",
                > 40 and <= 150 => lens == "TELEPHOTO",
                > 150 => lens == "SUPER TELEPHOTO",
                _ => false
            };

        return preferred ? 0 : 1;
    }
}
