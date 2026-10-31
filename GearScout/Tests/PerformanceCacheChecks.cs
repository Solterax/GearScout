using GearScout;

internal static class PerformanceCacheChecks
{
    public static void Run(Action<bool, string> check)
    {
        var cache = new OwnershipCache();
        var checkedAt = DateTimeOffset.Parse("2026-10-31T00:00:00Z");
        StorageSnapshot Source(ulong owner, uint item, DateTimeOffset? time = null) => new()
        {
            CharacterId = owner, Key = "dresser", Name = "Dresser", CheckedAtUtc = time ?? checkedAt,
            Items = [new() { ItemId = item, Quantity = 1, Materia = [], HighQuality = false }],
        };
        var snapshots = new List<StorageSnapshot> { Source(1, 10), Source(2, 20) };
        var copy = new OwnedItem(1, 1, "Bag", false, new uint[] { 1, 2 }) { Container = 0, InventorySlot = 2, HighQuality = true };
        var merged = cache.Merge([copy], snapshots, 1)!;
        check(merged.Select(i => i.ItemId).SequenceEqual(new uint[] { 1, 10 }), "Cached projection includes only the current character's storage");
        check(ReferenceEquals(merged, cache.Merge([copy with { Materia = new uint[] { 1, 2 } }], snapshots, 1)),
            "Fresh equal live readings reuse the ownership result");
        snapshots[1] = Source(2, 30);
        check(ReferenceEquals(merged, cache.Merge([copy], snapshots, 1)), "Another character's storage changes do not rebuild current ownership");
        foreach (var changed in new[] { copy with { Quantity = 2 }, copy with { InventorySlot = 3 }, copy with { Container = 1 },
            copy with { HighQuality = false }, copy with { Materia = new uint[] { 2, 1 } }, copy with { Materia = null }, copy with { ItemId = 99 } })
        {
            var update = cache.Merge([changed], snapshots, 1)!;
            check(!ReferenceEquals(merged, update) && update[0] == changed && ReferenceEquals(merged[1], update[1]),
                "Live changes update ownership while reusing unchanged saved item projections");
        }
        snapshots[0] = Source(1, 11);
        merged = cache.Merge([copy], snapshots, 1)!;
        check(merged.Any(i => i.ItemId == 11) && merged.All(i => i.ItemId != 10), "Replacing a snapshot invalidates saved projections");
        snapshots[0] = Source(1, 11, checkedAt.AddMinutes(1));
        var timestampUpdate = cache.Merge([copy], snapshots, 1)!;
        check(!ReferenceEquals(merged, timestampUpdate) && timestampUpdate[1].LastCheckedUtc == checkedAt.AddMinutes(1),
            "Refreshed storage timestamps reach ownership tooltips");
        snapshots.RemoveAt(0);
        check(cache.Merge([copy], snapshots, 1)!.Count == 1, "Forgetting storage removes its cached ownership immediately");
        snapshots.Add(Source(1, 12));
        check(cache.Merge([copy], snapshots, 1)!.Any(i => i.ItemId == 12), "Newly checked storage joins the projection");
        check(cache.Merge([], snapshots, 2)!.Single().ItemId == 30, "Character changes rebuild only the new character's projection");
        check(cache.Merge(null, snapshots, 1) == null && cache.Merge([copy], snapshots, 0) == null,
            "Unknown inventory or character never returns cached ownership");
        merged = cache.Merge([copy], snapshots, 1)!; cache.Reset();
        check(!ReferenceEquals(merged, cache.Merge([copy], snapshots, 1)), "Reset releases the old ownership result");

        var matcher = new SetMatchCache();
        var target = new GearPiece { Slot = "Head", ItemId = 100, Materia = [1] };
        var set = new TrackedSet { Job = "BRD", Pieces = [target] };
        var a = copy with { ItemId = 100, Materia = new uint[] { 1 } };
        var b = copy with { ItemId = 100, InventorySlot = 3, Materia = new uint[] { 2 } };
        List<OwnedItem> inventory = [a, b];
        var statuses = matcher.Get(set, inventory, 1);
        check(ReferenceEquals(statuses, matcher.Get(set, inventory, 1)) && statuses[0].Match == a,
            "Unchanged checklist draws reuse their physical-copy assignments");
        target.Materia = [2];
        var edited = matcher.Get(set, inventory, 2);
        check(!ReferenceEquals(statuses, edited) && edited[0].Match == b, "Saved target edits invalidate cached materia-copy choices");
        var removed = matcher.Get(set, [a], 2);
        check(removed[0].Match == a, "Inventory replacement invalidates the checklist cache");
        var other = new TrackedSet { Job = "BRD", Pieces = [new() { Slot = "Head", ItemId = 999 }] };
        check(matcher.Get(other, inventory, 2)[0].Match == null, "Set selection changes cannot reuse another set's matches");
        matcher.Clear();
        check(!ReferenceEquals(edited, matcher.Get(set, inventory, 2)), "Closing the checklist releases its cached result");

        var glam = new TrackedSet { IsGlam = true, Pieces = [new() { Slot = "Head", ItemId = 100, Materia = [2] }, new() { Slot = "Body", ItemId = 999 }] };
        var glamMatches = GearMatcher.Match(glam.Pieces, inventory, true);
        check(glamMatches[0].Match == b && glamMatches[1].Match == null,
            "Direct glam matching preserves best-copy scoring and missing-item results");
        var tracker = new WantedItemTracker();
        tracker.ObserveOwnership(1, [set], inventory, inventory);
        check(tracker.ObserveOwnership(1, [set], inventory, inventory).Count == 0, "Unchanged ownership stays silent on the cached notification path");
        tracker.Suspend();
        check(tracker.ObserveOwnership(1, [set], inventory, inventory).Count == 0, "Suspending re-baselines ownership even when its identity is unchanged");
    }
}
