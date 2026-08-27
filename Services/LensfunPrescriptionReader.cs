using HappyPhoton.LibRaw.Interop;

namespace HappyPhoton.Services;

internal sealed class LensfunPrescriptionReader
{
    private static readonly Lazy<LensfunDatabase> DefaultDatabase = new(
        () => new LensfunDatabase(Path.Combine(
            PackagedDataRoot.Resolve(), "data", "lensfun")),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Lazy<LensfunDatabase> _database;

    internal LensfunPrescriptionReader()
    {
        _database = DefaultDatabase;
    }

    internal LensfunPrescriptionReader(string directory)
    {
        _database = new Lazy<LensfunDatabase>(
            () => new LensfunDatabase(directory),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal static bool ForceSource { get; set; }

    internal LensPrescriptionReadResult Read(
        LibRawMetadata metadata,
        int sensorWidth,
        int sensorHeight)
    {
        try
        {
            var profile = _database.Value.Resolve(
                metadata.NormalizedMake ?? metadata.Make,
                metadata.NormalizedModel ?? metadata.Model,
                metadata.Lens,
                metadata.FocalLength ?? 0,
                metadata.Aperture,
                sensorWidth,
                sensorHeight);
            if (profile == null) return LensPrescriptionReadResult.None;
            var distortion = CreateDistortion(profile);
            var tca = CreateTca(profile);
            var vignette = CreateVignette(profile);
            if (distortion == null && tca == null && vignette == null)
                return LensPrescriptionReadResult.None;
            return LensPrescriptionReadResult.Available(new LensPrescription(
                LensPrescriptionSource.Lensfun,
                profile.LensName,
                [],
                [],
                LensFrameWindow.Full,
                LensFrameWindow.Full,
                LensfunDistortion: distortion,
                LensfunTca: tca,
                LensfunVignette: vignette,
                ClassSources: new LensClassSources(
                    distortion == null ? null : LensPrescriptionSource.Lensfun,
                    tca == null ? null : LensPrescriptionSource.Lensfun,
                    vignette == null ? null : LensPrescriptionSource.Lensfun)));
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or InvalidDataException or
            System.Xml.XmlException or ArgumentException or AggregateException)
        {
            return LensPrescriptionReadResult.Reject(
                LensPrescriptionStatus.Invalid,
                $"Invalid Lensfun database: {exception.Message}");
        }
    }

    internal static LensPrescription? Merge(
        LensPrescription? embedded,
        LensPrescription? lensfun)
    {
        if (embedded == null) return lensfun;
        if (lensfun == null) return embedded;
        LensPrescriptionSource? distortionSource = embedded.HasDistortion
            ? embedded.ClassSources?.Distortion ?? embedded.Source
            : lensfun.HasDistortion
                ? lensfun.ClassSources?.Distortion ?? lensfun.Source
                : null;
        LensPrescriptionSource? tcaSource = embedded.HasChromaticAberration
            ? embedded.ClassSources?.ChromaticAberration ?? embedded.Source
            : lensfun.HasChromaticAberration
                ? lensfun.ClassSources?.ChromaticAberration ?? lensfun.Source
                : null;
        LensPrescriptionSource? vignetteSource = embedded.HasVignetting
            ? embedded.ClassSources?.Vignetting ?? embedded.Source
            : lensfun.HasVignetting
                ? lensfun.ClassSources?.Vignetting ?? lensfun.Source
                : null;
        return embedded with
        {
            LensName = embedded.LensName ?? lensfun.LensName,
            LensfunDistortion = embedded.HasDistortion
                ? null
                : lensfun.LensfunDistortion,
            LensfunTca = embedded.HasChromaticAberration
                ? null
                : lensfun.LensfunTca,
            LensfunVignette = embedded.HasVignetting
                ? null
                : lensfun.LensfunVignette,
            ClassSources = new LensClassSources(
                distortionSource, tcaSource, vignetteSource)
        };
    }

    private static LensfunDistortion? CreateDistortion(
        LensfunResolvedProfile profile) => profile.Distortion switch
    {
        { Model: "poly3", Parameters: [var k1] } when Effect(k1) => new(
            LensfunDistortionModel.Poly3, [k1], profile.RadiusScale,
            profile.CenterX, profile.CenterY),
        { Model: "poly5", Parameters: [var k1, var k2] }
            when Effect(k1, k2) => new(
            LensfunDistortionModel.Poly5, [k1, k2], profile.RadiusScale,
            profile.CenterX, profile.CenterY),
        { Model: "ptlens", Parameters: [var a, var b, var c] }
            when Effect(a, b, c) => new(
            LensfunDistortionModel.Ptlens, [a, b, c], profile.RadiusScale,
            profile.CenterX, profile.CenterY),
        _ => null
    };

    private static LensfunTca? CreateTca(LensfunResolvedProfile profile) =>
        profile.Tca switch
        {
            { Model: "linear", Parameters: [var kr, var kb] }
                when Effect(kr - 1, kb - 1) => new(
                LensfunTcaModel.Linear, [kr], [kb], profile.RadiusScale,
                profile.CenterX, profile.CenterY),
            { Model: "poly3", Parameters:
                [var br, var cr, var vr, var bb, var cb, var vb] }
                when Effect(br, cr, vr - 1, bb, cb, vb - 1) => new(
                    LensfunTcaModel.Poly3, [br, cr, vr], [bb, cb, vb],
                    profile.RadiusScale, profile.CenterX, profile.CenterY),
            _ => null
        };

    private static LensfunVignette? CreateVignette(
        LensfunResolvedProfile profile) => profile.Vignette switch
    {
        { Model: "pa", Parameters: [var k1, var k2, var k3] }
            when Effect(k1, k2, k3) => new(
            k1, k2, k3, profile.VignetteRadiusScale,
            profile.CenterX, profile.CenterY),
        _ => null
    };

    private static bool Effect(params double[] values) =>
        values.Any(value => Math.Abs(value) > 1e-12);
}
