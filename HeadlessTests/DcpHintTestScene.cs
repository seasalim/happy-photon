using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

internal static class DcpHintTestScene
{
    internal static MainWindowViewModel Create(CatalogService catalog) => new(catalog,
        new NullBaseLoader(), _ => Task.CompletedTask,
        new TestSourceAvailabilityService(SourceAvailability.AvailableLocally))
    {
        DcpHintPlatform = OSPlatform.Windows,
        ProbeDcpProfilesAsync = _ => Task.FromResult(DcpAdobeProfilePresence.Unknown)
    };

    internal static async Task ScanAsync(MainWindowViewModel vm, string path)
    {
        var image = new ImageFile(path);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0);
        image.RawDecodeFailed = false;
        vm.ApplyRawProfileState(image, true, new DcpProfileState("hint", DcpProfileErrorCode.None,
            null, null, new CameraIdentity("Canon", "EOS 6D"), null));
        await TestWaits.UntilAsync(() => !vm.RawProfilePickerState.IsLoading);
        vm.TransientStatus = null;
    }

    internal static async Task StageUndoAsync(MainWindowViewModel vm, CatalogVmFixture files)
    {
        var source = new ImageFile(files.Path(new string('W', 90) + ".cr2"));
        var target = new ImageFile(files.Path("target.cr2"));
        vm.Browse.SetImages([source, target]);
        vm.SelectedImage = source;
        vm.Exposure = 1;
        vm.SelectAllCommand.Execute(null);
        vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        await TestWaits.UntilAsync(() => vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0);
        source.RawDecodeFailed = false;
        vm.TransientStatus = null;
        Assert.True(vm.IsBatchUndoOffered);
    }

    internal static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    internal static async Task AuditKeyWritesAsync(CatalogService catalog)
    {
        using var connection = KeyAuditConnection(catalog);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS hint_test_writes (key TEXT);
            CREATE TRIGGER IF NOT EXISTS hint_test_insert AFTER INSERT ON app_settings
            BEGIN INSERT INTO hint_test_writes VALUES (NEW.key); END;
            CREATE TRIGGER IF NOT EXISTS hint_test_update AFTER UPDATE ON app_settings
            BEGIN INSERT INTO hint_test_writes VALUES (NEW.key); END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task<long> KeyWritesAsync(CatalogService catalog, string key)
    {
        using var connection = KeyAuditConnection(catalog);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM hint_test_writes WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static SqliteConnection KeyAuditConnection(CatalogService catalog) => new(
        new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(catalog.CatalogPath, "catalog.db"),
            Pooling = false
        }.ToString());
}
