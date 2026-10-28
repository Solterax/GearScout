namespace GearScout;

public sealed class TrackedSet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled set";
    public string Job { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public List<GearPiece> Pieces { get; set; } = [];
}

public sealed class GearPiece
{
    public string Slot { get; set; } = "";
    public uint ItemId { get; set; }
    // null means an older saved import has no meld data; [] explicitly requests no melds.
    public List<uint>? Materia { get; set; }
}

public sealed record OwnedItem(uint ItemId, int Quantity, string Location, bool Equipped,
    IReadOnlyList<uint>? Materia = null, DateTimeOffset? LastCheckedUtc = null);
public sealed record PieceStatus(GearPiece Piece, OwnedItem? Match)
{
    public MateriaComparison? Melds => Piece.Materia != null && Match?.Materia != null
        ? MateriaMatcher.Compare(Piece.Materia, Match.Materia) : null;
}

public sealed record MateriaComparison(int Matched, IReadOnlyList<uint> Missing, IReadOnlyList<uint> Unexpected)
{
    public bool IsExact => Missing.Count == 0 && Unexpected.Count == 0;
}

public static class MateriaMatcher
{
    // Item IDs encode the stat and grade. Slot order is irrelevant, but repeats count separately.
    public static MateriaComparison Compare(IEnumerable<uint> expected, IEnumerable<uint> actual)
    {
        var remaining = actual.ToList();
        var missing = new List<uint>();
        var matched = 0;
        foreach (var id in expected)
        {
            if (remaining.Remove(id)) matched++;
            else missing.Add(id);
        }
        return new(matched, missing, remaining);
    }
}

public static class GearMatcher
{
    // Allocate physical copies once per set. One ring cannot satisfy two slots.
    // Rings can be worn on either hand; allocation is based on item ID, not slot.
    public static List<PieceStatus> Match(IEnumerable<GearPiece> pieces, IEnumerable<OwnedItem> inventory)
    {
        var targets = pieces.ToList();
        var owned = inventory.Where(i => i.Quantity > 0).OrderByDescending(i => i.Equipped).ToList();
        var matches = new OwnedItem?[targets.Count];
        foreach (var group in targets.Select((piece, index) => (piece, index)).GroupBy(p => p.piece.ItemId))
        {
            var slots = group.ToList();
            if (slots.Count > 12) throw new ArgumentException("Too many duplicate gear slots.");
            // Optimize the whole group together so a ring needed by a later target is reserved.
            // Each DP mask represents filled target slots; each physical copy is offered once.
            var states = new Assignment?[1 << slots.Count];
            states[0] = new(0, new OwnedItem?[slots.Count]);
            foreach (var item in owned.Where(i => i.ItemId == group.Key))
            for (var copy = 0; copy < Math.Min(item.Quantity, slots.Count); copy++)
            {
                var next = (Assignment?[])states.Clone();
                for (var mask = 0; mask < states.Length; mask++)
                {
                    var state = states[mask];
                    if (state == null) continue;
                    for (var slot = 0; slot < slots.Count; slot++)
                    {
                        if ((mask & (1 << slot)) != 0) continue;
                        var score = state.Score + Score(slots[slot].piece, item);
                        var newMask = mask | (1 << slot);
                        if (next[newMask] != null && next[newMask]!.Score >= score) continue;
                        var assigned = (OwnedItem?[])state.Items.Clone();
                        assigned[slot] = item;
                        next[newMask] = new(score, assigned);
                    }
                }
                states = next;
            }
            var best = states.Where(s => s != null).MaxBy(s => s!.Score)!;
            for (var slot = 0; slot < slots.Count; slot++) matches[slots[slot].index] = best.Items[slot];
        }
        return targets.Select((piece, index) => new PieceStatus(piece, matches[index])).ToList();
    }

    private static int Score(GearPiece target, OwnedItem item)
    {
        // Maximize owned pieces, then exactly melded pieces, then correct individual melds.
        var melds = target.Materia != null && item.Materia != null
            ? MateriaMatcher.Compare(target.Materia, item.Materia) : null;
        return 1_000_000 + (melds is { IsExact: true } ? 10_000 : 0) +
               (melds?.Matched ?? 0) * 100 - (melds?.Unexpected.Count ?? 0) * 10 +
               (melds != null ? 60 : 0) + (item.Equipped ? 1 : 0);
    }

    private sealed record Assignment(int Score, OwnedItem?[] Items);
}
