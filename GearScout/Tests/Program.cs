using GearScout;

var tests = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    tests++;
    Console.WriteLine("PASS: " + name);
}
void Reject(Action action, string name)
{
    var rejected = false;
    try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { rejected = true; }
    Check(rejected, name);
}

var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "bard.json"));
var example = XivGearImporter.Parse(fixture, "https://xivgear.app/sl/aadb9265-3645-45ae-8af6-3215eb26d637");
Check(example.Count == 1 && example[0].Job == "BRD" && example[0].Pieces.Count == 11, "Live example imports 11 Bard pieces");
Check(example[0].Pieces.Single(p => p.Slot == "Weapon").ItemId == 51004, "Relic weapon keeps its item ID");
var sheet = """
{"job":"BRD","sets":[{"name":"A","items":{"Weapon":{"id":1}}},{"isSeparator":true},{"name":"B","items":{"Head":{"id":2},"OffHand":null}}]}
""";
var parsed = XivGearImporter.Parse(sheet, "test");
Check(parsed.Count == 2 && parsed.All(s => s.Job == "BRD"), "Multi-set sheet inherits job and skips separators");
Check(parsed[1].Pieces.Count == 1, "Null slots do not become missing pieces");
Reject(() => XivGearImporter.Parse("{\"items\":{\"Weapon\":{\"id\":1}}}", "test"), "Missing job rejected instead of showing on wrong class");
Reject(() => XivGearImporter.Parse("{\"job\":\"BRD\",\"items\":{\"Weapon\":{\"id\":-1}}}", "test"), "Custom negative item IDs rejected");
Reject(() => XivGearImporter.Parse("{\"job\":\"BRD\",\"sets\":[]}", "test"), "Empty sheet rejected");
foreach (var bad in new[] { "https://evil.example/sl/test", "http://xivgear.app/sl/test", "https://xivgear.app.evil.example/x", "https://user@xivgear.app/x" })
    Reject(() => XivGearImporter.ValidateUrl(bad), "Reject URL " + bad);
Check(XivGearImporter.ValidateUrl("https://xivgear.app/#/sl/test").Fragment == "#/sl/test", "Legacy hash links retained");
var rings = new[] { new GearPiece { Slot = "RingLeft", ItemId = 1 }, new GearPiece { Slot = "RingRight", ItemId = 1 } };
var oneRing = GearMatcher.Match(rings, [new(1, 1, "Equipped", true)]);
Check(oneRing.Count(s => s.Match != null) == 1, "One ring does not satisfy two slots");
Check(GearMatcher.Match(rings, [new(1, 1, "Bag", false), new(1, 1, "Equipped", true)]).All(s => s.Match != null), "Two copies satisfy both ring slots");
Check(GearMatcher.Match(rings, [new(1, 1, "Bag", false), new(1, 1, "Equipped", true)])[0].Match!.Equipped, "Equipped copy allocated first");
Check(GearMatcher.Match([new GearPiece { ItemId = 10 }], [new(11, 1, "Bag", false)])[0].Match == null, "Different item IDs do not match");
Check(GearMatcher.Match(rings, []).All(s => s.Match == null), "Empty inventory produces missing pieces");

