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
var ringNoticeSets = new List<TrackedSet>
{
    new() { Name = "Two rings", Job = "BRD", Pieces = [new() { ItemId = 100 }, new() { ItemId = 100 }] },
    new() { Name = "Appearance", IsGlam = true, Pieces = [new() { ItemId = 100 }, new() { ItemId = 100 }] },
};
var ringDrop = new WantedItemTracker().ObserveLoot(100, ringNoticeSets, hat100, [drop]);
Check(ringDrop.Count == 1 && ringDrop[0].Sets.SequenceEqual(new[] { "Two rings (BRD)" }),
    "Drop matching counts duplicate normal targets but shares owned appearances for glam collections");

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

MateriaOption TestMateria(uint id, bool restricted = false, uint param = 27, int value = 10, uint level = 1) =>
    new(id, $"Materia {id}", param, value, level, restricted ? [0.12, 0, 0, 0] : [0.12, 0.06, 0.03, 0.02],
        restricted ? [0.17, 0, 0, 0] : [0.17, 0.10, 0.07, 0.05], restricted ? 0.4 : 0.8);
var materiaRules = new Dictionary<uint, MateriaOption> { [1] = TestMateria(1), [2] = TestMateria(2, true), [3] = TestMateria(3), [4] = TestMateria(4, level: 999) };
var accessoryRules = new MeldGearRules(1, true, 770, new Dictionary<uint, int>(), new Dictionary<uint, int> { [27] = 200 });
var armorRules = accessoryRules with { GuaranteedSlots = 2 };
OwnedItem MeldCopy(uint[] melds, int slot = 0, bool hq = true) => new(100, 1, "Bag 1", false, melds)
    { Container = 0, InventorySlot = slot, HighQuality = hq };
GearPiece MeldTarget(params uint[] melds) => new() { Slot = "Head", ItemId = 100, Materia = melds.ToList() };
Check(MeldPlanner.Rate(accessoryRules, materiaRules[1], true, 4) == 0.05 &&
    MeldPlanner.Rate(armorRules, materiaRules[1], true, 4) == 0.07, "One-slot accessories reach 5% while two-slot armor ends at 7%");
Check(MeldPlanner.Rate(accessoryRules, materiaRules[1], false, 4) == 0.02, "NQ quality changes the final accessory rate to 2%");
Check(MeldPlanner.Rate(armorRules, materiaRules[2], true, 1) == 1 &&
    MeldPlanner.Rate(armorRules, materiaRules[2], true, 2) == 0.17 &&
    MeldPlanner.Rate(armorRules, materiaRules[2], true, 3) == 0, "Restricted grades fit guaranteed and first advanced slots only");
Check(MeldPlanner.Rate(accessoryRules with { Advanced = false }, materiaRules[1], true, 1) == 0 &&
    MeldPlanner.Rate(accessoryRules with { GuaranteedSlots = 0 }, materiaRules[1], true, 0) == 0,
    "Items forbidding advanced melds or without sockets cannot be overmelded");
Check(MeldPlanner.Rate(armorRules, materiaRules[4], true, 0) == 0, "Materia minimum item level is enforced even for guaranteed slots");
var retained = MeldPlanner.Build(MeldTarget(1, 2), MeldCopy([2, 1]), armorRules, materiaRules);
Check(retained.Valid && retained.Actions.Count == 0 && retained.Preserved == 2, "Equivalent target order preserves existing valid melds without removals");
var rebuild = MeldPlanner.Build(MeldTarget(1, 2), MeldCopy([3, 1]), armorRules, materiaRules);
Check(rebuild.Valid && rebuild.Preserved == 0 && rebuild.Actions.Take(2).Select(a => a.MateriaId).SequenceEqual(new uint[] {1, 3}) &&
    rebuild.Actions.Take(2).All(a => a.Kind == MeldActionKind.Retrieve), "Correct later materia is removed first when it blocks an incorrect early meld");
Check(rebuild.Actions[0].Chance == 0.8 && rebuild.Actions[0].Before.SequenceEqual(new uint[] {3, 1}) && rebuild.Actions[0].After.SequenceEqual(new uint[] {3}),
    "Removal plan records recovery chance and exact before/after states");
var prefixPlan = MeldPlanner.Build(MeldTarget(1, 1, 2), MeldCopy([1, 3]), accessoryRules, materiaRules);
Check(prefixPlan.Valid && prefixPlan.Preserved == 1 && prefixPlan.Actions[1].MateriaId == 2 && prefixPlan.Actions[1].Chance == 0.17,
    "Planner preserves usable prefix and reserves first overmeld for restricted materia");
var rearrangedPrefix = MeldPlanner.Build(MeldTarget(1, 2, 2), MeldCopy([1]), accessoryRules, materiaRules);
Check(rearrangedPrefix.Valid && rearrangedPrefix.Preserved == 0 && rearrangedPrefix.Actions[0].Kind == MeldActionKind.Retrieve &&
    rearrangedPrefix.Actions.Skip(1).Take(2).All(a => a.MateriaId == 2),
    "A correct prefix is rebuilt when needed to fit restricted grades into legal positions");
var mixedRates = new Dictionary<uint, MateriaOption>(materiaRules) { [5] = TestMateria(5) with { HqRates = [0.90, 0.48, 0.28, 0.16] } };
var optimizedOrder = MeldPlanner.Build(MeldTarget(5, 1), MeldCopy([]), accessoryRules, mixedRates);
Check(optimizedOrder.Actions[0].MateriaId == 1 && optimizedOrder.Actions[1].MateriaId == 5,
    "Lowest-consumption legal order reserves a guaranteed socket for harder-to-overmeld materia");
Check(!MeldPlanner.Build(MeldTarget(2, 2, 2), MeldCopy([]), accessoryRules, materiaRules).Valid,
    "Impossible restricted-grade accessory plan is rejected");
