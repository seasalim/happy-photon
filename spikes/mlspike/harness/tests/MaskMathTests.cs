using HappyPhoton.MlSpike;
using Xunit;

namespace MlSpike.Tests;

public sealed class MaskMathTests
{
    private static ModelConfig Config => new()
    {
        Candidate = "synthetic", Capability = "subject", ModelSha256 = new string('0', 64),
        InputName = "image", OutputName = "mask", Width = 2, Height = 1,
        Mean = [0, 0, 0], Std = [1, 1, 1]
    };

    [Fact]
    public void PreprocessReordersBgraIntoPlanarRgbWithoutAlpha()
    {
        var image = new PreviewInput([0, 0, 255, 0, 255, 128, 0, 255], 2, 1);
        Assert.Equal(new float[] { 1, 0, 0, 128 / 255f, 0, 1 }, MaskMath.Preprocess(image, Config));
    }

    [Fact]
    public void PreprocessNormalizesAndSupportsNhwc()
    {
        var image = new PreviewInput([0, 0, 255, 255, 255, 0, 0, 255], 2, 1);
        var config = Config with { InputLayout = "NHWC", Mean = [0.5f, 0, 0.5f], Std = [0.5f, 1, 0.5f] };
        Assert.Equal(new float[] { 1, 0, -1, -1, 0, 1 }, MaskMath.Preprocess(image, config));
    }

    [Fact]
    public void PreprocessUsesHalfPixelBilinearSampling()
    {
        var image = new PreviewInput([0, 0, 0, 255, 255, 255, 255, 255], 2, 1);
        var actual = MaskMath.Preprocess(image, Config with { Width = 1 });
        Assert.Equal(new float[] { 0.5f, 0.5f, 0.5f }, actual);
    }

    [Fact]
    public void MaskResizeInterpolatesBeforeApplyingInclusiveThreshold()
    {
        Assert.Equal(new byte[] { 0, 255, 255 }, MaskMath.ResizeMask([0, 1], 2, 1, 3, 1));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, MaskMath.ResizeMask([0.5f], 1, 1, 2, 2));
    }

    [Fact]
    public void ResizeUsesBothDimensionsAndClampsBorders()
    {
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 255, 255 },
            MaskMath.ResizeMask([0, 1], 2, 1, 4, 2));
        Assert.Equal(new byte[] { 0, 0, 255, 255 },
            MaskMath.ResizeMask([0, 1], 1, 2, 1, 4));
    }

    [Fact]
    public void GroundTruthUsesNearestNeighbor()
    {
        Assert.Equal(new byte[] { 0, 255, 255 },
            MaskMath.ResizeLabels([0, 255], 2, 1, 3, 1));
    }

    [Fact]
    public void IoUCountsIntersectionOverUnionAndDefinesEmptyAsOne()
    {
        Assert.Equal(1.0 / 3, MaskMath.IoU([255, 255, 0, 0], [0, 255, 255, 0]), 12);
        Assert.Equal(1, MaskMath.IoU([0, 0], [0, 0]));
        Assert.Equal(0, MaskMath.IoU([255, 0], [0, 255]));
        Assert.Equal(0.5, MaskMath.Difference([0, 255], [255, 255]));
    }

    [Fact]
    public void SoftmaxSelectsClassWithStableLargeLogitsInEitherLayout()
    {
        var config = Config with { Activation = "softmax", ClassIndex = 1 };
        var probabilities = MaskMath.Probabilities([1000, 1001, 1001, 1000], [1, 2, 1, 2], config);
        Assert.InRange(probabilities[0], 0.731f, 0.732f);
        Assert.InRange(probabilities[1], 0.268f, 0.270f);
        Assert.Equal(probabilities, MaskMath.Probabilities(
            [1000, 1001, 1001, 1000], [1, 1, 2, 2], config with { OutputLayout = "NHWC" }));
    }

    [Fact]
    public void SigmoidDoesNotNormalizeMaskPerImage()
    {
        Assert.Equal(new float[] { 0.5f, 1 },
            MaskMath.Probabilities([0, 1000], [1, 1, 1, 2], Config));
    }

    [Fact]
    public void InvalidInputsFailInsteadOfProducingPlausibleMasks()
    {
        Assert.Throws<ArgumentException>(() => MaskMath.ResizeMask([float.NaN], 1, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => MaskMath.IoU([1], [0]));
        Assert.Throws<ArgumentException>(() => MaskMath.IoU([0], [0, 0]));
        Assert.Throws<InvalidDataException>(() => MaskMath.Probabilities([1], [1, 1, 1, 1],
            Config with { ClassIndex = 1 }));
        Assert.Throws<InvalidDataException>(() => MaskMath.Probabilities([2], [1, 1, 1, 1],
            Config with { Activation = "probability" }));
        Assert.Throws<InvalidDataException>(() => (Config with { Std = [0, 1, 1] }).Validate());
    }
}
