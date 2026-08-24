using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

internal sealed partial class LensCorrectionPlan
{
    private readonly LensWarpOperation[] _warps;
    private readonly LensTableWarpOperation[] _tableWarps;
    private readonly CompiledVignette[] _vignettes;
    private readonly CompiledTableVignette[] _tableVignettes;
    private readonly double _originX;
    private readonly double _originY;
    private readonly double _xStepX;
    private readonly double _xStepY;
    private readonly double _yStepX;
    private readonly double _yStepY;
    private readonly double _sourceScaleX;
    private readonly double _sourceScaleY;
    private readonly double _sourceOffsetX;
    private readonly double _sourceOffsetY;

    internal LensCorrectionPlan(
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight,
        int orientation,
        LensPrescription prescription,
        BaseDecodeSettings settings,
        double zoom,
        LensCorrectionReferenceFrame? referenceFrame = null)
    {
        var referenceWidth = referenceFrame?.SourceWidth ?? sourceWidth;
        var referenceHeight = referenceFrame?.SourceHeight ?? sourceHeight;
        var output = prescription.OutputWindow;
        var centerX = (output.Left + output.Right) * 0.5;
        var centerY = (output.Top + output.Bottom) * 0.5;
        var oriented = OrientedPixelAffine(outputWidth, outputHeight, orientation);
        _originX = centerX +
            (output.Left + oriented.OriginX * output.Width - centerX) / zoom;
        _originY = centerY +
            (output.Top + oriented.OriginY * output.Height - centerY) / zoom;
        _xStepX = oriented.XStepX * output.Width / zoom;
        _xStepY = oriented.XStepY * output.Height / zoom;
        _yStepX = oriented.YStepX * output.Width / zoom;
        _yStepY = oriented.YStepY * output.Height / zoom;

        var source = prescription.SourceWindow;
        var logicalWidth = Math.Max(2, (int)Math.Round(referenceWidth / source.Width));
        var logicalHeight = Math.Max(2, (int)Math.Round(referenceHeight / source.Height));
        _sourceScaleX = sourceWidth / source.Width;
        _sourceScaleY = sourceHeight / source.Height;
        _sourceOffsetX = -source.Left * _sourceScaleX - 0.5;
        _sourceOffsetY = -source.Top * _sourceScaleY - 0.5;
        _warps = prescription.Warps.Select(warp =>
            new LensWarpOperation(warp, logicalWidth, logicalHeight, settings)).ToArray();
        _tableWarps = prescription.RadialTableWarps.Select(warp =>
            new LensTableWarpOperation(warp, logicalWidth, logicalHeight, settings)).ToArray();
        CompileLensfunGeometry(
            prescription, settings, logicalWidth, logicalHeight);
        HasSharedGeometry = _warps.All(warp => warp.IsShared) &&
            _tableWarps.All(warp => warp.IsShared) &&
            _lensfunAnalyticTca == null && _lensfunTca == null;

        _vignettes = settings.Vignetting
            ? prescription.Vignettes.Select(vignette =>
                new CompiledVignette(
                    vignette, referenceWidth, referenceHeight, source)).ToArray()
            : [];
        _tableVignettes = settings.Vignetting
            ? prescription.RadialTableVignettes.Select(vignette =>
                new CompiledTableVignette(vignette, logicalWidth, logicalHeight)).ToArray()
            : [];
        _lensfunVignette = settings.Vignetting && prescription.LensfunVignette != null
            ? new CompiledLensfunVignette(
                prescription.LensfunVignette, logicalWidth, logicalHeight)
            : null;
        HasVignetting = _vignettes.Length != 0 || _tableVignettes.Length != 0 ||
            _lensfunVignette != null;
    }
    internal bool HasSharedGeometry { get; }
    internal bool HasVignetting { get; }

    internal LensPoint GetLogicalPoint(double x, double y) => new(
        _originX + x * _xStepX + y * _yStepX,
        _originY + x * _xStepY + y * _yStepY);

    internal LensPoint MapShared(LensPoint point) => Map(point, 1);

    internal LensPoint MapShared(
        LensPoint point,
        out LensPoint prescriptionPoint) =>
        Map(point, 1, out prescriptionPoint);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal LensPoint MapShared(int x, int y)
    {
        var point = new LensPoint(
            _originX + x * _xStepX + y * _yStepX,
            _originY + x * _xStepY + y * _yStepY);
        point = MapGeometry(point, 1);
        return new LensPoint(
            point.X * _sourceScaleX + _sourceOffsetX,
            point.Y * _sourceScaleY + _sourceOffsetY);
    }

