using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal static class PrecisionRawCases
{
    public static void Run(
        StringBuilder payload,
        PrecisionCensusManifest manifest,
        List<string> failures)
    {
        var fullPopulation = manifest.Population("real-raw-full-frames");
        var cropPopulation = manifest.Population("real-raw-focused-crops");
        AppendPopulation(payload, "case-5-real-raw", fullPopulation);
        AppendPopulation(payload, "case-5-real-raw", cropPopulation);
        payload.AppendLine(
            "CENSUS_RAW_POLICY base=production-full-resolution " +
            "baseCropping=forbidden roiUse=measurement-window-only " +
            "detailScale=BaseImageInfo.FullWidth-by-FullHeight " +
            "retentionCap=1000000 retentionStart=0 " +
            "retentionStride=ceil-eligible-over-cap " +
            "retentionRestart=case-population-boundary " +
            "exact=clip-counts,extrema,runs,max-deltaE,N,count-below-1");
        payload.AppendLine(
            "CENSUS_POPULATION_COMPARISON real=real-raw-full-frames " +
            "synthetic=synthetic-saturation-extreme pooled=false " +
            "headline=real-only syntheticMeaning=saturation-extreme-instrument");

        var headline = new ClipTotals();
        var asShot = new ClipTotals();
        var dispersion = manifest.FullFrameAssets.ToDictionary(
            asset => asset.Id,
            _ => new PrecisionRawDispersion(),
            StringComparer.Ordinal);
        var loader = new RawBaseLoader();
        foreach (var asset in manifest.FullFrameAssets)
        {
            using var baseImage = loader.LoadFullBase(
                new ImageFile(Path.Combine(
                    GoldenTestPaths.AssetDirectory,
                    asset.FileName)),
                BaseDecodeSettings.Default,
                CancellationToken.None);
            if (baseImage == null)
            {
                failures.Add($"case-5 could not decode {asset.FileName}");
                continue;
            }
            if (baseImage.Pixels.Width != (uint)baseImage.Info.FullWidth ||
                baseImage.Pixels.Height != (uint)baseImage.Info.FullHeight)
            {
                failures.Add($"case-5 {asset.Id} did not use its full production base");
                continue;
            }

            var rois = manifest.FocusedRois
                .Where(roi => roi.AssetId == asset.Id)
                .ToArray();
            foreach (var vector in manifest.RawSettings)
            {
                var settings = CreateSettings(vector);
                var measurement = Measure(
                    baseImage,
                    settings,
                    new Region(0, 0, baseImage.Info.FullWidth, baseImage.Info.FullHeight),
                    failures,
                    $"{asset.Id}/{vector.Id}");
                headline.Add(measurement.Clips);
                dispersion[asset.Id].Add(
                    vector.Id,
                    measurement.Clips.NegativeClips,
                    measurement.Clips.ChannelSamples);
                if (vector.Id == "as-shot")
                {
                    asShot.Add(measurement.Clips);
                }
                AppendMeasurement(
                    payload,
                    fullPopulation.Id,
                    asset.Id,
                    asset.Purpose,
                    vector.Id,
                    "full-frame",
                    measurement);

                foreach (var roi in rois)
                {
                    var region = ToRegion(
                        roi,
                        baseImage.Info.FullWidth,
                        baseImage.Info.FullHeight);
                    var crop = Measure(
                        baseImage,
                        settings,
                        region,
                        failures: null,
                        gateName: string.Empty,
                        renderOnce: measurement.RenderData);
                    AppendMeasurement(
                        payload,
                        cropPopulation.Id,
                        asset.Id,
                        roi.Id,
                        vector.Id,
                        "focused-roi",
                        crop);
                }
                measurement.RenderData.Dispose();
            }
        }

        foreach (var asset in manifest.FullFrameAssets)
        {
            dispersion[asset.Id].Append(payload, asset.Id, fullPopulation.Id);
        }
        payload.Append("CENSUS_RAW_AS_SHOT case=case-5-real-raw")
            .Append(" population=").Append(fullPopulation.Id)
            .Append(" weighting=pixel-weighted-one-vector-per-asset")
            .Append(" settings=as-shot")
            .Append(" assets=").Append(manifest.FullFrameAssets.Length)
            .Append(" channelSamples=").Append(asShot.ChannelSamples)
            .Append(" negativeClips=").Append(asShot.NegativeClips)
            .Append(" negativeChannelRate=").Append(Rate(
                asShot.NegativeClips, asShot.ChannelSamples))
            .Append(" pixels=").Append(asShot.Pixels)
            .Append(" anyNegativePixels=").Append(asShot.AnyNegativePixels)
            .Append(" anyNegativePixelRate=").Append(Rate(
                asShot.AnyNegativePixels, asShot.Pixels))
            .Append(" basis=exact-full-population")
            .AppendLine();
        payload.Append("CENSUS_RAW_HEADLINE case=case-5-real-raw")
            .Append(" population=").Append(fullPopulation.Id)
            .Append(" weighting=pixel-weighted-equal-settings-cross-product")
            .Append(" settingsPerAsset=").Append(manifest.RawSettings.Length)
            .Append(" assets=").Append(manifest.FullFrameAssets.Length)
            .Append(" channelSamples=").Append(headline.ChannelSamples)
            .Append(" negativeClips=").Append(headline.NegativeClips)
            .Append(" negativeChannelRate=").Append(Rate(
                headline.NegativeClips, headline.ChannelSamples))
            .Append(" pixels=").Append(headline.Pixels)
            .Append(" anyNegativePixels=").Append(headline.AnyNegativePixels)
            .Append(" anyNegativePixelRate=").Append(Rate(
                headline.AnyNegativePixels, headline.Pixels))
            .Append(" cropsExcluded=true pooledWithSynthetic=false basis=exact-full-population")
            .AppendLine();
    }

    private static RawMeasurement Measure(
        BaseImage baseImage,
        EditSettings settings,
        Region region,
        List<string>? failures,
        string gateName,
        RawRenderData? renderOnce = null)
    {
        var ownsRender = renderOnce == null;
        var render = renderOnce ?? Render(baseImage, settings);
        if (ownsRender && failures != null &&
            !render.Reconstructed.AsSpan().SequenceEqual(render.Pipeline))
        {
            failures.Add($"case-5 reconstruction differs for {gateName}");
        }
        var matrix = CreateNormalizedMatrix(baseImage.Info, settings);
        var tone = CreateTone(baseImage.Info, settings, matrix.Fold);
        var clips = new ClipTotals();
        var pixelCount = checked(region.Width * region.Height);
        for (var y = region.Y; y < region.Y + region.Height; y++)
        for (var x = region.X; x < region.X + region.Width; x++)
        {
            var pixel = y * baseImage.Info.FullWidth + x;
            var offset = pixel * 3;
            var anyNegative = false;
            var anyClip = false;
            for (var channel = 0; channel < 3; channel++)
            {
                var value =
                    matrix.Matrix[channel, 0] * render.BaseRgb[offset] /
                        ushort.MaxValue +
                    matrix.Matrix[channel, 1] * render.BaseRgb[offset + 1] /
                        ushort.MaxValue +
                    matrix.Matrix[channel, 2] * render.BaseRgb[offset + 2] /
                        ushort.MaxValue;
                if (value < 0)
                {
                    clips.NegativeClips++;
                    clips.MaximumNegative = Math.Max(clips.MaximumNegative, -value);
                    anyNegative = true;
                    anyClip = true;
                }
                else if (value > 1)
                {
                    clips.AboveWhiteClips++;
                    clips.MaximumAbove = Math.Max(clips.MaximumAbove, value - 1);
                    anyClip = true;
                }
            }
            clips.Pixels++;
            clips.ChannelSamples += 3;
            clips.AnyNegativePixels += anyNegative ? 1 : 0;
            clips.AnyClipPixels += anyClip ? 1 : 0;
        }
        var quality = PrecisionStreamingQuality.Measure(
            pixelCount,
            regionPixel => IsQualityEligible(
                regionPixel,
                region,
                baseImage.Info.FullWidth,
                render,
                matrix.Matrix,
                tone),
            regionPixel => CalculateError(
                regionPixel,
                region,
                baseImage.Info.FullWidth,
                render,
                matrix.Matrix,
                tone));
        return new RawMeasurement(clips, quality, render, ownsRender);
    }

    private static bool IsQualityEligible(
        int regionPixel,
        Region region,
        int fullWidth,
        RawRenderData render,
        double[,] matrix,
        ToneParams tone)
    {
        var x = region.X + regionPixel % region.Width;
        var y = region.Y + regionPixel / region.Width;
        var offset = (y * fullWidth + x) * 3;
        for (var channel = 0; channel < 3; channel++)
        {
            var reference = MatrixValue(matrix, render.BaseRgb, offset, channel);
            if (reference < 0 || reference > 1 ||
                !PrecisionCensusLogic.IsUseful(
                    PrecisionOracle.EvaluateTone(reference, tone)))
            {
                return false;
            }
        }
        return true;
    }

    private static double CalculateError(
        int regionPixel,
        Region region,
        int fullWidth,
        RawRenderData render,
        double[,] matrix,
        ToneParams tone)
    {
        var x = region.X + regionPixel % region.Width;
        var y = region.Y + regionPixel / region.Width;
        var offset = (y * fullWidth + x) * 3;
        Span<double> final = stackalloc double[3];
        for (var channel = 0; channel < 3; channel++)
        {
            var reference = MatrixValue(
                matrix, render.BaseRgb, offset, channel);
            final[channel] = PrecisionOracle.EvaluateTone(reference, tone);
        }
        return PrecisionDeltaE.FromSrgb(
            render.Pipeline[offset] / (double)ushort.MaxValue,
            render.Pipeline[offset + 1] / (double)ushort.MaxValue,
            render.Pipeline[offset + 2] / (double)ushort.MaxValue,
            final[0], final[1], final[2]);
    }

    private static double MatrixValue(
        double[,] matrix,
        ushort[] baseRgb,
        int offset,
        int channel) =>
        matrix[channel, 0] * baseRgb[offset] / ushort.MaxValue +
        matrix[channel, 1] * baseRgb[offset + 1] / ushort.MaxValue +
        matrix[channel, 2] * baseRgb[offset + 2] / ushort.MaxValue;

    private static RawRenderData Render(BaseImage baseImage, EditSettings settings)
    {
        var baseRgb = ReadRgb16(baseImage.Pixels);
        using var reconstructed = (MagickImage)baseImage.Pixels.Clone();
        var fold = RenderChromaticStage.Apply(reconstructed, baseImage.Info, settings);
        ToneLutApplicator.Apply(
            reconstructed,
            ToneLut.Compose(CreateTone(baseImage.Info, settings, fold)));
        RenderColorEncoding.RetagAsSrgb(reconstructed);
        var reconstructedRgb = ReadRgb16(reconstructed);
        using var pipeline = new RenderPipeline().Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            MaxDimension: null,
            new RenderOptions(false, false)));
        return new RawRenderData(
            baseRgb,
            reconstructedRgb,
            ReadRgb16(pipeline.Image));
    }

    private static EditSettings CreateSettings(PrecisionRawSettingManifest vector) =>
        new()
        {
            Wb = vector.Kelvin is { } kelvin
                ? new WhiteBalanceSettings
                {
                    Mode = WbMode.Custom,
                    Kelvin = kelvin,
                    Tint = vector.Tint
                }
                : new WhiteBalanceSettings { Mode = WbMode.AsShot },
            BaseLook = false,
            Detail = new DetailSettings
            {
                CaptureSharpen = 0,
                NoiseReduction = FbddMode.Off,
                ChromaNr = 0
            }
        };

    private static (double[,] Matrix, double Fold) CreateNormalizedMatrix(
        BaseImageInfo info,
        EditSettings settings)
    {
        if (settings.Wb.Mode == WbMode.AsShot)
        {
            return (new[,] { { 1d, 0, 0 }, { 0, 1d, 0 }, { 0, 0, 1d } }, 1);
        }
        var matrix = WhiteBalanceModel.CreateMatrix(
            settings.Wb.Kelvin!.Value,
            settings.Wb.Tint!.Value,
            info.AsShotKelvin,
            info.AsShotTint);
        var normalized = ChromaticAdaptation.NormalizeForRender(matrix);
        return (normalized.Matrix, normalized.Fold);
    }

    private static ToneParams CreateTone(
        BaseImageInfo info,
        EditSettings settings,
        double fold) =>
        new(
            settings.Exposure + info.SourceExposureBiasEv,
            fold,
            settings.Brightness,
            settings.Contrast,
            settings.Shadows,
            settings.Highlights,
            settings.BaseLook ?? info.IsRawSource,
            settings.Curve);

    private static void AppendMeasurement(
        StringBuilder payload,
        string population,
        string asset,
        string purpose,
        string vector,
        string regionKind,
        RawMeasurement value)
    {
        var clips = value.Clips;
        payload.Append("CENSUS_RAW case=case-5-real-raw")
            .Append(" population=").Append(population)
            .Append(" asset=").Append(asset)
            .Append(" purpose=").Append(purpose)
            .Append(" settings=").Append(vector)
            .Append(" region=").Append(regionKind)
            .Append(" channelSamples=").Append(clips.ChannelSamples)
            .Append(" negativeClips=").Append(clips.NegativeClips)
            .Append(" negativeChannelRate=").Append(Rate(
                clips.NegativeClips, clips.ChannelSamples))
            .Append(" aboveWhiteClips=").Append(clips.AboveWhiteClips)
            .Append(" pixels=").Append(clips.Pixels)
            .Append(" anyNegativePixels=").Append(clips.AnyNegativePixels)
            .Append(" anyNegativePixelRate=").Append(Rate(
                clips.AnyNegativePixels, clips.Pixels))
            .Append(" anyClipPixels=").Append(clips.AnyClipPixels)
            .Append(" maxNegativeExcursion=").Append(Format(clips.MaximumNegative))
            .Append(" maxAboveWhiteExcursion=").Append(Format(clips.MaximumAbove))
            .Append(" clipBasis=exact-full-population recoveryState=available")
            .Append(" recoverable=0 indeterminate=").Append(clips.NegativeClips)
            .AppendLine();
        PrecisionEvidenceReport.AppendQuality(
            payload,
            "case-5-real-raw",
            population,
            $"post-tone/{asset}/{vector}/{regionKind}/{purpose}",
            value.Quality,
            phaseZeroThresholdCrossed: false,
            plannedStageContractLoss: false,
            indeterminateCouldBeMaterial: clips.NegativeClips > 0);
    }

    private static void AppendPopulation(
        StringBuilder payload,
        string name,
        PrecisionPopulationManifest population) =>
        payload.Append("CENSUS_POPULATION case=").Append(name)
            .Append(" id=").Append(population.Id)
            .Append(" kind=").Append(population.Kind)
            .Append(" rowSemantics=").Append(population.RowSemantics)
            .Append(" intensity=").Append(population.Intensity)
            .AppendLine();

    private static Region ToRegion(PrecisionRoiManifest roi, int width, int height)
    {
        var left = (int)Math.Round(roi.Left * width);
        var top = (int)Math.Round(roi.Top * height);
        var right = (int)Math.Round(roi.Right * width);
        var bottom = (int)Math.Round(roi.Bottom * height);
        return new Region(left, top, right - left, bottom - top);
    }

    private static ushort[] ReadRgb16(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RAW census pixels.");

    private static string Rate(long count, long denominator) =>
        (count / (double)denominator).ToString("F12", CultureInfo.InvariantCulture);
    private static string Format(double value) =>
        value.ToString("F12", CultureInfo.InvariantCulture);

    private sealed record Region(int X, int Y, int Width, int Height);
    private sealed record RawMeasurement(
        ClipTotals Clips,
        PrecisionOutputQuality Quality,
        RawRenderData RenderData,
        bool OwnsRender);

    private sealed class RawRenderData : IDisposable
    {
        public RawRenderData(ushort[] baseRgb, ushort[] reconstructed, ushort[] pipeline)
        {
            BaseRgb = baseRgb;
            Reconstructed = reconstructed;
            Pipeline = pipeline;
        }
        public ushort[] BaseRgb { get; private set; }
        public ushort[] Reconstructed { get; private set; }
        public ushort[] Pipeline { get; private set; }
        public void Dispose()
        {
            BaseRgb = [];
            Reconstructed = [];
            Pipeline = [];
        }
    }

    private sealed class ClipTotals
    {
        public long ChannelSamples;
        public long Pixels;
        public long NegativeClips;
        public long AboveWhiteClips;
        public long AnyNegativePixels;
        public long AnyClipPixels;
        public double MaximumNegative;
        public double MaximumAbove;
        public void Add(ClipTotals other)
        {
            ChannelSamples += other.ChannelSamples;
            Pixels += other.Pixels;
            NegativeClips += other.NegativeClips;
            AboveWhiteClips += other.AboveWhiteClips;
            AnyNegativePixels += other.AnyNegativePixels;
            AnyClipPixels += other.AnyClipPixels;
            MaximumNegative = Math.Max(MaximumNegative, other.MaximumNegative);
            MaximumAbove = Math.Max(MaximumAbove, other.MaximumAbove);
        }
    }

}
