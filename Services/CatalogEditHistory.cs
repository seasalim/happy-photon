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
        string? operation = null) => PrepareAppend(state.Position,
            state.Position < 0 ? null : state.Entries[state.Position].Settings,
            before, after, operation);

    internal static CatalogEditHistoryMutation? PrepareAppend(
        int position, EditSettings? current, EditSettings before, EditSettings after,
        string? operation)
    {
        if (before.HasSameEdits(after)) return null;

        var appended = new List<CatalogEditHistoryEntry>(2);
        var sequence = position + 1;

        if (position < 0 || current == null || !before.HasSameEdits(current))
        {
            appended.Add(new(sequence++, "Original", before.Clone()));
        }

        appended.Add(new(sequence, EditHistoryLabel.Derive(
            before, after, operation), after.Clone()));

        return new(position, appended, sequence);
    }
}
