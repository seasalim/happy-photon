using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncTransferParityModelTests(ITestOutputHelper output)
{
    [Fact]
    public async Task G1_ModelTransferAndPresetBytes()
    {
        var recording = new SyncTransferParityRecording("model");
        var sources = SyncTransferParityCorpus.Sources();
        Assert.Equal(68, sources.Count);
        Assert.Equal(EditSettingsJson.Serialize(SyncTransferBatchBaselineTests.CreateLook()),
            EditSettingsJson.Serialize(SyncTransferParityCorpus.CreateLook()));
        using var directory = new TemporaryDirectory();
        var presetPath = Path.Combine(directory.Path, $"{SyncTransferParityCorpus.PresetId}.json");
        await File.WriteAllTextAsync(presetPath, JsonSerializer.Serialize(new UserPresetFile
        {
            Id = SyncTransferParityCorpus.PresetId,
            Name = "Sync parity"
        }));
        var service = new PresetService(directory.Path);
        await service.InitializeAsync();

        foreach (var destination in SyncTransferParityCorpus.Destinations())
        {
            recording.Add($"input/destination/{destination.Name}",
                SyncTransferParityRecording.Settings(destination.Settings));
        }

        foreach (var source in sources)
        {
            recording.Add($"input/source/{source.Name}", SyncTransferParityRecording.Settings(source.Settings));
            recording.Add($"copy/{source.Name}",
                SyncTransferParityRecording.Settings(EditSettingsTransfer.CopyGroups(source.Settings)));

            foreach (var destination in SyncTransferParityCorpus.Destinations())
            {
                var target = destination.Settings.Clone();
                EditSettingsTransfer.ApplyGroups(source.Settings, target);
                recording.Add($"transfer/{destination.Name}/{source.Name}",
                    SyncTransferParityRecording.Settings(target));
            }

            var preset = await service.SaveUserPresetAsync(
                "Sync parity", source.Settings, SyncTransferParityCorpus.PresetId);
            recording.Add($"preset/settings/{source.Name}", SyncTransferParityRecording.Settings(preset.Settings));
            // The frozen recording uses Windows JSON newlines and a fixed overwrite ID.
            var presetText = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(presetPath))
                .ReplaceLineEndings("\r\n");
            recording.Add($"preset/bytes/{source.Name}",
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(presetText)));
            var loaded = new PresetService(directory.Path);
            await loaded.InitializeAsync();
            recording.Add($"preset/reloaded/{source.Name}",
                SyncTransferParityRecording.Settings(Assert.Single(loaded.UserPresets).Settings));
        }

        recording.Verify(output);
    }
}
