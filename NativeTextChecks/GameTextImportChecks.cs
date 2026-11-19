using GearScout;
using Lumina.Excel.Sheets;

internal static class GameTextImportChecks
{
    public static void Run(Lumina.GameData game, IEnumerable<MateriaOption> materia)
    {
        var gear = new List<GearCandidate>();
        foreach (var row in game.GetExcelSheet<Item>()!)
        {
            if (!row.EquipSlotCategory.IsValid || !row.ClassJobCategory.IsValid || !row.ClassJobCategory.Value.ALC) continue;
            var c = row.EquipSlotCategory.Value;
            (string Slot, bool Fits)[] slots = [("Weapon", c.MainHand > 0), ("OffHand", c.OffHand > 0), ("Head", c.Head > 0),
                ("Body", c.Body > 0), ("Hand", c.Gloves > 0), ("Legs", c.Legs > 0), ("Feet", c.Feet > 0),
                ("Neck", c.Neck > 0), ("Ears", c.Ears > 0), ("Wrist", c.Wrists > 0),
                ("RingLeft", c.FingerL > 0 || c.FingerR > 0), ("RingRight", c.FingerL > 0 || c.FingerR > 0)];
            var mask = slots.Where(s => s.Fits).Aggregate(0UL, (m, s) => m | GearSearch.SlotBit(s.Slot));
            if (mask != 0) gear.Add(new(row.RowId, row.Name.ToString(), row.LevelItem.RowId, row.LevelEquip, mask, GearSearch.JobBit("ALC"), row.IsUnique));
        }
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "crafting.txt"));
        var imported = TextGearImporter.Parse(text, gear, materia).CreateSet("Crafting sample", "ALC");
        if (imported.Pieces.Count != 12 || imported.Pieces.Sum(p => p.Materia!.Count) != 55 ||
            imported.Pieces.Select(p => p.Slot).Distinct().Count() != 12 || imported.Job != "ALC")
            throw new Exception("Installed-game text import did not resolve all twelve pieces and fifty-five materia.");
        Console.WriteLine("1 installed-game text import check passed: all sample gear names, slots and materia grades resolve for Alchemist.");
    }
}
