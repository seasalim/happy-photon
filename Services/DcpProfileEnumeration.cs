using System.Runtime.InteropServices;

namespace HappyPhoton.Services;

internal sealed class DcpProfileEnumeration(
    Func<string, IEnumerable<FileSystemInfo>>? enumerateDirectory = null)
{
    internal bool IsComplete { get; private set; } = true;

    internal static StringComparer PathComparer(OSPlatform? platform = null) =>
        platform == OSPlatform.Linux || (platform == null && OperatingSystem.IsLinux())
            ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    internal IEnumerable<FileInfo> Enumerate(
        IEnumerable<string> roots, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>(roots.Reverse());
        var visited = new HashSet<string>(PathComparer());

        while (pending.TryPop(out var root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(root)) continue;

            var wildcard = root.IndexOf(
                $"{Path.DirectorySeparatorChar}*{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal);
            var directory = wildcard < 0 ? root : root[..wildcard];

            foreach (var entry in Entries(directory, cancellationToken))
            {
                if (entry is DirectoryInfo child)
                {
                    if (wildcard < 0 && (child.Attributes & FileAttributes.ReparsePoint) != 0) continue;

                    pending.Push(wildcard < 0 ? child.FullName :
                        Path.Combine(child.FullName, root[(wildcard + 3)..]));
                }
                else if (wildcard < 0 && entry is FileInfo file &&
                    file.Extension.Equals(".dcp", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private IEnumerable<FileSystemInfo> Entries(string root, CancellationToken cancellationToken)
    {
        IEnumerator<FileSystemInfo>? iterator = null;

        try
        {
            iterator = (enumerateDirectory?.Invoke(root) ??
                new DirectoryInfo(root).EnumerateFileSystemInfos("*", new EnumerationOptions
                {
                    IgnoreInaccessible = false,
                    AttributesToSkip = 0
                })).GetEnumerator();
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            IsComplete = false;
        }

        if (iterator == null) yield break;

        using (iterator)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var moved = false;

                try
                {
                    moved = iterator.MoveNext();
                }
                catch (DirectoryNotFoundException)
                {
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    IsComplete = false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!moved) yield break;

                yield return iterator.Current;
            }
        }
    }
}
