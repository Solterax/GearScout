namespace GearScout;

public static class CurrentGearSet
{
    public static TrackedSet Build(string name, string job, IReadOnlyCollection<OwnedItem>? inventory)
    {
        name = name.Trim();
        if (name.Length is 0 or > 120) throw new ArgumentException("Enter a set name between 1 and 120 characters.");
        if (GearSearch.JobBit(job) == 0) throw new ArgumentException("Wait for a valid current class or job.");
        if (inventory == null) throw new InvalidOperationException("Equipped gear is unavailable. Finish loading and try again.");
        var equipped = inventory.Where(i => i.IsLive && i.Equipped && i.Container == 1000 &&
            GearSearch.Slots.Any(slot => EquipSlots.Index(slot) == i.InventorySlot)).ToList();
        if (!equipped.Any(i => i.InventorySlot == 0) || equipped.Any(i => i.ItemId == 0 || i.Quantity <= 0) ||
            equipped.GroupBy(i => i.InventorySlot).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Equipped gear is incomplete or changing. Try again once your gear has finished updating.");
        return new TrackedSet
        {
            Name = name, Job = job.ToUpperInvariant(),
            Pieces = GearSearch.Slots.Select(slot => (Slot: slot, Item: equipped.FirstOrDefault(i => i.InventorySlot == EquipSlots.Index(slot))))
                .Where(p => p.Item != null).Select(p => new GearPiece
                {
                    Slot = p.Slot, ItemId = LootMatcher.BaseItemId(p.Item!.ItemId), Materia = p.Item.Materia?.ToList(),
                }).ToList(),
        };
    }
}
