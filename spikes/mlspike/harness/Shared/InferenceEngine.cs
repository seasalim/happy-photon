using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HappyPhoton.MlSpike;

public sealed class InferenceEngine : IDisposable
{
    private readonly InferenceSession _session;
    public ModelConfig Config { get; }

    public InferenceEngine(string modelPath, ModelConfig config, int physicalCores)
    {
        config.Validate();
        if (physicalCores < 1) throw new ArgumentOutOfRangeException(nameof(physicalCores));
        LocalFiles.Check(modelPath);
        Config = config;
        using var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Max(1, physicalCores - 1),
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };
        options.AppendExecutionProvider_CPU(0);
        _session = new InferenceSession(modelPath, options);
        if (!_session.InputMetadata.ContainsKey(config.InputName) ||
            !_session.OutputMetadata.ContainsKey(config.OutputName))
        {
            _session.Dispose();
            throw new InvalidDataException("Configured input/output names are absent from the model.");
        }
    }

    public byte[] Infer(PreviewInput image, RunOptions? runOptions = null, Action? enteringRun = null, Action? returningRun = null)
    {
        var values = MaskMath.Preprocess(image, Config);
        int[] dimensions = Config.InputLayout == "NCHW"
            ? [1, 3, Config.Height, Config.Width] : [1, Config.Height, Config.Width, 3];
        var input = NamedOnnxValue.CreateFromTensor(Config.InputName, new DenseTensor<float>(values, dimensions));
        using var ownedOptions = runOptions == null ? new RunOptions() : null;
        enteringRun?.Invoke();
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> result;
        try { result = _session.Run([input], [Config.OutputName], runOptions ?? ownedOptions!); }
        finally { returningRun?.Invoke(); }
        using var output = result;
        var tensor = output.First().AsTensor<float>();
        var shape = tensor.Dimensions.ToArray();
        var probabilities = MaskMath.Probabilities(tensor.ToArray(), shape, Config);
        return MaskMath.ResizeMask(probabilities, shape[Config.OutputLayout == "NCHW" ? 3 : 2],
            shape[Config.OutputLayout == "NCHW" ? 2 : 1], image.Width, image.Height);
    }

    public void Dispose() => _session.Dispose();
}
