using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Xunit;

namespace HappyPhoton.LibRaw.Interop.Tests;

public sealed class BridgeIntegrationTests
{
    [Fact]
    public void StagedBridge_PositiveOperationRoundTrips()
    {
        var source = Environment.GetEnvironmentVariable("HAPPY_PHOTON_LIBRAW_BRIDGE_DIR");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(source) || !Directory.Exists(source),
            "Set HAPPY_PHOTON_LIBRAW_BRIDGE_DIR to a complete bridge runtime directory.");
        using var staging = new TemporaryDirectory();
        foreach (var file in Directory.GetFiles(source!, "*.dll"))
            File.Copy(file, Path.Combine(staging.Path, Path.GetFileName(file)));
        var bridge = Path.Combine(staging.Path, "happyphoton_libraw_bridge.dll");
        var libraw = Path.Combine(staging.Path, "raw_r.dll");
        Assert.True(File.Exists(bridge) && File.Exists(libraw));
        var bridgeHash = Hash(bridge);
        var librawHash = Hash(libraw);
        Environment.SetEnvironmentVariable("HAPPY_PHOTON_LIBRAW_BRIDGE_DIR", staging.Path);

        var runtime = LibRawContext.Runtime;
        Assert.Equal(1u, runtime.BridgeAbiVersion);
        Assert.Equal(0x001602u, runtime.LibRawVersionNumber);
        AssertPathAndHash("happyphoton_libraw_bridge", staging.Path, bridgeHash);
        AssertPathAndHash("raw_r", staging.Path, librawHash);

