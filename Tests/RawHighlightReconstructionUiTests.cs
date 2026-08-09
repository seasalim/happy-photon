using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawHighlightReconstructionUiTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-highlight-ui-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task Selection_PersistsResetsAndUndoesAsOneEdit()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root, "missing.dng"));
        vm.SelectedImage = image;

        vm.HlReconstruction = HlReconstructionMode.Blend;
        await WaitUntilAsync(
            () => image.EditSettings.HlReconstruction ==
                  HlReconstructionMode.Blend);

        Assert.True(vm.CanReset);
        Assert.True(vm.CanUndo);

        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Equal(
            HlReconstructionMode.Clip,
            image.EditSettings.HlReconstruction);
        Assert.Equal(HlReconstructionMode.Clip, vm.HlReconstruction);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(
            HlReconstructionMode.Blend,
            image.EditSettings.HlReconstruction);
        Assert.Equal(HlReconstructionMode.Blend, vm.HlReconstruction);

        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task RawFallback_ShowsStatusAndKeepsEditsUsable()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var warnings = new List<string>();
        var loader = new BaseLoaderRouter(
            new RawBaseLoader(isAvailable: false),
            new StandardBaseLoader(
                (_, _) => new MagickImage(MagickColors.Gray, 64, 48)),
            () => false,
            warnings.Add);
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        var image = new ImageFile(Path.Combine(_root, "fallback.dng"))
        {
            EditSettings = new EditSettings
            {
                HlReconstruction = HlReconstructionMode.Blend
            }
        };
        vm.SelectedImage = image;

        Assert.True(vm.CanReset);
        await WaitUntilAsync(() => vm.IsWhiteBalanceReady);

        Assert.True(vm.CanReset);
        Assert.Equal(
            "Decoded via fallback — RAW controls unavailable",
            vm.TransientStatus);
        Assert.Single(warnings);
        Assert.Contains("fallback.dng", warnings[0]);

        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.True(vm.IsShowingOriginal);

        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Equal(
            HlReconstructionMode.Clip,
            image.EditSettings.HlReconstruction);
        Assert.False(vm.IsShowingOriginal);
        Assert.False(vm.CanReset);

        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ExtractedPanel_ForwardsToneCurveChanges()
    {
        using var catalog = new CatalogService(_root);
        catalog.InitializeAsync().GetAwaiter().GetResult();
        var vm = new MainWindowViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root, "missing.jpg"));
        vm.SelectedImage = image;
        var panel = new DevelopEditPanel
        {
            DataContext = vm
        };
        panel.Measure(new Size(250, 660));
        panel.Arrange(new Rect(0, 0, 250, 660));

        vm.CurrentCurve!.AddPointAndReturnIndex(0.5, 0.75);
        var curve = panel.FindControl<CurveView>("ToneCurveView")!;
        curve.Curve = vm.CurrentCurve;
        var curveChanged = false;
        curve.CurveChanged += (_, _) => curveChanged = true;
        curve.ResetCurve();

        Assert.True(curveChanged);
        Assert.Same(vm, panel.DataContext);
        Assert.True(vm.CanUndo);
        Assert.True(image.EditSettings.Curve.IsIdentity());

        await Task.Delay(250);
        panel.DataContext = null;
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task Selection_ClearsBeforeAfterState()
    {
        using var catalog = new CatalogService(_root);
        var vm = new MainWindowViewModel(catalog);
        vm.SelectedImage = new ImageFile(Path.Combine(_root, "first.jpg"));
        vm.IsShowingOriginal = true;

        vm.SelectedImage = new ImageFile(Path.Combine(_root, "second.jpg"));

        Assert.False(vm.IsShowingOriginal);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ScheduledHistogramRefresh_ClearsBeforeAfterState()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(
            catalog,
            CreateSyntheticLoader(),
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        await vm.InitializeAsync();
        var image = new ImageFile(Path.Combine(_root, "missing.png"));
        vm.SelectedImage = image;
        await WaitUntilAsync(
            () => vm.IsWhiteBalanceReady && vm.PreviewImage != null);

        vm.Exposure = 1;
        await WaitUntilAsync(() => image.EditSettings.Exposure == 1);
        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.True(vm.IsShowingOriginal);

        await WaitUntilAsync(() => !vm.IsShowingOriginal);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task PresetHoverAndRestore_ClearBeforeAfterState()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(
            catalog,
            CreateSyntheticLoader(),
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        await vm.InitializeAsync();
        vm.SelectedImage = new ImageFile(Path.Combine(_root, "missing.png"))
        {
            EditSettings = new EditSettings { Exposure = 1 }
        };
        await WaitUntilAsync(
            () => vm.IsWhiteBalanceReady && vm.PreviewImage != null);
        var preset = await vm.PresetService.SaveUserPresetAsync(
            "Hover",
            new EditSettings { Contrast = 20 });

        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.True(vm.IsShowingOriginal);
        await vm.PreviewPresetHoverAsync(preset.Id);
        Assert.False(vm.IsShowingOriginal);

        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.True(vm.IsShowingOriginal);
        await vm.RestoreFromHoverAsync();
        Assert.False(vm.IsShowingOriginal);

        await vm.DisposeAsync();
    }

    [Fact]
    public async Task ReplacementDecode_UsesDelayedArmingIndicator()
    {
        using var catalog = new CatalogService(_root);
        var vm = new MainWindowViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root, "photo.dng"));
        vm.SelectedImage = image;
        var slow = new PreviewBaseRefreshState(
            image,
            requestId: 41,
            isRefreshing: true);

        vm.ApplyBaseRefreshState(slow);

        Assert.False(vm.IsBaseArming);
        await WaitUntilAsync(() => vm.IsBaseArming);

        vm.ApplyBaseRefreshState(new PreviewBaseRefreshState(
            image,
            slow.RequestId,
            isRefreshing: false));
        Assert.False(vm.IsBaseArming);

        vm.ApplyBaseRefreshState(new PreviewBaseRefreshState(
            image,
            requestId: 42,
            isRefreshing: true));
        vm.ApplyBaseRefreshState(new PreviewBaseRefreshState(
            image,
            requestId: 42,
            isRefreshing: false));
        await Task.Delay(200);
        Assert.False(vm.IsBaseArming);

        await vm.DisposeAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("The expected UI state did not arrive.");
    }

    private static BaseLoaderRouter CreateSyntheticLoader() =>
        new(
            new RawBaseLoader(isAvailable: false),
            new StandardBaseLoader(
                (_, _) => new MagickImage(MagickColors.Gray, 64, 48)),
            () => false,
            _ => { });
}
