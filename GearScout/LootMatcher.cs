namespace GearScout;

public sealed record LootRollEntry(uint ItemId, bool CanRoll);

public static class LootMatcher
{
    public static uint BaseItemId(uint id) => id is >= 1_000_000 and < 2_000_000 ? id - 1_000_000 : id;

    public static HashSet<uint> TargetItems(IEnumerable<TrackedSet> sets, string currentJob, bool currentJobOnly) =>
        sets.Where(s => !currentJobOnly || string.Equals(s.Job, currentJob, StringComparison.OrdinalIgnoreCase))
            .SelectMany(s => s.Pieces).Select(p => p.ItemId).Where(id => id != 0).ToHashSet();

    public static bool CanHighlight(uint itemId, int uiIndex, IReadOnlyList<LootRollEntry> rolls, ISet<uint> targets)
    {
        var id = BaseItemId(itemId);
        if (id == 0 || !targets.Contains(id)) return false;
        // The UI normally retains the loot-slot index. Validate it before using its roll state.
        if (uiIndex >= 0 && uiIndex < rolls.Count && BaseItemId(rolls[uiIndex].ItemId) == id)
            return rolls[uiIndex].CanRoll;
        // If a UI change reorders slots, accept only an unambiguous eligible item.
        // Mixed eligible/resolved copies cannot be identified reliably by item ID alone.
        var candidates = rolls.Where(r => BaseItemId(r.ItemId) == id).ToList();
        return candidates.Count > 0 && candidates.All(r => r.CanRoll);
    }
}
