namespace HappyPhoton.Tests;

internal static class GoldenTestPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string AssetDirectory =>
        Path.Combine(RepositoryRoot, "Tests", "assets");

    public static string GoldenDirectory =>
        Path.Combine(RepositoryRoot, "Tests", "goldens");

    public static string ActiveVersionPath =>
        Path.Combine(GoldenDirectory, "ACTIVE_VERSION");

    public static bool UpdateGoldens =>
        Environment.GetEnvironmentVariable("HAPPY_PHOTON_UPDATE_GOLDENS") == "1";

    public static string ReadActiveVersion()
    {
        if (!File.Exists(ActiveVersionPath))
        {
            throw new InvalidOperationException(
                $"Golden baseline marker is missing: {ActiveVersionPath}");
        }

        return GoldenBaselineMarker.Parse(File.ReadAllText(ActiveVersionPath));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HappyPhoton.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find HappyPhoton.sln above {AppContext.BaseDirectory}.");
    }
}

internal static class GoldenBaselineMarker
{
    public static string Parse(string value)
    {
        var marker = value.Trim();
        if (marker == "pending")
        {
            return marker;
        }

        if (marker.Length < 2 || marker[0] != 'v' ||
            !int.TryParse(marker.AsSpan(1), out var version) || version < 0)
        {
            throw new InvalidOperationException(
                $"Invalid golden baseline marker '{marker}'. Expected pending or v<number>.");
        }

        return $"v{version}";
    }
}
