using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class DcpHintTests
{
    [AvaloniaTheory]
    [InlineData("Windows", true)]
    [InlineData("OSX", true)]
    [InlineData("Linux", true)]
    [InlineData("FreeBSD", false)]
    public async Task PlatformRuleControlsNoteAndNotice(string platform, bool expected)
    {
        var os = OSPlatform.Create(platform.ToUpperInvariant());
        var linux = os == OSPlatform.Linux;
        Assert.Equal(expected, MainWindowViewModel.SupportsDcpHint(os));
        using var files = new CatalogVmFixture();
        using var catalog = await files.CreateCatalogAsync();
        await using var vm = DcpHintTestScene.Create(catalog);
        vm.DcpHintPlatform = os;
        vm.ProbeDcpProfilesAsync = _ => Task.FromResult(DcpAdobeProfilePresence.None);
        vm.ShowFirstRunWelcome(files.Root);
        vm.FirstRunStep = FirstRunStep.AllSet;
        await vm.DcpHintProbe;
        Assert.Equal(expected, vm.IsDcpHintNoteVisible);
        Assert.Equal(linux ? MainWindowViewModel.DcpHintLinuxSentence : null, vm.DcpHintLinuxNote);

        await using var existing = DcpHintTestScene.Create(catalog);
        existing.DcpHintPlatform = os;
        existing.ShowWorkspaceReady(1);
        await existing.BackupNoticeLoad;
        existing.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => [];
        await DcpHintTestScene.ScanAsync(existing, files.Path("image.cr2"));
        Assert.Equal(expected ? existing.DcpHintNoticeText : null, existing.StatusMessage);
        Assert.Equal(linux, existing.DcpHintNoticeText.Contains("WINE", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task LateProbeUpdatesAllSetAndBackForwardReusesResult()
    {
        using var files = new CatalogVmFixture();
        using var catalog = await files.CreateCatalogAsync("catalog");
        var pictures = Directory.CreateDirectory(files.Path("pictures")).FullName;
        await using var vm = DcpHintTestScene.Create(catalog);
        var probe = new TaskCompletionSource<DcpAdobeProfilePresence>();
        var calls = 0;
        vm.ProbeDcpProfilesAsync = _ =>
        {
            calls++;

            return probe.Task;
        };
        vm.ShowFirstRunWelcome(pictures);
        vm.FirstRunStep = FirstRunStep.AllSet;
        Assert.False(vm.IsDcpHintNoteVisible);
        probe.SetResult(DcpAdobeProfilePresence.None);
        await vm.DcpHintProbe.WaitAsync(TestWaits.Condition);
        Assert.True(vm.IsDcpHintNoteVisible);
        vm.BackFirstRunCommand.Execute(null);
        Assert.False(vm.IsDcpHintNoteVisible);
        await vm.CompleteFirstRunFromLocationAsync(pictures);
        Assert.True(vm.IsDcpHintNoteVisible);
        Assert.Equal(1, calls);
    }

    [AvaloniaFact]
    public async Task CloseCancelsPendingProbeWithoutWaiting()
    {
        using var files = new CatalogVmFixture();
        using var catalog = await files.CreateCatalogAsync();
        var vm = DcpHintTestScene.Create(catalog);
        var probe = new TaskCompletionSource<DcpAdobeProfilePresence>();
        CancellationToken token = default;
        vm.ProbeDcpProfilesAsync = cancellation =>
        {
            token = cancellation;

            return probe.Task;
        };
        vm.ShowFirstRunWelcome(files.Root);
        await vm.DisposeAsync();
        Assert.True(token.IsCancellationRequested);
        Assert.False(probe.Task.IsCompleted);
        await vm.DcpHintProbe.WaitAsync(TestWaits.Condition);
    }

    [AvaloniaTheory]
    [InlineData("found")]
    [InlineData("unreadable")]
    [InlineData("presented")]
    public async Task ScanRequiresConclusiveAbsenceAndUnsetKey(string state)
    {
        using var files = new CatalogVmFixture();
        using var catalog = await files.CreateCatalogAsync();
        await using var vm = DcpHintTestScene.Create(catalog);
        File.WriteAllText(files.Path("malformed.dcp"), "malformed");

        if (state == "presented")
        {
            await catalog.SetAppSettingAsync(MainWindowViewModel.DcpHintPresentedKey, "true");
        }

        vm.ShowWorkspaceReady(1);
        await vm.BackupNoticeLoad;
        vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => state switch
        {
            "found" => [new FileInfo(files.Path("malformed.dcp"))],
            "unreadable" => throw new UnauthorizedAccessException(),
            _ => []
        };
        await DcpHintTestScene.ScanAsync(vm, files.Path("image.cr2"));
        Assert.Null(vm.StatusMessage);
        Assert.Equal(state == "presented" ? "true" : null,
            await catalog.GetAppSettingAsync(MainWindowViewModel.DcpHintPresentedKey));
    }

    [AvaloniaFact]
    public async Task BackupWinsUntilAcknowledgedAndHintRearmsOnNextScan()
    {
        using var files = new CatalogVmFixture();
        using var catalog = await files.CreateCatalogAsync();
        var outcome = JsonSerializer.Serialize(new BackupOutcome(DateTimeOffset.UtcNow, "failed"));
        await catalog.SetAppSettingAsync(CatalogBackupService.OutcomeKey, outcome);
        await DcpHintTestScene.AuditKeyWritesAsync(catalog);
        await using var vm = DcpHintTestScene.Create(catalog);
        vm.ShowWorkspaceReady(1);
        await vm.BackupNoticeLoad;
        vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => [];
        await DcpHintTestScene.ScanAsync(vm, files.Path("image.cr2"));
        Assert.Contains("last catalog backup failed", vm.StatusMessage);
        Assert.Null(await catalog.GetAppSettingAsync(MainWindowViewModel.DcpHintPresentedKey));
        var window = new Window { Width = 1200, Height = 700, Content = new StatusBarView { DataContext = vm } };
        using var scope = new TestUiScope(window);
        DcpHintTestScene.Render(window);
        Assert.Equal(outcome, await catalog.GetAppSettingAsync(CatalogBackupService.PresentedOutcomeKey));
        Assert.Contains("last catalog backup failed", vm.StatusMessage);
        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        Assert.Equal(MainWindowViewModel.DcpHintNotice, vm.StatusMessage);
        DcpHintTestScene.Render(window);
        Assert.Equal("true", await catalog.GetAppSettingAsync(MainWindowViewModel.DcpHintPresentedKey));
        vm.TransientStatus = "New operation";
        vm.TransientStatus = null;
        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        Assert.Null(vm.StatusMessage);
        Assert.Equal(1, await DcpHintTestScene.KeyWritesAsync(catalog, MainWindowViewModel.DcpHintPresentedKey));
        Assert.Equal(1, await DcpHintTestScene.KeyWritesAsync(catalog, CatalogBackupService.PresentedOutcomeKey));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrimmedNoticeWaitsUntilItFitsAndTooltipKeepsFullText(bool backup)
    {
        using var files = new CatalogVmFixture();
        using var catalog = await files.CreateCatalogAsync();
        var outcome = JsonSerializer.Serialize(new BackupOutcome(DateTimeOffset.UtcNow, "failed"));

        if (backup)
        {
            await catalog.SetAppSettingAsync(CatalogBackupService.OutcomeKey, outcome);
        }

        await using var vm = DcpHintTestScene.Create(catalog);
        vm.ShowWorkspaceReady(1);
        await vm.BackupNoticeLoad;
        vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => [];
        await DcpHintTestScene.StageUndoAsync(vm, files);
        await DcpHintTestScene.ScanAsync(vm, vm.SelectedImage!.FilePath);
        var status = new StatusBarView { DataContext = vm };
        var window = new Window { Width = 800, Height = 500, Content = status };
        using var scope = new TestUiScope(window);
        var key = backup ? CatalogBackupService.PresentedOutcomeKey : MainWindowViewModel.DcpHintPresentedKey;
        DcpHintTestScene.Render(window);
        var text = status.FindControl<TextBlock>("StatusText")!;
        Assert.Contains(text.TextLayout.TextLines, line => line.HasCollapsed);
        Assert.Equal(vm.StatusMessage, ToolTip.GetTip(text));
        Assert.Null(await catalog.GetAppSettingAsync(key));
        window.Width = 1200;
        window.Height = 700;
        await DcpHintTestScene.ScanAsync(vm, files.Path("short.cr2"));
        DcpHintTestScene.Render(window);
        Assert.DoesNotContain(text.TextLayout.TextLines, line => line.HasCollapsed);
        Assert.Equal(vm.StatusMessage, ToolTip.GetTip(text));
        Assert.Equal(backup ? outcome : "true", await catalog.GetAppSettingAsync(key));
    }
}
