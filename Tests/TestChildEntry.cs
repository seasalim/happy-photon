namespace HappyPhoton.Tests;

internal static class TestChildEntry
{
    internal static int Run(Action action) => Run(() =>
    {
        action();

        return Task.CompletedTask;
    });

    internal static int Run(Func<Task> action)
    {
        try
        {
            action().GetAwaiter().GetResult();

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);

            return 1;
        }
    }
}
