namespace HappyPhoton.Services;

/// <summary>How file paths compare on this OS: case-insensitive on Windows, exact elsewhere.</summary>
public static class PathComparison
{
    public static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer Comparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