Check(!MeldPlanner.Build(MeldTarget(1, 1), MeldCopy([]), accessoryRules with { Advanced = false }, materiaRules).Valid,
    "Too many targets for guaranteed-only gear are rejected");
Check(!MeldPlanner.Build(MeldTarget(999), MeldCopy([]), armorRules, materiaRules).Valid &&
    !MeldPlanner.Build(MeldTarget(1), null, armorRules, materiaRules).Valid, "Unknown materia and missing gear produce incomplete plans");
Check(!MeldPlanner.Build(new() { ItemId = 100 }, MeldCopy([]), armorRules, materiaRules).Valid &&
    !MeldPlanner.Build(MeldTarget(1, 1, 1, 1, 1, 1), MeldCopy([]), armorRules, materiaRules).Valid,
    "Unspecified targets and more than five targets cannot generate actions");
Check(!MeldPlanner.Build(MeldTarget(1), MeldCopy([]) with { HighQuality = null }, armorRules, materiaRules).Valid &&
    !MeldPlanner.Build(MeldTarget(1), MeldCopy([]) with { Materia = null }, armorRules, materiaRules).Valid,
    "Unknown quality or existing melds block a plan instead of guessing");
Check(MeldPlanner.Build(MeldTarget(), MeldCopy([1, 2]), armorRules, materiaRules).Actions.All(a => a.Kind == MeldActionKind.Retrieve),
    "An explicitly empty target removes all existing materia");
Check(MeldPlanner.Build(MeldTarget(1), MeldCopy([]), armorRules with { StatCaps = new Dictionary<uint, int> { [27] = 5 } }, materiaRules).Warnings.Any(w => w.Contains("wastes 5")),
    "Planner flags target stats exceeding the item cap");
Check(MeldPlanner.Build(MeldTarget(1), MeldCopy([]), armorRules with { StatCaps = new Dictionary<uint, int>() }, materiaRules).Warnings.Any(w => w.Contains("unknown")),
    "Unknown stat-cap data is explicitly labeled");
var geometric = MeldProbability.Distribution([0.2]);
Check(MeldProbability.Quantile(geometric, 0.90) == 11 && MeldProbability.Quantile(geometric, 0.95) == 14,
    "20% single meld needs 11 attempts for 90% and 14 attempts for 95%");
var certain = MeldProbability.Distribution([1, 1, 1]);
Check(certain.Length == 4 && certain[3] == 1 && MeldProbability.Quantile(certain, 0.99) == 3,
    "Guaranteed meld budgets equal the exact number of remaining melds");
var repeatedGeometric = MeldProbability.Distribution([0.5, 0.5]);
Check(Math.Abs(repeatedGeometric[2] - 0.25) < 1e-12 && Math.Abs(repeatedGeometric[3] - 0.25) < 1e-12,
    "Same-type multiple melds use the full attempt-count convolution");
Check(Math.Abs(MeldProbability.Distribution([1, 0.2])[2] - 0.2) < 1e-12, "Mixed guaranteed and advanced attempts combine correctly");
Reject(() => MeldProbability.Distribution([0]), "Impossible success probabilities are rejected");
Reject(() => MeldProbability.Quantile(geometric, 1), "Finite 100% budgets for random melds are not promised");
OwnedItem Loose(uint id, int qty, int slot = 1) => new(id, qty, "Bag 1", false, []) { Container = 0, InventorySlot = slot, HighQuality = false };
var twoTypes = MeldPlanner.Build(MeldTarget(1, 2), MeldCopy([]), accessoryRules, materiaRules);
var shopping = MeldProbability.Budget([twoTypes], [Loose(1, 2), new(2, 25, "Retainer: Test / bags (saved)", false, [], DateTimeOffset.UtcNow)], 0.95);
Check(shopping.CompletePlan && shopping.Items.Single(i => i.MateriaId == 2).Retainers == 25 && shopping.Items.Single(i => i.MateriaId == 2).Bags == 0,
    "Shopping separates bag supply from saved retainer supply");
Check(shopping.CompletionChance == 0 && shopping.Items.Single(i => i.MateriaId == 2).Additional > 0,
    "Retainer stock is not counted as usable bag stock for completion chance");
var joint = shopping.Items.Select(i =>
{
    var dist = MeldProbability.Distribution(twoTypes.Actions.Where(a => a.Kind == MeldActionKind.Meld && a.MateriaId == i.MateriaId).Select(a => a.Chance).ToArray());
    return dist.Take(i.Budget + 1).Sum();
}).Aggregate(1.0, (a, b) => a * b);
Check(joint >= 0.95, "Per-type budgets jointly reach the requested whole-plan confidence");
var purchaseAfterRemovals = MeldProbability.Budget([rebuild], [], 0.95);
Check(purchaseAfterRemovals.Items.Sum(i => i.Minimum) == 2 && purchaseAfterRemovals.Items.Sum(i => i.Budget) == 2,
    "Potential retrieval refunds are not subtracted from confirmed shopping requirements");
Check(!MeldProbability.Budget([MeldPlanner.Build(MeldTarget(1), null, armorRules, materiaRules)], [], 0.95).CompletePlan,
    "Invalid pieces cannot be represented as a complete whole-set estimate");
var guidePlan = MeldPlanner.Build(MeldTarget(1), MeldCopy([]), armorRules, materiaRules);
var guide = new MeldGuide();
guide.Start(100, [guidePlan], [MeldCopy([]), Loose(1, 5)], new Dictionary<uint, int> { [1] = 3 }, 3, 0);
guide.Observe(100, true, [MeldCopy([1]), Loose(1, 4)], 1);
Check(!guide.Running && guide.Completed == 1 && guide.Consumed == 1 && guide.Status.Contains("complete"),
    "Guidance observes the expected success and counts consumed materia");
