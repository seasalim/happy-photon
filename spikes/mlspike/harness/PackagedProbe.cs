namespace HappyPhoton.MlSpike;

internal static class PackagedProbe
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length != 3)
                throw new ArgumentException("Usage: --mlspike-probe <frozen-image> <new-mask.png>");
            var root = AppContext.BaseDirectory;
            if (OperatingSystem.IsMacOS())
            {
                var macOs = Path.GetDirectoryName(Environment.ProcessPath)
                    ?? throw new IOException("Cannot resolve bundle.");
                root = Path.GetFullPath(Path.Combine(macOs, "..", "Resources"));
            }
            var payload = Path.Combine(root, "mlspike");
            var capability = Environment.GetEnvironmentVariable("MLSPIKE_PROBE_CAPABILITY");
            if (capability is not (null or "subject" or "sky"))
                throw new ArgumentException("MLSPIKE_PROBE_CAPABILITY must be subject or sky.");
            var name = capability ?? "model";
            var config = ModelConfig.Read(Path.Combine(payload, name + ".json"));
            if (capability != null && config.Capability != capability)
                throw new InvalidDataException("Bundled capability differs from selected probe.");
            var model = Path.Combine(payload, name + ".onnx");
            if (!string.Equals(LocalFiles.Hash(model), config.ModelSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Bundled model hash mismatch.");
            using var session = new InferenceEngine(model, config, CpuTopology.PhysicalCores());
            var input = PreviewInput.Load(args[1]);
            MaskFiles.Write(args[2], session.Infer(input), input.Width, input.Height);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