Check(example[0].Pieces.Single(p => p.Slot == "Body").Materia!.SequenceEqual(new uint[] { 41772, 41772 }), "Import retains duplicate materia item IDs");
Check(example[0].Pieces.Single(p => p.Slot == "Weapon").Materia is { Count: 0 }, "Empty relic materia is a known empty target");
Check(parsed[0].Pieces[0].Materia == null, "Absent materia remains unknown for old imports");
var blanks = XivGearImporter.Parse("""{"job":"BRD","items":{"Head":{"id":1,"materia":[{"id":-1},null,{"id":0},{"id":41772}]}}}""", "test");
Check(blanks[0].Pieces[0].Materia!.SequenceEqual(new uint[] { 41772 }), "Empty materia placeholders are skipped");
Reject(() => XivGearImporter.Parse("""{"job":"BRD","items":{"Head":{"id":1,"materia":[{"id":"wrong"}]}}}""", "test"), "Invalid materia ID rejected");
Reject(() => XivGearImporter.Parse("""{"job":"BRD","items":{"Head":{"id":1,"materia":[{"id":-2}]}}}""", "test"), "Unsupported negative materia ID rejected");
Reject(() => XivGearImporter.Parse("""{"job":"BRD","items":{"Head":{"id":1,"materia":[{},{},{},{},{},{}]}}}""", "test"), "More than five meld slots rejected");
Check(MateriaMatcher.Compare([1, 2], [2, 1]).IsExact, "Materia order does not affect correctness");
var duplicateMelds = MateriaMatcher.Compare([1, 1], [1]);
Check(duplicateMelds.Matched == 1 && duplicateMelds.Missing.SequenceEqual(new uint[] { 1 }), "Repeated materia requires repeated melds");
var wrongGrade = MateriaMatcher.Compare([41772], [41771]);
Check(!wrongGrade.IsExact && wrongGrade.Missing.Count == 1 && wrongGrade.Unexpected.Count == 1, "Different materia grades do not match");
Check(MateriaMatcher.Compare([], []).IsExact, "Empty target matches unmelded gear");
Check(!MateriaMatcher.Compare([], [1]).IsExact, "Extra materia does not match an empty target");
Check(MateriaMatcher.Compare([1, 2, 3, 4, 5], [5, 4, 3, 2, 1]).IsExact, "All five overmeld slots compared");
var targetA = new GearPiece { ItemId = 10, Materia = [1] };
var targetB = new GearPiece { ItemId = 10, Materia = [2] };
OwnedItem Copy(uint[] melds, bool equipped = false) => new(10, 1, equipped ? "Equipped" : "Bag", equipped, melds);
var bestCopy = GearMatcher.Match([targetA], [Copy([2], true), Copy([1])])[0];
Check(bestCopy.Melds!.IsExact && !bestCopy.Match!.Equipped, "Correctly melded copy preferred over worn wrong copy");
var reversedRings = GearMatcher.Match([targetA, targetB], [Copy([2], true), Copy([1])]);
Check(reversedRings.All(s => s.Melds!.IsExact), "Duplicate rings assigned to their matching materia targets");
var scarce = GearMatcher.Match([targetA, targetB], [Copy([2])]);
Check(scarce[0].Match == null && scarce[1].Melds!.IsExact, "Scarce ring reserved for the target it actually completes");
var noGreedyTrap = GearMatcher.Match([targetA, targetB], [Copy([1, 2], true), Copy([1])]);
Check(noGreedyTrap[0].Melds!.IsExact && noGreedyTrap[1].Melds!.Matched == 1, "Group assignment avoids consuming the only exact match for a later slot");
Check(GearMatcher.Match([targetA], [new(10, 1, "Bag", false)])[0].Melds == null, "Unreadable owned materia is unknown, not incorrect");
Check(GearMatcher.Match([new GearPiece { ItemId = 10 }], [Copy([])])[0].Melds == null, "Legacy saved targets require refresh rather than claiming correct melds");
var saved = System.Text.Json.JsonSerializer.Serialize(example);
var restored = System.Text.Json.JsonSerializer.Deserialize<List<TrackedSet>>(saved)!;
Check(restored[0].Pieces.Single(p => p.Slot == "Body").Materia!.SequenceEqual(new uint[] { 41772, 41772 }), "Materia targets survive configuration serialization");

var lootSets = new[]
{
    new TrackedSet { Job = "BRD", Pieces = [new() { ItemId = 10 }] },
    new TrackedSet { Job = "BRD", Pieces = [new() { ItemId = 20 }] },
    new TrackedSet { Job = "WAR", Pieces = [new() { ItemId = 30 }] },
};
var lootTargets = LootMatcher.TargetItems(lootSets);
Check(lootTargets.SetEquals(new uint[] { 10, 20, 30 }), "Loot always checks every saved job");
var farmingSets = new[] { new TrackedSet { Job = "WAR", Pieces = [new() { ItemId = 20871 }] } };
var farmingTargets = LootMatcher.MissingItems(farmingSets, []);
Check(LootMatcher.CanHighlight(20871, 0, [new(20871, true)], farmingTargets), "Skallic Amulet of Fending can glow while farming Warrior gear on Black Mage");
Check(!LootMatcher.CanHighlight(20871, 0, [new(20871, true)], LootMatcher.MissingItems(farmingSets, [new(20871, 1, "Bag", false)])), "Other-job loot still stays unlit when already owned");
Check(LootMatcher.CanHighlight(10, 0, [new(10, true)], lootTargets), "Eligible target loot highlighted");
Check(LootMatcher.CanHighlight(1_000_010, 0, [new(10, true)], lootTargets), "HQ loot normalized to imported base item ID");
Check(!LootMatcher.CanHighlight(10, 0, [new(10, false)], lootTargets), "Rolled, passed or unavailable items not highlighted");
Check(!LootMatcher.CanHighlight(40, 0, [new(40, true)], lootTargets), "Non-target loot not highlighted");
Check(!LootMatcher.CanHighlight(0, 0, [new(0, true)], lootTargets), "Empty loot slots not highlighted");
Check(LootMatcher.CanHighlight(20, 0, [new(10, false), new(20, true)], lootTargets), "Reordered distinct loot slots matched by item ID");
Check(!LootMatcher.CanHighlight(10, 5, [new(10, true), new(10, false)], lootTargets), "Ambiguous duplicate loot not assigned another copy's roll permission");
Check(LootMatcher.CanHighlight(10, 1, [new(10, false), new(10, true)], lootTargets), "Duplicate loot uses the correct slot's roll state");
Check(!LootMatcher.CanHighlight(10, 0, [new(10, false), new(10, true)], lootTargets), "Resolved duplicate does not inherit an eligible copy's glow");
Check(!LootMatcher.CanHighlight(10, 0, [], lootTargets), "No live roll data means no glow");
Check(!LootMatcher.CanHighlight(10, 0, [new(10, true)], LootMatcher.TargetItems([])), "Removing a saved target removes its loot highlight");