var advancedGuidePlan = MeldPlanner.Build(MeldTarget(1, 1), MeldCopy([1]), accessoryRules, materiaRules);
guide.Start(100, [advancedGuidePlan], [MeldCopy([1]), Loose(1, 5)], new Dictionary<uint, int> { [1] = 2 }, 2, 0);
guide.Observe(100, true, [MeldCopy([1]), Loose(1, 4)], 1);
Check(guide.Running && guide.Completed == 0 && guide.Consumed == 1, "Failed overmeld consumes budget while leaving the current step pending");
guide.Observe(100, true, [MeldCopy([1]), Loose(1, 3)], 2);
Check(!guide.Running && guide.Consumed == 2 && guide.Status.Contains("limit", StringComparison.OrdinalIgnoreCase),
    "Guidance stops after exhausted failed-attempt budget");
guide.Start(100, [guidePlan], [MeldCopy([]), Loose(1, 5)], new Dictionary<uint, int> { [1] = 3 }, 3, 0);
guide.Observe(100, true, [MeldCopy([], slot: 9), Loose(1, 5)], 1);
Check(!guide.Running && guide.Status.Contains("moved"), "Moved physical gear stops guidance instead of selecting another copy");
guide.Start(100, [guidePlan], [MeldCopy([]), Loose(1, 5)], new Dictionary<uint, int> { [1] = 3 }, 3, 0);
guide.Observe(100, true, [MeldCopy([3]), Loose(1, 4)], 1);
Check(!guide.Running && guide.Status.Contains("unexpected"), "Unexpected materia changes stop the guided sequence");
foreach (var state in new[] { (Owner: 200UL, Ready: true), (Owner: 100UL, Ready: false) })
{
    guide.Start(100, [guidePlan], [MeldCopy([]), Loose(1, 5)], new Dictionary<uint, int> { [1] = 3 }, 3, 0);
    guide.Observe(state.Owner, state.Ready, [MeldCopy([]), Loose(1, 5)], 1);
    Check(!guide.Running, "Character and loading changes stop melding guidance");
}
guide.Start(100, [guidePlan], [MeldCopy([]), Loose(1, 5)], new Dictionary<uint, int> { [1] = 3 }, 3, 0);
guide.Stop(); guide.Observe(100, true, [MeldCopy([1])], 1);
Check(!guide.Running && guide.Completed == 0, "Stopped guidance does not continue observing actions");
guide.Start(100, [guidePlan], [MeldCopy([]), Loose(1, 5)], new Dictionary<uint, int> { [1] = 3 }, 3, 0);
guide.Observe(100, true, [MeldCopy([]), Loose(1, 5)], 180001);
Check(!guide.Running && guide.Status.Contains("timed out"), "Inactive guidance times out");
guide.Start(100, [guidePlan], [MeldCopy([])], new Dictionary<uint, int> { [1] = 3 }, 3, 0);
guide.Observe(100, true, [MeldCopy([])], 1);
Check(!guide.Running && guide.Status.Contains("unavailable"), "Missing bag materia stops guidance");
Reject(() => guide.Start(100, [guidePlan, guidePlan], [MeldCopy([])], new Dictionary<uint, int>(), 3, 0),
    "One physical gear copy cannot be guided for two target slots");
Reject(() => guide.Start(100, [MeldPlanner.Build(MeldTarget(1), MeldCopy([]) with { LastCheckedUtc = DateTimeOffset.UtcNow }, armorRules, materiaRules)], [], new Dictionary<uint, int>(), 3, 0),
    "Saved storage gear cannot be used as a live guidance target");
var qualitySnapshot = Snapshot(100, "retainer:test:bags", new StoredItem { ItemId = 100, Quantity = 1, HighQuality = true, Materia = [2, 1] });
var qualityReload = System.Text.Json.JsonSerializer.Deserialize<StorageSnapshot>(System.Text.Json.JsonSerializer.Serialize(qualitySnapshot))!;
var savedQuality = StorageCache.Merge([], [qualityReload], 100)!.Single();
Check(savedQuality.HighQuality == true && savedQuality.Materia!.SequenceEqual(new uint[] {2, 1}) && !savedQuality.IsLive,
    "Stored quality and materia order survive persistence without creating a fake live copy");

var socketlessRules = armorRules with { GuaranteedSlots = 0, Advanced = false };
var relicPlan = MeldPlanner.Build(new GearPiece { Slot = "Weapon", ItemId = 100, Materia = null },
    MeldCopy([]) with { Materia = null }, socketlessRules, materiaRules);
Check(relicPlan.Valid && relicPlan.SkipReason != null && relicPlan.Errors.Count == 0 && relicPlan.Actions.Count == 0,
    "Socketless relics are excluded before unreadable current melds or unspecified targets can block the plan");
var socketlessTarget = MeldPlanner.Build(MeldTarget(1), null, socketlessRules, materiaRules);
Check(socketlessTarget.Valid && socketlessTarget.Actions.Count == 0 && socketlessTarget.Warnings.Count == 1,
    "Materia targets cannot generate any actions on socketless gear, even when the gear is missing");
Check(!MeldEligibility.CanGuide(relicPlan) && MeldEligibility.Reason(relicPlan).Contains("No materia sockets"),
    "Excluded relic has an explicit reason and is never eligible for guidance");
Check(!MeldEligibility.CanGuide(retained) && MeldEligibility.Reason(retained).Contains("Already matches"),
    "Already-correct gear has no pointless guidance actions");
Check(MeldEligibility.CanGuide(guidePlan) && !MeldEligibility.CanGuide(MeldPlanner.Build(MeldTarget(1), MeldCopy([]) with { Materia = null }, armorRules, materiaRules)),
    "Unreadable melds still block socketed gear while valid live work remains eligible");
