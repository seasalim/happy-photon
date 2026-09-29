using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HappyPhoton.Tests;

// Read-only owner discovery: never ask Restart Manager to shut down an owner.
internal static class BackupCrashRestartManager
{
    public static string Owners(string path, int childPid)
    {
        if (!OperatingSystem.IsWindows()) return "restart_manager=unsupported ownership=unproven";

        var error = RmStartSession(out var session, 0, new StringBuilder(33));
        if (error != 0) return $"restart_manager=start_failed error={error} ownership=unproven";

        try
        {
            error = RmRegisterResources(session, 1, [path], 0, IntPtr.Zero, 0, IntPtr.Zero);
            if (error != 0) return $"restart_manager=register_failed error={error} ownership=unproven";

            uint count = 0;
            uint reasons = 0;
            error = RmGetList(session, out var needed, ref count, null, ref reasons);

            for (var attempt = 0; error == 234 && attempt < 3; attempt++)
            {
                var owners = new ProcessInfo[needed];
                count = needed;
                error = RmGetList(session, out needed, ref count, owners, ref reasons);
                if (error != 0) continue;

                var lines = new List<string> { $"restart_manager=ok owners={count} reboot_reasons={reasons}" };

                foreach (var owner in owners.Take((int)count))
                {
                    var pid = owner.Process.Pid;
                    var name = owner.AppName;

                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        name = process.ProcessName;
                    }
                    catch (Exception ex)
                    {
                        name += $" (process_name_unavailable={ex.GetType().Name})";
                    }

                    var relation = pid == Environment.ProcessId ? "test-process" :
                        pid == childPid ? "killed-child" : "other-process";
                    lines.Add($"owner_pid={pid} owner_name={name} app_name={owner.AppName} " +
                        $"application_type={owner.ApplicationType}({(int)owner.ApplicationType}) ownership={relation}");
                }

                return string.Join("\n", lines);
            }

            return $"restart_manager={(error == 0 ? "no-owners" : "get_list_failed")} " +
                $"error={error} ownership=unproven";
        }
        finally
        {
            RmEndSession(session);
        }
    }

    private enum ApplicationType
    {
        Unknown = 0,
        MainWindow = 1,
        OtherWindow = 2,
        Service = 3,
        Explorer = 4,
        Console = 5,
        Critical = 1000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess
    {
        public int Pid;

        public System.Runtime.InteropServices.ComTypes.FILETIME StartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        public UniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceName;

        public ApplicationType ApplicationType;

        public uint AppStatus;

        public uint SessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, uint flags, StringBuilder key);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint fileCount, string[] files,
        uint appCount, IntPtr apps, uint serviceCount, IntPtr services);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint session, out uint needed, ref uint count,
        [In, Out] ProcessInfo[]? owners, ref uint reasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);
}
