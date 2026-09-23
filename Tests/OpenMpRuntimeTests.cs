using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HappyPhoton.LibRaw.Interop;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[CollectionDefinition("OpenMP isolation", DisableParallelization = true)]
public sealed class OpenMpIsolationCollection;

[Collection("OpenMP isolation")]
public sealed class OpenMpRuntimeTests
{
    [Fact]
    public void FlavorMatchesBuild() => Assert.Equal(IsOpenMpBuild,
        MagickNET.Features.Contains("OpenMP", StringComparison.Ordinal));

    [Theory]
    [InlineData("magick", null, null)]
    [InlineData("libraw", null, null)]
    [InlineData("magick", "", null)]
    [InlineData("libraw", "", null)]
    [InlineData("magick", " ", null)]
    [InlineData("libraw", " ", null)]
    [InlineData("magick", null, "")]
    [InlineData("libraw", null, "")]
    [InlineData("magick", null, " ")]
    [InlineData("libraw", null, " ")]
    [InlineData("magick", "3", null)]
    [InlineData("libraw", "3", null)]
    [InlineData("magick", null, "1")]
    [InlineData("libraw", null, "1")]
    [InlineData("magick", "3", "1")]
    [InlineData("libraw", "3", "1")]
    public async Task NativeLimits_InFreshProcess(string first, string? omp, string? magick)
    {
        Assert.SkipUnless(IsOpenMpBuild, "This package has no OpenMP runtime.");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = GoldenTestPaths.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // Run the compiled xUnit executable directly: preserve this host's CPU budget,
        // and avoid a nested build/restore choosing a different package flavor.
        foreach (var argument in new[] { typeof(OpenMpRuntimeTests).Assembly.Location,
                     "-method", "HappyPhoton.Tests.OpenMpRuntimeTests.NativeLimits_Child" })
            start.ArgumentList.Add(argument);
        start.Environment["HAPPY_PHOTON_OPENMP_CHILD"] = first;
        start.Environment["HAPPY_PHOTON_EXPECTED_OMP"] = string.IsNullOrWhiteSpace(omp)
            ? Math.Min(Environment.ProcessorCount, 16).ToString() : omp;
        start.Environment["HAPPY_PHOTON_EXPECTED_MAGICK"] = string.IsNullOrWhiteSpace(magick)
            ? start.Environment["HAPPY_PHOTON_EXPECTED_OMP"] : magick;
        start.Environment["DOTNET_PROCESSOR_COUNT"] = Environment.ProcessorCount.ToString();
        start.Environment.Remove("OMP_NUM_THREADS");
        start.Environment.Remove("MAGICK_THREAD_LIMIT");
        if (omp != null) start.Environment["OMP_NUM_THREADS"] = omp;
        if (magick != null) start.Environment["MAGICK_THREAD_LIMIT"] = magick;
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TestWaits.Condition);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, $"{first}/{omp}/{magick}: {output}\n{error}");
        Assert.Contains("OPENMP_LIMITS_VERIFIED", output);
    }

    [Fact]
    public void NativeLimits_Child()
    {
        var first = Environment.GetEnvironmentVariable("HAPPY_PHOTON_OPENMP_CHILD");
        Assert.SkipWhen(first == null, "Runs only in an isolated child process.");
        AssertImageLibraryNotLoaded("Magick.Native");
        AssertImageLibraryNotLoaded(OperatingSystem.IsWindows() ? "raw_r" : "libraw");
        if (first == "magick")
        {
            LoadMagick();
            AssertImageLibraryNotLoaded(OperatingSystem.IsWindows() ? "raw_r" : "libraw");
            LoadLibRaw();
        }
        else
        {
            LoadLibRaw();
            AssertImageLibraryNotLoaded("Magick.Native");
            LoadMagick();
        }
        var expected = int.Parse(Environment.GetEnvironmentVariable("HAPPY_PHOTON_EXPECTED_OMP")!);
        var runtime = OperatingSystem.IsWindows() ? "vcomp140.dll" : "libgomp.so.1";
        var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Single(m => Path.GetFileName(m.FileName).StartsWith(runtime, StringComparison.OrdinalIgnoreCase));
        var handle = NativeLibrary.Load(module.FileName);
        try
        {
            var maximum = Marshal.GetDelegateForFunctionPointer<GetMaxThreads>(
                NativeLibrary.GetExport(handle, "omp_get_max_threads"))();
            Assert.Equal(expected, maximum);
            var limit = Environment.GetEnvironmentVariable("HAPPY_PHOTON_EXPECTED_MAGICK");
            Assert.Equal((ulong)(limit == null ? expected : Math.Min(expected, int.Parse(limit))),
                ResourceLimits.Thread);
            if (OperatingSystem.IsWindows())
                Assert.StartsWith(AppContext.BaseDirectory, module.FileName, StringComparison.OrdinalIgnoreCase);
        }
        finally { NativeLibrary.Free(handle); }
        Console.WriteLine("OPENMP_LIMITS_VERIFIED");
    }

    private static void AssertImageLibraryNotLoaded(string prefix) =>
        Assert.DoesNotContain(Process.GetCurrentProcess().Modules.Cast<ProcessModule>(),
            m => Path.GetFileName(m.FileName).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoadMagick() { using var image = new MagickImage(MagickColors.Gray, 2, 2); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoadLibRaw() => Assert.Equal(0x001602u, LibRawContext.Runtime.LibRawVersionNumber);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetMaxThreads();

    private static bool IsOpenMpBuild => typeof(OpenMpRuntimeTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "MagickFlavor").Value == "OpenMP-x64";
}
