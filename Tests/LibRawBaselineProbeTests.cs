using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Sdcb.LibRaw;
using Sdcb.LibRaw.Natives;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LibRawBaselineProbeTests
{
    private readonly ITestOutputHelper _output;

    public LibRawBaselineProbeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task CurrentRid_EmitsCompleteLibRawBaseline()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_BASELINE") != "1",
            "Set HAPPY_PHOTON_BASELINE=1 to run the LibRaw baseline probe.");
        Assert.True(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_BASELINE_ISOLATED") == "1",
            "Baseline memory would be contaminated. Run only this test with the documented exact filter and set HAPPY_PHOTON_BASELINE_ISOLATED=1.");

        var versionNumber = RawContext.VersionNumber;
        var version = RawContext.Version;
        var capabilities = LibRawNative.GetCapabilities();
        var loadedPath = FindLoadedLibRaw();
        var inventory = ResolveLoadedPrerequisites(NativeBinaryInspection.Inventory(
            loadedPath,
            Path.GetDirectoryName(loadedPath)!));
        var root = inventory.Single(item => item.ResolvedPath == loadedPath);
        Assert.NotNull(root.Binary);

        var raw = new ImageFile(Path.Combine(
            GoldenTestPaths.AssetDirectory, "canon-eos-350d.cr2"));
        var loader = new RawBaseLoader();
        using var warmPreview = LoadAndAssertRaw(loader, raw, preview: true);
        using var warmFull = LoadAndAssertRaw(loader, raw, preview: false);
        var preview = MeasureDecode(loader, raw, preview: true);
        using var previewImage = preview.Image;
        var full = MeasureDecode(loader, raw, preview: false);
        using var fullImage = full.Image;

        var exportRaw = Path.Combine(
            GoldenTestPaths.AssetDirectory, "fujifilm-x30.raf");
        var temporary = Path.Combine(
            Path.GetTempPath(), $"HappyPhotonBaseline_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        RawExportMeasurement export;
        try
        {
            await RawExportPerformanceMeasurement.MeasureAsync(
                exportRaw, Path.Combine(temporary, "warm-up"));
            export = await RawExportPerformanceMeasurement.MeasureAsync(
                exportRaw, Path.Combine(temporary, "measured"));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }

        _output.WriteLine("=== HAPPY PHOTON LIBRAW BASELINE BEGIN ===");
        _output.WriteLine($"date={DateTimeOffset.Now:yyyy-MM-dd}");
        _output.WriteLine($"rid={RuntimeInformation.RuntimeIdentifier}");
        _output.WriteLine($"os={RuntimeInformation.OSDescription}");
        _output.WriteLine($"process_architecture={RuntimeInformation.ProcessArchitecture}");
        _output.WriteLine($"framework={RuntimeInformation.FrameworkDescription}");
        _output.WriteLine($"processor={Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "not reported"}");
        _output.WriteLine($"logical_processors={Environment.ProcessorCount}");
        _output.WriteLine($"gc_available_memory_bytes={GC.GetGCMemoryInfo().TotalAvailableMemoryBytes}");
        _output.WriteLine($"libraw_version_number={versionNumber}");
        _output.WriteLine($"libraw_version_string={version}");
        _output.WriteLine($"capability_mask=0x{(uint)capabilities:X8}");
        foreach (var name in Enum.GetNames<LibRawCapability>())
        {
            var flag = Enum.Parse<LibRawCapability>(name);
            _output.WriteLine($"capability_{name}={capabilities.HasFlag(flag)}");
        }
        _output.WriteLine($"jpeg_available={capabilities.HasFlag(LibRawCapability.Jpeg)} (capability bit)");
        _output.WriteLine($"zlib_available={capabilities.HasFlag(LibRawCapability.Zlib)} (capability bit)");
        _output.WriteLine($"lcms_available={HasDependency(inventory, "lcms")} (dependency graph)");
        _output.WriteLine($"openmp_available={HasOpenMp(inventory)} (dependency graph)");
        _output.WriteLine($"loaded_module={loadedPath}");
        _output.WriteLine($"loaded_module_sha256={root.Sha256}");
        foreach (var item in inventory)
        {
            WriteInventory(item);
        }
        _output.WriteLine($"preview_fixture=canon-eos-350d.cr2; warm_up=one preview and one full decode; elapsed_ms={preview.Elapsed.TotalMilliseconds:F1}; size={preview.Image.Pixels.Width}x{preview.Image.Pixels.Height}; loader=RawLibRaw");
        _output.WriteLine($"full_fixture=canon-eos-350d.cr2; elapsed_ms={full.Elapsed.TotalMilliseconds:F1}; size={full.Image.Pixels.Width}x{full.Image.Pixels.Height}; loader=RawLibRaw");
        _output.WriteLine($"export_fixture=fujifilm-x30.raf; settings=JPEG quality 85, chroma NR 100; warm_up=one full export; sampling_ms={RawExportPerformanceMeasurement.SamplingIntervalMilliseconds}; elapsed_ms={export.Elapsed.TotalMilliseconds:F1}; size={export.Width}x{export.Height}; after_decode_private_delta_bytes={export.AfterDecodePrivateBytes}; peak_private_delta_bytes={export.PeakPrivateBytes}; loader={export.SourceKind}");
        _output.WriteLine($"declared_compatibility_floor={DeclaredCompatibilityFloor()}");
        _output.WriteLine("compatibility_note=encoded requirements above are binary facts; declared publisher floors are provenance facts recorded in the runtime audit");
        _output.WriteLine("isolation=verified by dedicated exact-filter command marker");
        _output.WriteLine("=== HAPPY PHOTON LIBRAW BASELINE END ===");
    }

    private static (BaseImage Image, TimeSpan Elapsed) MeasureDecode(
        RawBaseLoader loader,
        ImageFile file,
        bool preview)
    {
        var stopwatch = Stopwatch.StartNew();
        var image = LoadAndAssertRaw(loader, file, preview);
        stopwatch.Stop();
        return (image, stopwatch.Elapsed);
    }

    private static BaseImage LoadAndAssertRaw(
        RawBaseLoader loader,
        ImageFile file,
        bool preview)
    {
        var image = preview
            ? loader.LoadPreviewBase(file, BaseDecodeSettings.Default, CancellationToken.None)
            : loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(image);
        Assert.Equal(BaseSourceKind.RawLibRaw, image!.Info.Kind);
        Assert.True(image.Info.IsRawSource);
        return image;
    }

    private static string FindLoadedLibRaw()
    {
        _ = RawContext.VersionNumber;
        var candidates = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Select(module => module.FileName)
            .Where(path => path != null && IsLibRawName(Path.GetFileName(path)))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Single(candidates);
        return candidates[0];
    }

    private static bool IsLibRawName(string name) =>
        name.Equals("raw_r.dll", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("libraw_r.so", StringComparison.Ordinal) ||
        name.StartsWith("libraw.", StringComparison.Ordinal) && name.EndsWith(".dylib", StringComparison.Ordinal);

    private static bool HasDependency(
        IReadOnlyList<NativeDependencyInfo> inventory,
        string fragment) => inventory.Any(item =>
            item.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static bool HasOpenMp(IReadOnlyList<NativeDependencyInfo> inventory) =>
        HasDependency(inventory, "vcomp") || HasDependency(inventory, "libgomp") ||
        HasDependency(inventory, "libomp");

    private static string DeclaredCompatibilityFloor()
    {
        if (OperatingSystem.IsLinux())
        {
            return "Ubuntu 22.04 (Sdcb publisher provenance)";
        }
        if (OperatingSystem.IsMacOS())
        {
            return "macOS 13 (checked-in dylib build provenance)";
        }
        return "none separately declared for Windows";
    }

    private static IReadOnlyList<NativeDependencyInfo> ResolveLoadedPrerequisites(
        IReadOnlyList<NativeDependencyInfo> inventory)
    {
        var loaded = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Where(module => module.FileName != null)
            .GroupBy(module => module.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().FileName!,
                StringComparer.OrdinalIgnoreCase);
        return inventory.Select(item =>
        {
            if (item.ResolvedPath != null || item.Classification != "prerequisite" ||
                !loaded.TryGetValue(Path.GetFileName(item.Name), out var path))
            {
                return item;
            }

            return new NativeDependencyInfo(
                item.Name,
                Path.GetFullPath(path),
                item.Classification,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                NativeBinaryInspection.Inspect(path));
        }).ToArray();
    }

    private void WriteInventory(NativeDependencyInfo item)
    {
        _output.WriteLine($"dependency={item.Name}; classification={item.Classification}; path={item.ResolvedPath ?? "unresolved"}; sha256={item.Sha256 ?? "n/a"}");
        if (item.Binary == null)
        {
            return;
        }
        _output.WriteLine($"binary={item.Name}; format={item.Binary.Format}; architecture={item.Binary.Architecture}; identity={item.Binary.Identity ?? "none"}");
        _output.WriteLine($"imports={item.Name}: {string.Join(", ", item.Binary.Imports)}");
        _output.WriteLine($"encoded_requirements={item.Name}: {string.Join(", ", item.Binary.EncodedRequirements)}");
    }
}
