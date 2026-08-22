using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
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

    [AvaloniaFact]
    public async Task ImageSwitchRendersNeutralStatusUntilDiscoveryCompletes()
    {
        using var catalog = await _fx.CreateCatalogAsync("switch-status");
        await using var vm = _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        var picker = new RawProfilePicker { DataContext = vm };
        var window = new Window { Width = 260, Height = 180, Content = picker };
        window.Show();

        try
        {
            vm.SelectedImage = new ImageFile(_fx.Path("first.dng"));
            vm.SelectedImage = new ImageFile(_fx.Path("second.dng"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                RawProfilePickerProjector.AwaitingIdentityMessage,
                picker.FindControl<TextBlock>("RawProfileStatusText")!.Text);
            Assert.NotEqual(
                RawProfilePickerProjector.NoProfilesMessage,
                picker.FindControl<TextBlock>("RawProfileStatusText")!.Text);
        }
        finally
        {
            window.Close();
            picker.DataContext = null;
        }
    }

    [AvaloniaFact]
    public async Task UserFileShowsDeclaredBodyAndClosedRowTooltip()
    {
        using var catalog = await _fx.CreateCatalogAsync("declared-body");
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
            Location = _fx.Path("hand-picked.dcp"),
            ContentHash = new string('b', 64)
        };
        var option = new RawProfileOptionViewModel(new DcpProfileOption(
            "Hand-picked",
            selection,
            DcpProfileErrorCode.None,
            null)
        {
            DeclaredCameraModel = "  Canon EOS 6D  "
        });
        var picker = new RawProfilePicker { DataContext = vm };
        var window = new Window { Width = 260, Height = 180, Content = picker };
        window.Show();

        try
        {
            vm.RawProfilePickerState = new RawProfilePickerState(
                IsVisible: true,
                IsLoading: false,
                MainWindowViewModel.BuildRawProfileMenu(
                    [option, RawProfileOptionViewModel.BuiltIn()])
                    .ToImmutableArray(),
                option,
                "RAW CAMERA · 1 PROFILE");
            Dispatcher.UIThread.RunJobs();
            var comboBox = picker.FindControl<ComboBox>(
                "RawProfileComboBox")!;
            comboBox.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(
                comboBox.GetLogicalDescendants().OfType<TextBlock>(),
                text => text.IsVisible && text.Text == "Canon EOS 6D");
            Assert.Equal(
                "Hand-picked · Canon EOS 6D · Chosen file",
                ToolTip.GetTip(comboBox));
        }
        finally
        {
            window.Close();
            picker.DataContext = null;
        }
    }

    public void Dispose() => _fx.Dispose();
}
