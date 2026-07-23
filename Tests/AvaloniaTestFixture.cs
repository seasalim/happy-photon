using Xunit;
using Xunit.Sdk;

namespace HappyPhoton.Tests;

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
    public AvaloniaTestFixture()
    {
        if (OperatingSystem.IsWindows())
        {
            HappyPhoton.Program.BuildAvaloniaApp().SetupWithoutStarting();
        }
    }

    public void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("The platform bitmap integration test requires Windows WIC.");
        }
    }
}
