using Avalonia.Headless.XUnit;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncTransferParityViewModelTestsLocals(ITestOutputHelper output)
{
    [AvaloniaFact]
    public Task G1_Locals() => SyncTransferParityReplay.ReplayDestinationAsync(output, "locals");
}

public sealed class SyncTransferParityViewModelTestsGeometry(ITestOutputHelper output)
{
    [AvaloniaFact]
    public Task G1_Geometry() => SyncTransferParityReplay.ReplayDestinationAsync(output, "geometry");
}

public sealed class SyncTransferParityViewModelTestsRawProfile(ITestOutputHelper output)
{
    [AvaloniaFact]
    public Task G1_RawProfile() => SyncTransferParityReplay.ReplayDestinationAsync(output, "raw-profile");
}

public sealed class SyncTransferParityViewModelTestsPreset(ITestOutputHelper output)
{
    [AvaloniaFact]
    public Task G1_Preset() => SyncTransferParityReplay.ReplayDestinationAsync(output, "preset");
}

public sealed class SyncTransferParityViewModelTestsDeletedPreset(ITestOutputHelper output)
{
    [AvaloniaFact]
    public Task G1_DeletedPreset() => SyncTransferParityReplay.ReplayDestinationAsync(output, "deleted-preset");
}
