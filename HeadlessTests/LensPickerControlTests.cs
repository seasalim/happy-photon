using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensPickerControlTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("lens-picker");
    private const string LensA = "Nikon AF Nikkor 20mm f/2.8D";
    private const string LensB = "Nikon AF Nikkor 24mm f/2.8D";
    private const string AutomaticLens = "Nikon AF Nikkor 50mm f/1.8D";

    [AvaloniaTheory]
    [InlineData(false, "optics-picker-nodata")]
    [InlineData(true, "optics-picker-manual")]
    public async Task ShowcasePicker(bool manual, string scene)
    {
        using var catalog = await _fx.CreateCatalogAsync();
        await using var vm = _fx.CreateViewModel(catalog, new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(_fx.Path("unidentified.nef"));
        var automatic = await Task.Run(() => ReadSummary(null));
        vm.ApplyLensPrescription(true, automatic);
        var optics = new LensEditGroup { DataContext = vm };
        var window = new Window
        {
            Content = new Border { Padding = new Thickness(32), Child = optics }
        };
        using var scope = new TestUiScope(window, ThemeVariant.Dark);
        Dispatcher.UIThread.RunJobs();
        var picker = optics.FindControl<ComboBox>("LensPicker")!;
        Assert.True(picker.IsVisible);
        Assert.True(picker.IsEnabled);
        Assert.Equal("Automatic", picker.SelectedItem);
        Assert.False(optics.FindControl<Grid>("DistortionRow")!.IsEnabled);
        Assert.False(optics.FindControl<Grid>("ChromaticAberrationRow")!.IsEnabled);
        Assert.False(optics.FindControl<Grid>("VignettingRow")!.IsEnabled);
        if (manual)
        {
            picker.SelectedItem = LensA;
            Assert.Equal(LensA, vm.LensProfileOverride);
            Assert.True(vm.CanReset);
            vm.ApplyLensPrescription(true, await Task.Run(() => ReadSummary(LensA)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(LensA, picker.SelectedItem);
            Assert.True(optics.FindControl<Grid>("DistortionRow")!.IsEnabled);
            Assert.True(optics.FindControl<Grid>("ChromaticAberrationRow")!.IsEnabled);
            Assert.True(optics.FindControl<Grid>("VignettingRow")!.IsEnabled);
            Assert.Equal($"{LensA} · LENSFUN · MANUAL",
                optics.FindControl<TextBlock>("LensSourceText")!.Text);
        }
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(800, 500), ThemeVariant.Dark);
        optics.DataContext = null;
    }

    [AvaloniaFact]
    public async Task PickerStaysInsideNarrowPaneAndTrimsLongNames()
    {
        const string longName =
            "Sigma 100-300mm f/4 APO EX DG HSM + Kenko Teleplus PRO 300 AF 1.4x DGX extender";
        using var catalog = await _fx.CreateCatalogAsync();
        await using var vm = _fx.CreateViewModel(catalog, new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.ApplyLensPrescription(true, new LensPrescriptionSummary(
            longName, "LENSFUN", true, true, true)
        {
            IsManual = true,
            Camera = "Nikon D750",
            CompatibleLenses = [longName, LensA]
        });
        vm.LensProfileOverride = longName;
        var optics = new LensEditGroup { DataContext = vm };
        var window = new Window { Width = 240, Height = 320, Content = optics };
        using var scope = new TestUiScope(window, ThemeVariant.Dark);
        Dispatcher.UIThread.RunJobs();
        var picker = optics.FindControl<ComboBox>("LensPicker")!;
        Assert.True(Equals(longName, picker.SelectedItem),
            $"selected='{picker.SelectedItem}' override='{vm.LensProfileOverride}' name='{vm.SelectedLensName}' items={picker.ItemCount}");
        Assert.True(picker.Bounds.Width <= 240, $"picker width {picker.Bounds.Width}");
        Assert.True(optics.DesiredSize.Width <= 240, $"group width {optics.DesiredSize.Width}");
        Assert.Equal(longName, ToolTip.GetTip(picker));

        picker.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var row = Assert.IsType<ComboBoxItem>(picker.ContainerFromIndex(1));
        var text = row.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.Equal(longName, ToolTip.GetTip(text));
        Assert.True(text.MaxWidth < picker.Bounds.Width, $"row text max {text.MaxWidth}");
        Assert.True(text.Bounds.Width <= text.MaxWidth, $"row text width {text.Bounds.Width}");
        picker.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
        ShowcaseTestHelper.Capture("optics-picker-narrow", scope, new PixelSize(240, 320), ThemeVariant.Dark);
        optics.DataContext = null;
    }

    [AvaloniaFact]
    public async Task ManualWithoutDataReportsUnavailableCorrections()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        await using var vm = _fx.CreateViewModel(catalog, new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.ApplyLensPrescription(true, new LensPrescriptionSummary(
            LensA, string.Empty, false, false, false) { IsManual = true });
        Assert.Equal($"{LensA} · NO CORRECTION DATA · MANUAL", vm.LensSourceText);
        Assert.False(vm.HasLensDistortion);
        Assert.False(vm.HasLensChromaticAberration);
        Assert.False(vm.HasLensVignetting);
    }

    [AvaloniaFact]
    public async Task NikonFixtureSelectionsRedecodeAndInstallMatchingManualSummaries()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var loader = new RecordingRawLoader();
        await using var vm = _fx.CreateViewModel(catalog, loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        var image = new ImageFile(GoldenTestPaths.Asset("nikon-d70-burst-1.nef"));
        vm.SelectedImage = image;
        var optics = new LensEditGroup { DataContext = vm };
        var window = new Window { Width = 800, Height = 500, Content = optics };
        using var scope = new TestUiScope(window);
        var picker = optics.FindControl<ComboBox>("LensPicker")!;
        await Settled(AutomaticLens, false);
        Assert.Equal("Automatic", picker.SelectedItem);
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        foreach (var choice in new[] { LensA, LensB, "Automatic" })
        {
            var historyCount = vm.HistoryEntries.Count;
            picker.SelectedItem = choice;
            await Settled(choice == "Automatic" ? AutomaticLens : choice, choice != "Automatic");
            await TestWaits.UntilAsync(() => image.EditSettings.Lens.ProfileOverride ==
                (choice == "Automatic" ? null : choice));
            Assert.Equal(choice == "Automatic" ? null : choice, image.EditSettings.Lens.ProfileOverride);
            await TestWaits.UntilAsync(() => vm.HistoryEntries.Count > historyCount);
            Assert.Equal("Optics: lens profile",
                Assert.Single(vm.HistoryEntries, entry => entry.IsCurrent).Label);
        }
        var keys = loader.Keys.ToArray();
        Assert.Equal(4, keys.Length);
        Assert.Equal(3, keys.Distinct().Count());
        Assert.Equal(keys[0], keys[3]);
        Assert.All(keys.Zip(keys.Skip(1)), pair => Assert.NotEqual(pair.First, pair.Second));

        await vm.UndoCommand.ExecuteAsync(null);
        await Settled(LensB, true);
        Assert.Equal(LensB, image.EditSettings.Lens.ProfileOverride);
        await vm.RedoCommand.ExecuteAsync(null);
        await Settled(AutomaticLens, false);
        Assert.Null(image.EditSettings.Lens.ProfileOverride);

        // Reload persisted edit JSON into another image before testing Reset.
        var persisted = image.EditSettings.Clone();
        persisted.Lens.ProfileOverride = LensA;
        vm.SelectedImage = new ImageFile(image.FilePath)
        {
            EditSettings = EditSettingsJson.Deserialize(EditSettingsJson.Serialize(persisted), out _)
        };
        await Settled(LensA, true);
        Assert.Equal(LensA, picker.SelectedItem);
        Assert.True(vm.CanReset);
        await vm.ResetEditsCommand.ExecuteAsync(null);
        await Settled(AutomaticLens, false);
        Assert.Null(vm.SelectedImage.EditSettings.Lens.ProfileOverride);
        Assert.Equal("Automatic", picker.SelectedItem);
        window.Close();
        optics.DataContext = null;

        async Task Settled(string lens, bool manual)
        {
            await TestWaits.UntilAsync(() => vm.PreviewImage != null &&
                vm.LensPrescription?.LensName == lens && vm.LensPrescription.IsManual == manual &&
                vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0);
            Assert.Equal(manual, vm.LensSourceText.EndsWith(" · MANUAL", StringComparison.Ordinal));
        }
    }

    [AvaloniaTheory]
    [InlineData("preset")]
    [InlineData("paste")]
    [InlineData("untoggle")]
    [InlineData("reset")]
    public async Task EditActionsKeepPerImageLensUnlessExplicitlyReset(string action)
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var loader = new RecordingRawLoader();
        await using var vm = _fx.CreateViewModel(catalog, loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        await vm.PresetService.UseDirectoryAsync(_fx.Path("presets"));
        var transferred = new EditSettings
        {
            Contrast = 18,
            Lens = new LensSettings { Vignetting = true, ProfileOverride = LensB }
        };
        var preset = await vm.PresetService.SaveUserPresetAsync("Lens preservation", transferred);
        vm.SelectedImage = new ImageFile(_fx.Path("source.nef")) { EditSettings = transferred };
        vm.CopyEditSettingsCommand.Execute(null);
        var resetting = action is "untoggle" or "reset";
        var image = new ImageFile(GoldenTestPaths.Asset("nikon-d70-burst-1.nef"))
        {
            EditSettings = new EditSettings
            {
                Contrast = resetting ? 18 : 0,
                AppliedPresetId = resetting ? preset.Id : null,
                Lens = new LensSettings { ProfileOverride = LensA, Vignetting = resetting }
            }
        };
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null &&
            vm.LensPrescription?.LensName == LensA &&
            vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0);
        loader.Keys.Clear();

        if (action == "paste") await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        else if (action == "reset") await vm.ResetEditsCommand.ExecuteAsync(null);
        else await vm.ApplyPresetAsync(preset.Id);

        var expectedLens = action == "reset" ? null : LensA;
        Assert.Equal(expectedLens, vm.LensProfileOverride);
        Assert.Equal(expectedLens, image.EditSettings.Lens.ProfileOverride);
        Assert.Equal(!resetting, vm.LensVignetting);
        Assert.Equal(resetting ? 0 : 18, vm.Contrast);
        await TestWaits.UntilAsync(() => !loader.Keys.IsEmpty &&
            vm.LensPrescription?.LensName == (expectedLens ?? AutomaticLens) &&
            vm.LensPrescription.IsManual == (expectedLens != null) &&
            vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0);
        var stored = Assert.Single((await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath]);
        Assert.Equal(expectedLens, stored.EditSettings.Lens.ProfileOverride);
    }

    private static LensPrescriptionSummary ReadSummary(string? selected)
    {
        var metadata = new LibRawMetadata("Nikon", "D750", "Nikon", "D750",
            "Unidentified lens", 100, 0.01f, 8, 20, null, null, 1,
            new LibRawGpsFacts(false, null, null, null));
        return RawBaseLoader.ReadLensPrescription(new ImageFile("missing.nef"), metadata,
            null, new LibRawDimensions(0, 0, 6016, 4016, 0, 0, 1), selected).Summary!;
    }

    private sealed class RecordingRawLoader : IBaseImageLoader
    {
        private readonly RawBaseLoader _loader = new();
        internal ConcurrentQueue<string> Keys { get; } = new();
        public bool CanLoad(ImageFile file) => _loader.CanLoad(file);
        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file,
            BaseDecodeSettings decode, CancellationToken cancellationToken)
        {
            Keys.Enqueue(decode.CacheKey);
            return _loader.LoadPreviewBaseWithOutcome(file, decode, cancellationToken);
        }
        public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => _loader.LoadFullBase(file, decode, cancellationToken);
    }

    public void Dispose() => _fx.Dispose();
}