var missingTargets = LootMatcher.MissingItems(lootSets, [new(10, 1, "Equipped", true)]);
Check(missingTargets.SetEquals(new uint[] { 20, 30 }), "Owned gear excluded while wanted missing gear from all jobs remains eligible");
Check(!LootMatcher.CanHighlight(10, 0, [new(10, true)], missingTargets), "Already owned wanted loot does not glow");
Check(LootMatcher.CanHighlight(20, 0, [new(20, true)], missingTargets), "Wanted missing loot can glow");
Check(LootMatcher.MissingItems(lootSets, null).Count == 0, "Unknown inventory suppresses all loot glow");
Check(LootMatcher.MissingItems(lootSets, []).SetEquals(lootTargets), "Known empty inventory permits missing targets");
foreach (var location in new[] { "Bag 1", "Armoury Chest", "Equipped" })
    Check(!LootMatcher.MissingItems(lootSets, [new(10, 1, location, location == "Equipped")]).Contains(10),
        "Owned item in " + location + " suppresses loot glow");
var twoRingSet = new TrackedSet { Job = "BRD", Pieces = rings.ToList() };
Check(LootMatcher.MissingItems([twoRingSet], [new(1, 1, "Equipped", true)]).Contains(1), "One ring still glows when the same set needs a second copy");
Check(!LootMatcher.MissingItems([twoRingSet], [new(1, 1, "Equipped", true), new(1, 1, "Bag", false)]).Contains(1), "Two owned copies suppress two-ring target glow");
Check(!LootMatcher.MissingItems([lootSets[0], lootSets[0]], [new(10, 1, "Bag", false)]).Contains(10), "Alternative saved sets share one owned copy instead of demanding duplicates");
Check(LootMatcher.MissingItems(lootSets, [new(10, 1, "Bag", false), new(20, 1, "Bag", false)]).SetEquals(new uint[] { 30 }), "All-jobs scope still excludes owned targets");
Check(!LootMatcher.MissingItems([new TrackedSet { Job = "BRD", Pieces = [targetA] }], [Copy([2])]).Contains(10), "Incorrect materia does not make an owned gear item missing");
Check(LootMatcher.MissingItems(lootSets, [new(10, 0, "Bag", false)]).Contains(10), "Zero-quantity inventory entry does not suppress missing loot");
Check(!LootMatcher.MissingItems(lootSets, [new(1_000_010, 1, "Bag", false)]).Contains(10), "Owned HQ item suppresses target glow");

var checkedAt = DateTimeOffset.Parse("2026-10-28T00:00:00Z");
var storage = new List<StorageSnapshot>();
StorageSnapshot Snapshot(ulong owner, string key, params StoredItem[] items) => new()
{
    CharacterId = owner, Key = key, Name = key, CheckedAtUtc = checkedAt, Items = items.ToList(),
};
Check(StorageCache.Replace(storage, Snapshot(100, "retainer:1:bags", new StoredItem { ItemId = 10, Quantity = 1, Materia = [1] })), "First complete retainer check saved");
StorageCache.Replace(storage, Snapshot(200, "retainer:1:bags", new StoredItem { ItemId = 20, Quantity = 1 }));
var merged = StorageCache.Merge([], storage, 100)!;
Check(merged.Count == 1 && merged[0].ItemId == 10, "Storage ownership isolated to logged-in character");
Check(merged[0].LastCheckedUtc == checkedAt && merged[0].Location.Contains("saved"), "Saved source and check time preserved for ownership UI");
Check(!LootMatcher.MissingItems(lootSets, merged).Contains(10), "Saved retainer gear suppresses missing loot glow away from the bell");
Check(GearMatcher.Match([targetA], merged)[0].Melds!.IsExact, "Retainer materia preserved in saved ownership");
Check(StorageCache.Merge(null, storage, 100) == null, "Unreadable live inventory stays unknown with cached storage");
Check(StorageCache.Merge([], storage, 0) == null, "No character means no storage ownership");
Check(StorageCache.Merge([], storage, 300)!.Count == 0, "Unvisited character never inherits another character's gear");
Check(!StorageCache.Replace(storage, Snapshot(100, "retainer:1:bags", new StoredItem { ItemId = 10, Quantity = 1, Materia = [1] })), "Unchanged scans avoid repeated local saves");
var refreshed = Snapshot(100, "retainer:1:bags", new StoredItem { ItemId = 10, Quantity = 1, Materia = [1] });
refreshed.CheckedAtUtc = checkedAt.AddSeconds(31);
Check(StorageCache.Replace(storage, refreshed), "Revisits refresh last checked timestamp");
var explicitRefresh = Snapshot(100, "retainer:1:bags", new StoredItem { ItemId = 10, Quantity = 1, Materia = [1] });
explicitRefresh.CheckedAtUtc = checkedAt.AddSeconds(32);
Check(StorageCache.Replace(storage, explicitRefresh, true), "Explicit check forces a fresh timestamp");
Check(StorageCache.Replace(storage, Snapshot(100, "retainer:1:bags")), "Verified empty storage replaces old contents");
Check(StorageCache.Merge([], storage, 100)!.Count == 0, "Removed gear no longer counts after rescanning");
Check(storage.Count(s => s.CharacterId == 100 && s.Key == "retainer:1:bags") == 1, "Rechecks replace snapshots instead of duplicating copies");
StorageCache.Replace(storage, Snapshot(100, "dresser", new StoredItem { ItemId = 10, Quantity = 1, Materia = [] }));
StorageCache.Replace(storage, Snapshot(100, "armoire", new StoredItem { ItemId = 1, Quantity = 1, Materia = [] }));
var wardrobe = StorageCache.Merge([new OwnedItem(1, 1, "Bag", false)], storage, 100)!;
Check(GearMatcher.Match(rings, wardrobe).All(s => s.Match != null), "Live and armoire copies combine for a two-ring target");
Check(GearMatcher.Match([new GearPiece { ItemId = 10, Materia = [] }], wardrobe)[0].Melds!.IsExact, "Dresser gear records known empty melds");
var serializedStorage = System.Text.Json.JsonSerializer.Serialize(storage);
var reloadedStorage = System.Text.Json.JsonSerializer.Deserialize<List<StorageSnapshot>>(serializedStorage)!;
Check(StorageCache.Merge([], reloadedStorage, 100)!.Count == 2, "Storage contents survive local serialization and reload");
Check(StorageCache.Merge([], reloadedStorage, 200)!.Single().ItemId == 20, "Character isolation survives local serialization");
storage.RemoveAll(s => s.CharacterId == 100 && s.Key == "dresser");
Check(!StorageCache.Merge([], storage, 100)!.Any(i => i.ItemId == 10), "Forgetting stale storage removes its ownership contribution");
Reject(() => StorageCache.Replace(storage, Snapshot(0, "dresser")), "Storage scan without character identity rejected");
Reject(() => StorageCache.Replace(storage, Snapshot(100, "bad", new StoredItem { ItemId = 0, Quantity = 1 })), "Invalid storage entries cannot erase a saved check");

