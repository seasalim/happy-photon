using System.Globalization;
using System.Security;

namespace HappyPhoton.Services;

// key=value lines, not JSON: read before the first frame, and System.Text.Json's first use costs ~20 ms.
public sealed class WindowPlacementStore(string pointerRoot)
{
    public string PlacementPath { get; } = Path.Combine(pointerRoot, "window.txt");

    public WindowPlacement? Load()
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(PlacementPath))
            {
                var separator = line.IndexOf('=');
                if (separator > 0) values[line[..separator]] = line[(separator + 1)..];
            }

            return int.TryParse(values.GetValueOrDefault("version"), out var version) &&
                   Number(values, "x", out var x) && Number(values, "y", out var y) &&
                   Number(values, "width", out var width) &&
                   Number(values, "height", out var height) &&
                   Number(values, "scaling", out var scaling) &&
                   bool.TryParse(values.GetValueOrDefault("maximized"), out var maximized)
                ? new WindowPlacement(version, x, y, width, height, scaling, maximized)
                : null;
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        try
        {
            var root = Path.GetDirectoryName(PlacementPath)!;
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"""
                version={placement.Version}
                x={placement.X}
                y={placement.Y}
                width={placement.Width}
                height={placement.Height}
                scaling={placement.Scaling}
                maximized={placement.Maximized}
                """);
            AppDataRootOwnership.Claim(root);
            AppDataRootOwnership.WriteAtomicOwned(root, PlacementPath, text);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
        }
    }

    private static bool Number(Dictionary<string, string> values, string key, out double value) =>
        double.TryParse(
            values.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
            NotSupportedException or ArgumentException;
}
