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

if (args.Contains("--live"))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var live = await XivGearImporter.ImportAsync(http, example[0].SourceUrl, CancellationToken.None);
    Check(live.Count == 1 && live[0].Pieces.Count == 11 && live[0].Job == "BRD", "End-to-end HTTP import of user's URL");
    Check(live[0].Pieces.Single(p => p.Slot == "Body").Materia!.SequenceEqual(new uint[] { 41772, 41772 }), "Live import retains the example's materia");
}
Console.WriteLine($"{tests} checks passed.");
