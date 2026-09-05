using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;
using static HappyPhoton.Tests.ConstructionVmProbe;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class EditConstructionVmBaselineTests
{
    private static readonly string[] States = ["fresh", "dirty", "committed-crop", "draft-crop", "raw-profile", "stored-geometry", "live-geometry", "live-crop-horizon", "active-preset", "before-after-split"];

    [Fact]
    public async Task RenderConstructionHashesPinCurrentVmPaths()
    {
        // Before/after shares the resting capture at base. Pin the real call
        // site as well, so sampling that shared builder cannot hide divergence.
        var splitSource = File.ReadAllText(Path.Combine(ConstructionBaselineAssert.Root,
            "ViewModels", "MainWindowViewModel.BeforeAfterSplit.cs"));
        Assert.Contains("RequestBeforeAfterRender(CaptureRestingSettings());", splitSource);
        var observations = new List<string>();
        foreach (var baseline in new[] { LensBaseline.Standard, LensBaseline.Legacy })
        foreach (var state in States)
        {
            using var fx = new CatalogVmFixture("construction-vm");
            using var catalog = await fx.CreateCatalogAsync();
            await using var vm = CreateVm(fx, catalog);
            await Configure(vm, fx, catalog, baseline, state);
            var interactive = await Capture(vm, () => (Task)Call(vm, "UpdatePreviewWithCurrentSliders")!);
            var clipping = (EditSettings)Call(vm, "CaptureClippingRenderSettings", vm.SelectedImage)!;
            var resting = (EditSettings)Call(vm, "CaptureRestingSettings")!;
            // The edited capture passed into the split's original-frame projection.
            var beforeAfter = (EditSettings)Call(vm, "CaptureRestingSettings")!;
            var hover = await Capture(vm, () => vm.PreviewPresetHoverAsync("baseline-preset"));
            // Reverse only the two approved hover corrections to retain the base goldens.
            Assert.Equal(baseline, hover.Lens.Baseline);
            Assert.Equal(interactive.Geometry?.Vertical, hover.Geometry?.Vertical);
            Assert.Equal(interactive.Geometry?.Horizontal, hover.Geometry?.Horizontal);
            Assert.Equal(interactive.Geometry?.Aspect, hover.Geometry?.Aspect);
            Assert.Equal(interactive.Geometry?.Distortion, hover.Geometry?.Distortion);
            var storedGeometry = vm.SelectedImage!.EditSettings.Geometry?.Clone();
            if (!vm.IsCropMode && state != "active-preset")
            {
                await vm.ApplyPresetAsync("baseline-preset");
                var committed = (await catalog.LoadImageStatesAsync([vm.SelectedImage!.FilePath]))
                    [vm.SelectedImage.FilePath].Single().EditSettings;
                committed.AppliedPresetId = hover.AppliedPresetId;
                Assert.Equal(RenderSettingsHash.Compute(committed), RenderSettingsHash.Compute(hover));
            }
            hover.Lens.Baseline = LensBaseline.Standard;
            hover.Geometry = storedGeometry;
            observations.Add($"{baseline}/{state}|" + string.Join("|", new[] { interactive, clipping, resting, beforeAfter, hover }.Select(s => RenderSettingsHash.Compute(s))));
        }
        Assert.Equal(20, observations.Count);
        ConstructionBaselineAssert.Match("render", observations);
    }

    [Fact]
    public async Task PresetHoverAndCommitPinCatalogAndImmediateHistory()
    {
        using var fx = new CatalogVmFixture("construction-transfer");
        using var catalog = await fx.CreateCatalogAsync();
        await using var vm = CreateVm(fx, catalog);
        await Configure(vm, fx, catalog, LensBaseline.Legacy, "transfer");
        Call(vm, "BeginDevelopHistoryLoad", vm.SelectedImage);
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        var stored = vm.SelectedImage!.EditSettings.Clone();
        var live = (EditSettings)Call(vm, "CaptureLiveEditState")!;
        var hover = await Capture(vm, () => vm.PreviewPresetHoverAsync("baseline-preset"));
        await vm.ApplyPresetAsync("baseline-preset");
        var image = vm.SelectedImage!;
        var committed = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single().EditSettings;
        var history = await catalog.LoadEditHistoryAsync(image.CatalogId);
        var last = history.Entries.Last().Settings;
        // Rows are stored base, live base, hover argument, committed catalog, history.
        var baseHover = hover.Clone();
        baseHover.Lens.Baseline = LensBaseline.Standard;
        baseHover.Geometry = stored.Geometry?.Clone();
        ConstructionBaselineAssert.Match("transfer", new[] { stored, live, baseHover, committed, last }.Select(EditSettingsJson.Serialize));
        var normalizedCommit = committed.Clone();
        normalizedCommit.AppliedPresetId = hover.AppliedPresetId;
        Assert.Equal(EditSettingsJson.Serialize(normalizedCommit), EditSettingsJson.Serialize(hover));
        Assert.Equal(stored.Rotation, live.Rotation);
        Assert.Equal(stored.HorizonRotation, live.HorizonRotation);
        Assert.Equal(EditSettingsJson.Serialize(committed), EditSettingsJson.Serialize(last));
        Assert.Equal(31, committed.Geometry!.Vertical);
        Assert.Equal(31, hover.Geometry!.Vertical);
        Assert.Equal(LensBaseline.Legacy, committed.Lens.Baseline);
        Assert.Equal(LensBaseline.Legacy, hover.Lens.Baseline);
        foreach (var result in new[] { hover, committed, last })
        {
            Assert.Equal(stored.RawProfile!.Location, result.RawProfile!.Location);
            Assert.Equal(stored.RawProfile.ContentHash, result.RawProfile.ContentHash);
            Assert.Equal(stored.RawProfile.Source, result.RawProfile.Source);
            Assert.Equal(stored.Rotation, result.Rotation);
            Assert.Equal(stored.HorizonRotation, result.HorizonRotation);
            Assert.Equal(stored.Crop!.Left, result.Crop!.Left);
            Assert.Equal(stored.Crop.Top, result.Crop.Top);
            Assert.Equal(stored.Crop.Right, result.Crop.Right);
            Assert.Equal(stored.Crop.Bottom, result.Crop.Bottom);
        }
    }

    private static MainWindowViewModel CreateVm(CatalogVmFixture fx, CatalogService catalog) =>
        fx.CreateViewModel(catalog, new NoSourceLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            postSelection: _ => { }, timeProvider: new TestTimeProvider());

    private static async Task Configure(MainWindowViewModel vm, CatalogVmFixture fx,
        CatalogService catalog, LensBaseline baseline, string state)
    {
        var settings = new EditSettings();
        settings.Lens.Baseline = baseline;
        var framed = state is "committed-crop" or "draft-crop" or "live-crop-horizon" or "transfer";
        if (framed)
        {
            settings.Rotation = 90;
            settings.HorizonRotation = 1.25;
            settings.Crop = new CropRegion { Left = .1, Top = .2, Right = .9, Bottom = .8 };
        }
        if (state is "raw-profile" or "transfer")
            settings.RawProfile = new RawProfileSelection { Source = RawProfileSource.UserFile,
                Location = "C:/profiles/baseline.dcp", ContentHash = new string('a', 64) };
        if (state is "stored-geometry" or "live-geometry" or "transfer")
            settings.Geometry = new GeometrySettings { Vertical = 11, Horizontal = -12, Aspect = 13, Distortion = -14 };
        var image = new ImageFile(fx.Path("synthetic.jpg")) { EditSettings = settings };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        Set(vm, "_selectedImage", image);
        Set(vm, "_hasSelectedImage", true);
        Set(vm, "_isLoadingImage", true);
        vm.IsDevelopMode = true;
        await vm.PresetService.UseDirectoryAsync(fx.Path("presets"));
        var preset = new Preset("baseline-preset", "Baseline", new EditSettings { Exposure = .6, Contrast = 17, Saturation = -9 });
        Set(vm.PresetService, "_userPresetSnapshot", new[] { preset });
        var presets = (List<Preset>)typeof(PresetService).GetField("_userPresets",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm.PresetService)!;
        presets.Add(preset);
        Call(vm, "LoadSlidersFrom", settings);
        if (state == "dirty") { vm.Exposure = .8; vm.Contrast = 23; vm.Shadows = 19; }
        if (state is "live-geometry" or "transfer") vm.GeometryVertical = 31;
        if (state is "draft-crop" or "live-crop-horizon")
        {
            vm.IsCropMode = true;
            vm.CurrentCrop = new CropRegion { Left = .25, Top = .3, Right = .75, Bottom = .7 };
        }
        if (state == "live-crop-horizon") vm.HorizonRotation = 3.5;
        if (state == "active-preset")
        {
            EditSettingsTransfer.ApplySubset(preset.Settings, settings);
            settings.AppliedPresetId = preset.Id;
            Call(vm, "LoadSlidersFrom", settings);
        }
        if (state == "before-after-split") { vm.Exposure = .8; vm.IsBeforeAfterSplit = true; }
        Set(vm, "_isLoadingImage", false);
    }
}
