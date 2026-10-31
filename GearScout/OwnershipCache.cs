namespace GearScout;

// Published storage snapshots are replaced, never edited in place (StorageCache.Replace).
// Keep their managed projections until the snapshot identities or character change.
public sealed class OwnershipCache
{
    private ulong owner;
    private readonly List<StorageSnapshot> sources = [];
    private readonly List<OwnedItem> stored = [];
    private IReadOnlyList<OwnedItem>? previousLive;
    private List<OwnedItem>? merged;

    public void Reset()
    {
        owner = 0; sources.Clear(); stored.Clear(); previousLive = null; merged = null;
    }

    public List<OwnedItem>? Merge(IReadOnlyList<OwnedItem>? live, IReadOnlyList<StorageSnapshot> snapshots, ulong characterId)
    {
        if (live == null || characterId == 0) { Reset(); return null; }
        var changed = owner != characterId;
        var index = 0;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.CharacterId != characterId) continue;
            if (index >= sources.Count || !ReferenceEquals(sources[index], snapshot)) changed = true;
            index++;
        }
        changed |= index != sources.Count;
        if (changed)
        {
            sources.Clear(); stored.Clear(); owner = characterId;
            foreach (var snapshot in snapshots)
            {
                if (snapshot.CharacterId != characterId) continue;
                sources.Add(snapshot);
                var location = snapshot.Name + " (saved)";
                foreach (var item in snapshot.Items)
                    stored.Add(new(item.ItemId, item.Quantity, location, false, item.Materia, snapshot.CheckedAtUtc)
                        { HighQuality = item.HighQuality, StorageKey = snapshot.Key });
            }
        }
        if (merged != null && !changed && StorageCache.SameOwnership(previousLive, live)) return merged;
        previousLive = live;
        merged = new List<OwnedItem>(live.Count + stored.Count);
        merged.AddRange(live); merged.AddRange(stored);
        return merged;
    }
}
