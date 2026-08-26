namespace HappyPhoton.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    private const int CleanupAttempts = 3;

    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"happy-photon-test-{Guid.NewGuid():N}");

    public TemporaryDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        for (var attempt = 1; attempt <= CleanupAttempts; attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException &&
                attempt < CleanupAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25 * attempt));
            }
        }
    }
}
