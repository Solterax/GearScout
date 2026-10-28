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

if (args.Contains("--live"))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var live = await XivGearImporter.ImportAsync(http, example[0].SourceUrl, CancellationToken.None);
    Check(live.Count == 1 && live[0].Pieces.Count == 11 && live[0].Job == "BRD", "End-to-end HTTP import of user's URL");
    Check(live[0].Pieces.Single(p => p.Slot == "Body").Materia!.SequenceEqual(new uint[] { 41772, 41772 }), "Live import retains the example's materia");
}
Console.WriteLine($"{tests} checks passed.");