var bard = GearSearch.JobBit("BRD");
var warrior = GearSearch.JobBit("WAR");
var headSlot = GearSearch.SlotBit("Head");
var bodySlot = GearSearch.SlotBit("Body");
var ringSlots = GearSearch.SlotBit("RingLeft") | GearSearch.SlotBit("RingRight");
GearCandidate[] catalog =
[
    new(100, "Bard Test Hat", 700, 100, headSlot, bard, false),
    new(101, "Warrior Test Hat", 790, 100, headSlot, warrior, false),
    new(102, "Bard Test Coat", 790, 100, bodySlot, bard, false),
    new(103, "Bard Better Hat", 790, 100, headSlot, bard, false),
    new(104, "Shared Test Hat", 600, 90, headSlot, bard | warrior, false),
    new(105, "Unique Test Ring", 790, 100, ringSlots, bard, true),
    new(106, "Common Test Ring", 700, 100, ringSlots, bard, false),
];
Check(GearSearch.Search(catalog, "BRD", "Head", "").Select(i => i.ItemId).SequenceEqual(new uint[] { 103, 100, 104 }), "Editor search filters job and slot and orders by item level");
Check(GearSearch.Search(catalog, "WAR", "Head", "test").Select(i => i.ItemId).SequenceEqual(new uint[] { 101, 104 }), "Shared gear appears only for allowed jobs");
Check(GearSearch.Search(catalog, "brd", "Head", "TEST BARD").Single().ItemId == 100, "Gear search matches words case-insensitively in any order");
Check(GearSearch.Search(catalog, "BRD", "Head", "103").Single().ItemId == 103, "Gear search accepts an exact item ID");
Check(GearSearch.Search(catalog, "BRD", "Body", "hat").Count == 0, "Search never offers a matching name in the wrong slot");
Check(GearSearch.Search(catalog, "BRD", "OffHand", "").Count == 0, "Jobs without matching off-hand gear get no invalid suggestions");
Check(GearSearch.Search(catalog, "unknown", "Head", "").Count == 0, "Unknown job does not reveal unrestricted search results");
Check(GearSearch.Fits(catalog[5], "BRD", "RingLeft") && GearSearch.Fits(catalog[5], "BRD", "RingRight"), "Ring candidates fit both ring slots");
var edited = new TrackedSet { Job = "BRD", Name = "Editor test" };
GearSearch.SetPiece(edited, "Head", catalog[0]);
Check(edited.Pieces.Single().ItemId == 100 && edited.Pieces[0].Materia == null, "New selected piece has an unspecified materia target");
edited.Pieces[0].Materia = [41772];
GearSearch.SetPiece(edited, "Head", catalog[0]);
Check(edited.Pieces[0].Materia!.Count == 1, "Reselecting the same item preserves its materia plan");
GearSearch.SetPiece(edited, "Head", catalog[3]);
Check(edited.Pieces.Count == 1 && edited.Pieces[0].ItemId == 103 && edited.Pieces[0].Materia == null, "Changing an item replaces the slot and clears old materia targets");
Reject(() => GearSearch.SetPiece(edited, "Head", catalog[1]), "Editor rejects gear for another job");
Reject(() => GearSearch.SetPiece(edited, "Body", catalog[0]), "Editor rejects gear in the wrong slot");
GearSearch.SetPiece(edited, "RingLeft", catalog[5]);
Reject(() => GearSearch.SetPiece(edited, "RingRight", catalog[5]), "Unique ring cannot be selected in both slots");
GearSearch.SetPiece(edited, "RingLeft", catalog[6]);
GearSearch.SetPiece(edited, "RingRight", catalog[6]);
Check(edited.Pieces.Count(p => p.ItemId == 106) == 2, "Non-unique identical rings remain supported");
var importedSource = new TrackedSet { Job = "BRD", Name = "Imported", SourceUrl = "https://xivgear.app/sl/test", Pieces = [new() { Slot = "Head", ItemId = 100, Materia = [41772] }] };
var customDraft = GearSearch.Draft(importedSource);
customDraft.Pieces[0].Materia!.Clear();
GearSearch.SetPiece(customDraft, "Head", catalog[3]);
Check(customDraft.Id != importedSource.Id && customDraft.SourceUrl == "" && importedSource.Pieces[0].ItemId == 100 && importedSource.Pieces[0].Materia!.Count == 1,
    "Editing an imported set makes an independent local copy without changing the source");
