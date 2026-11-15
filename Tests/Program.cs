namespace HappyPhoton.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--backup-crash-child")
            return TestChildEntry.Run(() => BackupKillPointChild.RunAsync(move: false));

        if (args.FirstOrDefault() == "--move-backup-crash-child")
            return TestChildEntry.Run(() => BackupKillPointChild.RunAsync(move: true));

        if (args.FirstOrDefault() == "--move-backup-cleanup-child")
            return TestChildEntry.Run(MoveBackupCleanupTests.RunChildAsync);

        if (args.FirstOrDefault() == "--restore-hot-journal-child")
            return TestChildEntry.Run(RestoreHotJournalTests.RunChild);

        if (args.FirstOrDefault() == "--openmp-child")
            return TestChildEntry.Run(OpenMpRuntimeTests.RunChild);

        if (global::System.Linq.Enumerable.Any(args, arg => arg == "--server" || arg == "--internal-msbuild-node"))
            return global::Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions).GetAwaiter().GetResult();
        else
            return global::Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).GetAwaiter().GetResult();
    }
}
