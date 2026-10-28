namespace GearScout;

public sealed record GearCandidate(uint ItemId, string Name, uint ItemLevel, uint EquipLevel,
    ulong Slots, ulong Jobs, bool Unique);

public static class GearSearch
{
    public static readonly string[] Slots = ["Weapon", "OffHand", "Head", "Body", "Hand", "Legs", "Feet", "Ears", "Neck", "Wrist", "RingLeft", "RingRight"];
    private static readonly string[] Jobs = ["GLA", "PGL", "MRD", "LNC", "ARC", "CNJ", "THM", "ACN", "ROG", "PLD", "MNK", "WAR", "DRG", "BRD", "WHM", "BLM", "SMN", "SCH", "NIN", "MCH", "DRK", "AST", "SAM", "RDM", "BLU", "GNB", "DNC", "RPR", "SGE", "VPR", "PCT", "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL", "MIN", "BTN", "FSH"];

    public static ulong JobBit(string job)
    {
        var index = Array.FindIndex(Jobs, j => string.Equals(j, job, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : 1UL << index;
    }

    public static ulong SlotBit(string slot)
    {
        var index = Array.IndexOf(Slots, slot);
        return index < 0 ? 0 : 1UL << index;
    }

    public static bool Fits(GearCandidate item, string job, string slot) =>
        (item.Jobs & JobBit(job)) != 0 && (item.Slots & SlotBit(slot)) != 0;

    public static List<GearCandidate> Search(IEnumerable<GearCandidate> items, string job, string slot, string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items.Where(i => Fits(i, job, slot) && tokens.All(token =>
                i.Name.Contains(token, StringComparison.OrdinalIgnoreCase) || i.ItemId.ToString() == token))
            .OrderByDescending(i => i.ItemLevel).ThenByDescending(i => i.EquipLevel)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.ItemId).ToList();
    }

    public static bool ConflictsWithOtherRing(TrackedSet set, string slot, GearCandidate item) =>
        item.Unique && (slot is "RingLeft" or "RingRight") &&
        set.Pieces.Any(p => p.Slot == (slot == "RingLeft" ? "RingRight" : "RingLeft") && p.ItemId == item.ItemId);

    public static TrackedSet Draft(TrackedSet source) => new()
    {
        Id = string.IsNullOrWhiteSpace(source.SourceUrl) ? source.Id : Guid.NewGuid(),
        Job = source.Job, Name = source.Name + (string.IsNullOrWhiteSpace(source.SourceUrl) ? "" : " (custom)"),
        SourceUrl = "", Pieces = source.Pieces.Select(p => new GearPiece
        {
            Slot = p.Slot, ItemId = p.ItemId, Materia = p.Materia?.ToList(),
        }).ToList(),
    };

    public static void SetPiece(TrackedSet set, string slot, GearCandidate item)
    {
        if (!Fits(item, set.Job, slot)) throw new ArgumentException("That item does not fit this job and slot.");
        if (ConflictsWithOtherRing(set, slot, item)) throw new ArgumentException("A unique ring cannot occupy both ring slots.");
        var previous = set.Pieces.FirstOrDefault(p => p.Slot == slot);
        if (previous?.ItemId == item.ItemId) return;
        set.Pieces.RemoveAll(p => p.Slot == slot);
        // A changed piece has no specified meld plan. Never transfer the old item's materia targets.
        set.Pieces.Add(new GearPiece { Slot = slot, ItemId = item.ItemId });
    }

    public static string SlotName(string slot) => slot switch
    {
        "Weapon" => "Main hand", "OffHand" => "Off hand", "Hand" => "Hands", "Ears" => "Earrings",
        "Neck" => "Necklace", "Wrist" => "Bracelets", "RingLeft" => "Left ring", "RingRight" => "Right ring", _ => slot,
    };
}
