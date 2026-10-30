using GearScout;

internal static class CurrentGearChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        OwnedItem Worn(uint id, int slot, List<uint>? melds = null) => new(id, 1, "Equipped", true, melds)
            { Container = 1000, InventorySlot = slot };
        var melds = new List<uint> { 101, 101, 102 };
        List<OwnedItem> worn =
        [
            Worn(10, 0, []), Worn(20, 2, melds), Worn(30, 11, [101]), Worn(30, 12, [102]),
            Worn(999, 13), Worn(998, 5),
            new(888, 1, "Bag", false, []) { Container = 0, InventorySlot = 3 },
            Worn(777, 3) with { LastCheckedUtc = DateTimeOffset.UtcNow },
        ];
        var set = CurrentGearSet.Build("  My Bard gear  ", "brd", worn);
        check(set.Name == "My Bard gear" && set.Job == "BRD" && !set.IsGlam && set.SourceUrl == "" && set.AutoEquipTerritories.Count == 0,
            "Current gear becomes a named normal local set for the current job without zone assignments");
        check(set.Pieces.Select(p => p.Slot).SequenceEqual(new[] { "Weapon", "Head", "RingLeft", "RingRight" }),
            "Capture keeps real equipped slots and skips empty slots, soul crystal, waist, bags and saved storage");
        check(set.Pieces.Where(p => p.ItemId == 30).Count() == 2 &&
            set.Pieces.Single(p => p.Slot == "RingLeft").Materia!.SequenceEqual(new uint[] { 101 }) &&
            set.Pieces.Single(p => p.Slot == "RingRight").Materia!.SequenceEqual(new uint[] { 102 }),
            "Identical rings retain distinct left/right slots and their own materia");
        check(set.Pieces.Single(p => p.Slot == "Head").Materia!.SequenceEqual(new uint[] { 101, 101, 102 }),
            "Current-gear capture preserves repeated materia and socket order");
        melds.Clear(); worn.Clear();
        check(set.Pieces.Count == 4 && set.Pieces.Single(p => p.Slot == "Head").Materia!.Count == 3,
            "Saved gear and materia are independent copies of the inventory reading");
        check(CurrentGearSet.Build("Again", "BRD", [Worn(10, 0, [])]).Id != set.Id,
            "Saving another current set creates a new identity rather than overwriting an existing set");
        var unknown = CurrentGearSet.Build("Unknown melds", "BRD", [Worn(10, 0)]);
        check(unknown.Pieces[0].Materia == null && CurrentGearSet.Build("Empty melds", "BRD", [Worn(10, 0, [])]).Pieces[0].Materia!.Count == 0,
            "Unreadable materia stays unknown while verified unmelded gear has an empty target");
        check(CurrentGearSet.Build("HQ", "BRD", [Worn(1_000_010, 0)]).Pieces[0].ItemId == 10,
            "Captured item IDs use the normal gear-set base ID");
        reject(() => CurrentGearSet.Build("   ", "BRD", [Worn(10, 0)]), "Blank current-gear names cannot be saved");
        reject(() => CurrentGearSet.Build(new string('x', 121), "BRD", [Worn(10, 0)]), "Current-gear names obey the set name length limit");
        reject(() => CurrentGearSet.Build("Test", "INVALID", [Worn(10, 0)]), "Unknown jobs cannot be captured as a normal set");
        void Unavailable(Action action, string label)
        {
            var rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, label);
        }
        Unavailable(() => CurrentGearSet.Build("Test", "BRD", null), "Unavailable equipment cannot be saved as an empty set");
        Unavailable(() => CurrentGearSet.Build("Test", "BRD", []), "An empty equipment read is rejected");
        Unavailable(() => CurrentGearSet.Build("Test", "BRD", [Worn(20, 2)]), "Missing main hand rejects incomplete equipment data");
        Unavailable(() => CurrentGearSet.Build("Test", "BRD", [Worn(10, 0), Worn(20, 0)]), "Conflicting physical slots reject a changing equipment read");
        Unavailable(() => CurrentGearSet.Build("Test", "BRD", [Worn(10, 0) with { Quantity = 0 }]), "Invalid equipped quantities cannot create a gear target");
    }
}
