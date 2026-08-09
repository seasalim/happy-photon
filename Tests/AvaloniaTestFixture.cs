using System.Runtime.CompilerServices;
using Xunit;

[assembly: AssemblyFixture(
    typeof(HappyPhoton.Tests.AvaloniaPlatformAssemblyFixture))]

namespace HappyPhoton.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "The platform bitmap integration test requires Windows WIC.";
        }
    }
}

public static class AvaloniaTestCollection
{
    public const string Name = "Avalonia platform";
}

[CollectionDefinition(AvaloniaTestCollection.Name, DisableParallelization = true)]
public sealed class AvaloniaTestCollectionDefinition : ICollectionFixture<AvaloniaTestFixture>
{
}

public sealed class AvaloniaTestFixture
{
    public void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(
                "The platform bitmap integration test requires Windows WIC.");
        }
    }
}

public sealed class AvaloniaPlatformAssemblyFixture
{
    public AvaloniaPlatformAssemblyFixture()
    {
        if (OperatingSystem.IsWindows())
        {
            HappyPhoton.Program.BuildAvaloniaApp().SetupWithoutStarting();
        }
    }
}
