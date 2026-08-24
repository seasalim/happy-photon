using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace HappyPhoton.LibRaw.Interop.Tests;

public sealed unsafe class NativeContractTests
{
    [Fact]
    public void ManagedLayouts_MirrorBridgeHeader()
    {
        AssertLayout<NativeError>(32,
            (nameof(NativeError.AbiVersion), 0), (nameof(NativeError.StructSize), 4),
            (nameof(NativeError.ErrorClass), 8), (nameof(NativeError.NativeCode), 12),
            (nameof(NativeError.Text), 16), (nameof(NativeError.TextCapacity), 24),
            (nameof(NativeError.TextLength), 28));
        AssertLayout<NativeRuntimeInfo>(152,
            (nameof(NativeRuntimeInfo.AbiVersion), 0), (nameof(NativeRuntimeInfo.StructSize), 4),
            (nameof(NativeRuntimeInfo.LibRawVersionNumber), 8), (nameof(NativeRuntimeInfo.Capabilities), 12),
            (nameof(NativeRuntimeInfo.ThreadSafeVariant), 16), (nameof(NativeRuntimeInfo.VersionStringLength), 20),
            (nameof(NativeRuntimeInfo.VersionString), 24));
        AssertLayout<NativeDimensions>(40,
            (nameof(NativeDimensions.AbiVersion), 0), (nameof(NativeDimensions.StructSize), 4),
            (nameof(NativeDimensions.RawWidth), 8), (nameof(NativeDimensions.RawHeight), 12),
            (nameof(NativeDimensions.VisibleWidth), 16), (nameof(NativeDimensions.VisibleHeight), 20),
            (nameof(NativeDimensions.OutputWidth), 24), (nameof(NativeDimensions.OutputHeight), 28),
            (nameof(NativeDimensions.Orientation), 32), (nameof(NativeDimensions.Reserved), 36));
        AssertLayout<NativeSensorIdentity>(72,
            (nameof(NativeSensorIdentity.AbiVersion), 0), (nameof(NativeSensorIdentity.StructSize), 4),
            (nameof(NativeSensorIdentity.Colors), 8), (nameof(NativeSensorIdentity.Filters), 12),
            (nameof(NativeSensorIdentity.DngVersion), 16), (nameof(NativeSensorIdentity.XtransCount), 20),
            (nameof(NativeSensorIdentity.Xtrans), 24), (nameof(NativeSensorIdentity.CdescLength), 60),
            (nameof(NativeSensorIdentity.Cdesc), 64), (nameof(NativeSensorIdentity.Reserved), 69));
        AssertLayout<NativeGpsFacts>(32,
            (nameof(NativeGpsFacts.Parsed), 0), (nameof(NativeGpsFacts.CoordinatePresent), 4),
            (nameof(NativeGpsFacts.Latitude), 8), (nameof(NativeGpsFacts.Longitude), 16),
            (nameof(NativeGpsFacts.AltitudePresent), 24), (nameof(NativeGpsFacts.Altitude), 28));
        AssertLayout<NativeMetadata>(760,
            (nameof(NativeMetadata.AbiVersion), 0), (nameof(NativeMetadata.StructSize), 4),
            (nameof(NativeMetadata.MakeLength), 8), (nameof(NativeMetadata.Make), 12),
            (nameof(NativeMetadata.ModelLength), 140), (nameof(NativeMetadata.Model), 144),
            (nameof(NativeMetadata.NormalizedMakeLength), 272), (nameof(NativeMetadata.NormalizedMake), 276),
            (nameof(NativeMetadata.NormalizedModelLength), 404), (nameof(NativeMetadata.NormalizedModel), 408),
            (nameof(NativeMetadata.LensLength), 536), (nameof(NativeMetadata.Lens), 540),
            (nameof(NativeMetadata.IsoPresent), 668), (nameof(NativeMetadata.Iso), 672),
            (nameof(NativeMetadata.ShutterPresent), 676), (nameof(NativeMetadata.Shutter), 680),
            (nameof(NativeMetadata.AperturePresent), 684), (nameof(NativeMetadata.Aperture), 688),
            (nameof(NativeMetadata.FocalLengthPresent), 692), (nameof(NativeMetadata.FocalLength), 696),
            (nameof(NativeMetadata.FocalLength35mmPresent), 700), (nameof(NativeMetadata.FocalLength35mm), 704),
            (nameof(NativeMetadata.TimestampPresent), 708), (nameof(NativeMetadata.Timestamp), 712),
            (nameof(NativeMetadata.Orientation), 720), (nameof(NativeMetadata.Reserved), 724),
            (nameof(NativeMetadata.Gps), 728));
        AssertLayout<NativeCameraFacts>(180,
            (nameof(NativeCameraFacts.AbiVersion), 0), (nameof(NativeCameraFacts.StructSize), 4),
            (nameof(NativeCameraFacts.MultiplierCount), 8), (nameof(NativeCameraFacts.Multipliers), 12),
            (nameof(NativeCameraFacts.MatrixRows), 28), (nameof(NativeCameraFacts.MatrixColumns), 32),
            (nameof(NativeCameraFacts.CameraToSrgb), 36), (nameof(NativeCameraFacts.PreMultiplierCount), 84),
            (nameof(NativeCameraFacts.PreMultipliers), 88), (nameof(NativeCameraFacts.CameraFromXyzRows), 104),
            (nameof(NativeCameraFacts.CameraFromXyzColumns), 108), (nameof(NativeCameraFacts.CameraFromXyz), 112),
            (nameof(NativeCameraFacts.LinearMaxCount), 160), (nameof(NativeCameraFacts.LinearMax), 164));
        AssertLayout<NativeFujiFacts>(32,
            (nameof(NativeFujiFacts.AbiVersion), 0), (nameof(NativeFujiFacts.StructSize), 4),
            (nameof(NativeFujiFacts.Present), 8), (nameof(NativeFujiFacts.ExposureMidpointShift), 12),
            (nameof(NativeFujiFacts.DynamicRange), 16), (nameof(NativeFujiFacts.DynamicRangeSetting), 20),
            (nameof(NativeFujiFacts.DevelopmentDynamicRange), 24), (nameof(NativeFujiFacts.AutoDynamicRange), 28));
        AssertLayout<NativeLensIdentity>(672,
            (nameof(NativeLensIdentity.AbiVersion), 0), (nameof(NativeLensIdentity.StructSize), 4),
            (nameof(NativeLensIdentity.Present), 8), (nameof(NativeLensIdentity.Reserved), 12),
            (nameof(NativeLensIdentity.LensId), 16), (nameof(NativeLensIdentity.CameraId), 24),
            (nameof(NativeLensIdentity.TeleconverterId), 32), (nameof(NativeLensIdentity.AdapterId), 40),
            (nameof(NativeLensIdentity.AttachmentId), 48), (nameof(NativeLensIdentity.LensFormat), 56),
            (nameof(NativeLensIdentity.LensMount), 60), (nameof(NativeLensIdentity.CameraFormat), 64),
            (nameof(NativeLensIdentity.CameraMount), 68), (nameof(NativeLensIdentity.FocalType), 72),
            (nameof(NativeLensIdentity.FocalUnits), 76), (nameof(NativeLensIdentity.MinFocal), 80),
            (nameof(NativeLensIdentity.MaxFocal), 84), (nameof(NativeLensIdentity.MaxApertureAtMinFocal), 88),
            (nameof(NativeLensIdentity.MaxApertureAtMaxFocal), 92), (nameof(NativeLensIdentity.MinApertureAtMinFocal), 96),
            (nameof(NativeLensIdentity.MinApertureAtMaxFocal), 100), (nameof(NativeLensIdentity.MaxAperture), 104),
            (nameof(NativeLensIdentity.MinAperture), 108), (nameof(NativeLensIdentity.CurrentFocal), 112),
            (nameof(NativeLensIdentity.CurrentAperture), 116), (nameof(NativeLensIdentity.MaxApertureAtCurrentFocal), 120),
            (nameof(NativeLensIdentity.MinApertureAtCurrentFocal), 124), (nameof(NativeLensIdentity.MinFocusDistance), 128),
            (nameof(NativeLensIdentity.FocusRangeIndex), 132), (nameof(NativeLensIdentity.LensFStops), 136),
            (nameof(NativeLensIdentity.FocalLength35mm), 140), (nameof(NativeLensIdentity.LensLength), 144),
            (nameof(NativeLensIdentity.Lens), 148), (nameof(NativeLensIdentity.TeleconverterLength), 276),
            (nameof(NativeLensIdentity.Teleconverter), 280), (nameof(NativeLensIdentity.AdapterLength), 408),
            (nameof(NativeLensIdentity.Adapter), 412), (nameof(NativeLensIdentity.AttachmentLength), 540),
            (nameof(NativeLensIdentity.Attachment), 544));
        AssertLayout<NativeOutputConfig>(112,
            (nameof(NativeOutputConfig.AbiVersion), 0), (nameof(NativeOutputConfig.StructSize), 4),
            (nameof(NativeOutputConfig.OutputBits), 8), (nameof(NativeOutputConfig.OutputColor), 12),
            (nameof(NativeOutputConfig.GammaPower), 16), (nameof(NativeOutputConfig.GammaSlope), 24),
            (nameof(NativeOutputConfig.NoAutoBright), 32), (nameof(NativeOutputConfig.HalfSize), 36),
            (nameof(NativeOutputConfig.HighlightMode), 40), (nameof(NativeOutputConfig.FbddNoiseReduction), 44),
            (nameof(NativeOutputConfig.UseCameraWhiteBalance), 48), (nameof(NativeOutputConfig.UseAutoWhiteBalance), 52),
            (nameof(NativeOutputConfig.UserMultipliers), 56), (nameof(NativeOutputConfig.UseCameraMatrix), 72),
            (nameof(NativeOutputConfig.Reserved), 76), (nameof(NativeOutputConfig.UserSaturation), 80),
            (nameof(NativeOutputConfig.UserQualityPresent), 84), (nameof(NativeOutputConfig.UserQuality), 88),
            (nameof(NativeOutputConfig.CropBoxPresent), 92), (nameof(NativeOutputConfig.CropBox), 96));
        AssertLayout<NativeImageDescriptor>(56,
            (nameof(NativeImageDescriptor.AbiVersion), 0), (nameof(NativeImageDescriptor.StructSize), 4),
            (nameof(NativeImageDescriptor.Data), 8), (nameof(NativeImageDescriptor.ByteLength), 16),
            (nameof(NativeImageDescriptor.Width), 24), (nameof(NativeImageDescriptor.Height), 28),
            (nameof(NativeImageDescriptor.BitsPerSample), 32), (nameof(NativeImageDescriptor.Channels), 36),
            (nameof(NativeImageDescriptor.Format), 40), (nameof(NativeImageDescriptor.Reserved), 44),
            (nameof(NativeImageDescriptor.Allocation), 48));
        AssertLayout<NativeMosaicDescriptor>(16496,
            (nameof(NativeMosaicDescriptor.AbiVersion), 0), (nameof(NativeMosaicDescriptor.StructSize), 4),
            (nameof(NativeMosaicDescriptor.Data), 8), (nameof(NativeMosaicDescriptor.ByteLength), 16),
            (nameof(NativeMosaicDescriptor.RawPitch), 24), (nameof(NativeMosaicDescriptor.RawWidth), 28),
            (nameof(NativeMosaicDescriptor.RawHeight), 32), (nameof(NativeMosaicDescriptor.VisibleWidth), 36),
            (nameof(NativeMosaicDescriptor.VisibleHeight), 40), (nameof(NativeMosaicDescriptor.TopMargin), 44),
            (nameof(NativeMosaicDescriptor.LeftMargin), 48), (nameof(NativeMosaicDescriptor.Black), 52),
            (nameof(NativeMosaicDescriptor.Maximum), 56), (nameof(NativeMosaicDescriptor.CblackCount), 60),
            (nameof(NativeMosaicDescriptor.RepeatingRows), 64), (nameof(NativeMosaicDescriptor.RepeatingColumns), 68),
            (nameof(NativeMosaicDescriptor.Cblack), 72), (nameof(NativeMosaicDescriptor.Lease), 16488));
    }