var localDraft = GearSearch.Draft(edited);
localDraft.Pieces.Clear();
Check(localDraft.Id == edited.Id && edited.Pieces.Count > 0, "Editing a local set retains identity and keeps changes isolated until saved");
var persistedSet = System.Text.Json.JsonSerializer.Deserialize<TrackedSet>(System.Text.Json.JsonSerializer.Serialize(edited))!;
Check(persistedSet.Id == edited.Id && persistedSet.Job == "BRD" && persistedSet.SourceUrl == "" && persistedSet.Pieces.Count == edited.Pieces.Count,
    "Locally built set survives serialization with its identity and pieces");
Check(LootMatcher.MissingItems([edited], []).Contains(103), "Locally built items participate in missing-loot checks");
Check(!LootMatcher.MissingItems([edited], [new(103, 1, "Bag", false)]).Contains(103), "Owned local-set gear suppresses its loot highlight");

var glam = new TrackedSet { IsGlam = true, Name = "Glam test" };
Check(GearSearch.VisibleForJob(glam, "BRD") && GearSearch.VisibleForJob(glam, "WAR") && GearSearch.VisibleForJob(glam, "BLM"), "Glam set stays visible across jobs without a job assignment");
Check(!GearSearch.VisibleForJob(edited, "WAR") && GearSearch.VisibleForJob(edited, "BRD"), "Regular sets keep their current-job visibility restriction");
Check(GearSearch.Search(catalog, "", "Head", "hat", true).Select(i => i.ItemId).SequenceEqual(new uint[] { 103, 101, 100, 104 }), "Glam search includes matching gear from all jobs");
Check(GearSearch.Search(catalog, "", "Body", "hat", true).Count == 0, "Glam search still enforces equipment slots");
GearSearch.SetPiece(glam, "Head", catalog[1]);
Check(glam.Job == "" && glam.Pieces.Single().ItemId == 101, "Job-independent glam set accepts job-restricted gear");
GearSearch.SetPiece(glam, "RingLeft", catalog[5]);
GearSearch.SetPiece(glam, "RingRight", catalog[5]);
Check(glam.Pieces.Count(p => p.ItemId == 105) == 2, "One unique ring appearance may be used in both glam slots");
var appearanceInventory = new[] { new OwnedItem(101, 1, "Glamour dresser (saved)", false), new OwnedItem(105, 1, "Armoire (saved)", false) };
Check(GearMatcher.Match(glam.Pieces, appearanceInventory, true).All(s => s.Match != null), "One stored appearance satisfies repeated glam slots");
Check(LootMatcher.MissingItems([glam], appearanceInventory).Count == 0, "Owned repeated appearances do not trigger glam loot highlights");
Check(LootMatcher.MissingItems([glam], []).SetEquals(new uint[] { 101, 105 }), "Missing glam appearances participate in all-job loot tracking");
var physicalRings = new TrackedSet { Job = "BRD", Pieces = [new() { Slot = "RingLeft", ItemId = 105 }, new() { Slot = "RingRight", ItemId = 105 }] };
Check(LootMatcher.MissingItems([glam, physicalRings], appearanceInventory).Contains(105), "Combat set still demands a second physical copy alongside a satisfied glam set");
var savedGlam = System.Text.Json.JsonSerializer.Deserialize<TrackedSet>(System.Text.Json.JsonSerializer.Serialize(glam))!;
Check(savedGlam.IsGlam && savedGlam.Job == "" && savedGlam.Pieces.Count == 3, "Glam mode and its unassigned job survive saving and reloading");
Check(GearSearch.Draft(glam).IsGlam && GearSearch.Draft(glam).Id == glam.Id, "Editing a glam set preserves its type and identity");
Check(!System.Text.Json.JsonSerializer.Deserialize<TrackedSet>("{\"Job\":\"BRD\"}")!.IsGlam, "Older saved job sets remain regular sets");

var glamList = new TrackedSet { IsGlam = true, Name = "Appearance collection" };
foreach (var item in catalog.Where(i => GearSearch.Fits(i, "", "Head", true)))
    GearSearch.SetPiece(glamList, "Head", item);
GearSearch.SetPiece(glamList, "Body", catalog[2]);
Check(glamList.Pieces.Count(p => p.Slot == "Head") == 4 && glamList.Pieces.Count(p => p.Slot == "Body") == 1,
    "Glam list retains multiple choices in a slot and independent choices in other slots");
GearSearch.SetPiece(glamList, "Head", catalog[0]);
Check(glamList.Pieces.Count == 5, "Checking an already-selected glam item never creates a duplicate in its slot");
GearSearch.SetPiece(glamList, "RingLeft", catalog[5]);
GearSearch.SetPiece(glamList, "RingRight", catalog[5]);
GearSearch.RemovePiece(glamList, "RingLeft", 105);
Check(glamList.Pieces.Any(p => p.Slot == "RingRight" && p.ItemId == 105) &&
    !glamList.Pieces.Any(p => p.Slot == "RingLeft"), "Removing one glam entry preserves the same item in another slot");
