namespace GearScout;

public sealed class StoredItem
{
    public uint ItemId { get; set; }
    public int Quantity { get; set; }
    public List<uint>? Materia { get; set; }
    public bool? HighQuality { get; set; }
}

public sealed class StorageSnapshot
{
    public ulong CharacterId { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset CheckedAtUtc { get; set; }
    public List<StoredItem> Items { get; set; } = [];
}

public static class StorageCache
{
    public static bool Replace(List<StorageSnapshot> snapshots, StorageSnapshot incoming, bool forceTimestamp = false)
    {
        if (incoming.CharacterId == 0 || string.IsNullOrWhiteSpace(incoming.Key))
            throw new ArgumentException("Storage checks must identify a character and a storage source.");
        if (incoming.Items.Any(i => i.ItemId == 0 || i.Quantity <= 0))
            throw new ArgumentException("Invalid item in storage check.");
        var index = snapshots.FindIndex(s => s.CharacterId == incoming.CharacterId && s.Key == incoming.Key);
        if (index >= 0)
        {
            var previous = snapshots[index];
            // Store item changes immediately; refresh unchanged timestamps at most every 30 seconds.
            if (!forceTimestamp && previous.Name == incoming.Name && SameItems(previous.Items, incoming.Items) &&
                incoming.CheckedAtUtc - previous.CheckedAtUtc < TimeSpan.FromSeconds(30)) return false;
            snapshots[index] = incoming;
        }
        else snapshots.Add(incoming);
        return true;
    }

    public static List<OwnedItem>? Merge(IReadOnlyCollection<OwnedItem>? live,
        IEnumerable<StorageSnapshot> snapshots, ulong characterId)
    {
        // Live bags must be available before declaring anything missing or allocating duplicate copies.
        if (live == null || characterId == 0) return null;
        return live.Concat(snapshots.Where(s => s.CharacterId == characterId)
            .SelectMany(s => s.Items.Select(i => new OwnedItem(i.ItemId, i.Quantity,
                s.Name + " (saved)", false, i.Materia, s.CheckedAtUtc) { HighQuality = i.HighQuality, StorageKey = s.Key }))).ToList();
    }

    private static bool SameItems(List<StoredItem> left, List<StoredItem> right)
    {
        if (left.Count != right.Count) return false;
        // Scanners normally return the same slot order. Compare directly without sorting or strings.
        if (left.SequenceEqual(right, ItemComparer.Instance)) return true;
        // Storage ordering may change without changing contents; preserve duplicate multiplicities.
        var counts = new Dictionary<StoredItem, int>(ItemComparer.Instance);
        foreach (var item in left) counts[item] = counts.GetValueOrDefault(item) + 1;
        foreach (var item in right)
        {
            if (!counts.TryGetValue(item, out var count) || count == 0) return false;
            counts[item] = count - 1;
        }
        return true;
    }

    public static bool SameOwnership(IReadOnlyList<OwnedItem>? left, IReadOnlyList<OwnedItem>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            var a = left[index]; var b = right[index];
            if (a.ItemId != b.ItemId || a.Quantity != b.Quantity || a.Location != b.Location || a.Equipped != b.Equipped ||
                a.Container != b.Container || a.InventorySlot != b.InventorySlot || a.HighQuality != b.HighQuality ||
                a.StorageKey != b.StorageKey || a.LastCheckedUtc != b.LastCheckedUtc || !SameMateria(a.Materia, b.Materia)) return false;
        }
        return true;
    }

    private static bool SameMateria(IReadOnlyList<uint>? a, IReadOnlyList<uint>? b) =>
        ReferenceEquals(a, b) || (a != null && b != null && a.SequenceEqual(b));

    private sealed class ItemComparer : IEqualityComparer<StoredItem>
    {
        public static readonly ItemComparer Instance = new();
        public bool Equals(StoredItem? a, StoredItem? b) => ReferenceEquals(a, b) ||
            (a != null && b != null && a.ItemId == b.ItemId && a.Quantity == b.Quantity &&
             a.HighQuality == b.HighQuality && SameMateria(a.Materia, b.Materia));
        public int GetHashCode(StoredItem item)
        {
            var hash = new HashCode();
            hash.Add(item.ItemId); hash.Add(item.Quantity); hash.Add(item.HighQuality);
            hash.Add(item.Materia != null);
            if (item.Materia != null) foreach (var id in item.Materia) hash.Add(id);
            return hash.ToHashCode();
        }
    }
}
