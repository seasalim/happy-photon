using System.Text.Json;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed record CatalogHistoryRow(int Sequence, string Label, string SettingsJson);

public sealed record CatalogEditBatchTarget(long CatalogId, string? PreviousJson, int PreviousVersion,
    string WrittenJson, int PreviousPosition,
    CatalogHistoryRow? PreviousNewest, IReadOnlyList<CatalogHistoryRow> Deleted,
    IReadOnlyList<CatalogHistoryRow> Appended)
{
    // Decode only when restoring a model, from the same bytes restored to the catalog.
    public EditSettings Previous
    {
        get
        {
            if (PreviousJson == null || !EditSettingsJson.IsSupportedVersion(PreviousVersion)) return new();

            try
            {
                using var document = JsonDocument.Parse(PreviousJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("version", out var version) ||
                    !version.TryGetInt32(out var value) || value != PreviousVersion)
                {
                    return new();
                }

                return EditSettingsJson.Deserialize(PreviousJson, out _);
            }
            catch (JsonException)
            {
                return new();
            }
        }
    }
}

public sealed record CatalogEditBatchUndoResult(IReadOnlyList<long> Restored, IReadOnlyList<long> Skipped);