GearSearch.RemovePiece(glamList, "Head", 100);
Check(glamList.Pieces.Where(p => p.Slot == "Head").Select(p => p.ItemId).ToHashSet().SetEquals(new uint[] {101, 103, 104}) &&
    glamList.Pieces.Any(p => p.Slot == "Body"), "Removing a glam choice preserves its sibling choices and other slots");
GearSearch.SetPiece(glamList, "Head", catalog[0]);
Check(glamList.Pieces.Count(p => p.Slot == "Head") == 4, "An unchecked glam piece can be added again");
Reject(() => GearSearch.SetPiece(glamList, "Body", catalog[0]), "Multi-item glam lists still reject incorrect slots");
var listRoundTrip = System.Text.Json.JsonSerializer.Deserialize<TrackedSet>(System.Text.Json.JsonSerializer.Serialize(glamList))!;
Check(listRoundTrip.IsGlam && listRoundTrip.Id == glamList.Id &&
    listRoundTrip.Pieces.Select(p => (p.Slot, p.ItemId)).SequenceEqual(glamList.Pieces.Select(p => (p.Slot, p.ItemId))),
    "Every selected glam piece survives saving and reloading with the existing configuration format");
var listStatuses = GearMatcher.Match(listRoundTrip.Pieces, appearanceInventory, true);
Check(listStatuses.Count == 6 && listStatuses.Count(s => s.Match != null) == 2 &&
    listStatuses.Where(s => s.Match == null).Select(s => s.Piece.ItemId).ToHashSet().SetEquals(new uint[] {100, 102, 103, 104}),
    "Checklist accounts for every same-slot glam choice with its own ownership status");
Check(LootMatcher.MissingItems([listRoundTrip], appearanceInventory).SetEquals(new uint[] {100, 102, 103, 104}),
    "Every missing glam choice participates in loot tracking while owned choices are excluded");
var listDraft = GearSearch.Draft(listRoundTrip);
GearSearch.RemovePiece(listDraft, "Head", 101);
Check(listRoundTrip.Pieces.Any(p => p.Slot == "Head" && p.ItemId == 101) && listDraft.Pieces.Count == listRoundTrip.Pieces.Count - 1,
    "Editing or cancelling a multi-item glam draft does not alter the saved collection");
var largeGlamList = new TrackedSet { IsGlam = true };
foreach (var itemId in Enumerable.Range(1000, 50))
    GearSearch.SetPiece(largeGlamList, "Head", new((uint)itemId, "Collection item", 1, 1, headSlot, bard, false));
Check(largeGlamList.Pieces.Count == 50 && GearMatcher.Match(largeGlamList.Pieces, [], true).Count == 50,
    "Glam slots support lists larger than a full normal gear set without a slot-count cap");
foreach (var piece in largeGlamList.Pieces.ToArray()) GearSearch.RemovePiece(largeGlamList, piece.Slot, piece.ItemId);
Check(largeGlamList.Pieces.Count == 0, "All glam choices can be individually removed to leave an empty list");
var normalListGuard = new TrackedSet { Job = "BRD" };
GearSearch.SetPiece(normalListGuard, "Head", catalog[0]);
GearSearch.SetPiece(normalListGuard, "Body", catalog[2]);
GearSearch.SetPiece(normalListGuard, "Head", catalog[3]);
Check(normalListGuard.Pieces.Count == 2 && normalListGuard.Pieces.Single(p => p.Slot == "Head").ItemId == 103 &&
    normalListGuard.Pieces.Single(p => p.Slot == "Body").ItemId == 102,
    "Normal sets continue replacing the single chosen piece in a slot without changing other slots");

var noticeSet = new TrackedSet { Name = "Bard plan", Job = "BRD", Pieces = [new() { Slot = "Head", ItemId = 100 }] };
var noticeGlam = new TrackedSet { Name = "Hat collection", IsGlam = true, Pieces = [new() { Slot = "Head", ItemId = 100 }, new() { Slot = "Head", ItemId = 101 }] };
TrackedSet[] noticeSets = [noticeSet, noticeGlam];
OwnedItem[] hat100 = [new(100, 1, "Bag", false)];
var notices = new WantedItemTracker();
Check(notices.ObserveOwnership(100, noticeSets, hat100, hat100).Count == 0, "Startup baseline never announces already-owned target items");
Check(notices.ObserveOwnership(100, noticeSets, hat100, hat100).Count == 0, "Unchanged ownership never repeats acquisition messages");
var newFind = new WantedItemTracker();
newFind.ObserveOwnership(100, noticeSets, [], []);
var found = newFind.ObserveOwnership(100, noticeSets, hat100, hat100);
Check(found.Count == 1 && found[0].ItemId == 100 && found[0].Sets.SequenceEqual(new[] { "Bard plan (BRD)", "Hat collection (Glam)" }),
    "Newly obtained wanted item reports all matching normal and glam set names");