var copySuggestions = MeldEligibility.SuggestCopies([MeldTarget(1)],
    [new(100, 1, "Retainer (saved)", false, [1], DateTimeOffset.UtcNow), MeldCopy([])]);
Check(copySuggestions.Single().Match!.IsLive, "Live copies are preferred to perfectly melded cached copies for guidance");
GearPiece[] twoRingTargets = [new() { Slot = "RingLeft", ItemId = 100, Materia = [1] }, new() { Slot = "RingRight", ItemId = 100, Materia = [1] }];
var ringSuggestions = MeldEligibility.SuggestCopies(twoRingTargets, [MeldCopy([]), MeldCopy([], slot: 2)]);
Check(ringSuggestions.All(s => s.Match!.IsLive) && ringSuggestions.Select(s => s.Match!.CopyKey).Distinct().Count() == 2,
    "Live-first guidance suggestions still allocate separate copies to duplicate ring targets");
guide.Start(100, [relicPlan, guidePlan], [MeldCopy([]), Loose(1, 5)], new Dictionary<uint, int> { [1] = 3 }, 3, 0);
Check(guide.Running && guide.Total == 1, "A socketless relic cannot block guidance for the set's ready socketed gear");
Check(MeldProbability.Budget([relicPlan, guidePlan], [Loose(1, 1)], 0.95).CompletePlan,
    "Excluded socketless gear does not make the remaining materia budget an invalid partial plan");

var autoGuide = new MeldGuide();
var automatic = new AutomaticMeldSession(autoGuide);
long autoTime = 0;
AutoMeldView AutoView(uint[] melds, int stock = 5) => new(100, true, [MeldCopy(melds), Loose(1, stock)],
    true, true, true, true, true, false, true, true, false, false);
void StartAuto(PieceMeldPlan plan, AutoMeldView view, int limit = 5)
{
    automatic.Stop(); autoTime = 0;
    automatic.Start(100, [plan], view.Owned!, plan.Actions.Where(a => a.Kind == MeldActionKind.Meld).Select(a => a.MateriaId).Distinct().ToDictionary(id => id, _ => limit), limit, autoTime);
}
AutoMeldCommand AutoTick(AutoMeldView view, int elapsed = 350) => automatic.Tick(view, autoTime += elapsed);
AutoMeldCommand ToDialog(AutoMeldView view)
{
    AutoTick(view); AutoTick(view); AutoTick(view);
    return AutoTick(view);
}
var autoBefore = AutoView([]);
StartAuto(guidePlan, autoBefore);
Check(ToDialog(autoBefore) == AutoMeldCommand.SelectMateria && automatic.AttemptsAuthorized == 1,
    "Automatic melding selects one materia and reserves its budget before opening confirmation");
Check(AutoTick(autoBefore) == AutoMeldCommand.None, "A pending confirmation request is never blindly sent twice");
Check(AutoTick(autoBefore with { DialogOpen = true }) == AutoMeldCommand.Confirm,
    "Matching native dialog issues exactly one confirmation");
Check(AutoTick(autoBefore with { DialogOpen = true, Busy = true }) == AutoMeldCommand.None,
    "Confirmed attempt does not receive duplicate callbacks while busy");
var autoAfter = AutoView([1], 4);
AutoTick(autoAfter); AutoTick(autoAfter, 2200);
Check(!automatic.Running && autoGuide.Completed == 1 && autoGuide.Consumed == 1,
    "Settled gear and one-unit consumption complete the automatic action");
StartAuto(guidePlan, autoBefore);
Check(AutoTick(autoBefore with { WindowReady = false }) == AutoMeldCommand.OpenWindow &&
    AutoTick(autoBefore with { WindowReady = false }) == AutoMeldCommand.None,
    "Automatic flow opens the native window once and waits for readiness");
Check(AutoTick(autoBefore with { CategoryMatches = false }) == AutoMeldCommand.SelectCategory,
    "Exact inventory category is selected before gear");
Check(AutoTick(autoBefore with { GearMatches = false }) == AutoMeldCommand.SelectGear,
    "Exact physical gear copy is selected before materia");
Check(AutoTick(autoBefore with { AgentReady = false }) == AutoMeldCommand.None,
    "Native agent loading delays the next action");
Check(AutoTick(autoBefore) == AutoMeldCommand.SelectMateria, "Melding resumes after the selected gear is ready");
automatic.Stop();
Check(AutoTick(autoBefore with { DialogOpen = true }) == AutoMeldCommand.None && !automatic.Running,
    "Stop before confirmation prevents all future automatic callbacks");
foreach (var mismatch in new[]
{
    autoBefore with { DialogOpen = true, RateMatches = false },
    autoBefore with { DialogOpen = true, MateriaMatches = false },
    autoBefore with { DialogOpen = true, DialogMatches = false },
    autoBefore with { DialogOpen = true, GearMatches = false },
})
{
    StartAuto(guidePlan, autoBefore); ToDialog(autoBefore);
    Check(AutoTick(mismatch) == AutoMeldCommand.None && !automatic.Running,
        "Wrong rate, materia, dialog kind or selected gear blocks automatic confirmation");
}
StartAuto(guidePlan, autoBefore); ToDialog(autoBefore);
AutoTick(autoAfter with { DialogOpen = true });
Check(automatic.Phase == AutoMeldPhase.Result, "An externally confirmed result is recognized without confirming it twice");
AutoTick(autoAfter); AutoTick(autoAfter, 2200);
Check(!automatic.Running && autoGuide.Completed == 1, "Already-started external confirmation still requires verified inventory outcome");
StartAuto(guidePlan, autoBefore); ToDialog(autoBefore); AutoTick(autoBefore with { DialogOpen = true });
var excessConsumption = AutoView([1], 3);
AutoTick(excessConsumption); AutoTick(excessConsumption, 2200);
Check(!automatic.Running && automatic.Status.Contains("consumption"), "More than one consumed materia stops further attempts");
StartAuto(advancedGuidePlan, AutoView([1], 2), 1);
var failedBefore = AutoView([1], 2);
ToDialog(failedBefore); AutoTick(failedBefore with { DialogOpen = true });
var failedAfter = AutoView([1], 1);
AutoTick(failedAfter); AutoTick(failedAfter, 2200);
Check(!automatic.Running && automatic.AttemptsAuthorized == 1 && autoGuide.Consumed == 1,
    "Failed attempt at the material limit never authorizes a retry");
