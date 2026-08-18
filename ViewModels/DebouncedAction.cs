using System.Diagnostics;

namespace HappyPhoton.ViewModels;

internal static class DebouncedAction
{
    public static async Task RunAsync(
        string operationName,
        TimeSpan delay,
        CancellationToken cancellationToken,
        Func<Task> action,
        Action<string, Exception>? onError = null,
        TimeProvider? timeProvider = null)
    {
        try
        {
            await Task.Delay(
                delay,
                timeProvider ?? TimeProvider.System,
                cancellationToken);
            await action();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (onError != null)
            {
                onError(operationName, ex);
                return;
            }

            Debug.WriteLine($"Debounced {operationName} failed: {ex.Message}");
        }
    }
}
