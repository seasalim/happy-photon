using System.Text.Json;

namespace HappyPhoton.MlSpike;

public sealed record ModelConfig
{
    public required string Candidate { get; init; }
    public required string Capability { get; init; }
    public required string ModelSha256 { get; init; }
    public required string InputName { get; init; }
    public required string OutputName { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public float[] Mean { get; init; } = [0.485f, 0.456f, 0.406f];
    public float[] Std { get; init; } = [0.229f, 0.224f, 0.225f];
    public string InputLayout { get; init; } = "NCHW";
    public string OutputLayout { get; init; } = "NCHW";
    public string Activation { get; init; } = "sigmoid";
    public int ClassIndex { get; init; }

    public static ModelConfig Read(string path)
    {
        var config = JsonSerializer.Deserialize<ModelConfig>(LocalFiles.Read(path), JsonOptions)
            ?? throw new InvalidDataException("Missing model config.");
        config.Validate();
        return config;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Candidate) || Capability is not ("subject" or "sky") ||
            Width <= 0 || Height <= 0 || Mean.Length != 3 || Std.Length != 3 ||
            Mean.Any(v => !float.IsFinite(v)) || Std.Any(v => !float.IsFinite(v) || v <= 0) ||
            InputLayout is not ("NCHW" or "NHWC") || OutputLayout is not ("NCHW" or "NHWC") ||
            Activation is not ("sigmoid" or "softmax" or "probability") || ClassIndex < 0 ||
            string.IsNullOrWhiteSpace(InputName) || string.IsNullOrWhiteSpace(OutputName) ||
            ModelSha256.Length != 64 || !ModelSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid model config.");
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}
