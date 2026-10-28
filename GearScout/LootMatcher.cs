namespace GearScout;

public sealed record LootRollEntry(uint ItemId, bool CanRoll);

public static class LootMatcher
{
    public static uint BaseItemId(uint id) => id is >= 1_000_000 and < 2_000_000 ? id - 1_000_000 : id;

    public static HashSet<uint> TargetItems(IEnumerable<TrackedSet> sets) =>
        sets
            .SelectMany(s => s.Pieces).Select(p => p.ItemId).Where(id => id != 0).ToHashSet();

    public static HashSet<uint> MissingItems(IEnumerable<TrackedSet> sets,
        IReadOnlyCollection<OwnedItem>? inventory)
    {
        // Unknown inventory is not evidence an item is missing.
        if (inventory == null) return [];
        var counts = inventory.Where(i => i.Quantity > 0).GroupBy(i => BaseItemId(i.ItemId))
            .ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Quantity));
        // Check each alternative set independently: one owned copy can serve multiple saved sets.
        // Within one set, two identical rings still require two physical copies.
        return sets
            .SelectMany(s => s.Pieces.Where(p => p.ItemId != 0).GroupBy(p => p.ItemId)
                .Where(g => counts.GetValueOrDefault(g.Key) < (s.IsGlam ? 1 : g.Count())).Select(g => g.Key)).ToHashSet();
    }

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
