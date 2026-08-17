using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace HappyPhoton.LibRaw.Interop.Tests;

public sealed unsafe class NativeContractTests
{
    [Fact]
    public void ManagedLayouts_MirrorBridgeHeader()
    {
        Assert.Equal(32, Unsafe.SizeOf<NativeError>());
        Assert.Equal(16, Marshal.OffsetOf<NativeError>(nameof(NativeError.Text)).ToInt32());
        Assert.Equal(152, Unsafe.SizeOf<NativeRuntimeInfo>());
        Assert.Equal(40, Unsafe.SizeOf<NativeDimensions>());
        Assert.Equal(72, Unsafe.SizeOf<NativeSensorIdentity>());
        Assert.Equal(32, Unsafe.SizeOf<NativeGpsFacts>());
        Assert.Equal(760, Unsafe.SizeOf<NativeMetadata>());
        Assert.Equal(180, Unsafe.SizeOf<NativeCameraFacts>());
        Assert.Equal(0, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.AbiVersion)));
        Assert.Equal(4, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.StructSize)));
        Assert.Equal(8, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.MultiplierCount)));
        Assert.Equal(12, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.Multipliers)));
        Assert.Equal(28, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.MatrixRows)));
        Assert.Equal(32, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.MatrixColumns)));
        Assert.Equal(36, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.CameraToSrgb)));
        Assert.Equal(84, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.PreMultiplierCount)));
        Assert.Equal(88, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.PreMultipliers)));
        Assert.Equal(104, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.CameraFromXyzRows)));
        Assert.Equal(108, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.CameraFromXyzColumns)));
        Assert.Equal(112, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.CameraFromXyz)));
        Assert.Equal(160, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.LinearMaxCount)));
        Assert.Equal(164, OffsetOf<NativeCameraFacts>(nameof(NativeCameraFacts.LinearMax)));
        Assert.Equal(32, Unsafe.SizeOf<NativeFujiFacts>());
        Assert.Equal(80, Unsafe.SizeOf<NativeOutputConfig>());
        Assert.Equal(16, Marshal.OffsetOf<NativeOutputConfig>(nameof(NativeOutputConfig.GammaPower)).ToInt32());
        Assert.Equal(56, Marshal.OffsetOf<NativeOutputConfig>(nameof(NativeOutputConfig.UserMultipliers)).ToInt32());
        Assert.Equal(56, Unsafe.SizeOf<NativeImageDescriptor>());
        Assert.Equal(48, Marshal.OffsetOf<NativeImageDescriptor>(nameof(NativeImageDescriptor.Allocation)).ToInt32());
        Assert.Equal(16496, Unsafe.SizeOf<NativeMosaicDescriptor>());
        Assert.Equal(72, Marshal.OffsetOf<NativeMosaicDescriptor>(nameof(NativeMosaicDescriptor.Cblack)).ToInt32());
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
            AbiVersion = 2,
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
            AbiVersion = 2,
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
}
