using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawProfilePickerControlTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("profile-control");

    [AvaloniaFact]
    public async Task SnapshotReplacementRestoresSelectionWithoutTransition()
    {
        using var catalog = await _fx.CreateCatalogAsync("catalog");
        await using var vm = _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = _fx.Path("selected.dcp"),
            ContentHash = new string('a', 64)
        };
        var image = new ImageFile(_fx.Path("image.dng"))
        {
            EditSettings = new EditSettings { RawProfile = selection }
        };
        vm.SelectedImage = image;
        var picker = new RawProfilePicker { DataContext = vm };
        var window = new Window { Width = 260, Height = 180, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var replacement = new RawProfileOptionViewModel(
                new DcpProfileOption(
                    "Replacement label",
                    selection,
                    DcpProfileErrorCode.None,
                    null));
            var menu = MainWindowViewModel.BuildRawProfileMenu(
                [replacement, RawProfileOptionViewModel.BuiltIn()]);
            var generation = vm.LatestPreviewOutcomeGeneration;

            vm.RawProfilePickerState = new RawProfilePickerState(
                IsVisible: true,
                IsLoading: false,
                menu.ToImmutableArray(),
                replacement,
                "RAW CAMERA · 1 PROFILE");
            Dispatcher.UIThread.RunJobs();

            var comboBox = picker.FindControl<ComboBox>(
                "RawProfileComboBox")!;
            Assert.Same(replacement, comboBox.SelectedItem);
            Assert.Equal(generation, vm.LatestPreviewOutcomeGeneration);
            Assert.False(vm.CanUndo);
            Assert.Equal(
                selection.ContentHash,
                image.EditSettings.RawProfile?.ContentHash);
        }
        finally
        {
            window.Close();
            picker.DataContext = null;
        }
    }

    public void Dispose() => _fx.Dispose();
}
