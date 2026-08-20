using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditSettingsTransferDetailTests
{
    [Theory]
    [InlineData(null, 48)]
    [InlineData(0, 0)]
    [InlineData(25, 73)]
    public void CopyAndApply_PreserveNullableDetailSemantics(
        int? captureSharpen,
        int targetSharpen)
    {
        var source = new EditSettings
        {
            Detail = new DetailSettings
            {
                CaptureSharpen = captureSharpen,
                NoiseReduction = FbddMode.Full,
                ChromaNr = 61
            }
        };
        var target = new EditSettings
        {
            Detail = new DetailSettings
            {
                CaptureSharpen = targetSharpen,
                NoiseReduction = FbddMode.Light,
                ChromaNr = 12
            }
        };

        var copied = EditSettingsTransfer.CopySubset(source);
        EditSettingsTransfer.ApplySubset(copied, target);

        Assert.Equal(captureSharpen, copied.Detail.CaptureSharpen);
        Assert.Equal(captureSharpen, target.Detail.CaptureSharpen);
        Assert.Equal(FbddMode.Full, target.Detail.NoiseReduction);
        Assert.Equal(61, target.Detail.ChromaNr);
        Assert.NotSame(source.Detail, copied.Detail);
        Assert.NotSame(copied.Detail, target.Detail);
    }
}
