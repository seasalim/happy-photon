namespace HappyPhoton.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--restore-crash-child")
            return RestoreCrashChild.RunAsync().GetAwaiter().GetResult();

        if (global::System.Linq.Enumerable.Any(args, arg => arg == "--server" || arg == "--internal-msbuild-node"))
            return global::Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions).GetAwaiter().GetResult();
        else
            return global::Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).GetAwaiter().GetResult();
    }
}
