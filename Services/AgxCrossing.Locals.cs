namespace HappyPhoton.Services;

internal sealed partial class AgxCrossing
{
    private void ApplyLocals(ushort[] values, int count, int channels, int red, int green,
        int blue, RenderExecutionOptions? execution)
    {
        var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, (count + 32767) / 32768));
        workers = execution?.CapWorkers(workers) ?? workers;
        var exposure = Math.Pow(2, _parameters.ExposureEv + _parameters.SourceExposureEv);
        var toeScale = AgxToneEngine.TailScale(AgxToneEngine.XPivot,
            AgxToneEngine.YPivot, _slope, _toePower);
        var shoulderScale = AgxToneEngine.TailScale(1 - AgxToneEngine.XPivot,
            1 - AgxToneEngine.YPivot, _slope, _shoulderPower);
        var hasColor = _locals!.HasColor;
        var inset = new Matrix3x3(AgxToneEngine.InsetMatrix);
        var masterIdentity = _parameters.Curve.IsIdentity();
        var redIdentity = masterIdentity && (_parameters.CurveRed?.IsIdentity() ?? true);
        var greenIdentity = masterIdentity && (_parameters.CurveGreen?.IsIdentity() ?? true);
        var blueIdentity = masterIdentity && (_parameters.CurveBlue?.IsIdentity() ?? true);
        Parallel.For(0, workers, execution?.ParallelOptions ?? new ParallelOptions(), worker =>
        {
            var end = count * (worker + 1) / workers;
            for (var pixel = count * worker / workers; pixel < end; pixel++)
            {
                if ((pixel & 8191) == 0) execution?.ThrowIfCancellationRequested();
                var gain = hasColor ? 1 : _locals!.Gain(pixel);
                var offset = pixel * channels;
                var r = values[offset + red] * Q16ToUnit;
                var g = values[offset + green] * Q16ToUnit;
                var b = values[offset + blue] * Q16ToUnit;
                var ir = _input.Row0(r, g, b);
                var ig = _input.Row1(r, g, b);
                var ib = _input.Row2(r, g, b);
                var adjusted = gain != 1;
                if (hasColor)
                {
                    var cr = _localWhiteBalance.Row0(r, g, b);
                    var cg = _localWhiteBalance.Row1(r, g, b);
                    var cb = _localWhiteBalance.Row2(r, g, b);
                    if (_locals!.ApplyColor(pixel, ref cr, ref cg, ref cb))
                    {
                        ir = inset.Row0(cr, cg, cb); ig = inset.Row1(cr, cg, cb); ib = inset.Row2(cr, cg, cb);
                        adjusted = true;
                    }
                }
                double tr, tg, tb;
                if (!adjusted)
                {
                    tr = AgxToneLut.InterpolateUnchecked(_luts.Red, Clamp01(ir));
                    tg = AgxToneLut.InterpolateUnchecked(_luts.Green, Clamp01(ig));
                    tb = AgxToneLut.InterpolateUnchecked(_luts.Blue, Clamp01(ib));
                }
                else
                {
                    tr = Tone(ir * gain, _parameters.CurveRed, redIdentity);
                    tg = Tone(ig * gain, _parameters.CurveGreen, greenIdentity);
                    tb = Tone(ib * gain, _parameters.CurveBlue, blueIdentity);
                }
                values[offset + red] = EncodeQ16(_outset.Row0(tr, tg, tb));
                values[offset + green] = EncodeQ16(_outset.Row1(tr, tg, tb));
                values[offset + blue] = EncodeQ16(_outset.Row2(tr, tg, tb));
            }
        });
        double Tone(double value, HappyPhoton.Models.CurveData? channel, bool identity) =>
            AgxToneEngine.EvaluateToneExtendedUnchecked(value, _parameters, exposure,
                _log2Fold, _slope, _toePower, _shoulderPower, channel,
                toeScale, shoulderScale, identity);
    }
}
