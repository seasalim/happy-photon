using HappyPhoton.Tests;

var output = args.Length == 1
    ? Path.GetFullPath(args[0])
    : Path.Combine(AppContext.BaseDirectory, "fixtures");
Directory.CreateDirectory(output);

Write("matrix-only.dcp", new SyntheticDcpOptions
{
    Name = "Happy Photon synthetic matrix",
    UniqueCameraModel = "Synthetic Camera",
    EmbedPolicy = 3
});
Write("huesat-2_5d.dcp", new SyntheticDcpOptions
{
    Name = "Happy Photon synthetic 2.5D HueSat",
    UniqueCameraModel = "Synthetic Camera",
    EmbedPolicy = 3,
    HueSatDimensions = [6, 3, 1],
    HueSatTable1 = CreateTable(6, 3, 1, 8, 1.1f, 0.92f)
});
Write("dual-illuminant.dcp", new SyntheticDcpOptions
{
    Name = "Happy Photon synthetic dual illuminant",
    UniqueCameraModel = "Synthetic Camera",
    EmbedPolicy = 3,
    Illuminant1 = 17,
    Illuminant2 = 21,
    ColorMatrix2 = SyntheticDcpOptions.Identity,
    ForwardMatrix1 = D50Forward(),
    ForwardMatrix2 = D50Forward(),
    HueSatDimensions = [6, 3, 2],
    HueSatTable1 = CreateTable(6, 3, 2, -4, 0.95f, 1.05f),
    HueSatTable2 = CreateTable(6, 3, 2, 6, 1.08f, 0.96f),
    HueSatEncoding = 1
});

Console.WriteLine($"Generated synthetic DCP fixtures in {output}");

void Write(string name, SyntheticDcpOptions options) =>
    File.WriteAllBytes(
        Path.Combine(output, name),
        SyntheticDcpFactory.Create(options));

static double[] D50Forward() =>
    [0.96422, 0, 0, 0, 1, 0, 0, 0, 0.82521];

static float[] CreateTable(
    int hueDivisions,
    int saturationDivisions,
    int valueDivisions,
    float hueShift,
    float saturationScale,
    float valueScale)
{
    var result = new float[hueDivisions * saturationDivisions * valueDivisions * 3];
    for (var value = 0; value < valueDivisions; value++)
    for (var hue = 0; hue < hueDivisions; hue++)
    for (var saturation = 0; saturation < saturationDivisions; saturation++)
    {
        var index = ((value * hueDivisions + hue) *
            saturationDivisions + saturation) * 3;
        result[index] = hueShift;
        result[index + 1] = saturationScale;
        result[index + 2] = saturation == 0 ? 1 : valueScale;
    }
    return result;
}
