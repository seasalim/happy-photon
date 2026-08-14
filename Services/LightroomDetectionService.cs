using Microsoft.Win32;

namespace HappyPhoton.Services;

public sealed record LightroomDetectionResult(
    bool IsDetected,
    IReadOnlyList<string> CatalogPaths)
{
    public LightroomDetectionResult(bool isDetected, string? catalogPath)
        : this(
            isDetected,
            string.IsNullOrWhiteSpace(catalogPath) ? [] : [catalogPath])
    {
    }

    public static LightroomDetectionResult NotDetected { get; } = new(false, []);
}

public sealed class LightroomDetectionService
{
    private const int DefaultMaxDepth = 2;
    private const int DefaultEntryLimit = 256;
    private const int DefaultTotalEntryLimit = 4096;
    private const int MaxReportedCatalogs = 5;
    private readonly bool _isWindows;
    private readonly string? _defaultPicturesRoot;
    private readonly Func<string, bool> _isLocalFixedPath;
    private readonly IReadOnlyList<string> _installRoots;
    private readonly bool _probeRegistry;
    private readonly int _maxDepth;
    private readonly int _entryLimit;
    private readonly int _totalEntryLimit;

    public LightroomDetectionService()
        : this(
            OperatingSystem.IsWindows(),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            IsLocalFixedPath,
            GetInstallRoots(),
            probeRegistry: true,
            DefaultMaxDepth,
            DefaultEntryLimit,
            DefaultTotalEntryLimit)
    {
    }

    internal LightroomDetectionService(
        bool isWindows,
        string? defaultPicturesRoot,
        Func<string, bool> isLocalFixedPath,
        IReadOnlyList<string>? installRoots = null,
        bool probeRegistry = false,
        int maxDepth = DefaultMaxDepth,
        int entryLimit = DefaultEntryLimit,
        int totalEntryLimit = DefaultTotalEntryLimit)
    {
        _isWindows = isWindows;
        _defaultPicturesRoot = defaultPicturesRoot;
        _isLocalFixedPath = isLocalFixedPath;
        _installRoots = installRoots ?? [];
        _probeRegistry = probeRegistry;
        _maxDepth = Math.Max(0, maxDepth);
        _entryLimit = Math.Max(1, entryLimit);
        _totalEntryLimit = Math.Max(1, totalEntryLimit);
    }

    public Task<LightroomDetectionResult> DetectAsync(
        string? picturesRoot,
        string? catalogRoot,
        CancellationToken cancellationToken = default)
    {
        if (!_isWindows)
            return Task.FromResult(LightroomDetectionResult.NotDetected);

        return Task.Run(
            () => Detect(picturesRoot, catalogRoot, cancellationToken),
            cancellationToken);
    }

    private LightroomDetectionResult Detect(
        string? picturesRoot,
        string? catalogRoot,
        CancellationToken cancellationToken)
    {
        var installDetected = DetectInstall(cancellationToken);
        var budget = new ProbeBudget(_totalEntryLimit);
        var catalogs = new List<string>();
        foreach (var root in CatalogProbeRoots(picturesRoot, catalogRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindCatalogs(root, budget, catalogs, cancellationToken);
            if (budget.IsExhausted || catalogs.Count == MaxReportedCatalogs) break;
        }

        return installDetected || catalogs.Count > 0
            ? new LightroomDetectionResult(true, catalogs)
            : LightroomDetectionResult.NotDetected;
    }

    private IEnumerable<string> CatalogProbeRoots(
        string? picturesRoot,
        string? catalogRoot)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(_defaultPicturesRoot))
            roots.Add(Path.Combine(_defaultPicturesRoot, "Lightroom"));
        if (!string.IsNullOrWhiteSpace(picturesRoot)) roots.Add(picturesRoot);
        if (!string.IsNullOrWhiteSpace(catalogRoot)) roots.Add(catalogRoot);
        return roots.Distinct(comparison);
    }

    private void FindCatalogs(
        string root,
        ProbeBudget budget,
        List<string> catalogs,
        CancellationToken cancellationToken)
    {
        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(root);
            if (!_isLocalFixedPath(fullRoot) || !Directory.Exists(fullRoot) ||
                IsReparsePoint(fullRoot))
            {
                return;
            }
        }
        catch (Exception exception) when (IsSkippable(exception))
        {
            return;
        }

        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((fullRoot, 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (budget.Remaining == 0 || catalogs.Count == MaxReportedCatalogs)
                return;

            var (directory, depth) = pending.Dequeue();
            foreach (var entry in EnumerateBounded(directory, budget.Remaining))
            {
                cancellationToken.ThrowIfCancellationRequested();
                budget.Consume();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (IsSkippable(exception))
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (depth < _maxDepth) pending.Enqueue((entry, depth + 1));
                    continue;
                }

                if (string.Equals(
                        Path.GetExtension(entry),
                        ".lrcat",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var catalog = Path.GetFullPath(entry);
                    if (!catalogs.Contains(
                            catalog,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        catalogs.Add(catalog);
                    }
                    if (catalogs.Count == MaxReportedCatalogs) return;
                }
            }
        }
    }

    private IEnumerable<string> EnumerateBounded(
        string directory,
        int remainingEntries)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory)
                .Take(Math.Min(_entryLimit, remainingEntries))
                .ToArray();
        }
        catch (Exception exception) when (IsSkippable(exception))
        {
            return [];
        }
    }

    private bool DetectInstall(CancellationToken cancellationToken)
    {
        foreach (var root in _installRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!_isLocalFixedPath(root) || !Directory.Exists(root) ||
                    IsReparsePoint(root))
                {
                    continue;
                }
                if (Path.GetFileName(root).Contains(
                        "Lightroom Classic", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                foreach (var directory in Directory.EnumerateDirectories(root)
                             .Take(_entryLimit))
                {
                    if (IsReparsePoint(directory)) continue;
                    if (Path.GetFileName(directory).Contains(
                            "Lightroom Classic", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception exception) when (IsSkippable(exception))
            {
                // Install probes are independent; keep checking known local roots.
            }
        }

        return _probeRegistry && DetectRegistryInstall(cancellationToken);
    }

    private bool DetectRegistryInstall(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return false;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var machine = RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine, view);
                using var uninstall = machine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                foreach (var name in uninstall?.GetSubKeyNames() ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var product = uninstall!.OpenSubKey(name);
                    if (product?.GetValue("DisplayName") is string displayName &&
                        displayName.Contains(
                            "Lightroom Classic", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception exception) when (IsSkippable(exception))
            {
                // Registry views are independent and optional.
            }
        }

        return false;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsLocalFixedPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            var root = Path.GetPathRoot(fullPath);
            return !string.IsNullOrWhiteSpace(root) &&
                   new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> GetInstallRoots()
    {
        var roots = new List<string>();
        AddAdobeRoot(Environment.SpecialFolder.ProgramFiles);
        AddAdobeRoot(Environment.SpecialFolder.ProgramFilesX86);
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        void AddAdobeRoot(Environment.SpecialFolder folder)
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(path)) roots.Add(Path.Combine(path, "Adobe"));
        }
    }

    private static bool IsSkippable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or
            System.Security.SecurityException;

    private sealed class ProbeBudget(int remaining)
    {
        public int Remaining { get; private set; } = remaining;
        public bool IsExhausted { get; private set; }

        public void Consume()
        {
            Remaining--;
            if (Remaining == 0) IsExhausted = true;
        }
    }
}
