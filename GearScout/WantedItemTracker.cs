namespace GearScout;

public sealed record WantedItemNotice(uint ItemId, IReadOnlyList<string> Sets);
public sealed record WantedLootItem(uint ChestObjectId, uint ChestItemIndex, uint ItemId, bool CanRoll);

public sealed class WantedItemTracker
{
    private ulong owner;
    private bool needsBaseline = true;
    private readonly HashSet<uint> knownItems = [];
    private readonly HashSet<(uint Chest, uint Index, uint Item)> seenLoot = [];
    private readonly Queue<(uint Chest, uint Index, uint Item)> lootHistory = [];

    public void Suspend() => needsBaseline = true;

    public List<WantedItemNotice> ObserveOwnership(ulong characterId, IReadOnlyCollection<TrackedSet> sets,
        IReadOnlyCollection<OwnedItem>? live, IReadOnlyCollection<OwnedItem>? owned, bool storageTransfer = false)
    {
        SelectCharacter(characterId);
        if (characterId == 0 || live == null || owned == null) { Suspend(); return []; }
        var notices = new List<WantedItemNotice>();
        if (!needsBaseline && !storageTransfer)
        {
            foreach (var id in live.Where(i => i.Quantity > 0).Select(i => LootMatcher.BaseItemId(i.ItemId)).Distinct())
            {
                if (id == 0 || knownItems.Contains(id)) continue;
                var matching = sets.Where(s => s.Pieces.Any(p => LootMatcher.BaseItemId(p.ItemId) == id));
                var labels = Labels(matching);
                if (labels.Count > 0) notices.Add(new(id, labels));
            }
        }
        // Remember all known ownership, even non-targets and newly checked storage. Transfers,
        // set edits and later reacquiring a previously seen item must not look like a new find.
        knownItems.UnionWith(owned.Where(i => i.Quantity > 0).Select(i => LootMatcher.BaseItemId(i.ItemId)).Where(id => id != 0));
        needsBaseline = false;
        return notices;
    }

    public List<WantedItemNotice> ObserveLoot(ulong characterId, IReadOnlyCollection<TrackedSet> sets,
        IReadOnlyCollection<OwnedItem>? owned, IEnumerable<WantedLootItem> loot)
    {
        SelectCharacter(characterId);
        if (characterId == 0 || owned == null) return [];
        var missingBySet = sets.Select(s => (Set: s, Missing: LootMatcher.MissingItems([s], owned))).ToList();
        var notices = new List<WantedItemNotice>();
        foreach (var entry in loot)
        {
            var id = LootMatcher.BaseItemId(entry.ItemId);
            if (id == 0 || !entry.CanRoll) continue;
            var key = (entry.ChestObjectId, entry.ChestItemIndex, id);
            if (!seenLoot.Add(key)) continue;
            lootHistory.Enqueue(key);
            // Keep recent resolved entries too: window reopenings and slot reordering cannot repeat messages.
            while (lootHistory.Count > 4096) seenLoot.Remove(lootHistory.Dequeue());
            var labels = Labels(missingBySet.Where(s => s.Missing.Contains(id)).Select(s => s.Set));
            if (labels.Count > 0) notices.Add(new(id, labels));
        }
        return notices;
    }

    private void SelectCharacter(ulong characterId)
    {
        if (characterId == 0 || characterId == owner) return;
        owner = characterId;
        knownItems.Clear(); seenLoot.Clear(); lootHistory.Clear(); needsBaseline = true;
    }

    private static List<string> Labels(IEnumerable<TrackedSet> sets) => sets
        .Select(s => $"{s.Name} ({(s.IsGlam ? "Glam" : s.Job)})").Distinct().ToList();
}