StartAuto(advancedGuidePlan, AutoView([1], 5), 3);
var advancedBefore = AutoView([1], 5);
ToDialog(advancedBefore); AutoTick(advancedBefore with { DialogOpen = true });
var advancedFailed = AutoView([1], 4);
AutoTick(advancedFailed); AutoTick(advancedFailed, 2200);
Check(automatic.Running && automatic.AttemptsAuthorized == 1 && autoGuide.Completed == 0,
    "A settled failed overmeld remains on the same planned step within budget");
Check(ToDialog(advancedFailed) == AutoMeldCommand.SelectMateria && automatic.AttemptsAuthorized == 2,
    "Retry reserves a separate single-attempt authorization");
AutoTick(advancedFailed with { DialogOpen = true });
var advancedSuccess = AutoView([1, 1], 3);
AutoTick(advancedSuccess); AutoTick(advancedSuccess, 2200);
Check(!automatic.Running && autoGuide.Completed == 1 && autoGuide.Consumed == 2,
    "Success after failure completes the step with both attempts counted");
var retrieveOnly = MeldPlanner.Build(MeldTarget(), MeldCopy([1]), armorRules, materiaRules);
foreach (var recovered in new[] {0, 1})
{
    var retrieveBefore = AutoView([1], 0);
    StartAuto(retrieveOnly, retrieveBefore, 0);
    Check(ToDialog(retrieveBefore) == AutoMeldCommand.RequestRetrieve && automatic.AttemptsAuthorized == 0,
        "Automatic retrieval follows the plan without consuming a meld authorization");
    AutoTick(retrieveBefore with { DialogOpen = true, MateriaMatches = false });
    var retrieveAfter = AutoView([], recovered);
    AutoTick(retrieveAfter); AutoTick(retrieveAfter, 2200);
    Check(!automatic.Running && autoGuide.Completed == 1 && autoGuide.Consumed == 0,
        "Both recovered and destroyed retrieval outcomes advance after gear verification");
}
foreach (var interrupted in new[]
{
    autoBefore with { Ready = false }, autoBefore with { Owner = 200 }, autoBefore with { Owned = null },
    autoBefore with { UnexpectedDialog = true }, autoBefore with { DialogOpen = true },
    autoBefore with { Owned = new[] {MeldCopy([], slot: 9), Loose(1, 5)} },
})
{
    StartAuto(guidePlan, autoBefore);
    Check(AutoTick(interrupted) == AutoMeldCommand.None && !automatic.Running,
        "Loading, character, inventory, unexpected dialogs and moved copies stop automation");
}
StartAuto(guidePlan, autoBefore); ToDialog(autoBefore);
Check(AutoTick(autoBefore, 25001) == AutoMeldCommand.None && !automatic.Running,
    "Missing native confirmation times out without repeating a request");
StartAuto(guidePlan, autoBefore); ToDialog(autoBefore); AutoTick(autoBefore with { DialogOpen = true });
Check(AutoTick(autoBefore, 25001) == AutoMeldCommand.None && !automatic.Running,
    "Unacknowledged result times out instead of blindly retrying");
automatic.Stop();
Reject(() => automatic.Start(100, [guidePlan], autoBefore.Owned!, new Dictionary<uint, int> { [1] = 0 }, 0, 0),
    "Budget below the guaranteed minimum is rejected before any removal can start");
Reject(() => automatic.Start(100, [guidePlan], AutoView([], 0).Owned!, new Dictionary<uint, int> { [1] = 5 }, 5, 0),
    "Missing minimum bag stock is rejected before removing existing materia");
Check(!automatic.Running, "Failed automation preflight does not leave a running workflow");
var fullPlan = rebuild;
var fullStock = new Dictionary<uint, int> { [1] = 5, [2] = 5, [3] = 0 };
uint[] fullMelds = [3, 1];
AutoMeldView FullView() => autoBefore with
{
    Owned = new[] {MeldCopy(fullMelds)}.Concat(fullStock.Where(p => p.Value > 0).Select(p => Loose(p.Key, p.Value, (int)p.Key + 1))).ToArray(),
};
automatic.Stop(); autoTime = 0;
automatic.Start(100, [fullPlan], FullView().Owned!, new Dictionary<uint, int> { [1] = 5, [2] = 5 }, 5, 0);
var issued = new List<AutoMeldCommand>();
foreach (var action in fullPlan.Actions)
{
    var view = FullView();
    issued.Add(ToDialog(view));
    Check(AutoTick(view with { DialogOpen = true }) == AutoMeldCommand.Confirm, "Each planned retrieval or meld has its own verified confirmation");
    fullMelds = action.After.ToArray();
    fullStock[action.MateriaId] += action.Kind == MeldActionKind.Retrieve ? 1 : -1;
    AutoTick(FullView()); AutoTick(FullView(), 2200);
}
Check(!automatic.Running && autoGuide.Completed == fullPlan.Actions.Count && autoGuide.Consumed == 2 && automatic.AttemptsAuthorized == 2 &&
    issued.SequenceEqual(new[] {AutoMeldCommand.RequestRetrieve, AutoMeldCommand.RequestRetrieve, AutoMeldCommand.SelectMateria, AutoMeldCommand.SelectMateria}),
    "Automation retrieves bottom-first then automatically melds each new planned materia in sequence");

Check(!automatic.Running, "The final verified result leaves no queued automation commands");

