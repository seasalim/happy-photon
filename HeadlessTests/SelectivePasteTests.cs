using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SelectivePasteTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhiteBalanceOnlyPreservesOtherFieldsAndClearsMarker(bool browse)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var sourceSettings = SyncTransferParityCorpus.CreateLook();
        sourceSettings.Wb = new WhiteBalanceSettings { Mode = WbMode.Custom, Kelvin = 4200, Tint = 17 };
        var source = await fixture.ImageAsync("source", sourceSettings);
        await fixture.CopyAsync(source);
        Assert.Equal("Copied settings from source.jpg", fixture.Vm.TransientStatus);
        var targetSettings = SyncTransferParityCorpus.Destinations()
            .Single(item => item.Name == "preset").Settings.Clone();
        var target = await fixture.ImageAsync("target", targetSettings);
        await fixture.SelectAsync(target, develop: !browse);
        var expected = target.EditSettings.Clone();
        expected.Wb = source.EditSettings.Wb.Clone();
        expected.AppliedPresetId = null;
        fixture.Vm.RestorePasteGroups(WhiteBalanceChoice());
        fixture.Vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        Assert.DoesNotContain("Adjustments", fixture.Vm.PasteSettingsTooltip);

        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(target.EditSettings));
        var stored = await fixture.Catalog.LoadImageStatesAsync([target.FilePath]);
        Assert.Equal(EditSettingsJson.Serialize(expected),
            EditSettingsJson.Serialize(Assert.Single(stored[target.FilePath]).EditSettings));
        Assert.Null(fixture.Vm.ActivePresetId);
        Assert.Equal(browse ? "Applied to 1 photo" : "Pasted settings", fixture.Vm.TransientStatus);
    }

    [AvaloniaFact]
    public async Task UnsavedExposureSurvivesWhiteBalancePasteAndOneUndoRestoresIt()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", PasteGateSupport.SourceLook(true));
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target", new EditSettings());
        await fixture.SelectAsync(target);
        fixture.Vm.RestorePasteGroups(WhiteBalanceChoice());
        fixture.Vm.Exposure = 1.25;
        var unsaved = await fixture.Catalog.LoadImageStatesAsync([target.FilePath]);
        Assert.Equal(0, Assert.Single(unsaved[target.FilePath]).EditSettings.Exposure);
        var before = target.EditSettings.Clone();
        before.Exposure = 1.25;
        var expected = before.Clone();
        expected.Wb = source.EditSettings.Wb.Clone();

        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1.25, fixture.Vm.Exposure);
        Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(target.EditSettings));
        var stored = await fixture.Catalog.LoadImageStatesAsync([target.FilePath]);
        Assert.Equal(EditSettingsJson.Serialize(expected),
            EditSettingsJson.Serialize(Assert.Single(stored[target.FilePath]).EditSettings));
        Assert.Single(fixture.Vm.HistoryEntries, entry => entry.Label == "Paste settings");
        await fixture.Vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(EditSettingsJson.Serialize(before), EditSettingsJson.Serialize(target.EditSettings));
        Assert.Equal(1.25, fixture.Vm.Exposure);
    }

    [AvaloniaFact]
    public async Task PasteDiscardsLocalsGestureAndUndoRestoresPreGestureDocument()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", SyncTransferParityCorpus.CreateLook());
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target",
            SyncTransferParityCorpus.Destinations().Single(item => item.Name == "locals").Settings);
        await fixture.SelectAsync(target);
        await fixture.Vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        fixture.Vm.SelectedLocal = fixture.Vm.Locals[0];
        var before = EditSettingsJson.Serialize(target.EditSettings);
        Assert.True(fixture.Vm.BeginLocalsGesture(LocalHandle.Center, new Point(.5, .5)));
        fixture.Vm.MoveLocalsGesture(new Point(.6, .7), 100);
        Assert.NotEqual(before, EditSettingsJson.Serialize(target.EditSettings));

        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.False(fixture.Vm.IsLocalsGestureActive);
        await fixture.Vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(before, EditSettingsJson.Serialize(target.EditSettings));
    }

    [AvaloniaFact]
    public async Task BrowseFallbackCancelLeavesDocumentAndRememberedChoiceUntouched()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", SyncTransferParityCorpus.CreateLook());
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target", new EditSettings());
        await fixture.SelectAsync(target, develop: false);
        fixture.Vm.DeselectAllCommand.Execute(null);
        var before = EditSettingsJson.Serialize(target.EditSettings);
        var opened = false;
        fixture.Vm.ShowPasteSettingsAsync = model =>
        {
            opened = true;
            Assert.Equal("From source.jpg to this photo", model.Summary);
            model.NoneCommand.Execute(null);

            return Task.FromResult(false);
        };

        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.True(opened);
        Assert.Equal(before, EditSettingsJson.Serialize(target.EditSettings));
        Assert.Empty(fixture.Vm.CapturePasteGroups());
        Assert.Empty((await new AppSettingsService(fixture.Catalog).LoadAsync()).PasteGroups);
    }

    [AvaloniaFact]
    public async Task DialogShortcutRightClickTooltipAndCatalogAgree()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", PasteGateSupport.SourceLook());
        await fixture.CopyAsync(source);
        fixture.Vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, fixture.Vm);
        var opened = 0;
        fixture.Vm.ShowPasteSettingsAsync = _ =>
        {
            opened++;

            return Task.FromResult(false);
        };
        Dispatcher.UIThread.RunJobs();
        var binding = Assert.Single(window.KeyBindings,
            binding => binding.Gesture.Key == Key.V &&
                binding.Gesture.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift));
        Assert.Same(fixture.Vm.ChoosePasteSettingsCommand, binding.Command);
        binding.Command!.Execute(null);
        await fixture.Vm.ChoosePasteSettingsCommand.ExecutionTask!;
        var bar = new DevelopActionBar { DataContext = fixture.Vm };
        var button = bar.FindControl<Button>("PasteEditSettingsButton")!;
        button.RaiseEvent(new ContextRequestedEventArgs());
        await fixture.Vm.ChoosePasteSettingsCommand.ExecutionTask!;
        Assert.Equal(2, opened);
        Assert.Contains("Ctrl+Shift+V", fixture.Vm.PasteSettingsTooltip);
        Assert.Contains("Ctrl+Alt+Shift+V", fixture.Vm.PasteSettingsTooltip);
        Assert.All(EditSettingsTransfer.DefaultGroups,
            group => Assert.Contains(group.Name, fixture.Vm.PasteSettingsTooltip));
        Assert.Contains(ShortcutCatalog.Groups.SelectMany(group => group.Entries),
            entry => entry.Keys == "Ctrl+Alt+Shift+V");
        var workflow = File.ReadAllText(Path.Combine(GoldenTestPaths.RepositoryRoot, "docs", "WORKFLOW.md"));
        Assert.Contains("Ctrl+Alt+Shift+V", workflow);
        Assert.Contains("Ctrl+Shift+V", workflow);
    }

    private static Dictionary<string, bool> WhiteBalanceChoice() =>
        EditSettingsTransfer.LookGroups.ToDictionary(group => group.Name, group => group.Name == "White Balance");
}
