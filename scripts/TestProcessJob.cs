using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// Windows-only measurement support. The suspended start closes the job-assignment race.
public sealed class TestProcessJob : IDisposable
{
    private readonly IntPtr job = CreateJobObject(IntPtr.Zero, null);

    public TestProcessJob()
    {
        if (job == IntPtr.Zero) throw new Win32Exception();
    }

    public Process Start(string executable, string[] arguments, string directory, string output, string error)
    {
        var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
        var stdout = CreateFile(output, 0x40000000, 3, ref security, 2, 0x80, IntPtr.Zero);
        var stderr = CreateFile(error, 0x40000000, 3, ref security, 2, 0x80, IntPtr.Zero);
        var stdin = CreateFile("NUL", 0x80000000, 3, ref security, 3, 0x80, IntPtr.Zero);
        var info = new ProcessInformation();

        try
        {
            if (stdout == new IntPtr(-1) || stderr == new IntPtr(-1) || stdin == new IntPtr(-1))
                throw new Win32Exception();

            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(), Flags = 0x100,
                StdOutput = stdout, StdError = stderr, StdInput = stdin
            };
            var command = new StringBuilder(Quote(executable));

            foreach (var argument in arguments)
            {
                command.Append(' ').Append(Quote(argument));
            }

            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true,
                0x08000004, IntPtr.Zero, directory, ref startup, out info))
            {
                throw new Win32Exception();
            }

            if (!AssignProcessToJobObject(job, info.Process))
            {
                var failure = new Win32Exception();
                TerminateProcess(info.Process, 1); // Never leave a suspended process behind.
                throw failure;
            }

            var process = Process.GetProcessById(info.ProcessId);
            _ = process.Handle;

            if (ResumeThread(info.Thread) == uint.MaxValue)
            {
                var failure = new Win32Exception();
                TerminateProcess(info.Process, 1);
                process.Dispose();
                throw failure;
            }

            return process;
        }
        finally
        {
            CloseHandle(info.Thread);
            CloseHandle(info.Process);
            CloseHandle(stdout);
            CloseHandle(stderr);
            CloseHandle(stdin);
        }
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;

        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }

            result.Append('\\', slashes * (character == '"' ? 2 : 1));
            slashes = 0;

            if (character == '"') result.Append('\\');

            result.Append(character);
        }

        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    public double CpuSeconds()
    {
        if (!QueryInformationJobObject(job, 1, out var counters, Marshal.SizeOf<JobAccounting>(), IntPtr.Zero))
            throw new Win32Exception();

        return (counters.User + counters.Kernel) / 10000000.0;
    }

    public bool Contains(Process process)
    {
        if (!IsProcessInJob(process.Handle, job, out var contains)) throw new Win32Exception();

        return contains;
    }

    public int[] ProcessIds()
    {
        var size = 65536;

        while (true)
        {
            var buffer = Marshal.AllocHGlobal(size);

            try
            {
                if (QueryInformationJobObject(job, 3, buffer, size, IntPtr.Zero))
                {
                    var count = Marshal.ReadInt32(buffer, 4);
                    var ids = new int[count];

                    for (var index = 0; index < count; index++)
                    {
                        ids[index] = Marshal.ReadIntPtr(buffer, 8 + index * IntPtr.Size).ToInt32();
                    }

                    return ids;
                }

                if (Marshal.GetLastWin32Error() != 234) throw new Win32Exception();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            size = checked(size * 2);
        }
    }

    public static double CpuSeconds(Process process)
    {
        if (!GetProcessTimes(process.Handle, out _, out _, out var kernel, out var user)) throw new Win32Exception();

        return (kernel + user) / 10000000.0;
    }

    public static string[] SplitCommandLine(string commandLine)
    {
        var buffer = CommandLineToArgvW(commandLine, out var count);
        if (buffer == IntPtr.Zero) throw new Win32Exception();

        try
        {
            var arguments = new string[count];

            for (var index = 0; index < count; index++)
            {
                arguments[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, index * IntPtr.Size));
            }

            return arguments;
        }
        finally
        {
            LocalFree(buffer);
        }
    }

    public static Dictionary<int, int> Parents()
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) throw new Win32Exception();

        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            var parents = new Dictionary<int, int>();

            if (!Process32First(snapshot, ref entry)) throw new Win32Exception();

            do
            {
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
            }
            while (Process32Next(snapshot, ref entry));

            return parents;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    public void Dispose() => CloseHandle(job);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;

        public IntPtr Descriptor;

        [MarshalAs(UnmanagedType.Bool)] public bool Inherit;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;

        public string Reserved;

        public string Desktop;

        public string Title;

        public int X;

        public int Y;

        public int XSize;

        public int YSize;

        public int XCountChars;

        public int YCountChars;

        public int FillAttribute;

        public int Flags;

        public short ShowWindow;

        public short ReservedSize;

        public IntPtr ReservedPointer;

        public IntPtr StdInput;

        public IntPtr StdOutput;

        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;

        public IntPtr Thread;

        public int ProcessId;

        public int ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobAccounting
    {
        public long User;

        public long Kernel;

        public long PeriodUser;

        public long PeriodKernel;

        public uint PageFaults;

        public uint TotalProcesses;

        public uint ActiveProcesses;

        public uint TerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;

        public uint Usage;

        public uint ProcessId;

        public IntPtr DefaultHeap;

        public uint ModuleId;

        public uint Threads;

        public uint ParentProcessId;

        public int BasePriority;

        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int kind, out JobAccounting data, int size, IntPtr returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int kind, IntPtr data, int size, IntPtr returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string path, uint access, uint share, ref SecurityAttributes security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory,
        ref StartupInfo startup, out ProcessInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint code);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
}
