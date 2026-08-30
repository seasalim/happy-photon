using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

/// <summary>
/// Run 217 gate 3 reference: how many catalog history transactions a plain jump and
/// an edit-after-jump open today. The trim must open exactly one, like these.
/// </summary>
public sealed class EditHistoryWriteGateBaselineTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("history-write-gate");

    [Fact]
    public async Task JumpAndEditAfterJumpEachOpenOneHistoryTransaction()
    {
        using var catalog = await _fixture.CreateCatalogAsync("write-gate");
        var image = await SeedAsync(catalog, "gate.jpg", 3);
        var entries = 0;
        catalog.EditHistoryWriteGateAsync = () =>
        {
            entries++;
            return Task.CompletedTask;
        };
        await using var vm = CreateViewModel(catalog);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);

        var middle = vm.HistoryEntries.Single(entry => entry.Label == "Exposure +1.00");
        await vm.JumpToHistoryStepCommand.ExecuteAsync(middle);
        var jumpEntries = entries;

        image.EditSettings.Exposure = 5;
        await InvokeHistorySave(vm, image);
        var editEntries = entries - jumpEntries;

        Assert.Equal(1, jumpEntries);
        Assert.Equal(1, editEntries);
        var state = await catalog.LoadEditHistoryAsync(image.CatalogId);
        Assert.Equal(["Original", "Exposure +1.00", "Exposure +5.00 (+4.00)"],
            state.Entries.Select(entry => entry.Label));
        Assert.Equal(2, state.Position);
    }

    private MainWindowViewModel CreateViewModel(CatalogService catalog)
    {
        var vm = _fixture.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        return vm;
    }

    private async Task<ImageFile> SeedAsync(
        CatalogService catalog,
        string name,
        int exposure)
    {
        var current = new EditSettings { Exposure = exposure };
        var image = new ImageFile(_fixture.Path(name)) { EditSettings = current };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        var entries = Enumerable.Range(0, exposure + 1)
            .Select(index => new CatalogEditHistoryEntry(
                index,
                index == 0 ? "Original" : $"Exposure +{index}.00",
                new EditSettings { Exposure = index }))
            .ToArray();
        await catalog.SaveEditSettingsWithHistoryAsync(
            image.CatalogId,
            current,
            new CatalogEditHistoryMutation(-1, entries, exposure));
        return image;
    }

    private static Task InvokeHistorySave(MainWindowViewModel vm, ImageFile image)
    {
        var save = typeof(MainWindowViewModel).GetMethod(
            "SaveEditSettingsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(ImageFile)],
            null)!;
        return (Task)save.Invoke(vm, [image])!;
    }

    public void Dispose() => _fixture.Dispose();
}