Check(newFind.ObserveOwnership(100, noticeSets, hat100, hat100).Count == 0, "Repeated scans do not repeat a new-item message");
Check(newFind.ObserveOwnership(100, noticeSets, [new(100, 1, "Equipped", true)], [new(100, 1, "Equipped", true)]).Count == 0,
    "Moving wanted gear between bags and equipment does not announce it again");
Check(newFind.ObserveOwnership(100, noticeSets, [new(100, 2, "Bag", false)], [new(100, 2, "Bag", false)]).Count == 0,
    "Additional copies of an already-known item do not produce acquisition messages");
newFind.ObserveOwnership(100, noticeSets, [], []);
Check(newFind.ObserveOwnership(100, noticeSets, hat100, hat100).Count == 0, "Removing then reacquiring known gear does not repeat its acquisition notice");
var untargeted = new WantedItemTracker();
untargeted.ObserveOwnership(100, [], [], []);
Check(untargeted.ObserveOwnership(100, [], hat100, hat100).Count == 0 &&
    untargeted.ObserveOwnership(100, noticeSets, hat100, hat100).Count == 0, "Non-target gains and later set edits do not announce owned items");
var cachedFind = new WantedItemTracker();
cachedFind.ObserveOwnership(100, noticeSets, [], [new(100, 1, "Retainer (saved)", false)]);
Check(cachedFind.ObserveOwnership(100, noticeSets, hat100, hat100).Count == 0, "Withdrawing a known saved-storage item is not a new acquisition");
var storageFind = new WantedItemTracker();
storageFind.ObserveOwnership(100, noticeSets, [], []);
Check(storageFind.ObserveOwnership(100, noticeSets, [], [new(100, 1, "Dresser (saved)", false)]).Count == 0 &&
    storageFind.ObserveOwnership(100, noticeSets, hat100, hat100).Count == 0, "Discovering existing storage contents is silent and prevents later withdrawal messages");
var transferFind = new WantedItemTracker();
transferFind.ObserveOwnership(100, noticeSets, [], []);
Check(transferFind.ObserveOwnership(100, noticeSets, hat100, hat100, true).Count == 0 &&
    transferFind.ObserveOwnership(100, noticeSets, hat100, hat100).Count == 0, "Active or just-closed storage transfers do not create new-item messages");
var interruptedFind = new WantedItemTracker();
interruptedFind.ObserveOwnership(100, noticeSets, [], []);
Check(interruptedFind.ObserveOwnership(100, noticeSets, null, null).Count == 0 &&
    interruptedFind.ObserveOwnership(100, noticeSets, hat100, hat100).Count == 0, "Unavailable inventory requires a new silent baseline");
interruptedFind.Suspend();
Check(interruptedFind.ObserveOwnership(100, noticeSets, [new(101, 1, "Bag", false)], [new(101, 1, "Bag", false)]).Count == 0,
    "Loading transitions cannot announce items discovered on resuming");
Check(newFind.ObserveOwnership(200, noticeSets, [new(101, 1, "Bag", false)], [new(101, 1, "Bag", false)]).Count == 0,
    "Changing character establishes an independent silent baseline");
var hqFind = new WantedItemTracker();
hqFind.ObserveOwnership(100, noticeSets, [], []);
Check(hqFind.ObserveOwnership(100, noticeSets, [new(1_000_100, 1, "Bag", false)], [new(1_000_100, 1, "Bag", false)]).Single().ItemId == 100,
    "Acquisition notices normalize HQ item IDs");
var dropFind = new WantedItemTracker();
WantedLootItem drop = new(500, 0, 100, true);
Check(dropFind.ObserveLoot(100, noticeSets, null, [drop]).Count == 0, "Unknown ownership cannot produce a wanted-drop message");
var dropped = dropFind.ObserveLoot(100, noticeSets, [], [drop]);
Check(dropped.Count == 1 && dropped[0].Sets.SequenceEqual(found[0].Sets), "Missing wanted drop names every set that still needs it");
Check(dropFind.ObserveLoot(100, noticeSets, [], [drop]).Count == 0, "Repeated loot scans announce the same chest item only once");
dropFind.ObserveLoot(100, noticeSets, [], []);
Check(dropFind.ObserveLoot(100, noticeSets, [], [drop]).Count == 0, "Closing or reopening the loot window cannot repeat a drop message");
Check(dropFind.ObserveLoot(100, noticeSets, [], [drop with { ChestItemIndex = 1 }]).Count == 1,
    "A second copy from a distinct chest entry is a separate new drop");
Check(dropFind.ObserveLoot(100, noticeSets, [], [drop with { ChestObjectId = 501 }]).Count == 1,
    "The same item from a different chest can produce a new drop notice");
Check(dropFind.ObserveLoot(100, noticeSets, hat100, [drop with { ChestObjectId = 502 }]).Count == 0,
    "Already-owned items suppress wanted-drop messages");
Check(dropFind.ObserveLoot(100, noticeSets, [], [drop with { ChestObjectId = 502 }]).Count == 0,
    "Losing an owned item cannot make its old loot entry announce itself later");
Check(dropFind.ObserveLoot(100, noticeSets, [], [new(503, 0, 999, true)]).Count == 0,
    "Unwanted loot never creates a message");
