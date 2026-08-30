using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed record CatalogEditHistoryEntry(int Sequence, string Label,
    EditSettings Settings);

public sealed record CatalogEditHistoryState(
    IReadOnlyList<CatalogEditHistoryEntry> Entries, int Position);

public sealed record CatalogEditHistoryMutation(int TruncateAfter,
    IReadOnlyList<CatalogEditHistoryEntry> Appended, int Position);

public static class CatalogEditHistory
{
    public static CatalogEditHistoryMutation? PrepareAppend(
        CatalogEditHistoryState state, EditSettings before, EditSettings after,
        string? operation = null)
    {
        if (before.HasSameEdits(after)) return null;
        var appended = new List<CatalogEditHistoryEntry>(2);
        var sequence = state.Position + 1;
        if (state.Position < 0 || !before.HasSameEdits(
                state.Entries[state.Position].Settings))
        {
            appended.Add(new(sequence++, "Original", before.Clone()));
        }
        appended.Add(new(sequence, EditHistoryLabel.Derive(
            before, after, operation), after.Clone()));
        return new(state.Position, appended, sequence);
    }
}
