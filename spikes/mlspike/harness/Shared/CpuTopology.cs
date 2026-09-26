using System.Diagnostics;
using System.Globalization;

namespace HappyPhoton.MlSpike;

public static class CpuTopology
{
    public static int PhysicalCores()
    {
        var supplied = Environment.GetEnvironmentVariable("MLSPIKE_PHYSICAL_CORES");
        if (supplied != null)
        {
            if (!int.TryParse(supplied, out var value) || value < 1)
                throw new InvalidDataException("MLSPIKE_PHYSICAL_CORES must be a measured positive core count.");
            return value;
        }
        if (OperatingSystem.IsLinux())
        {
            var pairs = new HashSet<string>();
            foreach (var block in File.ReadAllText("/proc/cpuinfo").Split("\n\n"))
            {
                var fields = block.Split('\n').Select(line => line.Split(':', 2))
                    .Where(parts => parts.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
                if (fields.TryGetValue("physical id", out var socket) && fields.TryGetValue("core id", out var core))
                    pairs.Add(socket + ":" + core);
            }
            if (pairs.Count > 0) return pairs.Count;
        }
        else
        {
            var start = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/usr/sbin/sysctl",
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            if (OperatingSystem.IsWindows())
            {
                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-Command");
                start.ArgumentList.Add("(Get-CimInstance Win32_Processor | Measure-Object NumberOfCores -Sum).Sum");
            }
            else
            {
                start.ArgumentList.Add("-n");
                start.ArgumentList.Add("hw.physicalcpu");
            }
            using var process = Process.Start(start) ?? throw new IOException("Cannot read CPU topology.");
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0 && int.TryParse(output.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var cores) && cores > 0) return cores;
        }
        throw new InvalidOperationException("Cannot determine physical cores; set MLSPIKE_PHYSICAL_CORES from rig evidence.");
    }
}
