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
            if (!forceTimestamp && previous.Name == incoming.Name && Fingerprint(previous.Items) == Fingerprint(incoming.Items) &&
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

    private static string Fingerprint(IEnumerable<StoredItem> items) => string.Join(";", items.Select(i =>
        $"{i.ItemId}:{i.Quantity}:{i.HighQuality}:" + (i.Materia == null ? "?" : string.Join(",", i.Materia)))
        .Order(StringComparer.Ordinal));
}
