using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public sealed record EditHistoryEntry(int Sequence, string Label, EditSettings Settings)
{
    public bool IsCurrent { get; internal set; }
}

/// <summary>One ordered, persistent list of edit snapshots and its current position.</summary>
public sealed class EditHistory
{
    private readonly List<EditHistoryEntry> _entries = [];

    public IReadOnlyList<EditHistoryEntry> Entries => _entries;
    public int Position { get; private set; } = -1;
    public bool CanUndo => Position > 0;
    public bool CanRedo => Position >= 0 && Position < _entries.Count - 1;

    public void Load(IEnumerable<CatalogEditHistoryEntry> entries, int position)
    {
        _entries.Clear();
        _entries.AddRange(entries.OrderBy(entry => entry.Sequence).Select(entry =>
            new EditHistoryEntry(entry.Sequence, entry.Label, entry.Settings)));
        Position = _entries.Count == 0 ? -1 : Math.Clamp(position, 0, _entries.Count - 1);
        MarkCurrent();
    }

    public CatalogEditHistoryMutation? PrepareAppend(
        EditSettings before,
        EditSettings after,
        string? operation = null)
        => CatalogEditHistory.PrepareAppend(
            new CatalogEditHistoryState(
                _entries.Select(entry => new CatalogEditHistoryEntry(
                    entry.Sequence, entry.Label, entry.Settings)).ToArray(),
                Position),
            before,
            after,
            operation);

    public void Publish(CatalogEditHistoryMutation mutation)
    {
        if (_entries.Count > mutation.TruncateAfter + 1)
            _entries.RemoveRange(
                mutation.TruncateAfter + 1,
                _entries.Count - mutation.TruncateAfter - 1);
        _entries.AddRange(mutation.Appended.Select(entry =>
            new EditHistoryEntry(entry.Sequence, entry.Label, entry.Settings)));
        Position = mutation.Position;
        MarkCurrent();
    }

    public EditHistoryEntry? EntryAt(int position) =>
        position >= 0 && position < _entries.Count ? _entries[position] : null;

    public int PositionOf(EditHistoryEntry entry) => _entries.IndexOf(entry);

    public void PublishPosition(int position)
    {
        Position = position;
        MarkCurrent();
    }

    public void Clear() => Load([], -1);

    private void MarkCurrent()
    {
        for (var index = 0; index < _entries.Count; index++)
            _entries[index].IsCurrent = index == Position;
    }
}