    [Fact]
    public void CameraFactsConversion_PreservesDistinguishableFourChannelValues()
    {
        var native = CameraFactsWithRequiredFields(4);
        native.PreMultiplierCount = 4;
        native.CameraFromXyzRows = 4;
        native.CameraFromXyzColumns = 3;
        native.LinearMaxCount = 4;
        for (var channel = 0; channel < 4; channel++)
        {
            native.PreMultipliers[channel] = 20 + channel;
            native.LinearMax[channel] = (uint)(300 + channel);
            for (var column = 0; column < 3; column++)
                native.CameraFromXyz[channel * 3 + column] = 100 * channel + column;
        }

        var facts = LibRawContext.ConvertCameraFacts((NativeStatus.Ok, native));

        Assert.NotNull(facts);
        Assert.Equal([10f, 11f, 12f, 13f], facts!.Multipliers);
        Assert.Equal(Enumerable.Range(0, 12).Select(index => (float)index),
            facts.CameraToSrgb.Cast<float>());
        Assert.Equal([20f, 21f, 22f, 23f], facts.PreMultipliers!);
        Assert.Equal(
            new float[] { 0, 1, 2, 100, 101, 102, 200, 201, 202, 300, 301, 302 },
            facts.CameraFromXyz!.Cast<float>());
        Assert.Equal(new uint[] { 300, 301, 302, 303 }, facts.LinearMax!);
    }

