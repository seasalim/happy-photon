using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

internal static class AgxToneLut
{
    internal const int Length = ushort.MaxValue + 1;
    private const int CacheCapacity = 4;
    private static readonly object CacheLock = new();
    private static readonly List<CacheEntry> Cache = [];

    internal static ToneLuts Compose(
        AgxToneParameters parameters,
        double fold)
    {
        AgxToneEngine.Validate(parameters, fold);
        return ChannelCurveLutComposer.Compose(
            parameters.CurveRed,
            parameters.CurveGreen,
            parameters.CurveBlue,
            channelCurve => Compose(parameters, fold, channelCurve));
    }

    private static double[] Compose(
        AgxToneParameters parameters,
        double fold,
        HappyPhoton.Models.CurveData? channelCurve)
    {
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
                    shoulderPower,
                    channelCurve);
            }
        });
        return lut;
    }

    internal static ToneLuts ComposeCached(
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

    // Resting-path variant: identical composition to ComposeCached (the LUT
    // build is milliseconds, so no worker cap is needed), with a cancellation
    // check before the cache insert.
    internal static ToneLuts ComposeCached(
        AgxToneParameters parameters,
        double fold,
        RenderExecutionOptions execution)
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
        execution.ThrowIfCancellationRequested();
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

        private readonly CurveKey _red;
        private readonly CurveKey _green;
        private readonly CurveKey _blue;

        internal ToneLuts Lut { get; }

        internal CacheEntry(
            AgxToneParameters parameters,
            double fold,
            ToneLuts lut)
        {
            _exposureEv = parameters.ExposureEv;
            _sourceExposureEv = parameters.SourceExposureEv;
            _contrast = parameters.Contrast;
            _highlights = parameters.Highlights;
            _shadows = parameters.Shadows;
            _fold = fold;
            _identityCurve = parameters.Curve.IsIdentity();
            _curve = parameters.Curve.LookupTable.ToArray();
            _red = new CurveKey(parameters.CurveRed);
            _green = new CurveKey(parameters.CurveGreen);
            _blue = new CurveKey(parameters.CurveBlue);
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
            _curve.AsSpan().SequenceEqual(parameters.Curve.LookupTable) &&
            _red.Matches(parameters.CurveRed) &&
            _green.Matches(parameters.CurveGreen) &&
            _blue.Matches(parameters.CurveBlue);
    }

    private sealed class CurveKey
    {
        private readonly bool _present;
        private readonly bool _identity;
        private readonly byte[] _lookupTable;

        internal CurveKey(HappyPhoton.Models.CurveData? curve)
        {
            _present = curve != null;
            _identity = curve?.IsIdentity() ?? true;
            _lookupTable = curve?.LookupTable.ToArray() ?? [];
        }

        internal bool Matches(HappyPhoton.Models.CurveData? curve) =>
            _present == (curve != null) &&
            _identity == (curve?.IsIdentity() ?? true) &&
            (curve == null ||
                _lookupTable.AsSpan().SequenceEqual(curve.LookupTable));
    }
}
