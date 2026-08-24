using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LensfunPrescriptionReaderTests
{
    [Theory]
    [InlineData("Camera Co Ltd", "Camera Co", "Model One")]
    [InlineData("Camera Co", "Camera Co Ltd", "Camera Co Model One")]
    public void MatcherAllowsCameraMakerPrefixOnEitherIdentity(
        string databaseMaker,
        string suppliedMaker,
        string suppliedModel)
    {
        WriteDatabase(Lens("Exact Lens", "Mount A"), cameraMaker: databaseMaker);
        var database = new LensfunDatabase(_directory);

        var match = database.Resolve(
            suppliedMaker, suppliedModel, "Exact Lens", 35, 4, 6000, 4000);

        Assert.Equal("Exact Lens", match?.LensName);
    }

    [Fact]
    public void MatcherRejectsAmbiguousCameraMakerPrefixEquivalence()
    {
        WriteDatabase(
            Lens("Exact Lens", "Mount A"),
            secondCamera: true,
            cameraMaker: "Camera Co Ltd",
            secondCameraMaker: "Camera Co");
        var database = new LensfunDatabase(_directory);

        Assert.Null(database.Resolve(
            "Camera Co", "Model One", "Exact Lens", 35, 4, 6000, 4000));
    }
}