    internal LensPoint Map(LensPoint point, int channel) =>
        Map(point, channel, out _);

    internal LensPoint Map(
        LensPoint point,
        int channel,
        out LensPoint prescriptionPoint)
    {
        prescriptionPoint = MapGeometry(point, channel);
        return new LensPoint(
            prescriptionPoint.X * _sourceScaleX + _sourceOffsetX,
            prescriptionPoint.Y * _sourceScaleY + _sourceOffsetY);
    }

    private LensPoint MapGeometry(LensPoint point, int channel)
    {
        if (_lensfunDistortion is { } distortion)
            point = distortion.Apply(point);
        if (_lensfunAnalyticDistortion is { } analyticDistortion)
            point = analyticDistortion.Apply(point, 1);
        foreach (var warp in _warps)
            point = warp.Apply(point, channel);
        foreach (var warp in _tableWarps)
            point = warp.Apply(point, channel);
        if (_lensfunAnalyticTca is { } analyticTca)
            point = analyticTca.Apply(point, channel);
        if (_lensfunTca is { } tca)
            point = tca.Apply(point, channel);
        return point;
    }
    private static PixelAffine OrientedPixelAffine(
        int width,
        int height,
        int orientation)
    {
        var halfX = 0.5 / width;
        var halfY = 0.5 / height;
        var stepX = 1.0 / width;
        var stepY = 1.0 / height;
        return orientation switch
        {
            1 => new(halfX, halfY, stepX, 0, 0, stepY),
            2 => new(1 - halfX, halfY, -stepX, 0, 0, stepY),
            3 => new(1 - halfX, 1 - halfY, -stepX, 0, 0, -stepY),
            4 => new(halfX, 1 - halfY, stepX, 0, 0, -stepY),
            5 => new(halfY, halfX, 0, stepX, stepY, 0),
            6 => new(halfY, 1 - halfX, 0, -stepX, stepY, 0),
            7 => new(1 - halfY, 1 - halfX, 0, -stepX, -stepY, 0),
            8 => new(1 - halfY, halfX, 0, stepX, -stepY, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(orientation))
        };
    }

    private readonly record struct PixelAffine(
        double OriginX,
        double OriginY,
        double XStepX,
        double XStepY,
        double YStepX,
        double YStepY);