        var fixture = FindFixture();
        var unicodeFixture = Path.Combine(staging.Path, "写真-カメラ.cr2");
        File.Copy(fixture, unicodeFixture);
        ExerciseLinear(unicodeFixture);
        ExerciseSrgb(unicodeFixture);
        ExerciseOracleParity(FindRepositoryRoot());
        ExerciseLeakedLeaseRecovery(unicodeFixture);
    }

    private static void ExerciseLinear(string fixture)
    {
        using var context = LibRawContext.Open(fixture);
        var dimensions = context.GetDimensions();
        Assert.True(dimensions.RawWidth >= dimensions.VisibleWidth);
        var sensor = context.GetSensorIdentity();
        Assert.Equal(36, sensor.XTrans.Length);
        var metadata = context.GetMetadata();
        Assert.False(string.IsNullOrWhiteSpace(metadata.Make));
        var camera = context.GetCameraFacts();
        Assert.NotNull(camera);
        Assert.Null(context.GetFujiFacts());
        using var thumbnail = context.ExtractThumbnail();
        Assert.NotNull(thumbnail);
        Assert.NotEmpty(thumbnail!.CopyData());
        context.Unpack();
        using (var mosaic = context.BorrowMosaic())
        {
            Assert.NotNull(mosaic);
            Assert.False(mosaic!.Samples.IsEmpty);
            Assert.Throws<LibRawProgrammingException>(() => context.Process());
        }
        context.ConfigureOutput(LibRawOutputConfiguration.Linear(
            LibRawHighlightMode.Clip, LibRawFbddMode.Off, true));
        context.Process();
        using var image = context.MakeProcessedImage();
        Assert.Equal(16u, image.Description.BitsPerSample);
        Assert.Equal(3u, image.Description.Channels);
        Assert.NotEmpty(image.CopyData());
        image.Dispose();
        context.Dispose();
    }

    private static void ExerciseSrgb(string fixture)
    {
        using var context = LibRawContext.Open(fixture);
        context.Unpack();
        context.ConfigureOutput(LibRawOutputConfiguration.FullDecodeSrgb());
        context.Process();
        using var image = context.MakeProcessedImage();
        Assert.Equal(8u, image.Description.BitsPerSample);
        Assert.Equal(3u, image.Description.Channels);
        Assert.NotEmpty(image.CopyData());
    }

    private static void ExerciseLeakedLeaseRecovery(string fixture)
    {
        using var context = LibRawContext.Open(fixture);
        context.Unpack();
        var weakLease = LeakLease(context);
        for (var attempt = 0; attempt < 5 && weakLease.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(weakLease.IsAlive);
        context.ConfigureOutput(LibRawOutputConfiguration.FullDecodeSrgb());
        context.Process();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LeakLease(LibRawContext context)
    {
        var lease = context.BorrowMosaic();
        Assert.NotNull(lease);
        return new WeakReference(lease);
    }

    private static void AssertPathAndHash(string moduleName, string directory, string hash)
    {
        var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Single(value => string.Equals(Path.GetFileNameWithoutExtension(value.FileName),
                moduleName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Path.GetFullPath(directory), Path.GetDirectoryName(Path.GetFullPath(module.FileName)),
            ignoreCase: true);
        Assert.Equal(hash, Hash(module.FileName));
    }

    private static string FindFixture()
        => Path.Combine(FindRepositoryRoot(), "Tests", "assets", "canon-eos-350d.cr2");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "HappyPhoton.sln");
            if (File.Exists(candidate)) return directory.FullName;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate the repository root.");
    }

    private static void ExerciseOracleParity(string root)
    {
        var factsDirectory = Path.Combine(root, "native", "libraw", "oracle", "facts");
        foreach (var factsPath in Directory.GetFiles(factsDirectory, "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(factsPath));
            var json = document.RootElement;
            var fixture = Path.Combine(root, "Tests", "assets", json.GetProperty("fixture").GetString()!);
            using var context = LibRawContext.Open(fixture);
            var sensor = context.GetSensorIdentity();
            var expectedSensor = json.GetProperty("sensor");
            Assert.Equal(expectedSensor.GetProperty("colors").GetInt32(), sensor.Colors);
            Assert.Equal(expectedSensor.GetProperty("filters").GetUInt32(), sensor.Filters);
            Assert.Equal(expectedSensor.GetProperty("dng_version").GetUInt32(), sensor.DngVersion);
            Assert.Equal(expectedSensor.GetProperty("xtrans").EnumerateArray()
                .Select(value => (sbyte)value.GetInt32()), sensor.XTrans);
            Assert.Equal(expectedSensor.GetProperty("cdesc").GetString(), sensor.ColorDescription);
            var metadata = context.GetMetadata();
            var service = json.GetProperty("service");
            var expected35mm = service.GetProperty("focal_length_35mm").GetSingle();
            Assert.Equal(expected35mm > 0 ? expected35mm : null, metadata.FocalLength35mm);
            Assert.Equal(service.GetProperty("gps_parsed").GetInt32() != 0, metadata.Gps.Parsed);
            var expectedCamera = json.GetProperty("camera");
            var camera = context.GetCameraFacts();
            Assert.NotNull(camera);
            Assert.Equal(expectedCamera.GetProperty("multiplier_count").GetInt32(),
                camera!.Multipliers.Length);
            Assert.Equal(expectedCamera.GetProperty("multipliers").EnumerateArray()
                .Select(value => value.GetSingle()), camera.Multipliers);
            Assert.Equal(expectedCamera.GetProperty("matrix_rows").GetInt32(),
                camera.CameraToSrgb.GetLength(0));
            Assert.Equal(expectedCamera.GetProperty("matrix_columns").GetInt32(),
                camera.CameraToSrgb.GetLength(1));
            var expectedMatrix = expectedCamera.GetProperty("matrix").EnumerateArray()
                .SelectMany(row => row.EnumerateArray()).Select(value => value.GetSingle()).ToArray();
            Assert.Equal(expectedMatrix, camera.CameraToSrgb.Cast<float>());
            var expectedFuji = json.GetProperty("fuji");
            var fuji = context.GetFujiFacts();
            Assert.Equal(expectedFuji.GetProperty("present").GetBoolean(), fuji is not null);
            if (fuji is not null)
            {
                Assert.Equal(expectedFuji.GetProperty("exposure_midpoint_shift").GetSingle(),
                    fuji.ExposureMidpointShift, 5);
                Assert.Equal(expectedFuji.GetProperty("development_dynamic_range").GetUInt32(),
                    fuji.DevelopmentDynamicRange);
            }
            context.Unpack();
            using var mosaic = context.BorrowMosaic();
            Assert.NotNull(mosaic);
            var dimensions = json.GetProperty("dimensions");
            Assert.Equal(json.GetProperty("extent").GetUInt64(),
                (ulong)mosaic!.RawPitch * mosaic.RawHeight);
            Assert.Equal(json.GetProperty("raw_pitch").GetUInt32(), mosaic.RawPitch);
            Assert.Equal(dimensions.GetProperty("raw_width").GetUInt32(), mosaic.RawWidth);
            Assert.Equal(dimensions.GetProperty("raw_height").GetUInt32(), mosaic.RawHeight);
            Assert.Equal(dimensions.GetProperty("width").GetUInt32(), mosaic.VisibleWidth);
            Assert.Equal(dimensions.GetProperty("height").GetUInt32(), mosaic.VisibleHeight);
            Assert.Equal(dimensions.GetProperty("top_margin").GetUInt32(), mosaic.TopMargin);
            Assert.Equal(dimensions.GetProperty("left_margin").GetUInt32(), mosaic.LeftMargin);
            Assert.Equal(json.GetProperty("black").GetUInt32(), mosaic.Black);
            Assert.Equal(json.GetProperty("maximum").GetUInt32(), mosaic.Maximum);
            var cblack = json.GetProperty("cblack");
            Assert.Equal(cblack.GetProperty("block_rows").GetUInt32(), mosaic.RepeatingRows);
            Assert.Equal(cblack.GetProperty("block_columns").GetUInt32(), mosaic.RepeatingColumns);
            Assert.Equal(cblack.GetProperty("values").EnumerateArray().Select(value => value.GetUInt32()),
                mosaic.CBlack.Take(cblack.GetProperty("values").GetArrayLength()));
        }
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"hplr-managed-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
