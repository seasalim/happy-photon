using HappyPhoton.Models;

namespace HappyPhoton.Services;

public static partial class EditHistoryLabel
{
    // Gestures/actions supply an operation when the same value change has two meanings.
    public const string AddHealSpot = "Add Heal spot", AddCloneSpot = "Add Clone spot";
    public const string MoveSpot = "Move spot", MoveSpotSource = "Move spot source";
    public const string ResizeSpot = "Resize spot", SpotSize = "Spot size";
    public const string SpotFeather = "Spot feather", SpotOpacity = "Spot opacity";
    public const string SpotMode = "Spot mode", NewSpotSource = "New spot source";
    public const string DeleteSpot = "Delete spot", ClearSpots = "Clear spots";

    private static void AddRepairs(ICollection<string> changes, List<Repair>? before, List<Repair>? after)
    {
        before ??= [];
        after ??= [];
        if (before.SequenceEqual(after)) return;
        if (after.Count == before.Count + 1 && before.SequenceEqual(after.Take(before.Count)))
        {
            changes.Add(after[^1].Type == "clone" ? AddCloneSpot : AddHealSpot);
            return;
        }
        if (after.Count == 0)
        {
            changes.Add(before.Count == 1 ? DeleteSpot : ClearSpots);
            return;
        }
        if (before.Count == after.Count + 1 &&
            before.Where(r => after.Any(a => a.Id == r.Id)).SequenceEqual(after))
        {
            changes.Add(DeleteSpot);
            return;
        }
        var pairs = before.Zip(after).Where(p => p.First != p.Second).ToArray();
        if (before.Count != after.Count || pairs.Length != 1 || pairs[0].First.Id != pairs[0].Second.Id)
        {
            changes.Add("Spots");
            return;
        }
        var (old, current) = pairs[0];
        if (old.U != current.U || old.V != current.V) changes.Add(MoveSpot);
        if (old.Su != current.Su || old.Sv != current.Sv) changes.Add(MoveSpotSource);
        if (old.Radius != current.Radius) changes.Add(SpotSize);
        if (old.Feather != current.Feather) changes.Add(SpotFeather);
        if (old.Opacity != current.Opacity) changes.Add(SpotOpacity);
        if (old.Type != current.Type) changes.Add(SpotMode);
    }
}
