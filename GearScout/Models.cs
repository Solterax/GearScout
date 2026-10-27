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
}

public sealed record OwnedItem(uint ItemId, int Quantity, string Location, bool Equipped);
public sealed record PieceStatus(GearPiece Piece, OwnedItem? Match);

public static class GearMatcher
{
    // Allocate physical copies once per set. One ring cannot satisfy two slots.
    // Rings can be worn on either hand; allocation is based on item ID, not slot.
    public static List<PieceStatus> Match(IEnumerable<GearPiece> pieces, IEnumerable<OwnedItem> inventory)
    {
        var copies = inventory.Where(i => i.Quantity > 0).OrderByDescending(i => i.Equipped)
            .Select(i => (Item: i, Remaining: i.Quantity)).ToList();
        var result = new List<PieceStatus>();
        foreach (var piece in pieces)
        {
            var index = copies.FindIndex(i => i.Remaining > 0 && i.Item.ItemId == piece.ItemId);
            if (index < 0)
                result.Add(new(piece, null));
            else
            {
                var copy = copies[index];
                result.Add(new(piece, copy.Item));
                copies[index] = (copy.Item, copy.Remaining - 1);
            }
        }
        return result;
    }
}
