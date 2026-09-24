using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawProfileDiscoveryLifetimeTests
{
    [Fact]
    public async Task InvalidatedDiscoveryRemovesItsSourceBeforeDisposingIt()
    {
        using var fixture = new CatalogVmFixture("discovery-lifetime");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(fixture.Path("image.cr2"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.DcpDiscovery.DiscoveryGateAsync = () =>
        {
            entered.TrySetResult();
            return release.Task;
        };
        var discovery = vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            // Model Supersede's preemption after its generation increment, before
            // exchanging the CTS. No production-only scheduling hook is needed.
            var generation = typeof(MainWindowViewModel).GetField("_rawProfileDiscoveryGeneration",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            generation.SetValue(vm, (long)generation.GetValue(vm)! + 1);
            release.SetResult();
            await discovery.WaitAsync(TestWaits.Condition);
            // Old finally left a disposed CTS in the field; Reset then threw from Cancel.
            vm.ResetRawProfilePicker(vm.SelectedImage);
            var source = typeof(MainWindowViewModel).GetField("_rawProfilePickerCts",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Null(source.GetValue(vm));
        }
        finally
        {
            release.TrySetResult();
            await discovery.WaitAsync(TestWaits.Condition);
        }
    }
}