    [Fact]
    public void LensIdentityConversion_PreservesGenericFactsAndText()
    {
        var native = new NativeLensIdentity
        {
            AbiVersion = LibRawOutputConfiguration.Version,
            StructSize = (uint)Unsafe.SizeOf<NativeLensIdentity>(),
            Present = 1,
            LensId = 0x0123456789ABCDEF,
            CameraId = 2,
            TeleconverterId = 3,
            AdapterId = 4,
            AttachmentId = 5,
            LensFormat = 6,
            LensMount = 7,
            CameraFormat = 8,
            CameraMount = 9,
            MinFocal = 24,
            MaxFocal = 70
        };
        native.LensLength = 4;
        "Lens"u8.CopyTo(new Span<byte>(native.Lens, 4));

        var facts = LibRawContext.ConvertLensIdentity((NativeStatus.Ok, native));

        Assert.NotNull(facts);
        Assert.Equal(0x0123456789ABCDEFul, facts!.LensId);
        Assert.Equal("Lens", facts.Lens);
        Assert.Equal((6u, 7u, 8u, 9u),
            (facts.LensFormat, facts.LensMount, facts.CameraFormat, facts.CameraMount));
        Assert.Equal((24f, 70f), (facts.MinFocal, facts.MaxFocal));
        Assert.Null(LibRawContext.ConvertLensIdentity(
            (NativeStatus.Absent, default)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CameraFactsConversion_OptionalAbsenceDoesNotSuppressSiblings(int absentFact)
    {
        var native = CameraFactsWithRequiredFields(3);
        native.PreMultiplierCount = absentFact == 0 ? 0u : 3u;
        native.CameraFromXyzRows = absentFact == 1 ? 0u : 3u;
        native.CameraFromXyzColumns = absentFact == 1 ? 0u : 3u;
        native.LinearMaxCount = absentFact == 2 ? 0u : 3u;

        var facts = LibRawContext.ConvertCameraFacts((NativeStatus.Ok, native));

        Assert.NotNull(facts);
        Assert.Equal(absentFact == 0, facts!.PreMultipliers is null);
        Assert.Equal(absentFact == 1, facts.CameraFromXyz is null);
        Assert.Equal(absentFact == 2, facts.LinearMax is null);
    }

    [Theory]
    [InlineData((int)NativeErrorClass.LibRaw, typeof(LibRawDecodeException))]
    [InlineData((int)NativeErrorClass.Abi, typeof(LibRawDeploymentException))]
    [InlineData((int)NativeErrorClass.Programming, typeof(LibRawProgrammingException))]
    [InlineData((int)NativeErrorClass.Bridge, typeof(LibRawBridgeException))]
    public void ErrorClasses_MapIndependently(int errorClass, Type expected)
    {
        var exception = Record.Exception(() => NativeErrorMapper.Throw(-1,
            (NativeErrorClass)errorClass, -42, "failure"u8));
        Assert.IsType(expected, exception);
        if (exception is LibRawDecodeException decode)
        {
            Assert.Equal(-42, decode.NativeCode);
            Assert.Equal("failure", decode.NativeText);
        }
    }

    [Fact]
    public void ImageDescriptor_RejectsImpossibleLengthBeforeCopy()
    {
        var value = new NativeImageDescriptor
        {
            AbiVersion = LibRawOutputConfiguration.Version,
            StructSize = (uint)Unsafe.SizeOf<NativeImageDescriptor>(),
            Data = (byte*)1,
            ByteLength = 11,
            Width = 2,
            Height = 2,
            BitsPerSample = 8,
            Channels = 3,
            Format = 2,
            Allocation = 1
        };

        Assert.Throws<InvalidDataException>(() => LibRawImage.FromNative(value, true));
    }

    [Fact]
    public void CancellationSeam_StopsBetweenNativeCalls()
    {
        using var cancellation = new CancellationTokenSource();
        var observer = new CancelObserver(cancellation);

        Assert.Throws<OperationCanceledException>(() =>
            NativeCallCoordinator.Before(cancellation.Token, observer));
        Assert.Equal(1, observer.CallCount);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(16, 16)]
    [InlineData(24, 16)]
    public void OpenMpDefault_BoundsDecoderScratchWorkers(
        int processorCount,
        int expected)
    {
        Assert.Equal(expected,
            NativeLibraryResolver.GetDefaultOpenMpThreadCount(processorCount));
    }

    private sealed class CancelObserver(CancellationTokenSource cancellation) : INativeCallObserver
    {
        public int CallCount { get; private set; }
        public void BeforeNativeCall()
        {
            CallCount++;
            cancellation.Cancel();
        }
    }

    private static NativeCameraFacts CameraFactsWithRequiredFields(uint channelCount)
    {
        var native = new NativeCameraFacts
        {
            AbiVersion = LibRawOutputConfiguration.Version,
            StructSize = (uint)Unsafe.SizeOf<NativeCameraFacts>(),
            MultiplierCount = channelCount,
            MatrixRows = 3,
            MatrixColumns = channelCount
        };
        for (var channel = 0; channel < channelCount; channel++)
            native.Multipliers[channel] = 10 + channel;
        for (var index = 0; index < 12; index++)
            native.CameraToSrgb[index] = index;
        return native;
    }

    private static int OffsetOf<T>(string field) where T : struct =>
        Marshal.OffsetOf<T>(field).ToInt32();

    private static void AssertLayout<T>(int size, params (string Field, int Offset)[] fields)
        where T : unmanaged
    {
        Assert.Equal(size, Unsafe.SizeOf<T>());
        foreach (var field in fields) Assert.Equal(field.Offset, OffsetOf<T>(field.Field));
    }
}
