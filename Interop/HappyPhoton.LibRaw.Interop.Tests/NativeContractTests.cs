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
        Assert.Equal(84, Unsafe.SizeOf<NativeCameraFacts>());
        Assert.Equal(32, Unsafe.SizeOf<NativeFujiFacts>());
        Assert.Equal(80, Unsafe.SizeOf<NativeOutputConfig>());
        Assert.Equal(16, Marshal.OffsetOf<NativeOutputConfig>(nameof(NativeOutputConfig.GammaPower)).ToInt32());
        Assert.Equal(56, Marshal.OffsetOf<NativeOutputConfig>(nameof(NativeOutputConfig.UserMultipliers)).ToInt32());
        Assert.Equal(56, Unsafe.SizeOf<NativeImageDescriptor>());
        Assert.Equal(48, Marshal.OffsetOf<NativeImageDescriptor>(nameof(NativeImageDescriptor.Allocation)).ToInt32());
        Assert.Equal(16496, Unsafe.SizeOf<NativeMosaicDescriptor>());
        Assert.Equal(72, Marshal.OffsetOf<NativeMosaicDescriptor>(nameof(NativeMosaicDescriptor.Cblack)).ToInt32());
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
            AbiVersion = 1,
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
}
