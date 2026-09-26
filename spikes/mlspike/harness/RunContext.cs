using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using HappyPhoton.MlSpike;

namespace MlSpike.Harness;

internal sealed record Sample(string Id, string Category, string File, string Sha256,
    string? MaskFile, string? MaskSha256)
{
    public bool IsEdge => Category is "portrait" or "pet" or "product" or "low-light" or "multi-subject" or "landscape";
}

internal sealed class RunContext
{
    public const string FrozenManifest = "0e543077cf4614586a280b2a3e02dd82ce94e310389e1e1cf7c031564d0d84b6";
    public Dictionary<string, string> Args { get; }
    public ModelConfig Config { get; }
    public string ModelPath { get; }
    public string Output { get; }
    public string SampleRoot { get; }
    public Sample[] Samples { get; }
    public int Cores { get; }
    public object Identity { get; }
    public string Mode => Required("mode");

    public RunContext(string[] args)
    {
        Args = new Dictionary<string, string>();
        if (args.Length % 2 != 0) throw new ArgumentException("Arguments must be --name value pairs.");
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--") || !Args.TryAdd(args[i][2..], args[i + 1]))
                throw new ArgumentException("Invalid or duplicate argument.");
        }
        Config = ModelConfig.Read(Required("config"));
        ModelPath = Path.GetFullPath(Required("model"));
        var modelHash = LocalFiles.Hash(ModelPath);
        if (!modelHash.Equals(Config.ModelSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Model hash differs from config.");
        var manifest = Required("manifest");
        if (LocalFiles.Hash(manifest) != FrozenManifest)
            throw new InvalidDataException("Manifest hash differs from the owner-confirmed WP1 set.");
        using var document = JsonDocument.Parse(LocalFiles.Read(manifest));
        Samples = document.RootElement.GetProperty("samples").Deserialize<Sample[]>(ModelConfig.JsonOptions)
            ?? throw new InvalidDataException("Empty manifest.");
        if (Samples.Length != 80 || Samples.Select(s => s.Id).Distinct().Count() != 80 ||
            Samples.Any(s => !Regex.IsMatch(s.Id, "^[A-Za-z0-9_-]+$")))
            throw new InvalidDataException("Invalid sample identifiers/count.");
        SampleRoot = Path.GetFullPath(Required("sample-root"));
        foreach (var sample in Samples)
        {
            Verify(sample.File, sample.Sha256);
            if (sample.MaskFile != null) Verify(sample.MaskFile, sample.MaskSha256!);
        }
        Output = Path.GetFullPath(Required("output"));
        if (Directory.Exists(Output) || File.Exists(Output))
            throw new IOException("Output must be a new directory per invocation.");
        Cores = CpuTopology.PhysicalCores();
        var runtimePackage = Required("runtime-package");
        Identity = new
        {
            Config.Candidate, Config.Capability, ModelSha256 = modelHash,
            ManifestSha256 = FrozenManifest, ConfigSha256 = LocalFiles.Hash(Required("config")),
            RuntimePackageSha256 = RuntimeEvidence.Verify(runtimePackage), RuntimeVersion = "1.30.0",
            Environment = Required("environment"), RunnerImage = Environment.GetEnvironmentVariable("ImageVersion"),
            CpuModel = Required("cpu-model"), PhysicalCores = Cores,
            IntraOp = Math.Max(1, Cores - 1), InterOp = 1, Provider = "CPUExecutionProvider",
            Os = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Framework = RuntimeInformation.FrameworkDescription, Machine = Environment.MachineName,
            ProcessId = Environment.ProcessId, StartedUtc = DateTimeOffset.UtcNow
        };
    }

    public string Required(string key) => Args.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException($"Missing --{key}.");

    private void Verify(string file, string expected)
    {
        if (!LocalFiles.Hash(LocalFiles.Resolve(SampleRoot, file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Frozen sample hash mismatch: {file}");
    }

    public PreviewInput Load(Sample sample) => PreviewInput.Load(LocalFiles.Resolve(SampleRoot, sample.File));
    public InferenceEngine Session() => new(ModelPath, Config, Cores);

    public Sample[] Edges()
    {
        var samples = Samples.Where(s => s.IsEdge).ToArray();
        if (samples.Length != 36) throw new InvalidDataException("Expected 36 edges.");
        return samples;
    }

    public Sample SelectedImage()
    {
        var id = Required("image-id");
        return Edges().Single(s => s.Id == id);
    }

    public void Json(string name, object value)
    {
        using var stream = LocalFiles.Create(Path.Combine(Output, name));
        JsonSerializer.Serialize(stream, value, ModelConfig.JsonOptions);
    }

    public object Execute() => Mode switch
    {
        "quality" => QualityModes.Quality(this),
        "contact-sheet" => QualityModes.ContactSheet(this),
        "repeat" => QualityModes.Repeat(this),
        "latency" => PerformanceModes.Latency(this),
        "cold" => PerformanceModes.Cold(this),
        "memory" => PerformanceModes.Memory(this),
        "cancel" => StressModes.Cancel(this),
        "interaction" => StressModes.Interaction(this),
        _ => throw new ArgumentException("Unknown mode.")
    };
}
