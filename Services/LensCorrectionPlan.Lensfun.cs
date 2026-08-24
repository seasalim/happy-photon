using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

internal sealed partial class LensCorrectionPlan
{
    private CompiledLensfunDistortion? _lensfunDistortion;
    private LensWarpOperation? _lensfunAnalyticDistortion;
    private LensWarpOperation? _lensfunAnalyticTca;
    private CompiledLensfunTca? _lensfunTca;
    private CompiledLensfunVignette? _lensfunVignette;

    private void CompileLensfunGeometry(
        LensPrescription prescription,
        BaseDecodeSettings settings,
        int width,
        int height)
    {
        if (settings.Distortion && prescription.LensfunDistortion is { } distortion)
        {
            if (distortion.Model == LensfunDistortionModel.Ptlens)
                _lensfunDistortion = new CompiledLensfunDistortion(
                    distortion, width, height);
            else
                _lensfunAnalyticDistortion = new LensWarpOperation(
                    AnalyticDistortion(distortion, width, height), width, height,
                    settings with { ChromaticAberration = false });
        }
        if (settings.ChromaticAberration && prescription.LensfunTca is { } tca)
        {
            if (tca.Model == LensfunTcaModel.Poly3)
                _lensfunTca = new CompiledLensfunTca(tca, width, height);
            else
                _lensfunAnalyticTca = new LensWarpOperation(
                    AnalyticTca(tca), width, height,
                    settings with { Distortion = false });
        }
    }

    internal double GetVignetteGain(
        LensPoint outputGeometryPoint,
        LensPoint greenPostGeometryPoint)
    {
        var gain = 1.0;
        foreach (var vignette in _vignettes)
            gain *= vignette.GetGain(outputGeometryPoint);
        foreach (var vignette in _tableVignettes)
            gain *= vignette.GetGain(outputGeometryPoint);
        if (_lensfunVignette is { } lensfun)
            gain *= lensfun.GetGain(greenPostGeometryPoint);
        return Math.Max(0, gain);
    }

    private static LensWarp AnalyticDistortion(
        LensfunDistortion distortion,
        int width,
        int height)
    {
        var smaller = Math.Min(width - 1, height - 1);
        var mx = Math.Max(distortion.CenterX, 1 - distortion.CenterX) * (width - 1);
        var my = Math.Max(distortion.CenterY, 1 - distortion.CenterY) * (height - 1);
        var scale = smaller > 0
            ? 2 * distortion.RadiusScale * Math.Sqrt(mx * mx + my * my) / smaller
            : 0;
        var scale2 = scale * scale;
        var k1 = distortion.Coefficients[0];
        var coefficients = distortion.Model switch
        {
            LensfunDistortionModel.Poly3 => new LensWarpCoefficients(
                1 - k1, k1 * scale2, 0, 0, 0, 0),
            LensfunDistortionModel.Poly5 => new LensWarpCoefficients(
                1, k1 * scale2,
                distortion.Coefficients[1] * scale2 * scale2, 0, 0, 0),
            _ => throw new InvalidOperationException()
        };
        return new LensWarp(
            [coefficients], distortion.CenterX, distortion.CenterY);
    }

    private static LensWarp AnalyticTca(LensfunTca tca) => new(
        [
            new LensWarpCoefficients(tca.Red[0], 0, 0, 0, 0, 0),
            new LensWarpCoefficients(1, 0, 0, 0, 0, 0),
            new LensWarpCoefficients(tca.Blue[0], 0, 0, 0, 0, 0)
        ],
        tca.CenterX,
        tca.CenterY);

    private readonly struct CompiledLensfunDistortion
    {
        private readonly double _p0;
        private readonly double _p1;
        private readonly double _p2;
        private readonly LensfunCoordinates _coordinates;

        internal CompiledLensfunDistortion(
            LensfunDistortion distortion,
            int width,
            int height)
        {
            _p0 = distortion.Coefficients.ElementAtOrDefault(0);
            _p1 = distortion.Coefficients.ElementAtOrDefault(1);
            _p2 = distortion.Coefficients.ElementAtOrDefault(2);
            _coordinates = new LensfunCoordinates(
                distortion.RadiusScale, distortion.CenterX, distortion.CenterY,
                width, height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal LensPoint Apply(LensPoint point)
        {
            var radius = _coordinates.Radius(point);
            var factor = 1 - _p0 - _p1 - _p2 +
                radius * (_p2 + radius * (_p1 + radius * _p0));
            return _coordinates.Scale(point, factor);
        }
    }

    private readonly struct CompiledLensfunTca
    {
        private readonly double _red0;
        private readonly double _red1;
        private readonly double _red2;
        private readonly double _blue0;
        private readonly double _blue1;
        private readonly double _blue2;
        private readonly LensfunCoordinates _coordinates;

        internal CompiledLensfunTca(LensfunTca tca, int width, int height)
        {
            _red0 = tca.Red.ElementAtOrDefault(0);
            _red1 = tca.Red.ElementAtOrDefault(1);
            _red2 = tca.Red.ElementAtOrDefault(2);
            _blue0 = tca.Blue.ElementAtOrDefault(0);
            _blue1 = tca.Blue.ElementAtOrDefault(1);
            _blue2 = tca.Blue.ElementAtOrDefault(2);
            _coordinates = new LensfunCoordinates(
                tca.RadiusScale, tca.CenterX, tca.CenterY, width, height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal LensPoint Apply(LensPoint point, int channel)
        {
            if (channel == 1) return point;
            var radius = _coordinates.Radius(point);
            var first = channel == 0 ? _red0 : _blue0;
            var second = channel == 0 ? _red1 : _blue1;
            var third = channel == 0 ? _red2 : _blue2;
            var factor = third + radius * (second + first * radius);
            return _coordinates.Scale(point, factor);
        }
    }

    private readonly struct CompiledLensfunVignette
    {
        private readonly LensfunVignette _vignette;
        private readonly LensfunCoordinates _coordinates;

        internal CompiledLensfunVignette(
            LensfunVignette vignette,
            int width,
            int height)
        {
            _vignette = vignette;
            _coordinates = new LensfunCoordinates(
                vignette.RadiusScale, vignette.CenterX, vignette.CenterY,
                width, height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double GetGain(LensPoint point)
        {
            var radius = _coordinates.Radius(point);
            var r2 = radius * radius;
            var denominator = 1 + r2 * (_vignette.K1 + r2 *
                (_vignette.K2 + r2 * _vignette.K3));
            return denominator > 0 ? 1 / denominator : 0;
        }
    }

    private readonly struct LensfunCoordinates
    {
        private readonly double _centerX;
        private readonly double _centerY;
        private readonly double _xToRadius;
        private readonly double _yToRadius;

        internal LensfunCoordinates(
            double radiusScale,
            double centerX,
            double centerY,
            int width,
            int height)
        {
            _centerX = centerX;
            _centerY = centerY;
            var smaller = Math.Min(width - 1, height - 1);
            _xToRadius = smaller > 0 ? 2 * radiusScale * (width - 1) / smaller : 0;
            _yToRadius = smaller > 0 ? 2 * radiusScale * (height - 1) / smaller : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double Radius(LensPoint point)
        {
            var x = (point.X - _centerX) * _xToRadius;
            var y = (point.Y - _centerY) * _yToRadius;
            return Math.Sqrt(x * x + y * y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal LensPoint Scale(LensPoint point, double factor) => new(
            _centerX + (point.X - _centerX) * factor,
            _centerY + (point.Y - _centerY) * factor);
    }
}