var saddleChecks = new List<StorageSnapshot>
{
    Snapshot(100, "saddlebag", new StoredItem { ItemId = 100, Quantity = 1, HighQuality = true, Materia = [1] }),
    Snapshot(100, "saddlebag:premium", new StoredItem { ItemId = 101, Quantity = 1, HighQuality = false, Materia = [2] }),
    Snapshot(200, "saddlebag", new StoredItem { ItemId = 999, Quantity = 1 }),
};
var saddleOwned = StorageCache.Merge([], saddleChecks, 100)!;
Check(saddleOwned.Count == 2 && saddleOwned.All(i => i.LastCheckedUtc != null && !i.IsLive) &&
    saddleOwned.Select(i => i.StorageKey).ToHashSet().SetEquals(new[] { "saddlebag", "saddlebag:premium" }),
    "Standard and premium saddlebag ownership remains separate, saved and character-isolated");
Check(saddleOwned.Single(i => i.ItemId == 100).HighQuality == true && saddleOwned.Single(i => i.ItemId == 100).Materia!.Single() == 1,
    "Saddlebag ownership preserves gear quality and attached materia");
Check(LootMatcher.MissingItems(noticeSets, saddleOwned).Count == 0,
    "Wanted gear in either saddlebag suppresses missing-item loot indicators");
var saddleReload = System.Text.Json.JsonSerializer.Deserialize<List<StorageSnapshot>>(System.Text.Json.JsonSerializer.Serialize(saddleChecks))!;
Check(StorageCache.Merge([], saddleReload, 100)!.Count == 2 && StorageCache.Merge([], saddleReload, 200)!.Single().ItemId == 999,
    "Both saddlebag snapshots survive local reload without crossing characters");
StorageCache.Replace(saddleChecks, Snapshot(100, "saddlebag"));
var withdrawnSaddle = StorageCache.Merge(hat100, saddleChecks, 100)!;
Check(withdrawnSaddle.Where(i => i.ItemId == 100).Sum(i => i.Quantity) == 1 && withdrawnSaddle.Any(i => i.ItemId == 101),
    "Withdrawals replace the saddlebag snapshot without duplicating the live copy or erasing premium contents");
Check(LootMatcher.MissingItems(noticeSets, StorageCache.Merge([], saddleChecks, 100)).Contains(100),
    "An emptied saddlebag no longer contributes stale gear ownership");
StorageCache.Replace(saddleChecks, Snapshot(100, "saddlebag", new StoredItem { ItemId = 1, Quantity = 42 }));
StorageCache.Replace(saddleChecks, Snapshot(100, "saddlebag:premium", new StoredItem { ItemId = 1, Quantity = 8 }));
var saddleMateria = StorageCache.Merge([], saddleChecks, 100)!;
var saddleBudget = MeldProbability.Budget([guidePlan], saddleMateria, 0.95).Items.Single();
Check(saddleBudget.Saddlebags == 50 && saddleBudget.Bags == 0 && saddleBudget.Retainers == 0 && saddleBudget.AvailableChance == 0 && saddleBudget.Additional == 1,
    "Saddlebag materia is shown as remote stock without making it usable for automatic melding");
var saddleNotice = new WantedItemTracker();
saddleNotice.ObserveOwnership(100, noticeSets, [], saddleOwned);
Check(saddleNotice.ObserveOwnership(100, noticeSets, hat100, hat100).Count == 0,
    "Withdrawing previously checked saddlebag gear does not trigger a new-acquisition message");

OwnedItem GearInBag(uint id, int inventorySlot, uint[]? melds = null, bool hq = false) =>
    new(id, 1, "Bag 1", false, melds ?? []) { Container = 0, InventorySlot = inventorySlot, HighQuality = hq };
OwnedItem Worn(uint id, int slot, uint[]? melds = null, bool hq = false) =>
    new(id, 1, "Equipped", true, melds ?? []) { Container = 1000, InventorySlot = slot, HighQuality = hq };
TrackedSet EquipSet(params GearPiece[] targets) => new() { Name = "Equip test", Job = "BRD", Pieces = targets.ToList() };
var equip = new EquipSetSession();
var headTarget = new GearPiece { Slot = "Head", ItemId = 100, Materia = [1] };
var bodyTarget = new GearPiece { Slot = "Body", ItemId = 102 };
var equipSet = EquipSet(headTarget, bodyTarget);
equip.Start(equipSet, 100, "BRD", 0);
var equipMove = equip.Tick(100, "BRD", true, false, [GearInBag(100, 1, [1]), GearInBag(102, 2)], 1);
Check(equipMove?.Destination == 2 && equipMove.Source.InventorySlot == 1,
    "Equip pass chooses the exact available copy and the correct equipment destination");
Check(equip.Tick(100, "BRD", true, false, [GearInBag(100, 1, [1]), GearInBag(102, 2)], 2) == null,
    "An unacknowledged equip is not repeatedly issued");
equip.Tick(100, "BRD", true, false, [Worn(100, 2, [1]), GearInBag(102, 2)], 3);
var nextEquip = equip.Tick(100, "BRD", true, false, [Worn(100, 2, [1]), GearInBag(102, 2)], 400);
Check(nextEquip?.Destination == 3 && equip.Results.Single().Kind == EquipResultKind.Equipped,
    "Equip pass verifies the first slot before advancing to the next available piece");
equip.Tick(100, "BRD", true, false, [Worn(100, 2, [1]), Worn(102, 3)], 401);
Check(!equip.Running && equip.Results.Count == 2 && equip.Results.All(r => r.Kind == EquipResultKind.Equipped),
    "A fully equipped set ends with verified per-piece results");