Check(dropFind.ObserveLoot(100, noticeSets, [], [new(504, 0, 100, false)]).Count == 0 &&
    dropFind.ObserveLoot(100, noticeSets, [], [new(504, 0, 100, true)]).Count == 1,
    "Loot waits for roll eligibility before announcing a newly loaded entry");
Check(dropFind.ObserveLoot(100, noticeSets, [], [new(505, 0, 1_000_100, true)]).Single().ItemId == 100,
    "Wanted-drop messages normalize HQ IDs");
Check(dropFind.ObserveLoot(200, noticeSets, [], [drop]).Count == 1,
    "Drop deduplication is isolated by character");

var retainerScan = new RetainerScanSession();
RetainerScanTarget[] retainers = [new(10, "First"), new(20, "Second"), new(10, "Duplicate"), new(0, "Invalid")];
var waiting = new RetainerScanObservation(100, true, false, false, false, 0, false, false);
Reject(() => retainerScan.Start([], 100, 0), "Empty retainer roster is rejected");
Reject(() => retainerScan.Start(retainers, 0, 0), "Retainer scan requires character identity");
retainerScan.Start(retainers, 100, 0);
Check(retainerScan.Total == 2 && retainerScan.Current!.Id == 10, "Retainer queue removes invalid and duplicate identities");
Check(retainerScan.Tick(waiting, 1) == RetainerScanAction.None, "Retainer scan waits for the bell list");
for (var i = 0; i < 2; i++)
{
    var id = retainers[i].Id;
    var now = i * 1000 + 10;
    Check(retainerScan.Tick(waiting with { ListReady = true }, now) == RetainerScanAction.SelectRetainer,
        "Select next available retainer from list");
    Check(retainerScan.Tick(waiting with { ActiveRetainerId = 99 }, now + 1) == RetainerScanAction.None && retainerScan.Running,
        "Old selected identity during loading does not cancel navigation");
    var menu = waiting with { MenuReady = true, ActiveRetainerId = id };
    Check(retainerScan.Tick(menu, now + 2) == RetainerScanAction.OpenInventory, "Open inventory only for the intended retainer");
    Check(retainerScan.Tick(menu, now + 3) == RetainerScanAction.None, "Do not repeat inventory callback while loading");
    Check(retainerScan.Tick(menu with { InventoryReady = true }, now + 4) == RetainerScanAction.None,
        "Loaded inventory stays open until its snapshot is saved");
    Check(retainerScan.Tick(menu with { SnapshotSaved = true }, now + 5) == RetainerScanAction.None,
        "Saved data alone cannot close an inventory that is not ready");
    var savedInventory = menu with { InventoryReady = true, SnapshotSaved = true };
    Check(retainerScan.Tick(savedInventory, now + 6) == RetainerScanAction.CloseInventory, "Close only after inventory is ready and persisted");
    Check(retainerScan.Tick(savedInventory, now + 7) == RetainerScanAction.None, "Do not quit until inventory finishes closing");
    Check(retainerScan.Tick(menu, now + 8) == RetainerScanAction.QuitRetainer, "Quit through the returned retainer menu");
    Check(retainerScan.Tick(waiting, now + 9) == RetainerScanAction.None && retainerScan.Completed == i,
        "Do not advance until the bell list returns");
    retainerScan.Tick(waiting with { ListReady = true }, now + 10);
    Check(retainerScan.Completed == i + 1, "Count each completed inventory once");
}
Check(!retainerScan.Running && retainerScan.Phase == RetainerScanPhase.Complete && retainerScan.Current == null,
    "Retainer scan finishes after the last retainer");
Check(retainerScan.Tick(waiting with { ListReady = true }, 5000) == RetainerScanAction.None, "Completed scan sends no callbacks");
foreach (var interruption in new[]
{
    waiting with { CharacterId = 200 }, waiting with { PlayerReady = false }, waiting with { UnexpectedDialog = true },
})
{
    retainerScan.Start(retainers, 100, 0);
    Check(retainerScan.Tick(interruption, 1) == RetainerScanAction.None && !retainerScan.Running,
        "Character changes, loading and unexpected dialogs stop automation");
}
retainerScan.Start(retainers, 100, 0);
Check(retainerScan.Tick(waiting, 30001) == RetainerScanAction.None && !retainerScan.Running, "Retainer loading timeout stops callbacks");
retainerScan.Start(retainers, 100, 0);
retainerScan.Tick(waiting with { ListReady = true }, 1);
Check(retainerScan.Tick(waiting with { MenuReady = true, ActiveRetainerId = 20 }, 2) == RetainerScanAction.None && !retainerScan.Running,
    "Selecting another retainer cancels before any menu action");
retainerScan.Start(retainers, 100, 0);
retainerScan.Stop();
Check(retainerScan.Tick(waiting with { ListReady = true }, 1) == RetainerScanAction.None && !retainerScan.Running,
    "Stop button prevents further navigation");

if (args.Contains("--live"))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var live = await XivGearImporter.ImportAsync(http, example[0].SourceUrl, CancellationToken.None);
    Check(live.Count == 1 && live[0].Pieces.Count == 11 && live[0].Job == "BRD", "End-to-end HTTP import of user's URL");
    Check(live[0].Pieces.Single(p => p.Slot == "Body").Materia!.SequenceEqual(new uint[] { 41772, 41772 }), "Live import retains the example's materia");
}
Console.WriteLine($"{tests} checks passed.");
