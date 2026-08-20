using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

internal static class AgxToneLut
{
    internal const int Length = ushort.MaxValue + 1;
    private const int CacheCapacity = 4;
    private static readonly object CacheLock = new();
    private static readonly List<CacheEntry> Cache = [];

    internal static double[] Compose(
        AgxToneParameters parameters,
        double fold)
    {
        AgxToneEngine.Validate(parameters, fold);
        var lut = new double[Length];
        var exposureGain = Math.Pow(
            2,
            parameters.ExposureEv + parameters.SourceExposureEv);
        var log2Fold = Math.Log2(fold);
        var slope = AgxToneEngine.Slope(parameters.Contrast);
        var toePower = AgxToneEngine.ToePower(parameters.Shadows);
        var shoulderPower = AgxToneEngine.ShoulderPower(parameters.Highlights);
        var workers = Math.Min(
            Environment.ProcessorCount,
            Math.Max(1, Length / 8192));
        Parallel.For(0, workers, worker =>
        {
            var start = Length * worker / workers;
            var end = Length * (worker + 1) / workers;
            for (var index = start; index < end; index++)
            {
                lut[index] = AgxToneEngine.EvaluateToneUnchecked(
                    index / (double)(Length - 1),
                    parameters,
                    exposureGain,
                    log2Fold,
                    slope,
                    toePower,
                    shoulderPower);
            }
        });
        return lut;
    }

    internal static double[] ComposeCached(
        AgxToneParameters parameters,
        double fold)
    {
        AgxToneEngine.Validate(parameters, fold);
        lock (CacheLock)
        {
            var index = Cache.FindIndex(entry => entry.Matches(parameters, fold));
            if (index >= 0)
            {
                var hit = Cache[index];
                Cache.RemoveAt(index);
                Cache.Insert(0, hit);
                return hit.Lut;
            }
        }

        var composed = Compose(parameters, fold);
        lock (CacheLock)
        {
            var existing = Cache.FindIndex(
                entry => entry.Matches(parameters, fold));
            if (existing >= 0)
            {
                return Cache[existing].Lut;
            }

            Cache.Insert(0, new CacheEntry(parameters, fold, composed));
            if (Cache.Count > CacheCapacity)
            {
                Cache.RemoveAt(Cache.Count - 1);
            }
        }
        return composed;
    }

    internal static double Interpolate(double[] lut, double value)
    {
        ArgumentNullException.ThrowIfNull(lut);
        if (lut.Length != Length)
        {
            throw new ArgumentException(
                $"Expected a {Length}-entry LUT.",
                nameof(lut));
        }
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "LUT input must be finite.");
        }
        return InterpolateUnchecked(lut, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double InterpolateUnchecked(double[] lut, double value)
    {
        var position = Math.Clamp(value, 0, 1) * (Length - 1);
        var lower = (int)position;
        if (lower >= Length - 1)
        {
            return lut[^1];
        }

        var fraction = position - lower;
        return lut[lower] + (lut[lower + 1] - lut[lower]) * fraction;
    }

    private sealed class CacheEntry
    {
        private readonly double _exposureEv;
        private readonly double _sourceExposureEv;
        private readonly int _contrast;
        private readonly int _highlights;
        private readonly int _shadows;
        private readonly double _fold;
        private readonly bool _identityCurve;
        private readonly byte[] _curve;

        internal double[] Lut { get; }

        internal CacheEntry(
            AgxToneParameters parameters,
            double fold,
            double[] lut)
        {
            _exposureEv = parameters.ExposureEv;
            _sourceExposureEv = parameters.SourceExposureEv;
            _contrast = parameters.Contrast;
            _highlights = parameters.Highlights;
            _shadows = parameters.Shadows;
            _fold = fold;
            _identityCurve = parameters.Curve.IsIdentity();
            _curve = parameters.Curve.LookupTable.ToArray();
            Lut = lut;
        }

        internal bool Matches(AgxToneParameters parameters, double fold) =>
            _exposureEv == parameters.ExposureEv &&
            _sourceExposureEv == parameters.SourceExposureEv &&
            _contrast == parameters.Contrast &&
            _highlights == parameters.Highlights &&
            _shadows == parameters.Shadows &&
            _fold == fold &&
            _identityCurve == parameters.Curve.IsIdentity() &&
            _curve.AsSpan().SequenceEqual(parameters.Curve.LookupTable);
    }
}