equip.Start(EquipSet(headTarget), 100, "BRD", 0);
Check(equip.Tick(100, "BRD", true, false, [Worn(100, 2, [1])], 1) == null && equip.Results.Single().Kind == EquipResultKind.AlreadyEquipped,
    "Already-correct equipment is reported without an unnecessary swap");
equip.Start(equipSet, 100, "BRD", 0);
equip.Tick(100, "BRD", true, false, [GearInBag(102, 2)], 1);
Check(equip.Results.Single().Kind == EquipResultKind.Missing && equip.Running,
    "A missing piece is reported while the rest of the set remains eligible");
Check(equip.Tick(100, "BRD", true, false, [GearInBag(102, 2)], 400)?.Piece.ItemId == 102,
    "Finding one item missing does not stop equipping later available pieces");
equip.Reject("Requires a higher level.", 401);
Check(!equip.Running && equip.Results.Last().Kind == EquipResultKind.Failed && equip.Results.Last().Details.Contains("level"),
    "Found but unequippable items are distinguished from missing items");
equip.Start(EquipSet(headTarget), 100, "BRD", 0);
equip.Tick(100, "BRD", true, false, [new(100, 1, "Chocobo saddlebag (saved)", false, [1], DateTimeOffset.UtcNow)], 1);
Check(equip.Results.Single().Kind == EquipResultKind.Stored && equip.Results.Single().Details.Contains("saddlebag"),
    "Saddlebag-only gear is reported with its location and withdrawal instruction instead of being equipped remotely");
equip.Start(EquipSet(headTarget), 100, "BRD", 0);
var timedMove = equip.Tick(100, "BRD", true, false, [GearInBag(100, 1, [1])], 1);
equip.Tick(100, "BRD", true, false, [GearInBag(100, 1, [1])], 8001);
Check(timedMove != null && equip.Results.Single().Kind == EquipResultKind.Failed && !equip.Running,
    "An equip without game confirmation times out and reports a failure");
GearPiece[] duplicateRingTargets = [new() { Slot = "RingLeft", ItemId = 105 }, new() { Slot = "RingRight", ItemId = 105 }];
equip.Start(EquipSet(duplicateRingTargets), 100, "BRD", 0);
equip.Tick(100, "BRD", true, false, [Worn(105, 12)], 1);
equip.Tick(100, "BRD", true, false, [Worn(105, 12)], 400);
Check(equip.Results.Count(r => r.Kind == EquipResultKind.Missing) == 1 && equip.Results.Count(r => r.Kind == EquipResultKind.AlreadyEquipped) == 1,
    "One owned ring never fills two slots and its already-correct hand is preserved");
equip.Start(EquipSet(duplicateRingTargets), 100, "BRD", 0);
equip.Tick(100, "BRD", true, false, [new(105, 1, "Retainer: Test (saved)", false, [], DateTimeOffset.UtcNow)], 1);
equip.Tick(100, "BRD", true, false, [new(105, 1, "Retainer: Test (saved)", false, [], DateTimeOffset.UtcNow)], 400);
Check(equip.Results.Count(r => r.Kind == EquipResultKind.Stored) == 1 && equip.Results.Count(r => r.Kind == EquipResultKind.Missing) == 1,
    "A single saved ring is not reported as two separately available stored copies");
equip.Start(EquipSet(duplicateRingTargets), 100, "BRD", 0);
OwnedItem[] twoSavedRings = [new(105, 1, "Retainer: Test (saved)", false, [], DateTimeOffset.UtcNow), new(105, 1, "Retainer: Test (saved)", false, [], DateTimeOffset.UtcNow)];
equip.Tick(100, "BRD", true, false, twoSavedRings, 1); equip.Tick(100, "BRD", true, false, twoSavedRings, 400);
Check(equip.Results.All(r => r.Kind == EquipResultKind.Stored), "Two actual saved copies in one storage source are both reported accurately");
var ringLeft = new GearPiece { Slot = "RingLeft", ItemId = 105, Materia = [1] };
var ringRight = new GearPiece { Slot = "RingRight", ItemId = 105, Materia = [2] };
equip.Start(EquipSet(ringLeft, ringRight), 100, "BRD", 0);
var ringSwap = equip.Tick(100, "BRD", true, false, [Worn(105, 11, [2]), Worn(105, 12, [1])], 1);
Check(ringSwap?.Source.InventorySlot == 12 && ringSwap.Destination == 11,
    "Different ring materia targets select the matching physical copy even when hands are swapped");
OwnedItem[] swappedRings = [Worn(105, 11, [1]), Worn(105, 12, [2])];
equip.Tick(100, "BRD", true, false, swappedRings, 2); equip.Tick(100, "BRD", true, false, swappedRings, 400);
Check(!equip.Running && equip.Results.Last().Kind == EquipResultKind.AlreadyEquipped,
    "Fresh allocation recognizes the second ring after the first swap relocates it");
equip.Start(EquipSet(headTarget), 100, "BRD", 0);
Check(equip.Tick(100, "BRD", true, false, [GearInBag(100, 1, [3]), GearInBag(100, 2, [1])], 1)?.Source.InventorySlot == 2,
    "Equip selection prefers the correctly melded available copy");
equip.Stop(); equip.Start(EquipSet(new GearPiece { Slot = "Head", ItemId = 100 }), 100, "BRD", 0);
Check(equip.Tick(100, "BRD", true, false, [Worn(100, 2), GearInBag(100, 3, hq: true)], 1)?.Source.HighQuality == true,
    "HQ copies are preferred over NQ when materia quality is otherwise tied");
