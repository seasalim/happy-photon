using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncProfileDevelopTests
{
    [Trait("Category", "Quarantined")]
    [AvaloniaFact]
    public async Task CopyLoadedIdentitySurvivesNavigationAndUnavailableSourceWithNoSourceOpens()
    {
        var loader = new ProfileLoader();
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync(loader: loader, availability: availability);
        var source = await fixture.ImageAsync("source", Settings());
        var opens = new List<string>();
        fixture.Vm.PasteFrameReader.Opening = (path, _) => opens.Add(path);
        await fixture.CopyAsync(source);
        Assert.Empty(opens);
        var copiedHash = source.EditSettings.RawProfile!.ContentHash;
        // Sources are deliberately absent on disk; only the source-matched loaded base supplies identity.
        source.EditSettings.RawProfile.ContentHash = new string('c', 64);
        var target = await fixture.ImageAsync("target", Settings('b'));
        await fixture.SelectAsync(target);
        availability.Resolver = path => path == source.FilePath
            ? SourceAvailability.RequiresHydration : SourceAvailability.AvailableLocally;
        Choose(fixture, "Camera Profile");
        var before = loader.Decodes;
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(copiedHash, target.EditSettings.RawProfile!.ContentHash);
        Assert.Empty(opens);
        Assert.Equal(before + 1, loader.Decodes);
        Assert.Contains("replaced Camera Profile", fixture.Vm.TransientStatus);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LensOnlyDuringCropDraftKeepsFrameAndAppliesOrKeepsLensExactly(bool skip)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync(loader: new ProfileLoader());
        var sourceSettings = Settings();
        sourceSettings.Lens.ProfileOverride = "Canon EF 50mm f/1.8 MkII";
        var source = await fixture.ImageAsync("source", sourceSettings);
        await fixture.CopyAsync(source);
        var previous = Settings('b');
        previous.Crop = new() { Left = .1 };
        previous.HorizonRotation = 1;
        previous.Lens.ProfileOverride = "committed lens";
        var target = await fixture.ImageAsync(skip ? "nikon-target" : "target", previous);
        await fixture.SelectAsync(target);
        await fixture.EnterDraftAsync();
        fixture.Vm.LensProfileOverride = "draft lens";
        var crop = fixture.Vm.CurrentCrop!.Clone();
        var horizon = fixture.Vm.HorizonRotation;
        Choose(fixture, "Lens Profile");
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.True(fixture.Vm.IsCropMode);
        Assert.Equal(crop.Left, fixture.Vm.CurrentCrop!.Left);
        Assert.Equal(horizon, fixture.Vm.HorizonRotation);
        Assert.Equal(previous.Crop.Left, target.EditSettings.Crop!.Left);
        Assert.Equal(previous.HorizonRotation, target.EditSettings.HorizonRotation);
        Assert.Equal(skip ? "draft lens" : sourceSettings.Lens.ProfileOverride, fixture.Vm.LensProfileOverride);
        Assert.Equal(skip ? "committed lens" : sourceSettings.Lens.ProfileOverride, target.EditSettings.Lens.ProfileOverride);
        var history = await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId);

        if (skip)
        {
            Assert.Empty(history.Entries);
            Assert.Contains("Lens Profile kept on 1 (outside the lens mount)", fixture.Vm.TransientStatus);
        }
        else
        {
            Assert.Equal(1, history.Position);
            await fixture.ExitDraftAsync();
            await fixture.Vm.UndoCommand.ExecuteAsync(null);
            Assert.Equal("committed lens", fixture.Vm.LensProfileOverride);
            Assert.Equal("committed lens", target.EditSettings.Lens.ProfileOverride);
        }
    }

    [AvaloniaFact]
    public async Task AppliedLensEqualToCommittedValueClearsLiveDraftWithoutHistory()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync(loader: new ProfileLoader());
        var source = await fixture.ImageAsync("source", Settings());
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target", Settings());
        await fixture.SelectAsync(target);
        fixture.Vm.LensProfileOverride = "pending lens";
        Choose(fixture, "Lens Profile");
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Null(fixture.Vm.LensProfileOverride);
        Assert.Null(target.EditSettings.Lens.ProfileOverride);
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
    }

    [AvaloniaFact]
    public async Task IdenticalAdjustmentsKeepPendingExposureAutosaveWithoutPasteHistory()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new EditSettings { Exposure = .75 });
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target", new EditSettings());
        await fixture.SelectAsync(target);
        fixture.Vm.Exposure = .75;
        var pending = Assert.IsAssignableFrom<Task>(fixture.Vm.PendingPreviewDebounceTask);
        Assert.False(pending.IsCompleted);
        Choose(fixture, "Adjustments");
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        fixture.AdvancePreviewClock();
        await fixture.Vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
        var stored = await fixture.Catalog.LoadImageStatesAsync([target.FilePath]);
        Assert.Equal(.75, Assert.Single(stored[target.FilePath]).EditSettings.Exposure);
        var history = await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId);
        Assert.Equal(1, history.Position);
        Assert.DoesNotContain(history.Entries, entry => entry.Label == "Paste settings");
    }

    [AvaloniaTheory]
    [InlineData("Crop & Straighten")]
    [InlineData("Geometry")]
    public async Task IdenticalFrameDiscardsCropDraftWithoutHistory(string group)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var target = await fixture.ImageAsync("target", new EditSettings
        {
            Crop = new() { Left = .1 }, HorizonRotation = 1
        });
        // A version of the same file transfers the committed crop without header reads.
        await fixture.CopyAsync(target);
        var committed = EditSettingsJson.Serialize(target.EditSettings);
        await fixture.EnterDraftAsync();
        Choose(fixture, group);
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.False(fixture.Vm.IsCropMode);
        Assert.Equal(.1, fixture.Vm.CurrentCrop!.Left);
        Assert.Equal(1, fixture.Vm.HorizonRotation);
        fixture.AdvancePreviewClock();
        await fixture.Vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
        Assert.Equal(committed, EditSettingsJson.Serialize(target.EditSettings));
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
    }

    [AvaloniaFact]
    public async Task CopyDuringRedecodeRetainsCameraIdentityWithoutHeaderOpens()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var loader = new ProfileLoader();
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync(loader: loader);
        var source = await fixture.ImageAsync("source", Settings());
        await fixture.SelectAsync(source);
        var opens = new List<string>();
        fixture.Vm.PasteFrameReader.Opening = (path, _) => opens.Add(path);
        loader.BeforeDecode = () =>
        {
            started.Set();
            Assert.True(release.Wait(TestWaits.Condition));
        };

        try
        {
            fixture.Vm.LensProfileOverride = "Canon EF 50mm f/1.8 MkII";
            fixture.AdvancePreviewClock();
            await TestWaits.UntilAsync(() => started.IsSet);
            fixture.Vm.CopyEditSettingsCommand.Execute(null);
            Assert.Empty(opens);
        }
        finally
        {
            loader.BeforeDecode = null;
            release.Set();
        }

        await fixture.Vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
        var target = await fixture.ImageAsync("target", Settings('b'));
        await fixture.SelectAsync(target);
        Choose(fixture, "Camera Profile");
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(source.EditSettings.RawProfile!.ContentHash, target.EditSettings.RawProfile!.ContentHash);
        Assert.Empty(opens);
    }

    private static EditSettings Settings(char hash = 'a') => new()
    {
        RawProfile = new()
        {
            Source = RawProfileSource.UserFile, Location = "synthetic.dcp", ContentHash = new string(hash, 64)
        }
    };

    private static void Choose(SyncTransferParityVm fixture, string name) =>
        fixture.Vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name,
            group => group.Name == name));

    private sealed class ProfileLoader : IBaseImageLoader
    {
        internal int Decodes { get; private set; }

        internal Action? BeforeDecode { get; set; }

        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => BaseImageLoadOutcome.Loaded(Create(file, decode));

        public BaseImage LoadFullBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => Create(file, decode);

        private BaseImage Create(ImageFile file, BaseDecodeSettings decode)
        {
            BeforeDecode?.Invoke();
            Decodes++;

            return new(new MagickImage(MagickColors.Gray, 16, 12) { ColorSpace = ColorSpace.RGB },
                new BaseImageInfo(BaseSourceKind.RawLibRaw, true, decode, null, null, 6504, 0, false, null, 1, 16, 12)
                {
                    CameraIdentity = file.FileName.StartsWith("nikon")
                        ? new("Nikon", "D70") : new("Canon", "EOS 6D")
                });
        }
    }
}
