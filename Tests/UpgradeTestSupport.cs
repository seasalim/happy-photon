using HappyPhoton.Services;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Tests;

internal static class UpgradeTestSupport
{
    internal static async Task SeedAsync(string root, bool imagesOnly = false)
    {
        using (var catalog = new CatalogService(root))
        {
            await catalog.InitializeAsync();
            await catalog.GetOrCreateImageAsync("/virtual/original.jpg");
            await catalog.SetAppSettingAsync("FirstRunExperienceVersion", "1");
        }

        using var connection = CatalogService.OpenBackupCopy(Path.Combine(root, "catalog.db"), SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE edit_history;
            ALTER TABLE images DROP COLUMN history_position;
            UPDATE app_settings SET value='3' WHERE key='schema_version';
            """;
        command.ExecuteNonQuery();

        if (imagesOnly)
        {
            command.CommandText = "DROP TABLE app_settings; DROP TABLE image_assessments;";
            command.ExecuteNonQuery();
        }
    }

    internal static void Fail(CatalogBackupService backup, string cause)
    {
        backup.Step = step =>
        {
            if (step == "before:preserve-copy:catalog.db" && cause == "missing")
                throw new DirectoryNotFoundException("The backup drive is missing.");

            if (step == "before:preserve-zip" && cause == "disk-full")
                throw new IOException("The backup drive is full.");

            if (step == "before:upgrade-verify" && cause == "verification")
            {
                var zip = Directory.GetFiles(backup.Folder, "*.partial.zip").Single();
                File.WriteAllText(zip, "failed verification");
            }
        };
    }
}