    private readonly struct LensWarpOperation
    {
        private readonly WarpMode _mode;
        private readonly CompiledWarp _red;
        private readonly CompiledWarp _green;
        private readonly CompiledWarp _blue;

        internal LensWarpOperation(
            LensWarp warp,
            int width,
            int height,
            BaseDecodeSettings settings)
        {
            var greenIndex = warp.Planes.Count == 1 ? 0 : 1;
            _green = new CompiledWarp(
                warp, warp.Planes[greenIndex], width, height);
            _red = warp.Planes.Count == 3
                ? new CompiledWarp(warp, warp.Planes[0], width, height)
                : _green;
            _blue = warp.Planes.Count == 3
                ? new CompiledWarp(warp, warp.Planes[2], width, height)
                : _green;

            if (warp.Planes.Count == 1)
                _mode = settings.Distortion ? WarpMode.Shared : WarpMode.None;
            else if (settings.Distortion && settings.ChromaticAberration)
                _mode = warp.HasPerPlaneGeometry ? WarpMode.PerPlane : WarpMode.Shared;
            else if (settings.Distortion)
                _mode = WarpMode.Shared;
            else if (settings.ChromaticAberration && warp.HasPerPlaneGeometry)
                _mode = WarpMode.ChromaticOnly;
            else
                _mode = WarpMode.None;
        }

        internal bool IsShared => _mode is WarpMode.None or WarpMode.Shared;

        internal LensPoint Apply(LensPoint point, int channel) => _mode switch
        {
            WarpMode.None => point,
            WarpMode.Shared => _green.Apply(point),
            WarpMode.PerPlane => Select(channel).Apply(point),
            WarpMode.ChromaticOnly when channel == 1 => point,
            WarpMode.ChromaticOnly => Select(channel).Apply(_green.Invert(point)),
            _ => throw new InvalidOperationException()
        };

        private CompiledWarp Select(int channel) => channel switch
        {
            0 => _red,
            1 => _green,
            2 => _blue,
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
    }

    private enum WarpMode
    {
        None,
        Shared,
        PerPlane,
        ChromaticOnly
    }

    private readonly struct LensTableWarpOperation
    {
        private readonly WarpMode _mode;
        private readonly CompiledRadialTable? _distortion;
        private readonly CompiledRadialTable? _red;
        private readonly CompiledRadialTable? _blue;
        private readonly double _distortionValueScale;
        private readonly double _centerX;
        private readonly double _centerY;
        private readonly double _width;
        private readonly double _height;

        internal LensTableWarpOperation(
            LensTableWarp warp,
            int width,
            int height,
            BaseDecodeSettings settings)
        {
            _distortion = settings.Distortion && warp.Distortion != null
                ? new CompiledRadialTable(warp.Distortion, centerValue: 0)
                : null;
            _red = settings.ChromaticAberration && warp.ChromaticAberration != null
                ? new CompiledRadialTable(
                    warp.ChromaticAberration.Scale,
                    warp.ChromaticAberration.Radii,
                    warp.ChromaticAberration.Red,
                    warp.ChromaticAberration.Red[0],
                    warp.ChromaticAberration.NativePixelsPerRadiusUnit)
                : null;
            _blue = settings.ChromaticAberration && warp.ChromaticAberration != null
                ? new CompiledRadialTable(
                    warp.ChromaticAberration.Scale,
                    warp.ChromaticAberration.Radii,
                    warp.ChromaticAberration.Blue,
                    warp.ChromaticAberration.Blue[0],
                    warp.ChromaticAberration.NativePixelsPerRadiusUnit)
                : null;
            _distortionValueScale = warp.Distortion?.ValueScale ?? 1;
            _centerX = warp.CenterX;
            _centerY = warp.CenterY;
            _width = width - 1;
            _height = height - 1;
            _mode = (_distortion, _red) switch
            {
                (null, null) => WarpMode.None,
                (not null, null) => WarpMode.Shared,
                (null, not null) => WarpMode.ChromaticOnly,
                _ => WarpMode.PerPlane
            };
        }

        internal bool IsShared => _mode is WarpMode.None or WarpMode.Shared;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal LensPoint Apply(LensPoint point, int channel)
        {
            if (_mode == WarpMode.None) return point;
            var dx = (point.X - _centerX) * _width;
            var dy = (point.Y - _centerY) * _height;
            var radius = Math.Sqrt(dx * dx + dy * dy);
            if (radius == 0) return point;

            var offset = _distortion == null
                ? 0
                : radius * _distortion.Value.GetValue(radius) *
                    _distortionValueScale;
            if (_red != null && channel != 1)
            {
                var table = channel == 0 ? _red.Value : _blue!.Value;
                offset += radius * table.GetValue(radius);
            }
            var factor = (radius + offset) / radius;
            return new LensPoint(
                _centerX + dx * factor / _width,
                _centerY + dy * factor / _height);
        }
    }

    private readonly struct CompiledWarp
    {
        private readonly LensWarpCoefficients _coefficients;
        private readonly double _centerX;
        private readonly double _centerY;
        private readonly double _inputScaleX;
        private readonly double _inputScaleY;
        private readonly double _outputScaleX;
        private readonly double _outputScaleY;

        internal CompiledWarp(
            LensWarp warp,
            LensWarpCoefficients coefficients,
            int width,
            int height)
        {
            _coefficients = coefficients;
            _centerX = warp.CenterX;
            _centerY = warp.CenterY;
            var mx = Math.Max(_centerX * (width - 1), (1 - _centerX) * (width - 1));
            var my = Math.Max(_centerY * (height - 1), (1 - _centerY) * (height - 1));
            var maximum = Math.Sqrt(mx * mx + my * my);
            _inputScaleX = maximum > 0 ? (width - 1) / maximum : 0;
            _inputScaleY = maximum > 0 ? (height - 1) / maximum : 0;
            _outputScaleX = maximum > 0 ? maximum / (width - 1) : 0;
            _outputScaleY = maximum > 0 ? maximum / (height - 1) : 0;
        }

        internal LensPoint Apply(LensPoint point)
        {
            if (_inputScaleX == 0 || _inputScaleY == 0) return point;
            var dx = (point.X - _centerX) * _inputScaleX;
            var dy = (point.Y - _centerY) * _inputScaleY;
            var r2 = dx * dx + dy * dy;
            var f = _coefficients.Kr0 + r2 * (_coefficients.Kr1 +
                r2 * (_coefficients.Kr2 + r2 * _coefficients.Kr3));
            var tx = _coefficients.Kt0 * 2 * dx * dy +
                _coefficients.Kt1 * (r2 + 2 * dx * dx);
            var ty = _coefficients.Kt1 * 2 * dx * dy +
                _coefficients.Kt0 * (r2 + 2 * dy * dy);
            return new LensPoint(
                _centerX + _outputScaleX * (f * dx + tx),
                _centerY + _outputScaleY * (f * dy + ty));
        }

        internal LensPoint Invert(LensPoint target)
        {
            var estimate = target;
            for (var iteration = 0; iteration < 8; iteration++)
            {
                var mapped = Apply(estimate);
                estimate = new LensPoint(
                    estimate.X + target.X - mapped.X,
                    estimate.Y + target.Y - mapped.Y);
            }
            return estimate;
        }
    }

    private readonly struct CompiledVignette
    {
        private readonly LensVignette _vignette;
        private readonly double _fullWidth;
        private readonly double _fullHeight;
        private readonly double _inverseMaximumSquared;

        internal CompiledVignette(
            LensVignette vignette,
            int sourceWidth,
            int sourceHeight,
            LensFrameWindow sourceWindow)
        {
            _vignette = vignette;
            _fullWidth = sourceWidth / sourceWindow.Width;
            _fullHeight = sourceHeight / sourceWindow.Height;
            var mx = Math.Max(vignette.CenterX, 1 - vignette.CenterX) * _fullWidth;
            var my = Math.Max(vignette.CenterY, 1 - vignette.CenterY) * _fullHeight;
            _inverseMaximumSquared = 1 / (mx * mx + my * my);
        }

        internal double GetGain(LensPoint point)
        {
            var dx = (point.X - _vignette.CenterX) * _fullWidth;
            var dy = (point.Y - _vignette.CenterY) * _fullHeight;
            var r2 = (dx * dx + dy * dy) * _inverseMaximumSquared;
            return 1 + r2 * (_vignette.K0 + r2 * (_vignette.K1 +
                r2 * (_vignette.K2 + r2 * (_vignette.K3 + r2 * _vignette.K4))));
        }
    }

    private readonly struct CompiledTableVignette
    {
        private readonly LensTableVignette _vignette;
        private readonly CompiledRadialTable _table;
        private readonly double _width;
        private readonly double _height;

        internal CompiledTableVignette(
            LensTableVignette vignette,
            int width,
            int height)
        {
            _vignette = vignette;
            _table = new CompiledRadialTable(vignette.Table, centerValue: 100);
            _width = width - 1;
            _height = height - 1;
        }

        internal double GetGain(LensPoint point)
        {
            var dx = (point.X - _vignette.CenterX) * _width;
            var dy = (point.Y - _vignette.CenterY) * _height;
            var radius = Math.Sqrt(dx * dx + dy * dy);
            var transmission = _table.GetValue(radius);
            return transmission > 0 ? 100 / transmission : 0;
        }
    }

    private readonly struct CompiledRadialTable
    {
        private const int LookupIntervals = 4096;
        private readonly double[] _values;
        private readonly double _toIndex;

        internal CompiledRadialTable(LensRadialTable table, double centerValue)
            : this(table.Scale, table.Radii, table.Values, centerValue,
                table.NativePixelsPerRadiusUnit) { }

        internal CompiledRadialTable(
            double scale,
            IReadOnlyList<double> radii,
            IReadOnlyList<double> values,
            double centerValue,
            double nativePixelsPerRadiusUnit)
        {
            // Convert RAF table radius units once to native visible pixels;
            // otherwise LibRaw's decode scale changes correction strength.
            var maximumRadius = nativePixelsPerRadiusUnit *
                scale * values.Count * radii[^1];
            _toIndex = maximumRadius > 0 ? LookupIntervals / maximumRadius : 0;
            _values = new double[LookupIntervals + 1];
            for (var index = 0; index <= LookupIntervals; index++)
            {
                var normalized = index / (double)LookupIntervals * radii[^1];
                _values[index] = Interpolate(
                    radii, values, normalized, centerValue);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double GetValue(double radius)
        {
            var position = Math.Clamp(radius * _toIndex, 0, LookupIntervals);
            var index = (int)position;
            if (index == LookupIntervals) return _values[index];
            var fraction = position - index;
            return _values[index] + (_values[index + 1] - _values[index]) * fraction;
        }

        private static double Interpolate(
            IReadOnlyList<double> radii,
            IReadOnlyList<double> values,
            double normalized,
            double centerValue)
        {
            if (normalized <= radii[0])
            {
                if (radii[0] == 0) return values[0];
                var fraction = Math.Clamp(normalized / radii[0], 0, 1);
                return centerValue + (values[0] - centerValue) * fraction;
            }
            for (var index = 1; index < radii.Count; index++)
            {
                if (normalized > radii[index]) continue;
                var fraction = (normalized - radii[index - 1]) /
                    (radii[index] - radii[index - 1]);
                return values[index - 1] +
                    (values[index] - values[index - 1]) * fraction;
            }
            return values[^1];
        }
    }
}

internal readonly record struct LensPoint(double X, double Y);