equip.Stop();
foreach (var interruption in new[] { (200UL, "BRD", true, false), (100UL, "WAR", true, false), (100UL, "BRD", false, false), (100UL, "BRD", true, true) })
{
    equip.Start(equipSet, 100, "BRD", 0);
    equip.Tick(interruption.Item1, interruption.Item2, interruption.Item3, interruption.Item4, [GearInBag(100, 1)], 1);
    Check(!equip.Running && equip.Results.All(r => r.Kind == EquipResultKind.NotProcessed),
        "Character, job, loading and busy/combat changes cancel remaining equip requests");
}
equip.Start(equipSet, 100, "BRD", 0); equip.Stop();
Check(equip.Tick(100, "BRD", true, false, [GearInBag(100, 1)], 1) == null && equip.Results.Count == 2,
    "Stop leaves no future equipment requests and reports unprocessed pieces");
Reject(() => equip.Start(new() { IsGlam = true, Pieces = [headTarget] }, 100, "BRD", 0), "Glam collections cannot be equipped as a single loadout");
Reject(() => equip.Start(EquipSet(headTarget), 100, "WAR", 0), "Equip set does not change jobs or equip another job's loadout");
Reject(() => equip.Start(EquipSet(headTarget, headTarget), 100, "BRD", 0), "Duplicate target slots are rejected before any equipment changes");
Reject(() => equip.Start(EquipSet(), 100, "BRD", 0), "Empty sets cannot start an equip pass");
equip.Start(EquipSet(headTarget), 100, "BRD", 0);
var armoryCopy = GearInBag(100, 7, [1]) with { Container = 3202, Location = "Armoury Chest" };
Check(equip.Tick(100, "BRD", true, false, [armoryCopy], 1)?.Source.Container == 3202,
    "Available Armoury Chest gear is selected directly without moving it through bags first");
equip.Tick(100, "BRD", true, false, [Worn(100, 2) with { Materia = null }], 2);
Check(equip.Running && equip.Results.Count == 0, "Unknown destination materia cannot prematurely verify a specific known-meld copy");
equip.Tick(100, "BRD", true, false, [Worn(100, 2, [1])], 3);
Check(!equip.Running && equip.Results.Single().Kind == EquipResultKind.Equipped, "Equip verification completes once the selected copy's meld data is readable");
equip.Start(equipSet, 100, "BRD", 0);
equip.Tick(100, "BRD", true, false, [GearInBag(100, 1, [1]), GearInBag(102, 2)], 1);
equip.Tick(100, "BRD", true, false, [Worn(100, 2, [1]), GearInBag(102, 2)], 2);
equip.Tick(100, "BRD", true, false, [Worn(999, 2), GearInBag(102, 2)], 400);
Check(!equip.Running && equip.Results[0].Kind == EquipResultKind.Failed && equip.Results[1].Kind == EquipResultKind.NotProcessed,
    "Changing completed equipment cancels the pass and does not leave a false equipped result");

var comparisonStorage = new List<StorageSnapshot>();
StoredItem Stored(uint id, List<uint>? melds = null, bool? hq = false, int count = 1) =>
    new() { ItemId = id, Quantity = count, Materia = melds, HighQuality = hq };
StorageCache.Replace(comparisonStorage, Snapshot(100, "comparison", Stored(10, [1, 2]), Stored(20), Stored(20)));
Check(!StorageCache.Replace(comparisonStorage, Snapshot(100, "comparison", Stored(20), Stored(10, [1, 2]), Stored(20))),
    "Storage reorder preserves an unchanged snapshot including duplicate copies");
Check(StorageCache.Replace(comparisonStorage, Snapshot(100, "comparison", Stored(20), Stored(10, [1, 2]), Stored(10, [1, 2]))),
    "Same item count with different duplicate multiplicities updates storage");
foreach (var replacement in new[] { Stored(10, [2, 1]), Stored(10, []), Stored(10, null), Stored(10, [1, 2], true), Stored(10, [1, 2], false, 2) })
{
    comparisonStorage.Clear();
    StorageCache.Replace(comparisonStorage, Snapshot(100, "comparison", Stored(10, [1, 2])));
    Check(StorageCache.Replace(comparisonStorage, Snapshot(100, "comparison", replacement)),
        "Storage comparison detects ordered meld, unknown meld, quality and quantity changes");
}
var originalCopy = new OwnedItem(10, 1, "Bag", false, new uint[] { 1, 2 }) { Container = 1, InventorySlot = 2, HighQuality = false };
Check(StorageCache.SameOwnership([originalCopy], [originalCopy with { Materia = new uint[] { 1, 2 } }]),
    "Fresh identical inventory data preserves cached UI plans");
foreach (var changedCopy in new[] { originalCopy with { Materia = new uint[] { 2, 1 } }, originalCopy with { Materia = null },
    originalCopy with { InventorySlot = 3 }, originalCopy with { Container = 2 }, originalCopy with { Equipped = true },
    originalCopy with { HighQuality = true }, originalCopy with { Quantity = 2 }, originalCopy with { ItemId = 20 },
    originalCopy with { StorageKey = "other" }, originalCopy with { LastCheckedUtc = checkedAt }, originalCopy with { Location = "Other" } })
    Check(!StorageCache.SameOwnership([originalCopy], [changedCopy]), "Ownership changes invalidate cached UI plans");
Check(!StorageCache.SameOwnership([originalCopy], null) && !StorageCache.SameOwnership(null, [originalCopy]) &&
    !StorageCache.SameOwnership([originalCopy], []), "Unreadable or removed ownership invalidates cached plans");

ZoneEquipChecks.Run(Check, Reject);
CurrentGearChecks.Run(Check, Reject);
PerformanceCacheChecks.Run(Check);

if (args.Contains("--live"))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var live = await XivGearImporter.ImportAsync(http, example[0].SourceUrl, CancellationToken.None);
    Check(live.Count == 1 && live[0].Pieces.Count == 11 && live[0].Job == "BRD", "End-to-end HTTP import of user's URL");
    Check(live[0].Pieces.Single(p => p.Slot == "Body").Materia!.SequenceEqual(new uint[] { 41772, 41772 }), "Live import retains the example's materia");
}
Console.WriteLine($"{tests} checks passed.");
