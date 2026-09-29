using System.Reflection;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncPhotoReviewTests
{
    [AvaloniaFact]
    public async Task DevelopCropPastePreservesInterveningExposureAndUndoIsExact()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await CreateSourceAsync(fixture);
        var target = await CreateTargetAsync(fixture, "target");
        await fixture.SelectAsync(target);
        Choose(fixture.Vm, "Crop & Straighten");
        using var pause = new HeaderPause(fixture.Vm.PasteFrameReader, source.FilePath);
        var paste = fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        string before;

        try
        {
            await pause.Entered.Task.WaitAsync(TestWaits.Condition);
            fixture.Vm.Exposure = 1.25;
            before = Capture(fixture.Vm);
        }
        finally
        {
            pause.Release.Set();
            await paste.WaitAsync(TestWaits.Condition);
        }

        Assert.Equal(1.25, fixture.Vm.Exposure);
        Assert.Equal(1.25, target.EditSettings.Exposure);
        Assert.Equal(.2, target.EditSettings.Crop!.Left);
        Assert.Equal(2, pause.Opens);
        await fixture.Vm.UndoCommand.ExecuteAsync(null).WaitAsync(TestWaits.Condition);
        Assert.Equal(before, Capture(fixture.Vm));
    }

    [AvaloniaFact]
    public async Task BatchEnteringDevelopRebindsLocalsAndShowMask()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await CreateSourceAsync(fixture);
        var target = await CreateTargetAsync(fixture, "target");
        var other = await CreateTargetAsync(fixture, "other");
        await fixture.SelectAsync(target, develop: false);
        fixture.Vm.Browse.SetImages([target, other]);
        fixture.Vm.Browse.SelectOnly(target);
        fixture.Vm.Browse.ToggleSelection(other);
        Choose(fixture.Vm, "Crop & Straighten", "Locals");
        fixture.Vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        using var pause = new HeaderPause(fixture.Vm.PasteFrameReader, source.FilePath);
        var paste = fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);

        try
        {
            await pause.Entered.Task.WaitAsync(TestWaits.Condition);
            await fixture.SelectAsync(target);
            await fixture.Vm.ToggleLocalsModeCommand.ExecuteAsync(null);
            fixture.Vm.SelectedLocal = fixture.Vm.Locals[0];
            fixture.Vm.ShowLocalMask = true;
            Assert.True(fixture.Vm.IsLocalMaskVisible);
        }
        finally
        {
            pause.Release.Set();
            await paste.WaitAsync(TestWaits.Condition);
        }

        Assert.Equal(source.EditSettings.Locals![0].Id, Assert.Single(fixture.Vm.Locals).Id);
        Assert.Same(fixture.Vm.Locals[0], fixture.Vm.SelectedLocal);
        Assert.True(fixture.Vm.ShowLocalMask);
        Assert.True(fixture.Vm.IsLocalMaskVisible);
        Assert.Equal(.75, fixture.Vm.LocalExposure);
    }

    [AvaloniaTheory]
    [InlineData("Locals", 0)]
    [InlineData("Geometry", 1)]
    [InlineData("Crop & Straighten", 1)]
    public async Task BatchCapturesFolderPathsOnlyOnceWhenFrameGroupsChosen(string group, int expected)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        await CreateSourceAsync(fixture);
        var target = await CreateTargetAsync(fixture, "target");
        var other = await CreateTargetAsync(fixture, "other");
        await fixture.SelectAsync(target, develop: false);
        fixture.Vm.Browse.SetImages([target, other]);
        fixture.Vm.Browse.SelectOnly(target);
        fixture.Vm.Browse.ToggleSelection(other);
        Choose(fixture.Vm, group);
        fixture.Vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        var captures = new List<IReadOnlyList<string>>();
        fixture.Vm.CropWritePathsCaptured = captures.Add;
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(expected, captures.Count);

        if (expected != 0)
        {
            Assert.Contains(target.FilePath, captures[0]);
            Assert.Contains(other.FilePath, captures[0]);
        }
    }

    [AvaloniaTheory]
    [InlineData("Locals", true)]
    [InlineData("Geometry", false)]
    [InlineData("Crop & Straighten", false)]
    public async Task BatchEnteringDevelopKeepsCropDraftOnlyWithoutFrameGroups(string group, bool preserveDraft)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        await CreateSourceAsync(fixture);
        var target = await CreateTargetAsync(fixture, "target");
        await fixture.SelectAsync(target, develop: false);
        Choose(fixture.Vm, group);
        fixture.Vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Catalog.EditHistoryWriteGateAsync = () =>
        {
            entered.TrySetResult();

            return release.Task;
        };
        var paste = fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);

        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            await fixture.SelectAsync(target);
            await fixture.EnterDraftAsync();
        }
        finally
        {
            release.TrySetResult();
            await paste.WaitAsync(TestWaits.Condition);
            fixture.Catalog.EditHistoryWriteGateAsync = null;
        }

        Assert.Equal(preserveDraft, fixture.Vm.IsCropMode);
        Assert.Equal(preserveDraft ? .21 : target.EditSettings.Crop!.Left, fixture.Vm.CurrentCrop!.Left);
        Assert.Equal(preserveDraft ? -7.5 : target.EditSettings.HorizonRotation, fixture.Vm.HorizonRotation);
    }

    private static async Task<ImageFile> CreateSourceAsync(SyncTransferParityVm fixture)
    {
        var source = await fixture.ImageAsync("source", new EditSettings
        {
            Crop = new() { Left = .2, Top = .1, Right = .8, Bottom = .9 },
            Geometry = new() { Vertical = 5 },
            Locals = [new() { Id = "11111111111111111111111111111111", Exposure = .75 }]
        });
        WriteImage(source);
        await fixture.CopyAsync(source);

        return source;
    }

    private static async Task<ImageFile> CreateTargetAsync(SyncTransferParityVm fixture, string name)
    {
        var target = await fixture.ImageAsync(name, new EditSettings
        {
            Crop = new() { Left = .1 },
            Locals = [new() { Id = "22222222222222222222222222222222", Exposure = -.5 }]
        });
        WriteImage(target);

        return target;
    }

    private static void WriteImage(ImageFile file)
    {
        using var image = new MagickImage(MagickColors.Gray, 16, 12);
        image.Write(file.FilePath);
    }

    private static string Capture(MainWindowViewModel vm) => EditSettingsJson.Serialize(
        (EditSettings)typeof(MainWindowViewModel)
            .GetMethod("CaptureLiveEditState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, null)!);

    private static void Choose(MainWindowViewModel vm, params string[] names) =>
        vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name,
            group => names.Contains(group.Name)));

    private sealed class HeaderPause : IDisposable
    {
        private readonly PhotoFrameFactsReader _reader;

        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ManualResetEventSlim Release { get; } = new();

        internal int Opens { get; private set; }

        internal HeaderPause(PhotoFrameFactsReader reader, string source)
        {
            _reader = reader;
            reader.Opening = (path, _) =>
            {
                Opens++;
                if (path != source) return;

                Entered.TrySetResult();
                Assert.True(Release.Wait(TestWaits.Condition));
            };
        }

        public void Dispose()
        {
            _reader.Opening = null;
            Release.Dispose();
        }
    }
}

