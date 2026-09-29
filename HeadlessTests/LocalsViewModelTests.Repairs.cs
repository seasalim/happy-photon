using System.Reflection;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task RepairOnlyChangeRefreshesInstalledMaskAndRejectsPendingMask(bool pending, bool hue)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        vm.ShowLocalMask = false;
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedImage!.EditSettings.Locals = [new() { Cu = 2,
            Luminance = hue ? null : new() { Enabled = true, Lower = .2 },
            Hue = hue ? new() { Enabled = true } : null }];
        vm.SelectedLocal = vm.Locals[0];
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renders = 0;
        vm.LocalMaskRenderGateAsync = () => { renders++; return pending && renders == 1 ? release.Task : Task.CompletedTask; };
        try
        {
            vm.ShowLocalMask = true;
            if (!pending) await vm.PendingLocalMaskTask;
            var first = vm.LocalRangeMask;
            if (!pending) Assert.NotNull(first);
            vm.SelectedImage.EditSettings.Repairs = [new() { Su = .2 }];
            // Invoke the existing refresh seam without changing any other mask input.
            typeof(MainWindowViewModel).GetMethod("RefreshLocalRangeMask", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);
            Assert.Same(first, vm.LocalRangeMask);
            release.TrySetResult(); await vm.PendingLocalMaskTask;
            Assert.Equal(2, renders);
            Assert.NotNull(vm.LocalRangeMask);
            Assert.NotSame(first, vm.LocalRangeMask);
        }
        finally { release.TrySetResult(); }
    }

    [AvaloniaFact]
    public void BeforeProjectionExcludesRepairsFromRenderedPixels()
    {
        var settings = new EditSettings { Repairs = [new() { Type = "clone", Su = .2 }], Rotation = 90 };
        var method = typeof(MainWindowViewModel).GetMethod("BuildOriginalRenderSettings",
            BindingFlags.NonPublic | BindingFlags.Static, [typeof(EditSettings)])!;
        var before = Assert.IsType<EditSettings>(method.Invoke(null, [settings]));
        Assert.Null(before.Repairs); Assert.Equal(90, before.Rotation);
        using var basis = new LocalTestLoader().LoadFullBase(new ImageFile("before.jpg"), BaseDecodeSettings.Default, CancellationToken.None);
        using var actual = new RenderPipeline().Render(new(basis, before, RenderIntent.Preview, null, new(false, false)));
        settings.Repairs = null;
        using var expected = new RenderPipeline().Render(new(basis, settings, RenderIntent.Preview, null, new(false, false)));
        using var a = actual.Image.GetPixels(); using var b = expected.Image.GetPixels();
        Assert.Equal(a.ToShortArray(ImageMagick.PixelMapping.RGB), b.ToShortArray(ImageMagick.PixelMapping.RGB));
    }
}
