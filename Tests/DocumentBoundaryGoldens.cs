namespace HappyPhoton.Tests;

// Canonical 33e5cf5 outcomes with the retired lens property removed.
internal static class DocumentBoundaryGoldens
{
    internal const string Current = """{"version":3,"exposure":0.75,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":false,"vignetting":true},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}""";
    internal const string Neutral = """{"version":3,"exposure":0,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":true,"vignetting":false},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}""";
}
