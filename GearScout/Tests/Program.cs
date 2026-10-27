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

if (args.Contains("--live"))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var live = await XivGearImporter.ImportAsync(http, example[0].SourceUrl, CancellationToken.None);
    Check(live.Count == 1 && live[0].Pieces.Count == 11 && live[0].Job == "BRD", "End-to-end HTTP import of user's URL");
}
Console.WriteLine($"{tests} checks passed.");
