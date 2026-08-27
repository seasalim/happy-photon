namespace HappyPhoton.Services;

internal static class PackagedDataRoot
{
    internal static string Resolve() => Resolve(
        AppContext.BaseDirectory, OperatingSystem.IsMacOS());

    internal static string Resolve(string baseDirectory, bool isMacOS)
    {
        if (!isMacOS) return baseDirectory;
        var macOSDirectory = new DirectoryInfo(baseDirectory);
        var contentsDirectory = macOSDirectory.Parent;
        if (!string.Equals(macOSDirectory.Name, "MacOS", StringComparison.Ordinal) ||
            contentsDirectory == null ||
            !string.Equals(contentsDirectory.Name, "Contents",
                StringComparison.Ordinal))
            return baseDirectory;
        return Path.Combine(contentsDirectory.FullName, "Resources");
    }
}
