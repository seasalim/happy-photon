using HappyPhoton.Services;
using System.Text.Json;

namespace HappyPhoton.Tests;

internal static class LightroomV3CropFixture
{
    public static IReadOnlyList<Row> LoadRows() =>
        JsonSerializer.Deserialize<Row[]>(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "assets", "lightroom", "lrcrop-v3-rows.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static void Populate(
        LightroomCatalogFixture fixture,
        string destinationRoot,
        IReadOnlyList<Row> rows)
    {
        foreach (var row in rows)
        {
            var root = Path.Combine(destinationRoot, row.Root) +
                       Path.DirectorySeparatorChar;
            fixture.AddPhoto(row.Id, root, row.RelativeFolder, row.FileName,
                row.Rating, row.Pick, row.Label, row.MasterImage != null);
            fixture.AddDevelopSettingsRaw(row.Id, row.Blob, row.Orientation,
                Value(row.FileWidth), Value(row.FileHeight),
                Value(row.CroppedWidth), Value(row.CroppedHeight));
        }
    }

    public static LightroomCropFact ParseCrop(Row row) =>
        LightroomCatalogReader.ParseCrop(row.Blob, row.Orientation,
            Number(row.FileWidth), Number(row.FileHeight),
            Number(row.CroppedWidth), Number(row.CroppedHeight));

    private static object? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        _ => null
    };

    private static double? Number(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    internal sealed record Row(
        int Id,
        string Root,
        string RelativeFolder,
        string FileName,
        int? MasterImage,
        double? Rating,
        double Pick,
        string Label,
        string Orientation,
        JsonElement FileWidth,
        JsonElement FileHeight,
        JsonElement CroppedWidth,
        JsonElement CroppedHeight,
        string Blob);
}
