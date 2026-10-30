using GearScout;
using System.Text.Json;

internal static class ZoneEquipChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        TrackedSet Set(string job, string name) => new()
        {
            Job = job, Name = name, Pieces = [new() { Slot = "Head", ItemId = 100 }],
        };
        var bard = Set("BRD", "FRU Bard");
        var alternate = Set("BRD", "Alternate Bard");
        var warrior = Set("WAR", "FRU Warrior");
        var glam = Set("", "Glam"); glam.IsGlam = true;
        List<TrackedSet> sets = [bard, alternate, warrior, glam];
        ZoneEquipRules.Assign(sets, bard, 1238);
        ZoneEquipRules.Assign(sets, warrior, 1238);
        ZoneEquipRules.Assign(sets, bard, 100);
        check(bard.AutoEquipTerritories.SequenceEqual(new uint[] { 1238, 100 }) && warrior.AutoEquipTerritories.Contains(1238),
            "One set can cover multiple zones, and each job can have a set for the same duty");
        ZoneEquipRules.Assign(sets, alternate, 1238);
        check(!bard.AutoEquipTerritories.Contains(1238) && alternate.AutoEquipTerritories.Count == 1 && warrior.AutoEquipTerritories.Contains(1238),
            "Reassigning a duty replaces only the previous set for that job");
        ZoneEquipRules.Assign(sets, alternate, 1238);
        check(alternate.AutoEquipTerritories.Count == 1, "Repeated assignment cannot duplicate the same duty");
        reject(() => ZoneEquipRules.Assign(sets, glam, 1238), "Glam collections cannot be auto-equipped");
        reject(() => ZoneEquipRules.Assign(sets, bard, 0), "Zero territory cannot be assigned");
        reject(() => ZoneEquipRules.Assign(sets, Set("BRD", "Unsaved"), 1238), "Unsaved sets cannot own zone assignments");
        var empty = Set("BRD", "Empty"); empty.Pieces.Clear(); sets.Add(empty);
        reject(() => ZoneEquipRules.Assign(sets, empty, 1238), "Empty gear sets cannot own new zone assignments");
        var restored = JsonSerializer.Deserialize<List<TrackedSet>>(JsonSerializer.Serialize(sets))!;
        check(restored.Single(s => s.Id == alternate.Id).AutoEquipTerritories.SequenceEqual(new uint[] { 1238 }),
            "Zone assignments survive configuration serialization");
        check(JsonSerializer.Deserialize<TrackedSet>("{\"Name\":\"Legacy\"}")!.AutoEquipTerritories.Count == 0,
            "Legacy sets default to no auto-equip assignments");
        var draft = GearSearch.Draft(alternate);
        check(draft.Id == alternate.Id && draft.AutoEquipTerritories.SequenceEqual(alternate.AutoEquipTerritories) &&
            !ReferenceEquals(draft.AutoEquipTerritories, alternate.AutoEquipTerritories), "Editing a local set preserves independent assignment data");
        alternate.SourceUrl = "https://xivgear.app/sl/example";
        check(GearSearch.Draft(alternate).AutoEquipTerritories.Count == 0, "New local copies of imports do not steal their source's zone assignments");

        var session = new ZoneEquipSession();
        TrackedSet? Tick(long now, bool ready = true, bool blocked = false, uint zone = 1238, ulong owner = 1, string job = "BRD") =>
            session.Tick(zone, owner, job, ready, blocked, sets, now);
        check(Tick(5000) == null && !session.Pending, "No entry event means no automatic equipment changes");
        session.Enter(1238, 1, 0);
        check(Tick(100, ready: false) == null && Tick(3000, ready: false) == null && session.Pending,
            "Zone entry waits for character and inventory loading");
        check(Tick(4000) == null && Tick(5999) == null && Tick(6000) == alternate,
            "Matching current-job set starts after two stable seconds");
        check(Tick(9000) == null && !session.Pending, "An entry is consumed once and cannot repeatedly equip the set");
        session.Enter(1238, 1, 10_000);
        check(Tick(10_000) == null && Tick(12_000) == alternate, "Re-entering the same duty creates a new equip opportunity");
        session.Enter(1238, 1, 0); Tick(0);
        check(Tick(1500, blocked: true) == null && Tick(5000, blocked: true) == null && session.Pending,
            "Cutscenes, combat, dialogs and other automation delay auto-equip");
        check(Tick(6000) == null && Tick(7999) == null && Tick(8000) == alternate,
            "Blocked entry needs a new stable interval after the game becomes available");
        session.Enter(1238, 1, 0); Tick(0); Tick(1500, ready: false);
        check(Tick(2000) == null && Tick(4000) == alternate, "Another loading interval resets the settlement timer");
        session.Enter(1238, 0, 0);
        check(Tick(0, owner: 0, ready: false) == null && Tick(1000) == null && Tick(3000) == alternate,
            "An entry received before player data loads binds to the arriving character");
        session.Enter(1238, 1, 0); Tick(0);
        check(Tick(2000, owner: 2) == null && !session.Pending, "Character changes cancel pending zone equipping");
        session.Enter(1238, 1, 0); Tick(0);
        check(Tick(2000, zone: 999) == null && !session.Pending, "Leaving the target zone cancels its pending equip");
        session.Enter(1238, 1, 0);
        check(Tick(0, zone: 0) == null && Tick(3000, zone: 0) == null && session.Pending,
            "Unknown territory never triggers equipment changes");
        session.Enter(1238, 1, 0); Tick(0, job: "WAR");
        check(Tick(2000, job: "WAR") == warrior, "The same duty selects the Warrior assignment while on Warrior");
        session.Enter(1238, 1, 0); Tick(0, job: "WHM");
        check(Tick(2000, job: "WHM") == null && !session.Pending, "A job without an assignment leaves its gear unchanged");
        session.Enter(1238, 1, 0); Tick(0);
        alternate.AutoEquipTerritories.Clear();
        check(Tick(2000) == null && !session.Pending, "Removing an assignment before arrival prevents auto-equipping");
        ZoneEquipRules.Assign(sets, alternate, 1238);
        bard.AutoEquipTerritories.Add(1238);
        session.Enter(1238, 1, 0); Tick(0);
        check(Tick(2000) == null && !session.Pending && session.Status.Contains("multiple sets"),
            "Ambiguous saved assignments fail visibly without choosing arbitrary gear");
        bard.AutoEquipTerritories.Remove(1238);
        session.Enter(1238, 1, 0);
        check(Tick(180_000, blocked: true) == null && !session.Pending && session.Status.Contains("three minutes"),
            "Entry requests expire instead of changing equipment much later");
        session.Enter(1238, 1, 0); Tick(0); session.Cancel();
        check(Tick(2000) == null && !session.Pending, "Logout, disabling auto-equip and manual equip can cancel pending entry work");
        session.Enter(100, 1, 0); session.Enter(1238, 1, 1000); Tick(1000);
        check(Tick(3000) == alternate, "A newer territory entry replaces previous pending work");
    }
}
