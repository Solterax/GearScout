using GearScout;

internal static class TextImportChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var alc = GearSearch.JobBit("ALC"); var crafter = alc | GearSearch.JobBit("CRP");
        string[] names = ["Alembic of Stars", "Gold Thumb's Mortar", "Crested Headband of Crafting", "Crested Shirt of Crafting",
            "Crested Gloves of Crafting", "Crested Culottes of Crafting", "Crested Boots of Crafting", "Crested Necklace of Crafting",
            "Crested Earrings of Crafting", "Crested Bracelet of Crafting", "Crested Ring of Crafting"];
        string[] slots = ["Weapon", "OffHand", "Head", "Body", "Hand", "Legs", "Feet", "Neck", "Ears", "Wrist", "RingLeft"];
        var gear = names.Select((n, i) => new GearCandidate((uint)(100 + i), n, 700, 100,
            GearSearch.SlotBit(slots[i]) | (i == 10 ? GearSearch.SlotBit("RingRight") : 0), i < 2 ? alc : crafter, false)).ToList();
        string[] materiaNames = ["Craftsman's Command Materia XII", "Craftsman's Command Materia XI", "Craftsman's Competence Materia XII",
            "Craftsman's Competence Materia XI", "Craftsman's Cunning Materia XII", "Craftsman's Cunning Materia XI", "Craftsman's Cunning Materia IX", "Craftsman's Cunning Materia V"];
        var materia = materiaNames.Select((n, i) => new MateriaOption((uint)(200 + i), n, 1, 10, 1, [], [], 1)).ToList();
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "crafting.txt"));
        var result = TextGearImporter.Parse(fixture, gear, materia);
        check(result.Pieces.Count == 12 && result.Pieces.Sum(p => p.Materia!.Count) == 55 && result.Jobs.SequenceEqual(new[] { "ALC" }),
            "User's complete crafting list imports twelve pieces, fifty-five materia and the Alchemist job");
        check(result.HasHqLabels && result.Pieces.Single(p => p.Slot == "Weapon").Materia!.Count == 0,
            "HQ suffixes resolve gear names and a piece without materia retains an empty target");
        check(result.Pieces.Single(p => p.Slot == "Body").Materia!.SequenceEqual(new uint[] { 202, 202, 202, 203, 201 }),
            "Text import preserves repeated materia, grades and their listed order");
        var rings = result.Pieces.Where(p => p.ItemId == 110).ToList();
        check(rings.Count == 2 && rings.Select(p => p.Slot).SequenceEqual(new[] { "RingLeft", "RingRight" }) &&
            !ReferenceEquals(rings[0].Materia, rings[1].Materia), "Repeated ring blocks fill independent left and right targets");
        var escaped = fixture.Replace("**", "\\*\\*").Replace("\n", "\\\r\n&#x20;\r\n\\\r\n");
        check(TextGearImporter.Parse(escaped, gear, materia).Pieces.Sum(p => p.Materia!.Count) == 55,
            "Escaped Markdown, HTML spaces, CRLF and backslash line breaks import correctly");
        check(TextGearImporter.Parse("crested headband of crafting HQ\n• Craftsman’s Command Materia XII", gear, materia).Pieces[0].Materia![0] == 200,
            "Plain text, case differences, curly apostrophes and bullet lists resolve without fuzzy matching");
        check(TextGearImporter.Parse("Crested Headband of Crafting \uE03C", gear, materia).HasHqLabels,
            "The in-game HQ symbol is accepted after an item name");
        check(TextGearImporter.Parse("Crested Headband of Crafting", gear, materia).Jobs.Count == 2,
            "Shared crafting gear exposes every compatible job for explicit selection");
        var saved = result.CreateSet("  Crafting BiS  ", "alc");
        check(saved.Name == "Crafting BiS" && saved.Job == "ALC" && !saved.IsGlam && saved.SourceUrl == "" && saved.AutoEquipTerritories.Count == 0,
            "Review creates a named normal local set without external links or zone assignments");
        saved.Pieces[1].Materia!.Clear();
        check(result.Pieces[1].Materia!.Count == 5 && result.CreateSet("Second", "ALC").Id != saved.Id,
            "Saving copies the preview and creates a new set identity");
        reject(() => result.CreateSet(" ", "ALC"), "Text import requires a set name");
        reject(() => result.CreateSet(new string('x', 121), "ALC"), "Text import enforces the set-name length limit");
        reject(() => result.CreateSet("Test", "BRD"), "Text import rejects an incompatible job");
        reject(() => TextGearImporter.Parse("", gear, materia), "Empty pasted lists are rejected");
        reject(() => TextGearImporter.Parse(new string('x', 128001), gear, materia), "Oversized pasted lists are rejected");
        reject(() => TextGearImporter.Parse("Unknown Headband", gear, materia), "Unknown gear is reported instead of silently omitted");
        reject(() => TextGearImporter.Parse("- Craftsman's Command Materia XII", gear, materia), "Materia before gear is rejected");
        reject(() => TextGearImporter.Parse("Alembic of Stars\n- Unknown Materia", gear, materia), "Unknown materia is reported instead of silently omitted");
        reject(() => TextGearImporter.Parse("Alembic of Stars\n" + string.Concat(Enumerable.Repeat("- Craftsman's Command Materia XII\n", 6)), gear, materia),
            "More than five materia on one piece is rejected");
        reject(() => TextGearImporter.Parse("Crested Headband of Crafting\nCrested Headband of Crafting", gear, materia), "Duplicate non-ring slots cannot overwrite earlier pieces");
        reject(() => TextGearImporter.Parse(string.Concat(Enumerable.Repeat("Crested Ring of Crafting\n", 3)), gear, materia), "A third ring is rejected");
        var unique = gear.Select(g => g.ItemId == 110 ? g with { Unique = true } : g).ToList();
        reject(() => TextGearImporter.Parse("Crested Ring of Crafting\nCrested Ring of Crafting", unique, materia), "Unique ring restrictions apply to text imports");
        reject(() => TextGearImporter.Parse("Alembic of Stars", gear.Append(gear[0] with { ItemId = 999 }), materia), "Ambiguous gear names require correction instead of guessing an ID");
        reject(() => TextGearImporter.Parse("Alembic of Stars\n- Craftsman's Command Materia XII", gear, materia.Append(materia[0] with { ItemId = 999 })),
            "Ambiguous materia names require correction instead of guessing an ID");
        var incompatible = gear.Select(g => g.ItemId == 101 ? g with { Jobs = GearSearch.JobBit("CRP") } : g).ToList();
        reject(() => TextGearImporter.Parse("Alembic of Stars\nGold Thumb's Mortar", incompatible, materia), "Mixed-job tools cannot create an impossible set");
    }
}
