using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawProfileViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("profile-vm");

    [Fact]
    public async Task SelectionResetAndUndoPreserveProfileHistoryPolicy()
    {
        using var catalog = await _fx.CreateCatalogAsync("catalog");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("image.dng"));
        vm.SelectedImage = image;
        var option = Option('a');

        await vm.SelectRawProfileAsync(option);

        Assert.Equal(option.Selection?.ContentHash,
            image.EditSettings.RawProfile?.ContentHash);
        Assert.True(image.HasEdits);
        Assert.True(vm.CanReset);

        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Null(image.EditSettings.RawProfile);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(option.Selection?.ContentHash,
            image.EditSettings.RawProfile?.ContentHash);
    }

    [Fact]
    public async Task RapidSelectionLeavesNewestProfileInstalled()
    {
        using var catalog = await _fx.CreateCatalogAsync("rapid");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("rapid.dng"));
        vm.SelectedImage = image;
        var first = Option('b');
        var second = Option('c');

        var firstTask = vm.SelectRawProfileAsync(first);
        var secondTask = vm.SelectRawProfileAsync(second);
        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(second.Selection?.ContentHash,
            image.EditSettings.RawProfile?.ContentHash);
        Assert.Equal(second.Selection?.ContentHash,
            vm.SelectedRawProfileOption?.Selection?.ContentHash);
    }

    [Fact]
    public async Task ImageSwitchKeepsBuiltInAnchorAndOpenRebuildsMenu()
    {
        using var catalog = await _fx.CreateCatalogAsync("switch");
        await using var vm = CreateViewModel(catalog);
        var first = new ImageFile(_fx.Path("first.cr2"));
        var second = new ImageFile(_fx.Path("second.nef"));

        vm.SelectedImage = first;
        AssertBuiltInAnchor(vm);
        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        AssertBuiltInAnchor(vm);

        vm.SelectedImage = second;

        AssertBuiltInAnchor(vm);
        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        AssertBuiltInAnchor(vm);
        Assert.Contains(vm.RawProfileOptions, option =>
            option.IsGroupHeader && option.Label == "BUILT-IN");
        Assert.Contains(vm.RawProfileOptions, option => option.IsChooseFile);
    }

    [Fact]
    public async Task DropdownOpenRescansSelectedFileAndShowsRejection()
    {
        using var catalog = await _fx.CreateCatalogAsync("rescan");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("rescan.cr2"));
        vm.SelectedImage = image;
        var profilePath = SyntheticDcpFactory.WriteTemporary(
            _fx.Root,
            new SyntheticDcpOptions { Name = "Chosen profile" },
            "chosen.dcp");

        await vm.AddRawProfileFileAsync(profilePath);
        Assert.Equal("Chosen profile", vm.SelectedRawProfileOption?.Label);

        File.Delete(profilePath);
        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);

        Assert.Equal(
            "THE SELECTED CAMERA PROFILE IS MISSING.",
            vm.RawProfileStatusMessage);
        Assert.False(vm.SelectedRawProfileOption?.CanSelect);
        AssertBuiltInPresent(vm);
    }

    [Fact]
    public async Task IdentityPreloadPreservesInvalidPersistedSelectionStatus()
    {
        using var catalog = await _fx.CreateCatalogAsync("preload");
        await using var vm = CreateViewModel(catalog);
        var profilePath = SyntheticDcpFactory.WriteTemporary(
            _fx.Root,
            new SyntheticDcpOptions { Name = "Changed profile" },
            "changed.dcp");
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = profilePath,
            ContentHash = new string('f', 64)
        };
        var image = new ImageFile(_fx.Path("preload.cr2"))
        {
            EditSettings = new EditSettings { RawProfile = selection }
        };
        vm.SelectedImage = image;
        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        Assert.Equal(
            "The selected camera profile has changed on disk.",
            vm.SelectedRawProfileOption?.Status);
        Assert.False(vm.SelectedRawProfileOption?.CanSelect);
        var rejection = vm.SelectedRawProfileOption?.Status;

        vm.ApplyRawProfileState(
            image,
            isRawSource: true,
            new DcpProfileState(
                "rejected",
                DcpProfileErrorCode.HashMismatch,
                "The selected camera profile has changed on disk.",
                null,
                new CameraIdentity("Canon", "EOS 6D")));
        await TestWaits.UntilAsync(() => !vm.IsRawProfileLoading);

        Assert.Equal(rejection, vm.SelectedRawProfileOption?.Status);
        Assert.False(vm.SelectedRawProfileOption?.CanSelect);
        Assert.Equal(selection.ContentHash,
            vm.SelectedRawProfileOption?.Selection?.ContentHash);
    }

    [Fact]
    public async Task IdentityPreloadPreservesEmbeddedProfileEntries()
    {
        using var catalog = await _fx.CreateCatalogAsync("embedded");
        await using var vm = CreateViewModel(catalog);
        var dngPath = SyntheticDcpFactory.WriteTemporary(
            _fx.Root,
            new SyntheticDcpOptions { Name = "Embedded profile" },
            "embedded.dng");
        var image = new ImageFile(dngPath);
        vm.SelectedImage = image;
        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        var embedded = Assert.Single(vm.RawProfileOptions, option =>
            option.Selection?.Source == RawProfileSource.Embedded);

        vm.ApplyRawProfileState(
            image,
            isRawSource: true,
            new DcpProfileState(
                string.Empty,
                DcpProfileErrorCode.None,
                null,
                null,
                new CameraIdentity("Canon", "EOS 6D")));
        await TestWaits.UntilAsync(() => !vm.IsRawProfileLoading);

        var retained = Assert.Single(vm.RawProfileOptions, option =>
            option.Selection?.Source == RawProfileSource.Embedded);
        Assert.Equal(embedded.Label, retained.Label);
        Assert.Equal(embedded.Status, retained.Status);
    }

    [Fact]
    public void PickerMenuUsesExactProfileSourceOrder()
    {
        var menu = MainWindowViewModel.BuildRawProfileMenu(
        [
            Profile("Zulu", RawProfileSource.Adobe, 'z'),
            RawProfileOptionViewModel.BuiltIn(),
            Profile("Chosen", RawProfileSource.UserFile, 'u'),
            Profile("Embedded", RawProfileSource.Embedded, 'e'),
            Profile("Alpha", RawProfileSource.Adobe, 'a')
        ]);

        Assert.Equal(
        [
            "CHOSEN FILE",
            "Chosen",
            "DNG · EMBEDDED",
            "Embedded",
            "ADOBE · CAMERAPROFILES",
            "Alpha",
            "Zulu",
            "BUILT-IN",
            RawProfileOptionViewModel.BuiltInLabel,
            "<divider>",
            "Choose .dcp file…"
        ],
            menu.Select(option => option.IsDivider ? "<divider>" : option.Label));
    }

    private MainWindowViewModel CreateViewModel(CatalogService catalog)
    {
        var vm = _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        return vm;
    }

    private RawProfileOptionViewModel Option(char hashCharacter)
    {
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = _fx.Path($"{hashCharacter}.dcp"),
            ContentHash = new string(hashCharacter, 64)
        };
        return new RawProfileOptionViewModel(new DcpProfileOption(
            $"Profile {hashCharacter}",
            selection,
            DcpProfileErrorCode.None,
            null));
    }

    private RawProfileOptionViewModel Profile(
        string name,
        RawProfileSource source,
        char hashCharacter) => new(new DcpProfileOption(
            name,
            new RawProfileSelection
            {
                Source = source,
                Location = source == RawProfileSource.Embedded
                    ? null
                    : _fx.Path($"{name}.dcp"),
                ContentHash = new string(hashCharacter, 64)
            },
            DcpProfileErrorCode.None,
            null));

    private static void AssertBuiltInAnchor(MainWindowViewModel vm)
    {
        Assert.Equal(
            RawProfileOptionViewModel.BuiltInLabel,
            vm.SelectedRawProfileOption?.Label);
        AssertBuiltInPresent(vm);
    }

    private static void AssertBuiltInPresent(MainWindowViewModel vm) =>
        Assert.Contains(vm.RawProfileOptions, option =>
            option.IsProfile &&
            option.Label == RawProfileOptionViewModel.BuiltInLabel);

    public void Dispose() => _fx.Dispose();
}
